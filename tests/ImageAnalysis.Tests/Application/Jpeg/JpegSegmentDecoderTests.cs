using System.Text;
using ImageAnalysis.Application.Jpeg;

namespace ImageAnalysis.Tests.Application.Jpeg;

public class JpegSegmentDecoderTests
{
    // Tabela de luminância do Anexo K da T.81 (qualidade 50 da libjpeg), na ordem natural.
    private static readonly ushort[] AnnexKLuminanceNatural =
    [
        16, 11, 10, 16, 24, 40, 51, 61,
        12, 12, 14, 19, 26, 58, 60, 55,
        14, 13, 16, 24, 40, 57, 69, 56,
        14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77,
        24, 35, 55, 64, 81, 104, 113, 92,
        49, 64, 78, 87, 103, 121, 120, 101,
        72, 92, 95, 98, 112, 100, 103, 99,
    ];

    // A mesma tabela como vem no DQT, em zigzag. Escrita à mão, e não calculada pela tabela do
    // decoder, para que o teste não confirme a si mesmo.
    private static readonly byte[] AnnexKLuminanceZigzag =
    [
        16, 11, 12, 14, 12, 10, 16, 14, 13, 14, 18, 17, 16, 19, 24, 40,
        26, 24, 22, 22, 24, 49, 35, 37, 29, 40, 58, 51, 61, 60, 57, 51,
        56, 55, 64, 72, 92, 78, 64, 68, 87, 69, 55, 56, 80, 109, 81, 87,
        95, 98, 103, 104, 103, 62, 77, 113, 121, 112, 100, 120, 92, 101, 103, 99,
    ];

    // Tabela Huffman DC de luminância do Anexo K.
    private static readonly byte[] AnnexKDcLuminanceCounts = [0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] AnnexKDcLuminanceSymbols = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

    // ---- DQT ----

    [Fact]
    public void Parse_Dqt_ConverteDeZigzagParaOrdemNatural()
    {
        var structure = Parse(b => b.SegmentWith(0xDB, [0x00, .. AnnexKLuminanceZigzag]));

        var table = Assert.Single(structure.QuantizationTables);
        Assert.Equal(new QuantizationTable(0, QuantizationPrecision.EightBit, AnnexKLuminanceNatural), table);
        AssertNoIssues(structure);
    }

    [Fact]
    public void Parse_DqtComDuasTabelasNoMesmoSegmento_LeAsDuas()
    {
        var structure = Parse(b => b.SegmentWith(
            0xDB, [0x00, .. AnnexKLuminanceZigzag, 0x01, .. Enumerable.Repeat((byte)99, 64)]));

        Assert.Equal([0, 1], structure.QuantizationTables.Select(t => t.Id));
        Assert.All(structure.QuantizationTables[1].Coefficients, c => Assert.Equal(99, c));
        AssertNoIssues(structure);
    }

    [Fact]
    public void Parse_DqtDe16Bits_LeCoeficientesBigEndian()
    {
        var coefficients = Enumerable.Range(0, 64).SelectMany(_ => new byte[] { 0x01, 0x2C }); // 300

        var structure = Parse(b => b.SegmentWith(0xDB, [0x12, .. coefficients]));

        var table = Assert.Single(structure.QuantizationTables);
        Assert.Equal(2, table.Id);
        Assert.Equal(QuantizationPrecision.SixteenBit, table.Precision);
        Assert.All(table.Coefficients, c => Assert.Equal(300, c));
    }

    [Fact]
    public void Parse_DqtsEmSegmentosSeparados_MantemAOrdemDoArquivo()
    {
        var structure = Parse(b => b
            .SegmentWith(0xDB, [0x01, .. Enumerable.Repeat((byte)2, 64)])
            .SegmentWith(0xDB, [0x00, .. Enumerable.Repeat((byte)1, 64)]));

        Assert.Equal([1, 0], structure.QuantizationTables.Select(t => t.Id));
    }

    [Fact]
    public void Parse_DqtComTabelaIncompleta_LeAsAnterioresERegistraProblema()
    {
        var structure = Parse(b => b.SegmentWith(
            0xDB, [0x00, .. AnnexKLuminanceZigzag, 0x01, .. Enumerable.Repeat((byte)1, 30)]));

        Assert.Single(structure.QuantizationTables);
        AssertMalformed(structure, 0xDB, "incompleta");
    }

