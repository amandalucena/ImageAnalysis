using System.Text;
using ImageAnalysis.Application.Jpeg;
using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.ValueObjects;

namespace ImageAnalysis.Tests.Application.Jpeg;

public class JpegStructureFindingRulesTests
{
    private static readonly byte[] Dqt = [0x00, .. Enumerable.Repeat((byte)1, 64)];
    private static readonly byte[] Sos = [0x01, 0x01, 0x00, 0x00, 0x3F, 0x00];
    private static readonly byte[] Jfif = [.. "JFIF\0"u8, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00];

    // ---- Evaluate ----

    [Fact]
    public void Evaluate_JpegComum_SoGeraOPerfilDeCodificacao()
    {
        var result = Evaluate(new JpegBytes().Soi().Header().Scan().Eoi());

        var finding = Assert.Single(result.Findings);
        Assert.Equal(JpegStructureFindingNames.EncodingProfile, finding.Name);
        Assert.All(finding.Evidences, e => Assert.Equal(EvidenceDirection.Neutral, e.Direction));
    }

    [Fact]
    public void Evaluate_ArquivoSemSoi_NaoGeraAchados()
    {
        var result = JpegStructureFindingRules.Evaluate(JpegStructureParser.Parse("\x89PNG"u8));

        Assert.Same(AnalyzerResult.Empty, result);
    }

    [Fact]
    public void Evaluate_VariosProblemas_MantemAOrdemFixaDosAchados()
    {
        var result = Evaluate(new JpegBytes().Soi().Header().Soi().Scan().Eoi().Raw(0x01, 0x02).Raw(0x03));

        Assert.Equal(
            [JpegStructureFindingNames.UnexpectedMarkers, JpegStructureFindingNames.TrailingData, JpegStructureFindingNames.EncodingProfile],
            result.Findings.Select(f => f.Name));
    }

    [Fact]
    public void Evaluate_NenhumaEvidenciaAcimaDeLow()
    {
        // Força conservadora enquanto os limiares não são validados com o dataset.
        var bytes = new JpegBytes().Soi().Header().Raw(0x01, 0x02).Soi().Marker(0xD0)
            .SegmentWith(0xFE, [.. "CREATOR: gd-jpeg v1.0"u8]).SegmentWith(0xED, [.. "Photoshop 3.0\0"u8])
            .Scan().Eoi().Raw(0x12, 0x34);

        var result = Evaluate(bytes);

        Assert.All(result.Findings.SelectMany(f => f.Evidences), e => Assert.Equal(EvidenceStrength.Low, e.Strength));
    }

    // ---- jpeg.truncated ----

    [Fact]
    public void Truncated_ArquivoCortadoNoScan_RegistraNeutral()
    {
        var structure = Parse(new JpegBytes().Soi().Header().SegmentWith(0xDA, Sos).Raw(0x12, 0x34));

        var finding = JpegStructureFindingRules.Truncated(structure);

        var evidence = Assert.Single(finding!.Evidences);
        Assert.Equal(JpegStructureEvidenceNames.TruncatedFile, evidence.Name);
        Assert.Equal(EvidenceDirection.Neutral, evidence.Direction);
    }

    [Fact]
    public void Truncated_LeituraParadaPorComprimentoInvalido_RegistraFaltaDoEoi()
    {
        var structure = Parse(new JpegBytes().Soi().SegmentHeader(0xE1, 1));

        var finding = JpegStructureFindingRules.Truncated(structure);

        Assert.Equal(JpegStructureEvidenceNames.MissingEndOfImage, Assert.Single(finding!.Evidences).Name);
    }

    [Fact]
    public void Truncated_ComEoi_NaoGeraAchado()
    {
        Assert.Null(JpegStructureFindingRules.Truncated(Parse(new JpegBytes().Soi().Header().Scan().Eoi())));
    }

    // ---- jpeg.malformed-structure ----

