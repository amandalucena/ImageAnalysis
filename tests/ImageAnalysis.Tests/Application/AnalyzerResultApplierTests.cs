using ImageAnalysis.Application.Pipeline;
using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.ValueObjects;

namespace ImageAnalysis.Tests.Application;

public class AnalyzerResultApplierTests
{
    [Fact]
    public void Apply_ResultadoVazio_NaoAdicionaAchados()
    {
        var analysis = StartedAnalysis();

        AnalyzerResultApplier.Apply(analysis, SourceFindingType.Metadata, AnalyzerResult.Empty);

        Assert.Empty(analysis.Findings);
    }

    [Fact]
    public void Apply_CopiaAchadoEEvidenciasSemAlteracoes()
    {
        var analysis = StartedAnalysis();
        var result = new AnalyzerResult(
        [
            new AnalyzerFinding("metadata.editing-software", "Software de edição encontrado.",
            [
                new AnalyzerEvidence("software", "Adobe Photoshop 25.0", EvidenceStrength.Medium, EvidenceDirection.SupportsManipulation),
            ]),
        ]);

        AnalyzerResultApplier.Apply(analysis, SourceFindingType.Metadata, result);

        var finding = Assert.Single(analysis.Findings);
        Assert.Equal("metadata.editing-software", finding.Name);
        Assert.Equal("Software de edição encontrado.", finding.Description);
        Assert.Equal(analysis.Id, finding.AnalysisId);

        var evidence = Assert.Single(finding.Evidences);
        Assert.Equal("software", evidence.Name);
        Assert.Equal("Adobe Photoshop 25.0", evidence.Description);
        Assert.Equal(EvidenceStrength.Medium, evidence.Strength);
        Assert.Equal(EvidenceDirection.SupportsManipulation, evidence.Direction);
        Assert.Equal(finding.Id, evidence.FindingId);
    }

    [Fact]
    public void Apply_VariosAchados_MantemOrdemEEvidenciasNoAchadoCerto()
    {
        var analysis = StartedAnalysis();
        var result = new AnalyzerResult(
        [
            new AnalyzerFinding("a", "Achado A.", [Evidence("a1"), Evidence("a2")]),
            new AnalyzerFinding("b", "Achado B.", [Evidence("b1")]),
            new AnalyzerFinding("c", "Achado C.", []),
        ]);

        AnalyzerResultApplier.Apply(analysis, SourceFindingType.Metadata, result);

        Assert.Equal(["a", "b", "c"], analysis.Findings.Select(f => f.Name));
        Assert.Equal(["a1", "a2"], analysis.Findings.ElementAt(0).Evidences.Select(e => e.Name));
        Assert.Equal(["b1"], analysis.Findings.ElementAt(1).Evidences.Select(e => e.Name));
        Assert.Empty(analysis.Findings.ElementAt(2).Evidences);
    }

    [Theory]
    [InlineData(SourceFindingType.Metadata)]
    [InlineData(SourceFindingType.Ela)]
    public void Apply_GravaAOrigemEmTodosOsAchados(SourceFindingType source)
    {
        var analysis = StartedAnalysis();
        var result = new AnalyzerResult(
        [
            new AnalyzerFinding("a", "Achado A.", []),
            new AnalyzerFinding("b", "Achado B.", []),
        ]);

        AnalyzerResultApplier.Apply(analysis, source, result);

        Assert.All(analysis.Findings, f => Assert.Equal(source, f.SourceFindingType));
    }

    private static AnalyzerEvidence Evidence(string name) =>
        new(name, $"Evidência {name}.", EvidenceStrength.Low, EvidenceDirection.Neutral);

    private static Analysis StartedAnalysis()
    {
        var image = new ImageFile("foto.jpg", $"uploads/{Guid.NewGuid()}/original.jpg", 1_024, new string('a', 64), "image/jpeg");
        var analysis = new Analysis(null, image);
        analysis.Start();
        return analysis;
    }
}
