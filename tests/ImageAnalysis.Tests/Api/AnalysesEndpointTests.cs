using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ImageAnalysis.API.Contracts;
using ImageAnalysis.Application.Jpeg;
using ImageAnalysis.Application.Metadata;
using ImageAnalysis.Application.UseCases.GetAnalysisFindings;
using ImageAnalysis.Application.UseCases.GetAnalysisStatus;
using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.Interfaces;
using ImageAnalysis.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ImageAnalysis.Tests.Api;

[Collection("Api")]
public class AnalysesEndpointTests
{
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0xFF, 0xD9];

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public AnalysesEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Post_JpegValido_Retorna202ComLocationEGravaImagem()
    {
        // Conteúdo único para localizar o arquivo pelo hash entre os gravados pelos outros testes.
        byte[] content = [.. JpegBytes, .. Guid.NewGuid().ToByteArray()];

        var response = await _client.PostAsync("/analyses", Form(content, "foto.jpg"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreateAnalysisResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.EndsWith($"/analyses/{body.Id}", response.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);

        // O caminho no storage não é exposto pela API; o arquivo é localizado pelo SHA-256.
        var status = await GetStatus(body.Id);
        var savedFile = Assert.Single(
            Directory.EnumerateFiles(Path.Combine(_factory.StorageRootPath, "uploads"), "original.jpg", SearchOption.AllDirectories),
            path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) == status.Sha256);
        Assert.Equal(content, await File.ReadAllBytesAsync(savedFile));
    }

    // O status não é conferido aqui: o Worker pode já ter processado a análise antes do GET.
    [Fact]
    public async Task Get_AnaliseCriadaSemNome_RetornaDadosDaImagem()
    {
        var id = await CreateAnalysis(Form(JpegBytes, "foto.jpg"));

        var status = await GetStatus(id);

        Assert.Equal(id, status.Id);
        Assert.Null(status.Name);
        Assert.Equal("foto.jpg", status.OriginalFileName);
        Assert.Equal("image/jpeg", status.MimeType);
        Assert.Equal(JpegBytes.Length, status.SizeInBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(JpegBytes)), status.Sha256);
    }

    // Ponta a ponta: POST → fila → Worker → GET com a análise concluída. O Worker roda o
    // ExifTool real, então a imagem precisa ser um JPEG legível, e não só a assinatura.
    [Fact]
    public async Task Post_AnaliseCriada_WorkerProcessaAteCompleted()
    {
        var id = await CreateAnalysis(Form(ReadImageFixture("camera.jpg"), "camera.jpg"));

        var status = await WaitForStatus(id, "Completed", TimeSpan.FromSeconds(30));

        Assert.NotNull(status.StartedAt);
        Assert.NotNull(status.FinishedAt);
        Assert.True(status.StartedAt <= status.FinishedAt);
    }

    // Ponta a ponta: POST → fila → Worker (ExifTool real) → GET dos achados.
    [Fact]
    public async Task Post_ImagemEditada_WorkerGravaAchadosConsultaveis()
    {
        var id = await CreateAnalysis(Form(ReadImageFixture("edited.jpg"), "edited.jpg"));
        await WaitForStatus(id, "Completed", TimeSpan.FromSeconds(30));

        var findings = await GetFindings(id);

        Assert.Equal("Completed", findings.Status);
        var editing = Assert.Single(findings.Findings, f => f.Name == MetadataFindingNames.EditingSoftware);
        Assert.Equal("Metadata", editing.Source);
        Assert.Contains(editing.Evidences, e =>
            e.Name == MetadataEvidenceNames.EditingSoftwareTag
            && e.Strength == "Medium"
            && e.Direction == "SupportsManipulation"
            && e.Description.Contains("Photoshop"));
    }

    // Contrato com o frontend nos níveis que só aparecem com achados: campos em camelCase e
    // enums como texto, conferidos no JSON cru.
    [Fact]
    public async Task Get_AchadosDeImagemEditada_UsaCamelCaseEEnumsComoTexto()
    {
        var id = await CreateAnalysis(Form(ReadImageFixture("edited.jpg"), "edited.jpg"));
        await WaitForStatus(id, "Completed", TimeSpan.FromSeconds(30));

        var response = await _client.GetAsync($"/analyses/{id}/findings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var findings = json.RootElement.GetProperty("findings");
        Assert.NotEqual(0, findings.GetArrayLength());

        var finding = findings.EnumerateArray().First();
        Assert.Equal(
            ["id", "name", "source", "description", "evidences"],
            finding.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.String, finding.GetProperty("source").ValueKind);

        var evidence = finding.GetProperty("evidences").EnumerateArray().First();
        Assert.Equal(
            ["name", "description", "strength", "direction"],
            evidence.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.String, evidence.GetProperty("strength").ValueKind);
        Assert.Equal(JsonValueKind.String, evidence.GetProperty("direction").ValueKind);
    }

    // Contraponto: uma foto de câmera sem sinais de edição conclui sem achados que apoiem
    // manipulação; da estrutura JPEG sai só o perfil de codificação, que é informativo.
    [Fact]
    public async Task Post_FotoDeCamera_WorkerConcluiSemAchadosDeManipulacao()
    {
        var id = await CreateAnalysis(Form(ReadImageFixture("camera.jpg"), "camera.jpg"));
        await WaitForStatus(id, "Completed", TimeSpan.FromSeconds(30));

        var findings = await GetFindings(id);

        Assert.Equal("Completed", findings.Status);
        var finding = Assert.Single(findings.Findings);
        Assert.Equal(JpegStructureFindingNames.EncodingProfile, finding.Name);
        Assert.Equal(nameof(SourceFindingType.JpegStructure), finding.Source);
    }

    [Fact]
    public async Task Get_AnaliseCriadaComNome_RetornaNome()
    {
        var id = await CreateAnalysis(Form(JpegBytes, "foto.jpg", name: "Análise de teste"));

        var status = await GetStatus(id);

        Assert.Equal("Análise de teste", status.Name);
    }

    [Fact]
    public async Task Post_SemArquivo_Retorna400()
    {
        var form = new MultipartFormDataContent { { new StringContent("Análise de teste"), "Name" } };

        var response = await _client.PostAsync("/analyses", form);

        AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_NomeAcimaDe200Caracteres_Retorna400()
    {
        var response = await _client.PostAsync("/analyses", Form(JpegBytes, "foto.jpg", name: new string('a', 201)));

        AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_PdfComExtensaoJpg_Retorna400()
    {
        var response = await _client.PostAsync("/analyses", Form("%PDF-1.7 conteudo"u8.ToArray(), "foto.jpg"));

        AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_ImagemInvalidaComAcceptHtml_Retorna400()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/analyses")
        {
            Content = Form("%PDF-1.7 conteudo"u8.ToArray(), "foto.jpg"),
        };
        request.Headers.Accept.ParseAdd("text/html");

        var response = await _client.SendAsync(request);

        AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_ArquivoVazio_Retorna400()
    {
        var response = await _client.PostAsync("/analyses", Form([], "foto.jpg"));

        AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_ArquivoAcimaDe20MB_Retorna413()
    {
        var content = new byte[UploadLimits.MaxImageSizeInBytes + 1];
        JpegBytes.CopyTo(content, 0);

        var response = await _client.PostAsync("/analyses", Form(content, "grande.jpg"));

        AssertProblem(response, HttpStatusCode.RequestEntityTooLarge);
    }

    // Acima do limite da requisição: barrada pelo Content-Length antes da leitura do formulário.
    [Fact]
    public async Task Post_RequisicaoAcimaDoLimiteDoFormulario_Retorna413()
    {
        var content = new byte[UploadLimits.MaxRequestSizeInBytes + 1];
        JpegBytes.CopyTo(content, 0);

        var response = await _client.PostAsync("/analyses", Form(content, "enorme.jpg"));

        AssertProblem(response, HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Get_AnaliseInexistente_Retorna404()
    {
        var response = await _client.GetAsync($"/analyses/{Guid.NewGuid()}");

        AssertProblem(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_AchadosDeAnaliseInexistente_Retorna404()
    {
        var response = await _client.GetAsync($"/analyses/{Guid.NewGuid()}/findings");

        AssertProblem(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_AchadosComIdQueNaoEGuid_Retorna404()
    {
        var response = await _client.GetAsync("/analyses/abc/findings");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // O JpegBytes não tem dimensões, então o Worker marca a análise como Failed. Em qualquer
    // momento em que o GET chegue (Pending, Processing ou Failed), a lista vem vazia, e o
    // teste não depende de quando o Worker processa a análise.
    [Fact]
    public async Task Get_AchadosDeAnaliseRecemCriada_RetornaStatusESemAchados()
    {
        var id = await CreateAnalysis(Form(JpegBytes, "foto.jpg"));

        var findings = await GetFindings(id);

        Assert.Equal(id, findings.AnalysisId);
        Assert.Contains(findings.Status, new[] { "Pending", "Processing", "Failed" });
        Assert.Empty(findings.Findings);
    }

    // Contrato com o frontend: a desserialização do record não diferencia maiúsculas de
    // minúsculas, então os nomes dos campos são conferidos no JSON cru.
    [Fact]
    public async Task Get_Achados_UsaCamposEmCamelCase()
    {
        var id = await CreateAnalysis(Form(JpegBytes, "foto.jpg"));

        var response = await _client.GetAsync($"/analyses/{id}/findings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            ["analysisId", "status", "findings"],
            json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("findings").ValueKind);
    }

    // Ponta a ponta com o Redis real: entradas gravadas antes do processamento somem quando o
    // Worker conclui. A análise é criada pelos serviços, e não pelo POST, para que as entradas
    // existam antes de a mensagem chegar ao Worker; pelo POST, o id só é conhecido depois da
    // publicação e o Worker poderia terminar antes da gravação.
    [Fact]
    public async Task Worker_AnaliseConcluida_RemoveDoCacheAsEntradasDaAnalise()
    {
        var content = ReadImageFixture("camera.jpg");
        var image = new ImageFile(
            "camera.jpg",
            $"uploads/{Guid.NewGuid()}/original.jpg",
            content.Length,
            Convert.ToHexStringLower(SHA256.HashData(content)),
            "image/jpeg");
        var analysis = new Analysis(null, image);

        var cache = _factory.Services.GetRequiredService<IAnalysisCache>();
        var otherAnalysisId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            await using (var stream = new MemoryStream(content, writable: false))
                await scope.ServiceProvider.GetRequiredService<IImageStorage>().SaveAsync(image.StoragePath, stream);

            await scope.ServiceProvider.GetRequiredService<IAnalysisRepository>().AddAsync(analysis);
        }

        await cache.SetAsync(analysis.Id, "ela", new[] { 1, 2, 3 });
        await cache.SetAsync(analysis.Id, "regions", "regioes");
        await cache.SetAsync(otherAnalysisId, "ela", "outra");

        await _factory.Services.GetRequiredService<IAnalysisQueue>().PublishAsync(analysis.Id);
        await WaitForStatus(analysis.Id, "Completed", TimeSpan.FromSeconds(30));

        // A limpeza roda logo depois da gravação final, que é o que o GET enxerga como Completed.
        await WaitUntil(async () => await cache.GetAsync<string>(analysis.Id, "regions") is null, TimeSpan.FromSeconds(5));

        Assert.Null(await cache.GetAsync<int[]>(analysis.Id, "ela"));
        Assert.Null(await cache.GetAsync<string>(analysis.Id, "regions"));
        Assert.Equal("outra", await cache.GetAsync<string>(otherAnalysisId, "ela"));

        await cache.RemoveAllAsync(otherAnalysisId);
    }

    private static byte[] ReadImageFixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Metadata", name));

    private static MultipartFormDataContent Form(byte[] content, string fileName, string? name = null)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

        var form = new MultipartFormDataContent { { file, "File", fileName } };
        if (name is not null)
            form.Add(new StringContent(name), "Name");

        return form;
    }

    private async Task<Guid> CreateAnalysis(MultipartFormDataContent form)
    {
        var response = await _client.PostAsync("/analyses", form);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CreateAnalysisResponse>())!.Id;
    }

    private async Task<AnalysisStatusResult> GetStatus(Guid id)
    {
        var response = await _client.GetAsync($"/analyses/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<AnalysisStatusResult>())!;
    }

    private async Task<AnalysisFindingsResult> GetFindings(Guid id)
    {
        var response = await _client.GetAsync($"/analyses/{id}/findings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<AnalysisFindingsResult>())!;
    }

    // O processamento é assíncrono, então o status é consultado até chegar ao esperado.
    private async Task<AnalysisStatusResult> WaitForStatus(Guid id, string expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            var status = await GetStatus(id);
            if (status.Status == expected || DateTime.UtcNow >= deadline)
            {
                Assert.Equal(expected, status.Status);
                return status;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    private static async Task WaitUntil(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (!await condition() && DateTime.UtcNow < deadline)
            await Task.Delay(TimeSpan.FromMilliseconds(100));
    }

    private static void AssertProblem(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
