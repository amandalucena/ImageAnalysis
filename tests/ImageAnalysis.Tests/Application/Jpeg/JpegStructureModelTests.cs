using ImageAnalysis.Application.Jpeg;

namespace ImageAnalysis.Tests.Application.Jpeg;

public class JpegStructureModelTests
{
    // ---- JpegSegment ----

    [Fact]
    public void JpegSegment_MarcadorConhecido_ExpoeOMarcador()
    {
        var segment = new JpegSegment(0xDB, offset: 20, length: 67);

        Assert.Equal(JpegMarker.Dqt, segment.Marker);
        Assert.Equal(20, segment.Offset);
        Assert.Equal(67, segment.Length);
    }

    [Fact]
    public void JpegSegment_MarcadorDesconhecido_GuardaOCodigoSemMarcador()
    {
        var segment = new JpegSegment(0x42, offset: 100, length: 10);

        Assert.Equal(0x42, segment.MarkerCode);
        Assert.Null(segment.Marker);
    }

    [Theory]
    [InlineData(0xD8)]
    [InlineData(0xD9)]
    [InlineData(0xD3)]
    [InlineData(0x01)]
    public void JpegSegment_MarcadorSemDadosComComprimento_LancaExcecao(byte code)
    {
        Assert.Throws<ArgumentException>(() => new JpegSegment(code, offset: 0, length: 2));
    }

