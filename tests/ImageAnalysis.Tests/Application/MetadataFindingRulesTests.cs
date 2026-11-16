using ImageAnalysis.Application.Metadata;
using ImageAnalysis.Domain.ValueObjects;
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

    // --- InconsistentDates ---

    private const string Capture = "2023:05:10 14:22:31";

    private static MetadataDate Date(string raw, TimeSpan? offset = null) =>
        new(raw, DateTime.ParseExact(raw, "yyyy:MM:dd HH:mm:ss", null), offset);

    private static MetadataDate InvalidDate() => new("0000:00:00 00:00:00", null, null);

    private static NormalizedMetadata Dates(
        MetadataDate? original = null,
        MetadataDate? create = null,
        MetadataDate? modify = null,
        CaptureDevice? device = null,
        string? make = null,
        string? model = null) =>
        new()
        {
            DateTimeOriginal = original,
            CreateDate = create,
            ModifyDate = modify,
            CaptureDevice = device,
            Make = make,
            Model = model
        };

    [Fact]
    public void InconsistentDates_SemDatas_RetornaNull()
    {
        Assert.Null(MetadataFindingRules.InconsistentDates(new NormalizedMetadata()));
    }

    [Fact]
    public void InconsistentDates_SoDatasInvalidas_RetornaNull()
    {
        var metadata = Dates(InvalidDate(), InvalidDate(), InvalidDate());

        Assert.Null(MetadataFindingRules.InconsistentDates(metadata));
    }

    [Fact]
    public void InconsistentDates_DataInvalidaContraValida_NaoCompara()
    {
        var metadata = Dates(Date(Capture), InvalidDate(), InvalidDate());

        Assert.Null(MetadataFindingRules.InconsistentDates(metadata));
    }

    [Fact]
    public void InconsistentDates_CameraNormal_RetornaNull()
    {
        var metadata = Dates(Date(Capture), Date(Capture), Date(Capture), CaptureDevice.Camera);

        Assert.Null(MetadataFindingRules.InconsistentDates(metadata));
    }

    [Fact]
    public void InconsistentDates_DiferencaDentroDaTolerancia_RetornaNull()
    {
        var metadata = Dates(Date(Capture), Date("2023:05:10 14:22:32"), Date("2023:05:10 14:22:30"));

        Assert.Null(MetadataFindingRules.InconsistentDates(metadata));
    }

    [Fact]
    public void InconsistentDates_ModifyAntesDaCaptura_EvidenciaMediumComValoresBrutos()
    {
        var metadata = Dates(Date(Capture), Date(Capture), Date("2023:05:09 14:22:31"));

        var finding = MetadataFindingRules.InconsistentDates(metadata);

        Assert.NotNull(finding);
        Assert.Equal(MetadataFindingNames.InconsistentDates, finding.Name);

        var evidence = Assert.Single(finding.Evidences);
        Assert.Equal(MetadataEvidenceNames.ModifyBeforeCapture, evidence.Name);
        Assert.Equal(EvidenceStrength.Medium, evidence.Strength);
        Assert.Equal(EvidenceDirection.SupportsManipulation, evidence.Direction);
        Assert.Contains("2023:05:09 14:22:31", evidence.Description);
        Assert.Contains(Capture, evidence.Description);
    }

    [Fact]
    public void InconsistentDates_ModifyAntesDaCaptura_SemDateTimeOriginal_UsaCreateDate()
    {
        var metadata = Dates(create: Date(Capture), modify: Date("2023:05:09 14:22:31"));

        var finding = MetadataFindingRules.InconsistentDates(metadata);

        var evidence = Assert.Single(finding!.Evidences);
        Assert.Equal(MetadataEvidenceNames.ModifyBeforeCapture, evidence.Name);
        Assert.Contains("CreateDate", evidence.Description);
    }

    [Fact]
    public void InconsistentDates_CreateDiferenteDoOriginal_Camera_EvidenciaMedium()
    {
        var metadata = Dates(Date(Capture), Date("2023:05:10 18:00:00"), device: CaptureDevice.Camera);

        var evidence = Assert.Single(MetadataFindingRules.InconsistentDates(metadata)!.Evidences);

        Assert.Equal(MetadataEvidenceNames.CreateOriginalMismatch, evidence.Name);
        Assert.Equal(EvidenceStrength.Medium, evidence.Strength);
        Assert.Equal(EvidenceDirection.SupportsManipulation, evidence.Direction);
    }

    [Fact]
    public void InconsistentDates_CreateDiferenteDoOriginal_Scanner_EvidenciaNeutral()
    {
        var metadata = Dates(Date("1995:07:20 00:00:00"), Date(Capture), device: CaptureDevice.Scanner);

        var evidence = Assert.Single(MetadataFindingRules.InconsistentDates(metadata)!.Evidences);

        Assert.Equal(MetadataEvidenceNames.ScannedOriginalDate, evidence.Name);
        Assert.Equal(EvidenceStrength.Low, evidence.Strength);
        Assert.Equal(EvidenceDirection.Neutral, evidence.Direction);
        Assert.Contains("scanner", evidence.Description);
    }

    [Theory]
    [InlineData("EPSON", "Perfection V600")]
    [InlineData("Canon", "CanoScan LiDE 400")]
    public void InconsistentDates_SemFileSource_MakeOuModelDeScanner_TratadoComoScanner(string make, string model)
    {
        var metadata = Dates(Date("1995:07:20 00:00:00"), Date(Capture), make: make, model: model);

        var evidence = Assert.Single(MetadataFindingRules.InconsistentDates(metadata)!.Evidences);

        Assert.Equal(MetadataEvidenceNames.ScannedOriginalDate, evidence.Name);
        Assert.Equal(EvidenceDirection.Neutral, evidence.Direction);
    }

    [Fact]
    public void InconsistentDates_FileSourceCamera_VenceMakeDeScanner()
    {
        var metadata = Dates(Date(Capture), Date("2023:05:10 18:00:00"),
            device: CaptureDevice.Camera, model: "CanoScan LiDE 400");

        var evidence = Assert.Single(MetadataFindingRules.InconsistentDates(metadata)!.Evidences);

        Assert.Equal(MetadataEvidenceNames.CreateOriginalMismatch, evidence.Name);
        Assert.Equal(EvidenceStrength.Medium, evidence.Strength);
    }

    [Fact]
    public void InconsistentDates_CreateDiferenteDoOriginal_DispositivoDesconhecido_LowCitandoScan()
    {
        var metadata = Dates(Date(Capture), Date("2023:05:10 18:00:00"), make: "Canon", model: "Canon EOS R5");

        var evidence = Assert.Single(MetadataFindingRules.InconsistentDates(metadata)!.Evidences);

        Assert.Equal(MetadataEvidenceNames.CreateOriginalMismatch, evidence.Name);
        Assert.Equal(EvidenceStrength.Low, evidence.Strength);
        Assert.Equal(EvidenceDirection.SupportsManipulation, evidence.Direction);
        Assert.Contains("escaneadas", evidence.Description);
    }

    [Fact]
    public void InconsistentDates_ModifyUmMesDepois_EvidenciaLow()
    {
        var metadata = Dates(Date(Capture), Date(Capture), Date("2023:06:10 09:00:00"));

        var evidence = Assert.Single(MetadataFindingRules.InconsistentDates(metadata)!.Evidences);

        Assert.Equal(MetadataEvidenceNames.ModifiedAfterCapture, evidence.Name);
        Assert.Equal(EvidenceStrength.Low, evidence.Strength);
        Assert.Equal(EvidenceDirection.SupportsManipulation, evidence.Direction);
    }

    [Fact]
    public void InconsistentDates_ModifyAbaixoDoLimite_RetornaNull()
    {
        var metadata = Dates(Date(Capture), Date(Capture), Date("2023:05:10 14:23:01"));

        Assert.Null(MetadataFindingRules.InconsistentDates(metadata));
    }

    [Fact]
    public void InconsistentDates_ScannerComFotoAntiga_ModifyNoDiaDoScan_NaoContaComoEdicao()
    {
        // Foto de 1995 escaneada em 2023: ModifyDate é o dia do scan, não uma edição.
        var metadata = Dates(
            Date("1995:07:20 00:00:00"), Date(Capture), Date(Capture), CaptureDevice.Scanner);

        var evidence = Assert.Single(MetadataFindingRules.InconsistentDates(metadata)!.Evidences);

        Assert.Equal(MetadataEvidenceNames.ScannedOriginalDate, evidence.Name);
    }

    [Fact]
    public void InconsistentDates_OffsetsDiferentesMesmoInstante_SemInconsistencia()
    {
        var metadata = Dates(
            Date("2023:05:10 10:00:00", TimeSpan.FromHours(-3)),
            Date("2023:05:10 13:00:00", TimeSpan.Zero));

        Assert.Null(MetadataFindingRules.InconsistentDates(metadata));
    }

    [Fact]
    public void InconsistentDates_MesmoHorarioComOffsetsDiferentes_Inconsistencia()
    {
        var metadata = Dates(
            Date("2023:05:10 10:00:00", TimeSpan.FromHours(-3)),
            Date("2023:05:10 10:00:00", TimeSpan.Zero),
            device: CaptureDevice.Camera);

        var evidence = Assert.Single(MetadataFindingRules.InconsistentDates(metadata)!.Evidences);

        Assert.Equal(MetadataEvidenceNames.CreateOriginalMismatch, evidence.Name);
    }

    [Fact]
    public void InconsistentDates_OffsetEmSoUmaData_ComparaSemFuso()
    {
        var metadata = Dates(
            Date("2023:05:10 10:00:00", TimeSpan.FromHours(-3)),
            Date("2023:05:10 10:00:00"));

        Assert.Null(MetadataFindingRules.InconsistentDates(metadata));
    }

    [Fact]
    public void InconsistentDates_ModifyAntesECreateDiferente_UmAchadoComDuasEvidencias()
    {
        var metadata = Dates(
            Date(Capture), Date("2023:05:10 18:00:00"), Date("2023:05:01 08:00:00"), CaptureDevice.Camera);

        var finding = MetadataFindingRules.InconsistentDates(metadata);

        Assert.NotNull(finding);
        Assert.Equal(2, finding.Evidences.Count);
        Assert.Contains(finding.Evidences, e => e.Name == MetadataEvidenceNames.CreateOriginalMismatch);
        Assert.Contains(finding.Evidences, e => e.Name == MetadataEvidenceNames.ModifyBeforeCapture);
    }

    [Fact]
    public void InconsistentDates_MetadataNulo_LancaExcecao()
    {
        Assert.Throws<ArgumentNullException>(() => MetadataFindingRules.InconsistentDates(null!));
    }

    // --- MetadataUnavailable ---

    [Fact]
    public void MetadataUnavailable_SemNenhumMetadado_EvidenciaNeutral()
    {
        var finding = MetadataFindingRules.MetadataUnavailable(new NormalizedMetadata());

        Assert.NotNull(finding);
        Assert.Equal(MetadataFindingNames.MetadataUnavailable, finding.Name);

        var evidence = Assert.Single(finding.Evidences);
        Assert.Equal(MetadataEvidenceNames.MetadataAbsent, evidence.Name);
        Assert.Equal(EvidenceStrength.Low, evidence.Strength);
        Assert.Equal(EvidenceDirection.Neutral, evidence.Direction);
        Assert.Contains("não indica manipulação", evidence.Description);
    }

    [Fact]
    public void MetadataUnavailable_SoEstruturaDoArquivo_AindaConsideraAusente()
    {
        var metadata = new NormalizedMetadata
        {
            HasJfif = true,
            JfifVersion = "1.01",
            Dimensions = new ImageDimensions(800, 600),
            Warnings = ["JPEG format error"]
        };

        Assert.NotNull(MetadataFindingRules.MetadataUnavailable(metadata));
    }

    public static TheoryData<NormalizedMetadata> AnyCaptureField => new()
    {
        new NormalizedMetadata { Make = "Apple" },
        new NormalizedMetadata { Model = "iPhone 13" },
        new NormalizedMetadata { Software = [new SoftwareTag("IFD0:Software", "17.4.1")] },
        new NormalizedMetadata { DateTimeOriginal = new MetadataDate("0000:00:00 00:00:00", null, null) },
        new NormalizedMetadata { CreateDate = new MetadataDate(Capture, null, null) },
        new NormalizedMetadata { ModifyDate = new MetadataDate(Capture, null, null) },
        new NormalizedMetadata { HasThumbnail = true },
        new NormalizedMetadata { HasGps = true },
        new NormalizedMetadata { CaptureDevice = CaptureDevice.Camera },
        new NormalizedMetadata { DeclaredDimensions = new ImageDimensions(4032, 3024) }
    };

    [Theory]
    [MemberData(nameof(AnyCaptureField))]
    public void MetadataUnavailable_QualquerCampoDeCaptura_RetornaNull(NormalizedMetadata metadata)
    {
        Assert.Null(MetadataFindingRules.MetadataUnavailable(metadata));
    }

    [Fact]
    public void MetadataUnavailable_MetadataNulo_LancaExcecao()
    {
        Assert.Throws<ArgumentNullException>(() => MetadataFindingRules.MetadataUnavailable(null!));
    }

    // --- StrippedMetadata ---

    [Fact]
    public void StrippedMetadata_SemExif_RetornaNull()
    {
        Assert.Null(MetadataFindingRules.StrippedMetadata(new NormalizedMetadata()));
    }

    [Fact]
    public void StrippedMetadata_CameraCompleta_RetornaNull()
    {
        var metadata = new NormalizedMetadata
        {
            Make = "Apple",
            Model = "iPhone 13",
            DateTimeOriginal = Date(Capture),
            HasThumbnail = true
        };

        Assert.Null(MetadataFindingRules.StrippedMetadata(metadata));
    }

    [Fact]
    public void StrippedMetadata_UmSinal_RetornaNull()
    {
        // Câmera presente, só a miniatura falta.
        var metadata = new NormalizedMetadata { Make = "Apple", DateTimeOriginal = Date(Capture) };

        Assert.Null(MetadataFindingRules.StrippedMetadata(metadata));
    }

    [Fact]
    public void StrippedMetadata_DoisSinais_DuasEvidenciasLow()
    {
        var metadata = new NormalizedMetadata { DateTimeOriginal = Date(Capture) };

        var finding = MetadataFindingRules.StrippedMetadata(metadata);

        Assert.NotNull(finding);
        Assert.Equal(MetadataFindingNames.StrippedMetadata, finding.Name);
        Assert.Equal(
            [MetadataEvidenceNames.CameraIdentityMissing, MetadataEvidenceNames.ThumbnailMissing],
            finding.Evidences.Select(e => e.Name));
        Assert.All(finding.Evidences, e =>
        {
            Assert.Equal(EvidenceStrength.Low, e.Strength);
            Assert.Equal(EvidenceDirection.SupportsManipulation, e.Direction);
        });
    }

    [Fact]
    public void StrippedMetadata_TresSinais_IncluiEditorSemCamera()
    {
        var metadata = new NormalizedMetadata
        {
            Software = [new SoftwareTag("IFD0:Software", "Adobe Photoshop 25.0 (Windows)")],
            DateTimeOriginal = Date(Capture)
        };

        var finding = MetadataFindingRules.StrippedMetadata(metadata);

        Assert.NotNull(finding);
        Assert.Equal(3, finding.Evidences.Count);

        var editor = Assert.Single(finding.Evidences, e => e.Name == MetadataEvidenceNames.EditorWithoutCamera);
        Assert.Contains("IFD0:Software", editor.Description);
        Assert.Contains("Adobe Photoshop 25.0 (Windows)", editor.Description);
    }

    [Fact]
    public void StrippedMetadata_EditorComCameraEMiniatura_RetornaNull()
    {
        var metadata = new NormalizedMetadata
        {
            Make = "Canon",
            Model = "Canon EOS R5",
            Software = [new SoftwareTag("IFD0:Software", "Adobe Photoshop 25.0 (Windows)")],
            HasThumbnail = true
        };

        Assert.Null(MetadataFindingRules.StrippedMetadata(metadata));
    }

    [Fact]
    public void StrippedMetadata_SomaComEditingSoftware()
    {
        var metadata = new NormalizedMetadata
        {
            Software = [new SoftwareTag("IFD0:Software", "GIMP 2.10.36")],
            DateTimeOriginal = Date(Capture)
        };

        Assert.NotNull(MetadataFindingRules.EditingSoftware(metadata));
        Assert.NotNull(MetadataFindingRules.StrippedMetadata(metadata));
    }

    [Theory]
    [MemberData(nameof(MixedCases))]
    public void AusenteERemovido_NuncaDisparamJuntos(NormalizedMetadata metadata)
    {
        var unavailable = MetadataFindingRules.MetadataUnavailable(metadata);
        var stripped = MetadataFindingRules.StrippedMetadata(metadata);

        Assert.False(unavailable is not null && stripped is not null);
    }

    public static TheoryData<NormalizedMetadata> MixedCases => new()
    {
        new NormalizedMetadata(),
        new NormalizedMetadata { HasJfif = true, Warnings = ["aviso"] },
        new NormalizedMetadata { DateTimeOriginal = new MetadataDate(Capture, null, null) },
        new NormalizedMetadata { Software = [new SoftwareTag("IFD0:Software", "GIMP 2.10")] },
        new NormalizedMetadata { DeclaredDimensions = new ImageDimensions(10, 10) }
    };

    [Fact]
    public void StrippedMetadata_MetadataNulo_LancaExcecao()
    {
        Assert.Throws<ArgumentNullException>(() => MetadataFindingRules.StrippedMetadata(null!));
    }

    // --- ReadWarnings ---

    [Fact]
    public void ReadWarnings_SemAvisos_RetornaNull()
    {
        Assert.Null(MetadataFindingRules.ReadWarnings(new NormalizedMetadata()));
    }

    [Fact]
    public void ReadWarnings_UmAviso_EvidenciaNeutralComOTexto()
    {
        var metadata = new NormalizedMetadata { Warnings = ["JPEG format error"] };

        var finding = MetadataFindingRules.ReadWarnings(metadata);

        Assert.NotNull(finding);
        Assert.Equal(MetadataFindingNames.ReadWarnings, finding.Name);

        var evidence = Assert.Single(finding.Evidences);
        Assert.Equal(MetadataEvidenceNames.ExifToolWarning, evidence.Name);
        Assert.Equal(EvidenceStrength.Low, evidence.Strength);
        Assert.Equal(EvidenceDirection.Neutral, evidence.Direction);
        Assert.Contains("JPEG format error", evidence.Description);
    }

    [Fact]
    public void ReadWarnings_DoisAvisos_DuasEvidencias()
    {
        var metadata = new NormalizedMetadata
        {
            Warnings = ["Possibly incorrect maker notes offsets", "Bad IFD1 directory"]
        };

        var finding = MetadataFindingRules.ReadWarnings(metadata);

        Assert.Equal(2, finding!.Evidences.Count);
    }

    [Fact]
    public void ReadWarnings_MetadataNulo_LancaExcecao()
    {
        Assert.Throws<ArgumentNullException>(() => MetadataFindingRules.ReadWarnings(null!));
    }
}