    [Fact]
    public void MalformedStructure_BytesSoltosNoMeio_ApoiaManipulacao()
    {
        var structure = Parse(new JpegBytes().Soi().SegmentWith(0xE0, Jfif).Raw(1, 2, 3).SegmentWith(0xDB, Dqt).Scan().Eoi());

        var evidence = Assert.Single(JpegStructureFindingRules.MalformedStructure(structure)!.Evidences);
        Assert.Equal(JpegStructureEvidenceNames.UnexpectedData, evidence.Name);
        Assert.Equal(EvidenceDirection.SupportsManipulation, evidence.Direction);
        Assert.Contains("3 bytes", evidence.Description);
        Assert.Contains("0xFFDB", evidence.Description);
    }

    [Fact]
    public void MalformedStructure_BytesSoltosAntesDoEoi_Neutral()
    {
        var structure = Parse(new JpegBytes().Soi().Header().Scan().Segment(0xE0).Raw(0, 0).Eoi());

        var evidence = Assert.Single(JpegStructureFindingRules.MalformedStructure(structure)!.Evidences);
        Assert.Equal(EvidenceDirection.Neutral, evidence.Direction);
    }

    [Fact]
    public void MalformedStructure_BytesSoltosAteOFim_Neutral()
    {
        var structure = Parse(new JpegBytes().Soi().SegmentWith(0xE0, Jfif).Raw(1, 2, 3));

        var evidence = Assert.Single(JpegStructureFindingRules.MalformedStructure(structure)!.Evidences);
        Assert.Equal(EvidenceDirection.Neutral, evidence.Direction);
        Assert.Contains("até o fim do arquivo", evidence.Description);
    }

    [Fact]
    public void MalformedStructure_SegmentoMalformadoEMarcadorDesconhecido_Neutral()
    {
        var structure = Parse(new JpegBytes().Soi().Segment(0x42).SegmentWith(0xDD, 0x01).Header().Scan().Eoi());

        var finding = JpegStructureFindingRules.MalformedStructure(structure)!;

        Assert.Equal(
            [JpegStructureEvidenceNames.UnknownMarker, JpegStructureEvidenceNames.MalformedSegment],
            finding.Evidences.Select(e => e.Name));
        Assert.All(finding.Evidences, e => Assert.Equal(EvidenceDirection.Neutral, e.Direction));
    }

    [Fact]
    public void MalformedStructure_SoTruncamento_NaoGeraAchado()
    {
        // O truncamento é relatado no jpeg.truncated, não aqui.
        var structure = Parse(new JpegBytes().Soi().Header().SegmentWith(0xDA, Sos).Raw(0x12));

        Assert.Null(JpegStructureFindingRules.MalformedStructure(structure));
    }

    [Fact]
    public void MalformedStructure_MuitosProblemas_LimitaAsEvidencias()
    {
        var builder = new JpegBytes().Soi();
        for (var i = 0; i < 50; i++)
            builder.Segment(0x42);

        var finding = JpegStructureFindingRules.MalformedStructure(Parse(builder.Header().Scan().Eoi()))!;

        Assert.Equal(JpegStructureFindingRules.MaxEvidencesPerFinding, finding.Evidences.Count);
        var last = finding.Evidences[^1];
        Assert.Equal(JpegStructureEvidenceNames.MoreOccurrences, last.Name);
        Assert.Contains("Mais 31", last.Description);
    }

    // ---- jpeg.unexpected-markers ----

    [Fact]
    public void UnexpectedMarkers_SoiRepetido_ApoiaManipulacao()
    {
        var structure = Parse(new JpegBytes().Soi().Header().Soi().Scan().Eoi());

        var evidence = Assert.Single(JpegStructureFindingRules.UnexpectedMarkers(structure)!.Evidences);
        Assert.Equal(JpegStructureEvidenceNames.RepeatedStartOfImage, evidence.Name);
        Assert.Equal(EvidenceDirection.SupportsManipulation, evidence.Direction);
    }

