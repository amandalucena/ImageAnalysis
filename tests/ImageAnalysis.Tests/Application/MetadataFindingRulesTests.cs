using ImageAnalysis.Application.Metadata;
using ImageAnalysis.Domain.Enums;

namespace ImageAnalysis.Tests.Application;

public class MetadataFindingRulesTests
{
    private static NormalizedMetadata WithSoftware(params SoftwareTag[] software) =>
        new() { Software = software };

    [Fact]
    public void EditingSoftware_SemSoftware_RetornaNull()
    {
        Assert.Null(MetadataFindingRules.EditingSoftware(new NormalizedMetadata()));
    }

    [Fact]
    public void EditingSoftware_SoFirmwareDeCamera_RetornaNull()
    {
        var metadata = WithSoftware(new SoftwareTag("IFD0:Software", "Ver.1.00"));

        Assert.Null(MetadataFindingRules.EditingSoftware(metadata));
    }

    [Fact]
    public void EditingSoftware_SoGeradorDeIa_RetornaNull()
    {
        var metadata = WithSoftware(new SoftwareTag("XMP-xmp:CreatorTool", "Midjourney"));

        Assert.Null(MetadataFindingRules.EditingSoftware(metadata));
    }

    [Fact]
    public void EditingSoftware_EditorEmUmaTag_GeraAchadoComEvidenciaMedium()
    {
        var metadata = WithSoftware(new SoftwareTag("IFD0:Software", "Adobe Photoshop 25.0 (Windows)"));

        var finding = MetadataFindingRules.EditingSoftware(metadata);

        Assert.NotNull(finding);
        Assert.Equal(MetadataFindingNames.EditingSoftware, finding.Name);

        var evidence = Assert.Single(finding.Evidences);
        Assert.Equal(MetadataEvidenceNames.EditingSoftwareTag, evidence.Name);
        Assert.Equal(EvidenceStrength.Medium, evidence.Strength);
        Assert.Equal(EvidenceDirection.SupportsManipulation, evidence.Direction);
        Assert.Contains("IFD0:Software", evidence.Description);
        Assert.Contains("Adobe Photoshop 25.0 (Windows)", evidence.Description);
    }

    [Fact]
    public void EditingSoftware_MesmoEditorEmDuasTags_DuasEvidenciasSemDivergencia()
    {
        var metadata = WithSoftware(
            new SoftwareTag("IFD0:Software", "Adobe Photoshop 25.0 (Windows)"),
            new SoftwareTag("XMP-xmp:CreatorTool", " adobe photoshop 25.0 (windows) "));

        var finding = MetadataFindingRules.EditingSoftware(metadata);

        Assert.NotNull(finding);
        Assert.Equal(2, finding.Evidences.Count);
        Assert.All(finding.Evidences, e => Assert.Equal(MetadataEvidenceNames.EditingSoftwareTag, e.Name));
    }

    [Fact]
    public void EditingSoftware_FontesDivergentes_AdicionaEvidenciaLow()
    {
        var metadata = WithSoftware(
            new SoftwareTag("IFD0:Software", "Ver.1.00"),
            new SoftwareTag("XMP-xmp:CreatorTool", "Adobe Photoshop CC 2019"));

        var finding = MetadataFindingRules.EditingSoftware(metadata);

        Assert.NotNull(finding);
        Assert.Equal(2, finding.Evidences.Count);

        var divergence = Assert.Single(finding.Evidences, e => e.Name == MetadataEvidenceNames.SoftwareDivergence);
        Assert.Equal(EvidenceStrength.Low, divergence.Strength);
        Assert.Equal(EvidenceDirection.SupportsManipulation, divergence.Direction);
        Assert.Contains("IFD0:Software = Ver.1.00", divergence.Description);
        Assert.Contains("XMP-xmp:CreatorTool = Adobe Photoshop CC 2019", divergence.Description);
    }

    [Fact]
    public void EditingSoftware_DivergenciaSemEditor_RetornaNull()
    {
        var metadata = WithSoftware(
            new SoftwareTag("IFD0:Software", "Ver.1.00"),
            new SoftwareTag("XMP-xmp:CreatorTool", "Picasa"));

        Assert.Null(MetadataFindingRules.EditingSoftware(metadata));
    }

    [Fact]
    public void EditingSoftware_MetadataNulo_LancaExcecao()
    {
        Assert.Throws<ArgumentNullException>(() => MetadataFindingRules.EditingSoftware(null!));
    }

    [Fact]
    public void AiGenerator_SemSoftware_RetornaNull()
    {
        Assert.Null(MetadataFindingRules.AiGenerator(new NormalizedMetadata()));
    }

    [Fact]
    public void AiGenerator_SoEditor_RetornaNull()
    {
        var metadata = WithSoftware(new SoftwareTag("IFD0:Software", "Adobe Photoshop 25.0 (Windows)"));

        Assert.Null(MetadataFindingRules.AiGenerator(metadata));
    }

    [Fact]
    public void AiGenerator_GeradorEmUmaTag_GeraAchadoComEvidenciaHigh()
    {
        var metadata = WithSoftware(new SoftwareTag("XMP-xmp:CreatorTool", "Midjourney"));

        var finding = MetadataFindingRules.AiGenerator(metadata);

        Assert.NotNull(finding);
        Assert.Equal(MetadataFindingNames.AiGenerator, finding.Name);

        var evidence = Assert.Single(finding.Evidences);
        Assert.Equal(MetadataEvidenceNames.AiGeneratorTag, evidence.Name);
        Assert.Equal(EvidenceStrength.High, evidence.Strength);
        Assert.Equal(EvidenceDirection.SupportsManipulation, evidence.Direction);
        Assert.Contains("XMP-xmp:CreatorTool", evidence.Description);
        Assert.Contains("Midjourney", evidence.Description);
    }

    [Fact]
    public void AiGenerator_FontesDivergentes_NaoAdicionaEvidenciaDeDivergencia()
    {
        var metadata = WithSoftware(
            new SoftwareTag("IFD0:Software", "Ver.1.00"),
            new SoftwareTag("XMP-xmp:CreatorTool", "Stable Diffusion XL"));

        var finding = MetadataFindingRules.AiGenerator(metadata);

        Assert.NotNull(finding);
        var evidence = Assert.Single(finding.Evidences);
        Assert.Equal(MetadataEvidenceNames.AiGeneratorTag, evidence.Name);
    }

    [Fact]
    public void EditorEGeradorNoMesmoValor_AsDuasRegrasGeramAchado()
    {
        var metadata = WithSoftware(new SoftwareTag("XMP-xmp:CreatorTool", "Adobe Photoshop 25.0 (Firefly)"));

        Assert.NotNull(MetadataFindingRules.EditingSoftware(metadata));
        Assert.NotNull(MetadataFindingRules.AiGenerator(metadata));
    }

    [Fact]
    public void AiGenerator_MetadataNulo_LancaExcecao()
    {
        Assert.Throws<ArgumentNullException>(() => MetadataFindingRules.AiGenerator(null!));
    }
}
