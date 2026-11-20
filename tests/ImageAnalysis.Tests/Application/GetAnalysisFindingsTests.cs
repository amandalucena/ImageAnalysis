using ImageAnalysis.Application.UseCases.GetAnalysisFindings;
using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.ValueObjects;
using ImageAnalysis.Tests.Application.Fakes;

namespace ImageAnalysis.Tests.Application;

public class GetAnalysisFindingsTests
{
    private readonly FakeAnalysisRepository _repository = new();

    [Fact]
    public async Task ExecuteAsync_AnaliseInexistente_RetornaNull()
    {
        var result = await Execute(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteAsync_AnalisePendente_RetornaStatusESemAchados()
    {
        var analysis = AddAnalysis();

        var result = await Execute(analysis.Id);

        Assert.NotNull(result);
        Assert.Equal(analysis.Id, result.AnalysisId);
        Assert.Equal("Pending", result.Status);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task ExecuteAsync_AnaliseEmProcessamento_RetornaStatusESemAchados()
    {
        var analysis = AddAnalysis();
        analysis.Start();

        var result = await Execute(analysis.Id);

        Assert.Equal("Processing", result?.Status);
        Assert.Empty(result!.Findings);
    }

    [Fact]
    public async Task ExecuteAsync_AnaliseFalhou_RetornaStatusESemAchados()
    {
        var analysis = AddAnalysis();
        analysis.Start();
        analysis.Fail(new AnalysisFailure(AnalysisFailureType.AnalyzerCrashed, "Erro."));

        var result = await Execute(analysis.Id);

        Assert.Equal("Failed", result?.Status);
        Assert.Empty(result!.Findings);
    }

    [Fact]
    public async Task ExecuteAsync_AnaliseConcluida_RetornaAchadoComEvidenciasEEnumsComoTexto()
    {
        var analysis = AddAnalysis();
        analysis.Start();
        var finding = analysis.AddFinding("metadata.editing-software", SourceFindingType.Metadata, "Software de edição encontrado.");
        analysis.AddEvidence(finding.Id, "editing-software", "Adobe Photoshop 25.0", EvidenceStrength.Medium, EvidenceDirection.SupportsManipulation);
        Complete(analysis);

        var result = await Execute(analysis.Id);

        Assert.NotNull(result);
        Assert.Equal("Completed", result.Status);

        var findingResult = Assert.Single(result.Findings);
        Assert.Equal(finding.Id, findingResult.Id);
        Assert.Equal("metadata.editing-software", findingResult.Name);
        Assert.Equal("Metadata", findingResult.Source);
        Assert.Equal("Software de edição encontrado.", findingResult.Description);

        var evidence = Assert.Single(findingResult.Evidences);
        Assert.Equal(new EvidenceResult("editing-software", "Adobe Photoshop 25.0", "Medium", "SupportsManipulation"), evidence);
    }

    [Fact]
    public async Task ExecuteAsync_OrdenaAchadosPeloTextoDaOrigemENomeEEvidenciasPorNome()
    {
        var analysis = AddAnalysis();
        analysis.Start();
        var ela = analysis.AddFinding("ela.region", SourceFindingType.Ela, "Região ELA.");
        var dates = analysis.AddFinding("metadata.inconsistent-dates", SourceFindingType.Metadata, "Datas.");
        analysis.AddFinding("metadata.ai-generator", SourceFindingType.Metadata, "IA.");
        analysis.AddEvidence(dates.Id, "modified-after-capture", "Depois.", EvidenceStrength.Low, EvidenceDirection.SupportsManipulation);
        analysis.AddEvidence(dates.Id, "create-date-mismatch", "Diferente.", EvidenceStrength.Medium, EvidenceDirection.SupportsManipulation);
        analysis.AddEvidence(ela.Id, "ela", "Região.", EvidenceStrength.Low, EvidenceDirection.Neutral);
        Complete(analysis);

        var result = await Execute(analysis.Id);

        // "Ela" vem antes de "Metadata" pelo texto, embora Metadata seja o primeiro valor do enum.
        Assert.Equal(
            ["ela.region", "metadata.ai-generator", "metadata.inconsistent-dates"],
            result!.Findings.Select(f => f.Name));
        Assert.Equal(
            ["create-date-mismatch", "modified-after-capture"],
            result.Findings[2].Evidences.Select(e => e.Name));
    }

    private static void Complete(Analysis analysis) =>
        analysis.Complete(AnalysisResultStatus.Inconclusive, ConfidenceLevel.Low, "Resumo.");

    private Analysis AddAnalysis()
    {
        var image = new ImageFile("foto.jpg", $"uploads/{Guid.NewGuid()}/original.jpg", 1_024, new string('a', 64), "image/jpeg");
        var analysis = new Analysis(null, image);
        _repository.Analyses.Add(analysis);
        return analysis;
    }

    private Task<AnalysisFindingsResult?> Execute(Guid analysisId) =>
        new GetAnalysisFindings(_repository).ExecuteAsync(new GetAnalysisFindingsQuery(analysisId));
}