    [Fact]
    public void UnexpectedMarkers_DoisFramesSemDhp_RegistraVariosFrames()
    {
        var structure = Parse(new JpegBytes().Soi().Header()
            .SegmentWith(0xC0, 0x08, 0x00, 0x10, 0x00, 0x10, 0x01, 0x01, 0x11, 0x00).Scan().Eoi());

        var evidence = Assert.Single(JpegStructureFindingRules.UnexpectedMarkers(structure)!.Evidences);
        Assert.Equal(JpegStructureEvidenceNames.MultipleFrames, evidence.Name);
        Assert.Contains("2 cabeçalhos", evidence.Description);
    }

    [Fact]
    public void UnexpectedMarkers_DoisFramesComDhp_NaoEhAnomalia()
    {
        // JPEG hierárquico: o DHP declara a imagem e cada SOF é uma camada.
        var structure = Parse(new JpegBytes().Soi().Segment(0xDE, 15).SegmentWith(0xDB, Dqt)
            .SegmentWith(0xC5, 0x08, 0x00, 0x10, 0x00, 0x10, 0x01, 0x01, 0x11, 0x00).Scan()
            .SegmentWith(0xC5, 0x08, 0x00, 0x10, 0x00, 0x10, 0x01, 0x01, 0x11, 0x00).Scan().Eoi());

        Assert.DoesNotContain(
            JpegStructureFindingRules.UnexpectedMarkers(structure)?.Evidences ?? [],
            e => e.Name == JpegStructureEvidenceNames.MultipleFrames);
    }

    [Fact]
    public void UnexpectedMarkers_RstForaDoScan_Registra()
    {
        var structure = Parse(new JpegBytes().Soi().Header().Marker(0xD3).Scan().Eoi());

        var evidence = Assert.Single(JpegStructureFindingRules.UnexpectedMarkers(structure)!.Evidences);
        Assert.Equal(JpegStructureEvidenceNames.RestartOutsideScan, evidence.Name);
        Assert.Contains("0xFFD3", evidence.Description);
    }

    [Fact]
    public void UnexpectedMarkers_ScanAntesDoFrame_Registra()
    {
        var structure = Parse(new JpegBytes().Soi().SegmentWith(0xDB, Dqt).Scan()
            .SegmentWith(0xC0, 0x08, 0x00, 0x10, 0x00, 0x10, 0x01, 0x01, 0x11, 0x00).Eoi());

        Assert.Contains(
            JpegStructureFindingRules.UnexpectedMarkers(structure)!.Evidences,
            e => e.Name == JpegStructureEvidenceNames.ScanBeforeFrame);
    }

    [Fact]
    public void UnexpectedMarkers_EoiSemScan_Registra()
    {
        var structure = Parse(new JpegBytes().Soi().Header().Eoi());

        var evidence = Assert.Single(JpegStructureFindingRules.UnexpectedMarkers(structure)!.Evidences);
        Assert.Equal(JpegStructureEvidenceNames.ImageWithoutScan, evidence.Name);
    }

    [Fact]
    public void UnexpectedMarkers_FrameUsaTabelaNaoDefinida_Registra()
    {
        // O Header define só a tabela 0; o frame usa a 0 e a 1.
        var structure = Parse(new JpegBytes().Soi().SegmentWith(0xDB, [0x00, .. Enumerable.Repeat((byte)1, 64)])
            .SegmentWith(0xC0, 0x08, 0x00, 0x10, 0x00, 0x10, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01)
            .Scan().Eoi());

        var evidence = Assert.Single(JpegStructureFindingRules.UnexpectedMarkers(structure)!.Evidences);
        Assert.Equal(JpegStructureEvidenceNames.UndefinedQuantizationTable, evidence.Name);
        Assert.Contains("tabela de quantização 1", evidence.Description);
    }