    [Fact]
    public void Parse_DqtComPrecisaoInvalida_RegistraProblema()
    {
        var structure = Parse(b => b.SegmentWith(0xDB, [0x20, .. Enumerable.Repeat((byte)1, 64)]));

        Assert.Empty(structure.QuantizationTables);
        AssertMalformed(structure, 0xDB, "precisão inválida");
    }

    [Fact]
    public void Parse_DqtVazio_RegistraProblema()
    {
        var structure = Parse(b => b.SegmentWith(0xDB));

        AssertMalformed(structure, 0xDB, "nenhuma tabela");
    }

    // ---- SOFn ----

    [Fact]
    public void Parse_Sof0Com3Componentes_LeOCabecalhoEOSubsampling()
    {
        // 8 bits, 480 × 640, Y 2×2 tabela 0, Cb e Cr 1×1 tabela 1 (4:2:0).
        var structure = Parse(b => b.SegmentWith(
            0xC0, 0x08, 0x01, 0xE0, 0x02, 0x80, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01));

        var frame = Assert.Single(structure.Frames);
        Assert.Equal(
            new JpegFrameHeader(
                JpegMarker.Sof0, 8, 480, 640,
                [new JpegFrameComponent(1, 2, 2, 0), new JpegFrameComponent(2, 1, 1, 1), new JpegFrameComponent(3, 1, 1, 1)]),
            frame);
        AssertNoIssues(structure);
    }

    [Fact]
    public void Parse_Sof2_GuardaOMarcadorProgressive()
    {
        var structure = Parse(b => b.SegmentWith(0xC2, 0x08, 0x00, 0x10, 0x00, 0x10, 0x01, 0x01, 0x11, 0x00));

        Assert.True(Assert.Single(structure.Frames).IsProgressive);
    }

    [Fact]
    public void Parse_SofSemEspacoParaOsComponentes_NaoCriaOFrame()
    {
        // Declara 3 componentes, mas só traz 1.
        var structure = Parse(b => b.SegmentWith(0xC0, 0x08, 0x00, 0x10, 0x00, 0x10, 0x03, 0x01, 0x11, 0x00));

        Assert.Empty(structure.Frames);
        AssertMalformed(structure, 0xC0, "não tem espaço");
    }

    [Fact]
    public void Parse_SofComBytesAMais_CriaOFrameERegistraProblema()
    {
        var structure = Parse(b => b.SegmentWith(0xC0, 0x08, 0x00, 0x10, 0x00, 0x10, 0x01, 0x01, 0x11, 0x00, 0xAA, 0xBB));

        Assert.Single(structure.Frames);
        AssertMalformed(structure, 0xC0, "2 bytes além");
    }

    [Fact]
    public void Parse_SofMenorQueOCabecalho_RegistraProblema()
    {
        var structure = Parse(b => b.SegmentWith(0xC0, 0x08, 0x00));

        Assert.Empty(structure.Frames);
        AssertMalformed(structure, 0xC0, "incompleto");
    }

    // ---- DHT ----

    [Fact]
    public void Parse_Dht_GuardaContagensESimbolos()
    {
        var structure = Parse(b => b.SegmentWith(0xC4, [0x00, .. AnnexKDcLuminanceCounts, .. AnnexKDcLuminanceSymbols]));

        var table = Assert.Single(structure.HuffmanTables);
        Assert.Equal(
            new HuffmanTable(HuffmanTableClass.Dc, 0, AnnexKDcLuminanceCounts, AnnexKDcLuminanceSymbols),
            table);
        AssertNoIssues(structure);
    }