    [Fact]
    public void JpegSegment_ComprimentoInvalidoNoArquivo_EhAceito()
    {
        // 0 e 1 são inválidos na T.81, mas descrevem o que foi lido; o leitor registra o problema.
        var segment = new JpegSegment(0xE1, offset: 2, length: 1);

        Assert.Equal(1, segment.Length);
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(0, -1)]
    [InlineData(0, 65536)]
    public void JpegSegment_PosicaoOuComprimentoForaDoIntervalo_LancaExcecao(long offset, int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JpegSegment(0xE0, offset, length));
    }

    // ---- JpegFrameHeader ----

    [Theory]
    [InlineData(JpegMarker.Sof0, true, false, false, false, false)]
    [InlineData(JpegMarker.Sof1, false, false, false, false, false)]
    [InlineData(JpegMarker.Sof2, false, true, false, false, false)]
    [InlineData(JpegMarker.Sof3, false, false, true, false, false)]
    [InlineData(JpegMarker.Sof6, false, true, false, true, false)]
    [InlineData(JpegMarker.Sof9, false, false, false, false, true)]
    [InlineData(JpegMarker.Sof10, false, true, false, false, true)]
    [InlineData(JpegMarker.Sof15, false, false, true, true, true)]
    public void JpegFrameHeader_TipoDeProcesso_SegueOMarcador(
        JpegMarker marker, bool baseline, bool progressive, bool lossless, bool hierarchical, bool arithmetic)
    {
        var frame = Frame(marker);

        Assert.Equal(baseline, frame.IsBaseline);
        Assert.Equal(progressive, frame.IsProgressive);
        Assert.Equal(lossless, frame.IsLossless);
        Assert.Equal(hierarchical, frame.IsHierarchical);
        Assert.Equal(arithmetic, frame.IsArithmetic);
    }

    [Theory]
    [InlineData(JpegMarker.Dht)]
    [InlineData(JpegMarker.Dac)]
    [InlineData(JpegMarker.Jpg)]
    [InlineData(JpegMarker.Sos)]
    public void JpegFrameHeader_MarcadorQueNaoEhSof_LancaExcecao(JpegMarker marker)
    {
        Assert.Throws<ArgumentException>(() => Frame(marker));
    }

    [Fact]
    public void JpegFrameHeader_AlturaZero_EhAceita()
    {
        // A altura pode vir depois, num segmento DNL.
        var frame = new JpegFrameHeader(JpegMarker.Sof0, 8, height: 0, width: 640, [Component(1)]);

        Assert.Equal(0, frame.Height);
    }

    [Fact]
    public void JpegFrameHeader_ComponenteNulo_LancaExcecao()
    {
        Assert.Throws<ArgumentException>(() =>
            new JpegFrameHeader(JpegMarker.Sof0, 8, 480, 640, [Component(1), null!]));
    }

    [Fact]
    public void JpegFrameHeader_MesmosValores_SaoIguais()
    {
        Assert.Equal(Frame(JpegMarker.Sof0), Frame(JpegMarker.Sof0));
        Assert.Equal(Frame(JpegMarker.Sof0).GetHashCode(), Frame(JpegMarker.Sof0).GetHashCode());
        Assert.NotEqual(Frame(JpegMarker.Sof0), Frame(JpegMarker.Sof2));
    }

    [Theory]
    [InlineData(16, 1)]
    [InlineData(1, 16)]
    public void JpegFrameComponent_AmostragemAcimaDe4Bits_LancaExcecao(byte horizontal, byte vertical)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JpegFrameComponent(1, horizontal, vertical, 0));
    }

    // ---- QuantizationTable ----

    [Fact]
    public void QuantizationTable_64Coeficientes_MantemValores()
    {
        var coefficients = Enumerable.Range(1, 64).Select(c => (ushort)c).ToArray();

        var table = new QuantizationTable(0, QuantizationPrecision.EightBit, coefficients);

        Assert.Equal(coefficients, table.Coefficients);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(65)]
    public void QuantizationTable_QuantidadeDiferenteDe64_LancaExcecao(int count)
    {
        Assert.Throws<ArgumentException>(() =>
            new QuantizationTable(0, QuantizationPrecision.EightBit, Enumerable.Repeat((ushort)1, count)));
    }

    [Fact]
    public void QuantizationTable_OitoBitsComCoeficienteAcimaDe255_LancaExcecao()
    {
        var coefficients = Enumerable.Repeat((ushort)1, 63).Append((ushort)256);

        Assert.Throws<ArgumentException>(() => new QuantizationTable(0, QuantizationPrecision.EightBit, coefficients));
    }

    [Fact]
    public void QuantizationTable_DezesseisBits_AceitaCoeficientesAcimaDe255()
    {
        var coefficients = Enumerable.Repeat((ushort)1000, 64);

        var table = new QuantizationTable(1, QuantizationPrecision.SixteenBit, coefficients);

        Assert.All(table.Coefficients, c => Assert.Equal(1000, c));
    }

    [Fact]
    public void QuantizationTable_IdAcimaDe4Bits_LancaExcecao()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new QuantizationTable(16, QuantizationPrecision.EightBit, Enumerable.Repeat((ushort)1, 64)));
    }

    [Fact]
    public void QuantizationTable_MesmosCoeficientes_SaoIguais()
    {
        var a = new QuantizationTable(0, QuantizationPrecision.EightBit, Enumerable.Repeat((ushort)2, 64));
        var b = new QuantizationTable(0, QuantizationPrecision.EightBit, Enumerable.Repeat((ushort)2, 64));
        var c = new QuantizationTable(0, QuantizationPrecision.EightBit, Enumerable.Repeat((ushort)3, 64));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void QuantizationTable_CopiaOsCoeficientes()
    {
        var coefficients = Enumerable.Repeat((ushort)1, 64).ToArray();
        var table = new QuantizationTable(0, QuantizationPrecision.EightBit, coefficients);

        coefficients[0] = 99;

        Assert.Equal(1, table.Coefficients[0]);
    }

    // ---- JpegStructure ----

    [Fact]
    public void JpegStructure_ComEoi_InformaPosicaoEDadosAposOFim()
    {
        var structure = Structure(endOfImageOffset: 5000, trailingDataLength: 128);

        Assert.True(structure.HasEndOfImage);
        Assert.Equal(5000, structure.EndOfImageOffset);
        Assert.Equal(128, structure.TrailingDataLength);
    }

    [Fact]
    public void JpegStructure_SemEoi_NaoTemDadosAposOFim()
    {
        var structure = Structure(endOfImageOffset: null, trailingDataLength: 0);

        Assert.False(structure.HasEndOfImage);
    }

    [Fact]
    public void JpegStructure_SemEoiComDadosAposOFim_LancaExcecao()
    {
        Assert.Throws<ArgumentException>(() => Structure(endOfImageOffset: null, trailingDataLength: 10));
    }

    [Theory]
    [InlineData(-1L, 0L)]
    [InlineData(100L, -1L)]
    public void JpegStructure_ValoresNegativos_LancaExcecao(long endOfImageOffset, long trailingDataLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Structure(endOfImageOffset, trailingDataLength));
    }

    [Fact]
    public void JpegStructure_ListaComNulo_LancaExcecao()
    {
        Assert.Throws<ArgumentException>(() =>
            new JpegStructure([null!], [], [], endOfImageOffset: null, trailingDataLength: 0, []));
    }

    // ---- JpegReadIssue ----

    [Fact]
    public void JpegReadIssue_ValoresValidos_MantemValores()
    {
        var issue = new JpegReadIssue(JpegReadIssueKind.TruncatedFile, 1234, "O arquivo terminou no meio do segmento DQT.");

        Assert.Equal(JpegReadIssueKind.TruncatedFile, issue.Kind);
        Assert.Equal(1234, issue.Offset);
    }

    [Fact]
    public void JpegReadIssue_ComQuantidadeEMarcador_MantemValores()
    {
        var issue = new JpegReadIssue(
            JpegReadIssueKind.UnexpectedData, 500, "5 bytes ignorados antes do marcador 0xFFDB.", length: 5, markerCode: 0xDB);

        Assert.Equal(5, issue.Length);
        Assert.Equal((byte)0xDB, issue.MarkerCode);
    }

    [Fact]
    public void JpegReadIssue_SemQuantidadeEMarcador_FicamNulos()
    {
        var issue = new JpegReadIssue(JpegReadIssueKind.TruncatedFile, 0, "x");

        Assert.Null(issue.Length);
        Assert.Null(issue.MarkerCode);
    }

    [Fact]
    public void JpegReadIssue_QuantidadeNegativa_LancaExcecao()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new JpegReadIssue(JpegReadIssueKind.UnexpectedData, 0, "x", length: -1));
    }

    [Fact]
    public void JpegReadIssue_TipoIndefinido_LancaExcecao()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JpegReadIssue((JpegReadIssueKind)99, 0, "x"));
    }

    [Fact]
    public void JpegReadIssue_SemDescricao_LancaExcecao()
    {
        Assert.Throws<ArgumentException>(() => new JpegReadIssue(JpegReadIssueKind.UnknownMarker, 0, " "));
    }

    // ---- HuffmanTable ----

    [Fact]
    public void HuffmanTable_SimbolosDiferentesDaSomaDasContagens_LancaExcecao()
    {
        byte[] counts = [0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        Assert.Throws<ArgumentException>(() => new HuffmanTable(HuffmanTableClass.Dc, 0, counts, [1]));
    }

    [Fact]
    public void HuffmanTable_QuantidadeDeContagensDiferenteDe16_LancaExcecao()
    {
        Assert.Throws<ArgumentException>(() => new HuffmanTable(HuffmanTableClass.Ac, 0, new byte[15], []));
    }

    [Fact]
    public void HuffmanTable_MesmosValores_SaoIguais()
    {
        byte[] counts = [0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        var a = new HuffmanTable(HuffmanTableClass.Ac, 1, counts, [1, 2]);
        var b = new HuffmanTable(HuffmanTableClass.Ac, 1, counts.ToArray(), [1, 2]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new HuffmanTable(HuffmanTableClass.Ac, 1, counts, [1, 3]));
    }

    // ---- JpegScanHeader ----

    [Fact]
    public void JpegScanHeader_MesmosValores_SaoIguais()
    {
        JpegScanHeader Scan() => new(10, [new JpegScanComponent(1, 0, 0)], 0, 63, 0, 0);

        Assert.Equal(Scan(), Scan());
        Assert.Equal(Scan().GetHashCode(), Scan().GetHashCode());
    }

    [Fact]
    public void JpegScanHeader_AproximacaoAcimaDe4Bits_LancaExcecao()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JpegScanHeader(0, [], 0, 63, 16, 0));
    }

    // ---- JpegApplicationSegment ----

    [Fact]
    public void JpegApplicationSegment_MarcadorQueNaoEhApp_LancaExcecao()
    {
        Assert.Throws<ArgumentException>(() =>
            new JpegApplicationSegment(JpegMarker.Com, 0, 10, JpegApplicationKind.Unknown, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 65
    public void JpegApplicationSegment_IdentificadorVazioOuLongo_LancaExcecao(string identifier)
    {
        Assert.Throws<ArgumentException>(() =>
            new JpegApplicationSegment(JpegMarker.App1, 0, 10, JpegApplicationKind.Unknown, identifier));
    }

    // ---- JpegComment ----

    [Fact]
    public void JpegComment_TextoAcimaDoLimite_LancaExcecao()
    {
        Assert.Throws<ArgumentException>(() => new JpegComment(0, new string('x', JpegComment.MaxTextLength + 1), 5000));
    }

    [Theory]
    [InlineData(1024, false)]
    [InlineData(1025, true)]
    public void JpegComment_IsTruncated_SegueOTamanhoNoArquivo(int dataLength, bool truncated)
    {
        Assert.Equal(truncated, new JpegComment(0, "x", dataLength).IsTruncated);
    }

    private static JpegFrameComponent Component(byte id) => new(id, 1, 1, 0);

    private static JpegFrameHeader Frame(JpegMarker marker) =>
        new(marker, 8, 480, 640, [new JpegFrameComponent(1, 2, 2, 0), Component(2), Component(3)]);

    private static JpegStructure Structure(long? endOfImageOffset, long trailingDataLength) =>
        new([new JpegSegment(0xD8, 0, 0)], [], [], endOfImageOffset, trailingDataLength, []);
}