    [Fact]
    public void UnexpectedMarkers_DepoisDeBytesSoltos_FicaNeutral()
    {
        // O caso da miniatura do EXIF: o SOI repetido é da miniatura, lida por engano.
        var structure = Parse(new JpegBytes().Soi().Header().Raw(0x01, 0x02).Soi().Scan().Eoi());

        var evidence = Assert.Single(JpegStructureFindingRules.UnexpectedMarkers(structure)!.Evidences);
        Assert.Equal(JpegStructureEvidenceNames.RepeatedStartOfImage, evidence.Name);
        Assert.Equal(EvidenceDirection.Neutral, evidence.Direction);
        Assert.Contains("recomeçado dentro de outro segmento", evidence.Description);
    }

    [Fact]
    public void UnexpectedMarkers_JpegComum_NaoGeraAchado()
    {
        Assert.Null(JpegStructureFindingRules.UnexpectedMarkers(Parse(new JpegBytes().Soi().Header().Scan().Eoi())));
    }

    // ---- jpeg.trailing-data ----

    [Fact]
    public void TrailingData_JpegDeclaradoNoMpf_Neutral()
    {
        var structure = Parse(new JpegBytes().Soi().SegmentWith(0xE2, [.. "MPF\0"u8, 0x4D, 0x4D]).Header().Scan().Eoi()
            .Soi().SegmentWith(0xDB, Dqt).Eoi());

        var evidence = Assert.Single(JpegStructureFindingRules.TrailingData(structure)!.Evidences);
        Assert.Equal(JpegStructureEvidenceNames.MpfImage, evidence.Name);
        Assert.Equal(EvidenceDirection.Neutral, evidence.Direction);
    }

    [Fact]
    public void TrailingData_JpegSemMpf_ApoiaManipulacao()
    {
        var structure = Parse(new JpegBytes().Soi().Header().Scan().Eoi().Soi().SegmentWith(0xDB, Dqt).Eoi());

        var evidence = Assert.Single(JpegStructureFindingRules.TrailingData(structure)!.Evidences);
        Assert.Equal(JpegStructureEvidenceNames.AppendedJpeg, evidence.Name);
        Assert.Equal(EvidenceDirection.SupportsManipulation, evidence.Direction);
    }

    [Fact]
    public void TrailingData_VideoMp4_Neutral()
    {
        var structure = Parse(new JpegBytes().Soi().Header().Scan().Eoi()
            .Raw(0x00, 0x00, 0x00, 0x18).Raw([.. "ftypmp42"u8]).Raw(new byte[100]));

        var evidence = Assert.Single(JpegStructureFindingRules.TrailingData(structure)!.Evidences);
        Assert.Equal(JpegStructureEvidenceNames.AppendedVideo, evidence.Name);
        Assert.Equal(EvidenceDirection.Neutral, evidence.Direction);
    }

    [Fact]
    public void TrailingData_Zeros_Neutral()
    {
        var structure = Parse(new JpegBytes().Soi().Header().Scan().Eoi().Raw(new byte[300]));

        var evidence = Assert.Single(JpegStructureFindingRules.TrailingData(structure)!.Evidences);
        Assert.Equal(JpegStructureEvidenceNames.ZeroPadding, evidence.Name);
        Assert.Contains("300 bytes", evidence.Description);
    }

    [Fact]
    public void TrailingData_ConteudoDesconhecido_ApoiaManipulacaoEMostraOInicio()
    {
        var structure = Parse(new JpegBytes().Soi().Header().Scan().Eoi().Raw([.. "PK\x03\x04secret"u8]));

        var evidence = Assert.Single(JpegStructureFindingRules.TrailingData(structure)!.Evidences);
        Assert.Equal(JpegStructureEvidenceNames.UnknownTrailingData, evidence.Name);
        Assert.Equal(EvidenceDirection.SupportsManipulation, evidence.Direction);
        Assert.Contains("504B0304", evidence.Description);
    }