    [Fact]
    public void Parse_DhtComDuasTabelas_LeAsDuas()
    {
        byte[] acCounts = [0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        var structure = Parse(b => b.SegmentWith(
            0xC4, [0x00, .. AnnexKDcLuminanceCounts, .. AnnexKDcLuminanceSymbols, 0x11, .. acCounts, 0x01, 0x02]));

        Assert.Equal(
            [(HuffmanTableClass.Dc, (byte)0), (HuffmanTableClass.Ac, (byte)1)],
            structure.HuffmanTables.Select(t => (t.Class, t.Id)));
    }

    [Fact]
    public void Parse_DhtComMenosSimbolosQueAsContagens_RegistraProblema()
    {
        var structure = Parse(b => b.SegmentWith(0xC4, [0x00, .. AnnexKDcLuminanceCounts, 0, 1, 2]));

        Assert.Empty(structure.HuffmanTables);
        AssertMalformed(structure, 0xC4, "12 símbolos");
    }

    [Fact]
    public void Parse_DhtSemAsContagens_RegistraProblema()
    {
        var structure = Parse(b => b.SegmentWith(0xC4, 0x00, 0x01, 0x02));

        AssertMalformed(structure, 0xC4, "incompleta");
    }

    [Fact]
    public void Parse_DhtComClasseInvalida_RegistraProblema()
    {
        var structure = Parse(b => b.SegmentWith(0xC4, [0x20, .. AnnexKDcLuminanceCounts, .. AnnexKDcLuminanceSymbols]));

        AssertMalformed(structure, 0xC4, "classe inválida");
    }

    // ---- DRI ----

    [Fact]
    public void Parse_Dri_LeOIntervalo()
    {
        var structure = Parse(b => b.SegmentWith(0xDD, 0x01, 0x40));

        Assert.Equal([320], structure.RestartIntervals);
        AssertNoIssues(structure);
    }

    [Theory]
    [InlineData(new byte[] { 0x01 }, new ushort[0])]
    [InlineData(new byte[] { 0x00, 0x10, 0xFF }, new ushort[] { 16 })]
    public void Parse_DriComTamanhoDiferenteDe2_RegistraProblema(byte[] payload, ushort[] expected)
    {
        var structure = Parse(b => b.SegmentWith(0xDD, payload));

        Assert.Equal(expected, structure.RestartIntervals);
        AssertMalformed(structure, 0xDD, "2 bytes");
    }

    // ---- SOS ----

    [Fact]
    public void Parse_Sos_LeOCabecalhoDoScan()
    {
        var builder = new JpegBytes().Soi();
        var sosOffset = builder.Position;
        var bytes = builder
            .SegmentWith(0xDA, 0x03, 0x01, 0x00, 0x02, 0x11, 0x03, 0x11, 0x00, 0x3F, 0x00)
            .Raw(0x12, 0x34).Eoi().ToArray();

        var structure = JpegStructureParser.Parse(bytes);

        var scan = Assert.Single(structure.Scans);
        Assert.Equal(
            new JpegScanHeader(
                sosOffset,
                [new JpegScanComponent(1, 0, 0), new JpegScanComponent(2, 1, 1), new JpegScanComponent(3, 1, 1)],
                0, 63, 0, 0),
            scan);
        AssertNoIssues(structure);
    }

    [Fact]
    public void Parse_ProgressiveComVariosScans_LeTodosNaOrdem()
    {
        var bytes = new JpegBytes().Soi()
            .SegmentWith(0xDA, 0x01, 0x01, 0x00, 0x00, 0x00, 0x01).Raw(0x10)  // DC, Al = 1
            .SegmentWith(0xDA, 0x01, 0x01, 0x00, 0x01, 0x05, 0x02).Raw(0x20)  // AC 1–5, Al = 2
            .SegmentWith(0xDA, 0x01, 0x01, 0x00, 0x06, 0x3F, 0x21).Raw(0x30)  // AC 6–63, Ah = 2, Al = 1
            .Eoi().ToArray();

        var structure = JpegStructureParser.Parse(bytes);

        Assert.Equal(
            [(0, 0, 0, 1), (1, 5, 0, 2), (6, 63, 2, 1)],
            structure.Scans.Select(s => ((int)s.SpectralStart, (int)s.SpectralEnd,
                (int)s.SuccessiveApproximationHigh, (int)s.SuccessiveApproximationLow)));
    }

    [Fact]
    public void Parse_SosSemEspacoParaOsComponentes_RegistraProblema()
    {
        var bytes = new JpegBytes().Soi().SegmentWith(0xDA, 0x03, 0x01, 0x00).Raw(0x12).Eoi().ToArray();

        var structure = JpegStructureParser.Parse(bytes);

        Assert.Empty(structure.Scans);
        AssertMalformed(structure, 0xDA, "não tem espaço");
    }

    // ---- APPn ----

    [Fact]
    public void Parse_App0Jfif_LeOCabecalho()
    {
        // JFIF 1.01, dpi, 72 × 72, sem miniatura.
        var structure = Parse(b => b.SegmentWith(0xE0, [.. "JFIF\0"u8, 0x01, 0x01, 0x01, 0x00, 0x48, 0x00, 0x48, 0x00, 0x00]));

        var app = Assert.Single(structure.ApplicationSegments);
        Assert.Equal(JpegApplicationKind.Jfif, app.Kind);
        Assert.Equal("JFIF", app.Identifier);
        Assert.Equal(new JfifHeader(1, 1, 1, 72, 72, 0, 0), app.Jfif);
        AssertNoIssues(structure);
    }

    [Fact]
    public void Parse_App0JfifIncompleto_IdentificaERegistraProblema()
    {
        var structure = Parse(b => b.SegmentWith(0xE0, [.. "JFIF\0"u8, 0x01, 0x01]));

        var app = Assert.Single(structure.ApplicationSegments);
        Assert.Equal(JpegApplicationKind.Jfif, app.Kind);
        Assert.Null(app.Jfif);
        AssertMalformed(structure, 0xE0, "JFIF");
    }

    [Theory]
    [InlineData(0xE0, "JFXX\0", JpegApplicationKind.JfifExtension, "JFXX")]
    [InlineData(0xE1, "Exif\0\0", JpegApplicationKind.Exif, "Exif")]
    [InlineData(0xE1, "http://ns.adobe.com/xap/1.0/\0", JpegApplicationKind.Xmp, "http://ns.adobe.com/xap/1.0/")]
    [InlineData(0xE1, "http://ns.adobe.com/xmp/extension/\0", JpegApplicationKind.ExtendedXmp, "http://ns.adobe.com/xmp/extension/")]
    [InlineData(0xE2, "MPF\0", JpegApplicationKind.Mpf, "MPF")]
    [InlineData(0xED, "Photoshop 3.0\0", JpegApplicationKind.Photoshop, "Photoshop 3.0")]
    public void Parse_AppnConhecido_IdentificaOTipo(byte code, string prefix, JpegApplicationKind kind, string identifier)
    {
        var structure = Parse(b => b.SegmentWith(code, [.. Encoding.ASCII.GetBytes(prefix), 0x4D, 0x4D, 0x00, 0x2A]));

        var app = Assert.Single(structure.ApplicationSegments);
        Assert.Equal(kind, app.Kind);
        Assert.Equal(identifier, app.Identifier);
        Assert.Equal((JpegMarker)code, app.Marker);
        AssertNoIssues(structure);
    }

    [Fact]
    public void Parse_App2Icc_LeOPedaco()
    {
        var structure = Parse(b => b.SegmentWith(0xE2, [.. "ICC_PROFILE\0"u8, 0x02, 0x03, 0x00, 0x00]));

        var app = Assert.Single(structure.ApplicationSegments);
        Assert.Equal(JpegApplicationKind.IccProfile, app.Kind);
        Assert.Equal(new IccProfileChunk(2, 3), app.IccProfileChunk);
    }

    [Fact]
    public void Parse_App14Adobe_LeOCabecalho()
    {
        // Versão 100, flags 0, transform 1 (YCbCr).
        var structure = Parse(b => b.SegmentWith(0xEE, [.. "Adobe"u8, 0x00, 0x64, 0x00, 0x00, 0x00, 0x00, 0x01]));

        var app = Assert.Single(structure.ApplicationSegments);
        Assert.Equal(JpegApplicationKind.Adobe, app.Kind);
        Assert.Equal("Adobe", app.Identifier);
        Assert.Equal(new AdobeHeader(100, 0, 0, 1), app.Adobe);
    }

    [Fact]
    public void Parse_AssinaturaNoMarcadorErrado_EhDesconhecido()
    {
        // "Exif" só é reconhecido no APP1.
        var structure = Parse(b => b.SegmentWith(0xE0, [.. "Exif\0\0"u8, 0x00]));

        var app = Assert.Single(structure.ApplicationSegments);
        Assert.Equal(JpegApplicationKind.Unknown, app.Kind);
        Assert.Equal("Exif", app.Identifier);
    }

    [Fact]
    public void Parse_AppnDesconhecido_GuardaOIdentificador()
    {
        var structure = Parse(b => b.SegmentWith(0xEC, [.. "Ducky"u8, 0x00, 0x01, 0x00]));

        var app = Assert.Single(structure.ApplicationSegments);
        Assert.Equal(JpegApplicationKind.Unknown, app.Kind);
        Assert.Equal("Ducky", app.Identifier);
        AssertNoIssues(structure);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x00, 0x01, 0x02 })]
    public void Parse_AppnSemTextoNoInicio_IdentificadorNulo(byte[] payload)
    {
        var structure = Parse(b => b.SegmentWith(0xE5, payload));

        Assert.Null(Assert.Single(structure.ApplicationSegments).Identifier);
    }

    [Fact]
    public void Parse_AppnComTextoLongo_LimitaOIdentificador()
    {
        var structure = Parse(b => b.SegmentWith(0xE5, Enumerable.Repeat((byte)'A', 200).ToArray()));

        Assert.Equal(JpegApplicationSegment.MaxIdentifierLength, Assert.Single(structure.ApplicationSegments).Identifier!.Length);
    }

    // ---- COM ----

    [Fact]
    public void Parse_Com_GuardaOTextoSemOsNulosDoFim()
    {
        const string text = "CREATOR: gd-jpeg v1.0 (using IJG JPEG v62), quality = 75";

        var structure = Parse(b => b.SegmentWith(0xFE, [.. Encoding.ASCII.GetBytes(text), 0x00]));

        var comment = Assert.Single(structure.Comments);
        Assert.Equal(text, comment.Text);
        Assert.Equal(text.Length + 1, comment.DataLength);
        Assert.False(comment.IsTruncated);
    }

    [Fact]
    public void Parse_ComLatin1_DecodificaSemErro()
    {
        var structure = Parse(b => b.SegmentWith(0xFE, 0x43, 0xE9, 0x75)); // "Céu" em Latin-1

        Assert.Equal("Céu", Assert.Single(structure.Comments).Text);
    }

    [Fact]
    public void Parse_ComMaiorQueOLimite_CortaOTexto()
    {
        var structure = Parse(b => b.SegmentWith(0xFE, Enumerable.Repeat((byte)'x', 5000).ToArray()));

        var comment = Assert.Single(structure.Comments);
        Assert.Equal(JpegComment.MaxTextLength, comment.Text.Length);
        Assert.Equal(5000, comment.DataLength);
        Assert.True(comment.IsTruncated);
    }

    // ---- Integração com a leitura ----

    [Fact]
    public void Parse_SegmentoComComprimentoQuePassaDoFim_NaoEhInterpretado()
    {
        var bytes = new JpegBytes().Soi().SegmentHeader(0xDB, 1000).Raw(0x00, 0x10, 0x20).ToArray();

        var structure = JpegStructureParser.Parse(bytes);

        Assert.Empty(structure.QuantizationTables);
        Assert.Equal(JpegReadIssueKind.InvalidSegmentLength, Assert.Single(structure.Issues).Kind);
    }

    [Fact]
    public void Parse_MantemOsDadosDaLeituraEOsProblemasDelaPrimeiro()
    {
        var bytes = new JpegBytes().Soi()
            .Segment(0x42, 4)                // UnknownMarker, da leitura
            .SegmentWith(0xDD, 0x01)         // MalformedSegment, da interpretação
            .Scan().Eoi().Raw(0x01, 0x02).ToArray();

        var read = JpegSegmentReader.Read(bytes);
        var structure = JpegStructureParser.Parse(bytes);

        Assert.Equal(read.Segments, structure.Segments);
        Assert.Equal(read.EndOfImageOffset, structure.EndOfImageOffset);
        Assert.Equal(2, structure.TrailingDataLength);
        Assert.Equal(
            [JpegReadIssueKind.UnknownMarker, JpegReadIssueKind.MalformedSegment],
            structure.Issues.Select(i => i.Kind));
    }

    [Fact]
    public void Parse_BytesAleatorios_NuncaLancaExcecao()
    {
        var random = new Random(23);
        byte[] markers = [0xDB, 0xC0, 0xC2, 0xC4, 0xDD, 0xDA, 0xE0, 0xE1, 0xE2, 0xEE, 0xFE];

        for (var i = 0; i < 3000; i++)
        {
            // Segmentos com comprimento correto e conteúdo aleatório, para exercitar o decoder.
            var builder = new JpegBytes().Soi();
            for (var s = 0; s < random.Next(1, 6); s++)
            {
                var payload = new byte[random.Next(0, 200)];
                random.NextBytes(payload);
                builder.SegmentWith(markers[random.Next(markers.Length)], payload);
            }

            JpegStructureParser.Parse(builder.Eoi().ToArray());
        }
    }

    private static JpegStructure Parse(Func<JpegBytes, JpegBytes> segments) =>
        JpegStructureParser.Parse(segments(new JpegBytes().Soi()).Eoi().ToArray());

    private static void AssertNoIssues(JpegStructure structure) => Assert.Empty(structure.Issues);

    private static void AssertMalformed(JpegStructure structure, byte markerCode, string descriptionFragment)
    {
        var issue = Assert.Single(structure.Issues);
        Assert.Equal(JpegReadIssueKind.MalformedSegment, issue.Kind);
        Assert.Equal(markerCode, issue.MarkerCode);
        Assert.Contains(descriptionFragment, issue.Description);
    }
}
