using System.Security.Cryptography;
using ImageAnalysis.Application.Exceptions;
using ImageAnalysis.Application.UseCases.CreateImageAnalysis;
using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.ValueObjects;
using ImageAnalysis.Tests.Application.Fakes;

namespace ImageAnalysis.Tests.Application;

public class CreateImageAnalysisTests
{
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0xFF, 0xD9];
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

    private readonly FakeImageStorage _storage = new();
    private readonly FakeAnalysisRepository _repository = new();

    [Fact]
    public async Task ExecuteAsync_JpegValido_CriaAnalisePendenteComImagemNoStorage()
    {
        var result = await Execute("Análise de teste", "foto.jpg", JpegBytes);

        var analysis = Assert.Single(_repository.Analyses);
        var (key, savedBytes) = Assert.Single(_storage.Files);

        Assert.Equal(analysis.Id, result.AnalysisId);
        Assert.Equal(AnalysisStatus.Pending, analysis.Status);
        Assert.Equal("Análise de teste", analysis.Name);
        Assert.Matches(@"^uploads/[0-9a-f-]{36}/original\.jpg$", key);
        Assert.Equal(key, analysis.Image.StoragePath);
        Assert.Equal("foto.jpg", analysis.Image.OriginalFileName);
        Assert.Equal("image/jpeg", analysis.Image.MimeType);
        Assert.Equal(JpegBytes.Length, analysis.Image.SizeInBytes);
        Assert.Equal(JpegBytes, savedBytes);
    }

    [Fact]
    public async Task ExecuteAsync_JpegValido_Sha256CorrespondeAosBytesGravados()
    {
        await Execute(null, "foto.jpg", JpegBytes);

        var analysis = Assert.Single(_repository.Analyses);
        var savedBytes = Assert.Single(_storage.Files).Value;

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(savedBytes)), analysis.Image.Sha256);
    }

    [Fact]
    public async Task ExecuteAsync_IdDoUploadEhDiferenteDoIdDaAnalise()
    {
        var result = await Execute(null, "foto.jpg", JpegBytes);

        var key = Assert.Single(_storage.Files).Key;
        var uploadId = Guid.Parse(key.Split('/')[1]);

        Assert.NotEqual(result.AnalysisId, uploadId);
    }

    [Fact]
    public async Task ExecuteAsync_SemNome_CriaAnaliseComNomeNulo()
    {
        await Execute(null, "foto.jpg", JpegBytes);

        Assert.Null(Assert.Single(_repository.Analyses).Name);
    }

    [Fact]
    public async Task ExecuteAsync_PngComExtensaoJpg_UsaTipoDetectadoPeloConteudo()
    {
        await Execute(null, "foto.jpg", PngBytes);

        var analysis = Assert.Single(_repository.Analyses);

        Assert.Equal("image/png", analysis.Image.MimeType);
        Assert.EndsWith("/original.png", analysis.Image.StoragePath);
    }

    [Fact]
    public async Task ExecuteAsync_PdfComExtensaoJpg_RejeitaSemGravarNada()
    {
        await Assert.ThrowsAsync<InvalidImageException>(() => Execute(null, "foto.jpg", "%PDF-1.7 conteudo"u8.ToArray()));

        AssertNothingSaved();
    }

    [Fact]
    public async Task ExecuteAsync_TipoReconhecidoNaoSuportado_RejeitaSemGravarNada()
    {
        byte[] gifBytes = "GIF89a"u8.ToArray();

        await Assert.ThrowsAsync<InvalidImageException>(() => Execute(null, "animacao.gif", gifBytes));

        AssertNothingSaved();
    }

    [Fact]
    public async Task ExecuteAsync_ArquivoVazio_RejeitaSemGravarNada()
    {
        await Assert.ThrowsAsync<InvalidImageException>(() => Execute(null, "foto.jpg", []));

        AssertNothingSaved();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_SemNomeDeArquivo_RejeitaSemGravarNada(string originalFileName)
    {
        await Assert.ThrowsAsync<InvalidImageException>(() => Execute(null, originalFileName, JpegBytes));

        AssertNothingSaved();
    }

    [Fact]
    public async Task ExecuteAsync_NomeDoArquivoAcimaDoLimite_RejeitaSemGravarNada()
    {
        var fileName = new string('a', ImageFile.MaxOriginalFileNameLength) + ".jpg";

        await Assert.ThrowsAsync<InvalidImageException>(() => Execute(null, fileName, JpegBytes));

        AssertNothingSaved();
    }

    [Fact]
    public async Task ExecuteAsync_NomeDaAnaliseAcimaDoLimite_RejeitaSemGravarNada()
    {
        var name = new string('a', Analysis.MaxNameLength + 1);

        await Assert.ThrowsAsync<InvalidImageException>(() => Execute(name, "foto.jpg", JpegBytes));

        AssertNothingSaved();
    }

    [Fact]
    public async Task ExecuteAsync_NomeDaAnaliseNoLimiteComEspacosNasPontas_Aceita()
    {
        var name = $"  {new string('a', Analysis.MaxNameLength)}  ";

        await Execute(name, "foto.jpg", JpegBytes);

        Assert.Equal(Analysis.MaxNameLength, Assert.Single(_repository.Analyses).Name!.Length);
    }

    [Fact]
    public async Task ExecuteAsync_StreamSemSeek_CopiaEGravaConteudoCompleto()
    {
        var useCase = new CreateImageAnalysis(_storage, _repository);

        await useCase.ExecuteAsync(new CreateImageAnalysisCommand(null, "foto.jpg", new NonSeekableStream(JpegBytes)));

        var analysis = Assert.Single(_repository.Analyses);
        Assert.Equal(JpegBytes, Assert.Single(_storage.Files).Value);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(JpegBytes)), analysis.Image.Sha256);
    }

    [Fact]
    public async Task ExecuteAsync_StreamForaDoInicio_ProcessaConteudoCompleto()
    {
        var content = new MemoryStream(JpegBytes) { Position = 5 };

        await new CreateImageAnalysis(_storage, _repository)
            .ExecuteAsync(new CreateImageAnalysisCommand(null, "foto.jpg", content));

        Assert.Equal(JpegBytes, Assert.Single(_storage.Files).Value);
    }

    private sealed class NonSeekableStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override bool CanSeek => false;
    }

    private Task<CreateImageAnalysisResult> Execute(string? name, string originalFileName, byte[] content)
    {
        var useCase = new CreateImageAnalysis(_storage, _repository);
        return useCase.ExecuteAsync(new CreateImageAnalysisCommand(name, originalFileName, new MemoryStream(content)));
    }

    private void AssertNothingSaved()
    {
        Assert.Empty(_storage.Files);
        Assert.Empty(_repository.Analyses);
    }
}