    [Fact]
    public void TrailingData_SemDadosAposOEoi_NaoGeraAchado()
    {
        Assert.Null(JpegStructureFindingRules.TrailingData(Parse(new JpegBytes().Soi().Header().Scan().Eoi())));
    }

    [Fact]
    public void Read_GuardaNoMaximo16BytesDoInicioDosDadosAposOEoi()
    {
        var structure = Parse(new JpegBytes().Soi().Header().Scan().Eoi().Raw(Enumerable.Range(1, 40).Select(b => (byte)b).ToArray()));

        Assert.Equal(40, structure.TrailingDataLength);
        Assert.Equal(Enumerable.Range(1, 16).Select(b => (byte)b), structure.TrailingDataPrefix);
    }

    // ---- jpeg.editor-signatures ----

    [Theory]
    [InlineData("CREATOR: gd-jpeg v1.0 (using IJG JPEG v62), quality = 75", "GD")]
    [InlineData("CREATOR: XV Version 3.10a", "IJG")]
    [InlineData("Created with GIMP", "GIMP")]
    [InlineData("LEAD Technologies Inc. V1.01", "LEADTOOLS")]
    public void EditorSignatures_ComentarioDeEncoder_ApoiaManipulacao(string comment, string encoder)
    {
        var structure = Parse(new JpegBytes().Soi().SegmentWith(0xFE, Encoding.ASCII.GetBytes(comment)).Header().Scan().Eoi());

        var evidence = Assert.Single(JpegStructureFindingRules.EditorSignatures(structure)!.Evidences);
        Assert.Equal(JpegStructureEvidenceNames.EncoderComment, evidence.Name);
        Assert.Equal(EvidenceDirection.SupportsManipulation, evidence.Direction);
        Assert.Contains(encoder, evidence.Description);
        Assert.Contains(comment, evidence.Description);
    }

    [Fact]
    public void EditorSignatures_PhotoshopEAdobe_Registra()
    {
        var structure = Parse(new JpegBytes().Soi()
            .SegmentWith(0xED, [.. "Photoshop 3.0\0"u8, 0x38, 0x42])
            .SegmentWith(0xEE, [.. "Adobe"u8, 0x00, 0x64, 0x00, 0x00, 0x00, 0x00, 0x01])
            .Header().Scan().Eoi());

        Assert.Equal(
            [JpegStructureEvidenceNames.PhotoshopResources, JpegStructureEvidenceNames.AdobeSegment],
            JpegStructureFindingRules.EditorSignatures(structure)!.Evidences.Select(e => e.Name));
    }

    [Fact]
    public void EditorSignatures_ComentarioQualquer_NaoGeraAchado()
    {
        var structure = Parse(new JpegBytes().Soi().SegmentWith(0xFE, [.. "Férias 2026"u8]).Header().Scan().Eoi());

        Assert.Null(JpegStructureFindingRules.EditorSignatures(structure));
    }

    [Fact]
    public void EditorSignatures_ComentarioLongo_CortaOTextoNaDescricao()
    {
        var comment = "CREATOR: " + new string('x', 900);
        var structure = Parse(new JpegBytes().Soi().SegmentWith(0xFE, Encoding.ASCII.GetBytes(comment)).Header().Scan().Eoi());

        var evidence = Assert.Single(JpegStructureFindingRules.EditorSignatures(structure)!.Evidences);
        Assert.EndsWith("…", evidence.Description);
        Assert.True(evidence.Description.Length < 400);
    }

    // ---- jpeg.encoding-profile ----

