using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ImageAnalysis.API.Contracts;
using ImageAnalysis.Application.UseCases.GetAnalysisStatus;

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

    [Fact]
    public async Task Get_AnaliseCriadaSemNome_RetornaPendingComDadosDaImagem()
    {
        var id = await CreateAnalysis(Form(JpegBytes, "foto.jpg"));

        var status = await GetStatus(id);

        Assert.Equal(id, status.Id);
        Assert.Null(status.Name);
        Assert.Equal("Pending", status.Status);
        Assert.Equal("foto.jpg", status.OriginalFileName);
        Assert.Equal("image/jpeg", status.MimeType);
        Assert.Equal(JpegBytes.Length, status.SizeInBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(JpegBytes)), status.Sha256);
        Assert.Null(status.StartedAt);
        Assert.Null(status.FinishedAt);
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

    [Fact]
    public async Task Get_AnaliseInexistente_Retorna404()
    {
        var response = await _client.GetAsync($"/analyses/{Guid.NewGuid()}");

        AssertProblem(response, HttpStatusCode.NotFound);
    }

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

    private static void AssertProblem(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
