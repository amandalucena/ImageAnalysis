using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.ValueObjects;

namespace ImageAnalysis.Tests.Domain;

public class AnalyzerResultTests
{
    [Fact]
    public void Empty_NaoTemAchados()
    {
        Assert.Empty(AnalyzerResult.Empty.Findings);
    }

    [Fact]
    public void Construtor_CopiaListaDeAchados()
    {
        var findings = new List<AnalyzerFinding> { NewFinding() };

        var result = new AnalyzerResult(findings);
        findings.Add(NewFinding());

        Assert.Single(result.Findings);
    }

    [Fact]
    public void Construtor_AchadoNulo_LancaExcecao()
    {
        Assert.Throws<ArgumentException>(() => new AnalyzerResult(new AnalyzerFinding[] { null! }));
    }

    [Theory]
    [InlineData(null, "Descrição")]
    [InlineData("   ", "Descrição")]
    [InlineData("Nome", null)]
    [InlineData("Nome", "")]
    public void AnalyzerFinding_SemNomeOuDescricao_LancaExcecao(string? name, string? description)
    {
        Assert.Throws<ArgumentException>(() => new AnalyzerFinding(name!, description!, Array.Empty<AnalyzerEvidence>()));
    }

    [Fact]
    public void AnalyzerFinding_EvidenciaNula_LancaExcecao()
    {
        Assert.Throws<ArgumentException>(() =>
            new AnalyzerFinding("Nome", "Descrição", new AnalyzerEvidence[] { null! }));
    }

    [Fact]
    public void AnalyzerFinding_SemEvidencias_Aceita()
    {
        var finding = new AnalyzerFinding("Nome", "Descrição", Array.Empty<AnalyzerEvidence>());

        Assert.Empty(finding.Evidences);
    }

    [Theory]
    [InlineData(null, "Descrição")]
    [InlineData("Nome", "  ")]
    public void AnalyzerEvidence_SemNomeOuDescricao_LancaExcecao(string? name, string? description)
    {
        Assert.Throws<ArgumentException>(() =>
            new AnalyzerEvidence(name!, description!, EvidenceStrength.Low, EvidenceDirection.Neutral));
    }

    [Fact]
    public void AnalyzerEvidence_EnumInvalido_LancaExcecao()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AnalyzerEvidence("Nome", "Descrição", (EvidenceStrength)99, EvidenceDirection.Neutral));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AnalyzerEvidence("Nome", "Descrição", EvidenceStrength.Low, (EvidenceDirection)99));
    }

    [Fact]
    public void AnalyzerContext_SemIdOuImagem_LancaExcecao()
    {
        var image = new ImageFile("foto.jpg", "uploads/1/original.jpg", 1_024, new string('a', 64), "image/jpeg");

        Assert.Throws<ArgumentException>(() => new AnalyzerContext(Guid.Empty, image));
        Assert.Throws<ArgumentNullException>(() => new AnalyzerContext(Guid.NewGuid(), null!));
    }

    private static AnalyzerFinding NewFinding() => new(
        "Software de edição",
        "Campo Software indica Photoshop.",
        new[] { new AnalyzerEvidence("Software", "Adobe Photoshop 25.0", EvidenceStrength.Medium, EvidenceDirection.SupportsManipulation) });
}
