using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.Exceptions;
using ImageAnalysis.Domain.ValueObjects;
using ImageAnalysis.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

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
    public async Task UpdateAsync_DuasInstanciasCompletamMesmaAnalise_SegundaLancaConflito()
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

        // AnalysisResults tem índice único em AnalysisId; o INSERT do segundo pode falhar
        // nesse índice antes do UPDATE da raiz, e ainda assim deve virar conflito.
        first.Complete(AnalysisResultStatus.RequiresFurtherInvestigation, ConfidenceLevel.Low, "Primeiro Worker.");
        await firstRepository.UpdateAsync(first);

        second.Complete(AnalysisResultStatus.RequiresFurtherInvestigation, ConfidenceLevel.Low, "Segundo Worker.");
        var exception = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => secondRepository.UpdateAsync(second));
        Assert.Equal(analysis.Id, exception.AnalysisId);
    }

    [Fact]
    public async Task UpdateAsync_DuasInstanciasDefinemMetadados_SegundaLancaConflito()
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

        first.SetMetadata($"analysis-{analysis.Id}/metadata.json", 4032, 3024);
        await firstRepository.UpdateAsync(first);

        second.SetMetadata($"analysis-{analysis.Id}/metadata.json", 4032, 3024);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => secondRepository.UpdateAsync(second));
    }

    [Fact]
    public async Task UpdateAsync_DuasInstanciasFalhamMesmaAnalise_SegundaLancaConflito()
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

        first.Fail(new AnalysisFailure(AnalysisFailureType.StorageUnavailable, "Primeiro Worker."));
        await firstRepository.UpdateAsync(first);

        second.Fail(new AnalysisFailure(AnalysisFailureType.StorageUnavailable, "Segundo Worker."));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => secondRepository.UpdateAsync(second));

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var loaded = (await new AnalysisRepository(dbContext).GetByIdAsync(analysis.Id))!;
            Assert.Equal("Primeiro Worker.", loaded.Failure!.Message);
        }
    }

    [Fact]
    public async Task UpdateAsync_FalhaConcorrenteComAnaliseCompletada_LancaConflitoEMantemResultado()
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

        first.Complete(AnalysisResultStatus.RequiresFurtherInvestigation, ConfidenceLevel.Low, "Primeiro Worker.");
        await firstRepository.UpdateAsync(first);

        // Para o segundo a análise ainda está Processing, então o domínio aceita o Fail;
        // só o banco sabe que ela já foi completada.
        second.Fail(new AnalysisFailure(AnalysisFailureType.StorageUnavailable, "Segundo Worker."));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => secondRepository.UpdateAsync(second));

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var loaded = (await new AnalysisRepository(dbContext).GetByIdAsync(analysis.Id))!;
            Assert.Equal(AnalysisStatus.Completed, loaded.Status);
            Assert.NotNull(loaded.Result);
            Assert.Null(loaded.Failure);
        }
    }

    [Fact]
    public async Task UpdateAsync_AposConflito_RecarregarNoMesmoContextoPermiteRetry()
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

        first.AddFinding("Primeiro", SourceFindingType.Metadata, "Primeiro Worker.");
        await firstRepository.UpdateAsync(first);

        second.AddFinding("Segundo", SourceFindingType.Metadata, "Segundo Worker.");
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => secondRepository.UpdateAsync(second));

        // Mesmo contexto (mesmo escopo de DI): o GetByIdAsync deve trazer a versão atual,
        // e não a instância desatualizada que estava rastreada.
        var reloaded = (await secondRepository.GetByIdAsync(analysis.Id))!;
        Assert.NotSame(second, reloaded);
        Assert.Single(reloaded.Findings);

        reloaded.AddFinding("Segundo", SourceFindingType.Metadata, "Segundo Worker.");
        await secondRepository.UpdateAsync(reloaded);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var loaded = (await new AnalysisRepository(dbContext).GetByIdAsync(analysis.Id))!;
            Assert.Equal(2, loaded.Findings.Count);
        }
    }

    [Fact]
    public async Task UpdateAsync_FilhoNovoTratadoComoExistente_NaoViraConflito()
    {
        var analysis = NewAnalysis();
        analysis.Start();
        await using (var dbContext = _fixture.CreateDbContext())
            await new AnalysisRepository(dbContext).AddAsync(analysis);

        await using var dbContextUpdate = _fixture.CreateDbContext();
        var repository = new AnalysisRepository(dbContextUpdate);
        var loaded = (await repository.GetByIdAsync(analysis.Id))!;

        // Simula um erro de mapeamento: o filho novo vira UPDATE e afeta 0 linhas.
        // Não é um conflito com outra operação, então não deve ser traduzido.
        var finding = loaded.AddFinding("Software de edição", SourceFindingType.Metadata, "Campo Software indica Photoshop.");
        dbContextUpdate.Entry(finding).State = EntityState.Modified;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => repository.UpdateAsync(loaded));
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