    [Fact]
    public void EncodingProfile_Baseline420ComTabelasPadrao_DescreveOsFatos()
    {
        var bytes = new JpegBytes().Soi().SegmentWith(0xE0, Jfif)
            .SegmentWith(0xDB, [0x00, .. Enumerable.Repeat((byte)1, 64)])
            .SegmentWith(0xC0, 0x08, 0x01, 0xE0, 0x02, 0x80, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x00, 0x03, 0x11, 0x00)
            .SegmentWith(0xC4, StandardDht(0x00, StandardHuffmanTables.DcLuminance))
            .SegmentWith(0xC4, StandardDht(0x10, StandardHuffmanTables.AcLuminance))
            .SegmentWith(0xDD, 0x00, 0x08)
            .Scan().Eoi();

        var evidences = JpegStructureFindingRules.EncodingProfile(Parse(bytes))!.Evidences.ToDictionary(e => e.Name, e => e.Description);

        Assert.Contains("baseline com Huffman", evidences[JpegStructureEvidenceNames.FrameProcess]);
        Assert.Contains("640 × 480", evidences[JpegStructureEvidenceNames.FrameProcess]);
        Assert.Contains("4:2:0", evidences[JpegStructureEvidenceNames.ChromaSubsampling]);
        Assert.Contains("padrão do Anexo K", evidences[JpegStructureEvidenceNames.HuffmanTables]);
        Assert.Contains("8 blocos", evidences[JpegStructureEvidenceNames.RestartInterval]);
        Assert.Contains("1.01", evidences[JpegStructureEvidenceNames.JfifVersion]);
        Assert.DoesNotContain(JpegStructureEvidenceNames.ProgressiveScans, evidences.Keys);
    }

    [Theory]
    [InlineData(0x11, "4:4:4")]
    [InlineData(0x21, "4:2:2")]
    [InlineData(0x22, "4:2:0")]
    [InlineData(0x12, "4:4:0")]
    [InlineData(0x41, "4:1:1")]
    [InlineData(0x31, "fora dos padrões")]
    public void EncodingProfile_Subamostragem_RotulaPeloFatorDoY(byte ySampling, string expected)
    {
        var bytes = new JpegBytes().Soi().SegmentWith(0xDB, Dqt)
            .SegmentWith(0xC0, 0x08, 0x00, 0x10, 0x00, 0x10, 0x03, 0x01, ySampling, 0x00, 0x02, 0x11, 0x00, 0x03, 0x11, 0x00)
            .Scan().Eoi();

        var evidence = JpegStructureFindingRules.EncodingProfile(Parse(bytes))!.Evidences
            .Single(e => e.Name == JpegStructureEvidenceNames.ChromaSubsampling);

        Assert.Contains(expected, evidence.Description);
    }

    [Fact]
    public void EncodingProfile_Progressive_ContaOsScansEApontaTabelasOtimizadas()
    {
        var bytes = new JpegBytes().Soi().SegmentWith(0xDB, Dqt)
            .SegmentWith(0xC2, 0x08, 0x00, 0x10, 0x00, 0x10, 0x01, 0x01, 0x11, 0x00)
            .SegmentWith(0xC4, 0x00, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x03)
            .Scan().Scan().Scan().Eoi();

        var evidences = JpegStructureFindingRules.EncodingProfile(Parse(bytes))!.Evidences.ToDictionary(e => e.Name, e => e.Description);

        Assert.Contains("progressive", evidences[JpegStructureEvidenceNames.FrameProcess]);
        Assert.Contains("3 scans", evidences[JpegStructureEvidenceNames.ProgressiveScans]);
        Assert.Contains("otimizadas", evidences[JpegStructureEvidenceNames.HuffmanTables]);
        Assert.Contains("tons de cinza", evidences[JpegStructureEvidenceNames.ChromaSubsampling]);
    }

    [Fact]
    public void EncodingProfile_SemDht_ExplicaAsTabelasImplicitas()
    {
        var bytes = new JpegBytes().Soi().SegmentWith(0xDB, Dqt)
            .SegmentWith(0xC0, 0x08, 0x00, 0x10, 0x00, 0x10, 0x01, 0x01, 0x11, 0x00).Scan().Eoi();

        var evidence = JpegStructureFindingRules.EncodingProfile(Parse(bytes))!.Evidences
            .Single(e => e.Name == JpegStructureEvidenceNames.HuffmanTables);

        Assert.Contains("MJPEG", evidence.Description);
    }

