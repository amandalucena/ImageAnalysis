using ImageAnalysis.Application.Exceptions;
using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.ValueObjects;
using ImageAnalysis.Infrastructure.Persistence.Repositories;

namespace ImageAnalysis.Tests.Infrastructure.Persistence;

[Collection("Postgres")]
public class AnalysisRepositoryTests
{
    private readonly PostgresFixture _fixture;

    public AnalysisRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAsync_PersisteAnaliseComImagem()
    {
        var analysis = NewAnalysis();

        await using (var dbContext = _fixture.CreateDbContext())
            await new AnalysisRepository(dbContext).AddAsync(analysis);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var loaded = await new AnalysisRepository(dbContext).GetByIdAsync(analysis.Id);

            Assert.NotNull(loaded);
            Assert.Equal(analysis.Name, loaded.Name);
            Assert.Equal(AnalysisStatus.Pending, loaded.Status);
            Assert.Equal(analysis.Image, loaded.Image);
            Assert.Null(loaded.Metadata);
            Assert.Null(loaded.Result);
            Assert.Null(loaded.Failure);
            Assert.Empty(loaded.Findings);
        }
    }

    [Fact]
    public async Task GetByIdAsync_AnaliseInexistente_RetornaNull()
    {
        await using var dbContext = _fixture.CreateDbContext();

        var loaded = await new AnalysisRepository(dbContext).GetByIdAsync(Guid.NewGuid());

        Assert.Null(loaded);
    }

    [Fact]
    public async Task UpdateAsync_PersisteCicloCompletoComFilhosNovos()
    {
        var analysis = NewAnalysis();
        await using (var dbContext = _fixture.CreateDbContext())
            await new AnalysisRepository(dbContext).AddAsync(analysis);

        // Filhos com Id gerado no domínio, adicionados a uma Analysis já existente:
        // devem virar INSERT, não UPDATE.
        Guid findingId;
        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new AnalysisRepository(dbContext);
            var loaded = (await repository.GetByIdAsync(analysis.Id))!;

            loaded.Start();
            loaded.SetMetadata($"analysis-{loaded.Id}/metadata.json", 4032, 3024);
            var finding = loaded.AddFinding("Software de edição", SourceFindingType.Metadata, "Campo Software indica Photoshop.");
            loaded.AddEvidence(finding.Id, "Software", "Software = Adobe Photoshop", EvidenceStrength.Medium, EvidenceDirection.SupportsManipulation);
            loaded.Complete(AnalysisResultStatus.RequiresFurtherInvestigation, ConfidenceLevel.Low, "Indício de edição nos metadados.");
            findingId = finding.Id;

            await repository.UpdateAsync(loaded);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var loaded = (await new AnalysisRepository(dbContext).GetByIdAsync(analysis.Id))!;

            Assert.Equal(AnalysisStatus.Completed, loaded.Status);
            Assert.NotNull(loaded.StartedAt);
            Assert.NotNull(loaded.FinishedAt);

            Assert.NotNull(loaded.Metadata);
            Assert.Equal(4032, loaded.Metadata.Width);
            Assert.Equal(3024, loaded.Metadata.Height);

            var finding = Assert.Single(loaded.Findings);
            Assert.Equal(findingId, finding.Id);
            Assert.Equal(SourceFindingType.Metadata, finding.SourceFindingType);

            var evidence = Assert.Single(finding.Evidences);
            Assert.Equal(EvidenceStrength.Medium, evidence.Strength);
            Assert.Equal(EvidenceDirection.SupportsManipulation, evidence.Direction);

            Assert.NotNull(loaded.Result);
            Assert.Equal(AnalysisResultStatus.RequiresFurtherInvestigation, loaded.Result.Status);
            Assert.Equal(ConfidenceLevel.Low, loaded.Result.Confidence);
        }
    }

    [Fact]
    public async Task UpdateAsync_PersisteFalha()
    {
        var analysis = NewAnalysis();
        await using (var dbContext = _fixture.CreateDbContext())
            await new AnalysisRepository(dbContext).AddAsync(analysis);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new AnalysisRepository(dbContext);
            var loaded = (await repository.GetByIdAsync(analysis.Id))!;

            loaded.Fail(new AnalysisFailure(AnalysisFailureType.StorageUnavailable, "Imagem não encontrada no storage."));

            await repository.UpdateAsync(loaded);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var loaded = (await new AnalysisRepository(dbContext).GetByIdAsync(analysis.Id))!;

            Assert.Equal(AnalysisStatus.Failed, loaded.Status);
            Assert.Null(loaded.StartedAt);
            Assert.NotNull(loaded.Failure);
            Assert.Equal(AnalysisFailureType.StorageUnavailable, loaded.Failure.Type);
            Assert.Equal("Imagem não encontrada no storage.", loaded.Failure.Message);
        }
    }

    [Fact]
    public async Task UpdateAsync_AnaliseNaoObtidaPeloRepositorio_LancaExcecao()
    {
        await using var dbContext = _fixture.CreateDbContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AnalysisRepository(dbContext).UpdateAsync(NewAnalysis()));
    }

    [Fact]
    public async Task UpdateAsync_DuasInstanciasAlteramMesmaAnalise_SegundaLancaConflito()
    {
        var analysis = NewAnalysis();
        await using (var dbContext = _fixture.CreateDbContext())
            await new AnalysisRepository(dbContext).AddAsync(analysis);

        // Simula dois Workers que carregaram a mesma análise antes de qualquer um salvar.
        await using var firstContext = _fixture.CreateDbContext();
        await using var secondContext = _fixture.CreateDbContext();
        var firstRepository = new AnalysisRepository(firstContext);
        var secondRepository = new AnalysisRepository(secondContext);
        var first = (await firstRepository.GetByIdAsync(analysis.Id))!;
        var second = (await secondRepository.GetByIdAsync(analysis.Id))!;

        first.Start();
        await firstRepository.UpdateAsync(first);

        second.Start();
        var exception = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => secondRepository.UpdateAsync(second));
        Assert.Equal(analysis.Id, exception.AnalysisId);
    }

    [Fact]
    public async Task UpdateAsync_ConcorrenciaApenasComFilhosNovos_SegundaLancaConflito()
    {
        var analysis = NewAnalysis();
        analysis.Start();
        await using (var dbContext = _fixture.CreateDbContext())
            await new AnalysisRepository(dbContext).AddAsync(analysis);

        await using var firstContext = _fixture.CreateDbContext();
        await using var secondContext = _fixture.CreateDbContext();
        var firstRepository = new AnalysisRepository(firstContext);
        var secondRepository = new AnalysisRepository(secondContext);
        var first = (await firstRepository.GetByIdAsync(analysis.Id))!;
        var second = (await secondRepository.GetByIdAsync(analysis.Id))!;

        // AddFinding não altera colunas da Analysis; o conflito só é detectado
        // porque o repositório força o UPDATE da raiz.
        first.AddFinding("Software de edição", SourceFindingType.Metadata, "Campo Software indica Photoshop.");
        await firstRepository.UpdateAsync(first);

        second.AddFinding("Software de edição", SourceFindingType.Metadata, "Campo Software indica Photoshop.");
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => secondRepository.UpdateAsync(second));

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var loaded = (await new AnalysisRepository(dbContext).GetByIdAsync(analysis.Id))!;
            Assert.Single(loaded.Findings);
        }
    }

    [Fact]
    public async Task UpdateAsync_SalvamentosSequenciaisNoMesmoContexto_NaoLancamConflito()
    {
        var analysis = NewAnalysis();
        await using (var dbContext = _fixture.CreateDbContext())
            await new AnalysisRepository(dbContext).AddAsync(analysis);

        // O xmin lido é atualizado após cada save, então o mesmo Worker pode salvar várias vezes.
        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new AnalysisRepository(dbContext);
            var loaded = (await repository.GetByIdAsync(analysis.Id))!;

            loaded.Start();
            await repository.UpdateAsync(loaded);

            loaded.AddFinding("Software de edição", SourceFindingType.Metadata, "Campo Software indica Photoshop.");
            await repository.UpdateAsync(loaded);

            loaded.Complete(AnalysisResultStatus.RequiresFurtherInvestigation, ConfidenceLevel.Low, "Indício de edição nos metadados.");
            await repository.UpdateAsync(loaded);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var loaded = (await new AnalysisRepository(dbContext).GetByIdAsync(analysis.Id))!;
            Assert.Equal(AnalysisStatus.Completed, loaded.Status);
            Assert.Single(loaded.Findings);
        }
    }

    private static Analysis NewAnalysis()
    {
        var image = new ImageFile(
            originalFileName: "foto.jpg",
            storagePath: $"analysis-{Guid.NewGuid()}/original.jpg",
            sizeInBytes: 1_024,
            sha256: new string('a', 64),
            mimeType: "image/jpeg");

        return new Analysis("Análise de teste", image);
    }
}