    [Fact]
    public void EncodingProfile_AdobeEComentario_Registra()
    {
        var bytes = new JpegBytes().Soi()
            .SegmentWith(0xEE, [.. "Adobe"u8, 0x00, 0x64, 0x00, 0x00, 0x00, 0x00, 0x02])
            .SegmentWith(0xFE, [.. "Férias"u8])
            .Header().Scan().Eoi();

        var evidences = JpegStructureFindingRules.EncodingProfile(Parse(bytes))!.Evidences.ToDictionary(e => e.Name, e => e.Description);

        Assert.Contains("YCCK", evidences[JpegStructureEvidenceNames.AdobeTransform]);
        Assert.Contains(JpegStructureEvidenceNames.Comment, evidences.Keys);
    }

    [Fact]
    public void EncodingProfile_SemFrame_NaoGeraAchado()
    {
        Assert.Null(JpegStructureFindingRules.EncodingProfile(Parse(new JpegBytes().Soi().Segment(0xE0).Eoi())));
    }

    // ---- Tabelas do Anexo K ----

    [Fact]
    public void StandardHuffmanTables_AcCobremTodosOsSimbolosUmaVez()
    {
        // Cada tabela AC tem EOB (0x00), ZRL (0xF0) e um símbolo para cada par (zeros 0–15,
        // tamanho 1–10): 162 no total, sem repetição. Pega erros de digitação nas tabelas.
        var expected = new[] { (byte)0x00, (byte)0xF0 }
            .Concat(from run in Enumerable.Range(0, 16) from size in Enumerable.Range(1, 10) select (byte)(run << 4 | size))
            .Order();

        Assert.Equal(expected, StandardHuffmanTables.AcLuminance.Symbols.Order());
        Assert.Equal(expected, StandardHuffmanTables.AcChrominance.Symbols.Order());
    }

    [Fact]
    public void StandardHuffmanTables_IsStandard_IgnoraOId()
    {
        var table = new HuffmanTable(
            HuffmanTableClass.Ac, 3, StandardHuffmanTables.AcLuminance.Counts, StandardHuffmanTables.AcLuminance.Symbols);

        Assert.True(StandardHuffmanTables.IsStandard(table));
    }

    [Fact]
    public void StandardHuffmanTables_IsStandard_ConsideraAClasse()
    {
        // Mesmas contagens e símbolos da DC de luminância, mas declarada como AC.
        var table = new HuffmanTable(
            HuffmanTableClass.Ac, 0, StandardHuffmanTables.DcLuminance.Counts, StandardHuffmanTables.DcLuminance.Symbols);

        Assert.False(StandardHuffmanTables.IsStandard(table));
    }

    // ---- Fixtures ----

    [Theory]
    [InlineData("camera.jpg")]
    [InlineData("edited.jpg")]
    [InlineData("no-metadata.jpg")]
    public void Evaluate_JpegReal_SoGeraOPerfilDeCodificacao(string fileName)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Metadata", fileName));

        var result = JpegStructureFindingRules.Evaluate(JpegStructureParser.Parse(bytes));

        Assert.Equal(JpegStructureFindingNames.EncodingProfile, Assert.Single(result.Findings).Name);
    }

    private static byte[] StandardDht(byte classAndId, HuffmanTable table) => [classAndId, .. table.Counts, .. table.Symbols];

    private static JpegStructure Parse(JpegBytes bytes) => JpegStructureParser.Parse(bytes.ToArray());

    private static AnalyzerResult Evaluate(JpegBytes bytes) => JpegStructureFindingRules.Evaluate(Parse(bytes));
}
