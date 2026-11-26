using ImageAnalysis.Application.Jpeg;

namespace ImageAnalysis.Tests.Application.Jpeg;

public class JpegSegmentReaderTests
{
    // ---- Arquivos válidos ----

    [Fact]
    public void Read_JpegBaseline_RegistraOsSegmentosNaOrdemComPosicaoEComprimento()
    {
        var bytes = new JpegBytes().Soi().Header().Scan(8).Eoi().ToArray();

        var structure = JpegSegmentReader.Read(bytes);

        Assert.Equal(
            [
                new JpegSegment(0xD8, 0, 0),
                new JpegSegment(0xE0, 2, 16),
                new JpegSegment(0xDB, 20, 67),
                new JpegSegment(0xC0, 89, 17),
                new JpegSegment(0xC4, 108, 31),
                new JpegSegment(0xDA, 141, 12),
                new JpegSegment(0xD9, 163, 0),
            ],
            structure.Segments);

        Assert.Equal(163, structure.EndOfImageOffset);
        Assert.Equal(0, structure.TrailingDataLength);
        Assert.Empty(structure.Issues);
    }

    [Fact]
    public void Read_NaoDecodificaOConteudoDosSegmentos()
    {
        var structure = JpegSegmentReader.Read(new JpegBytes().Soi().Header().Scan().Eoi().ToArray());

        Assert.Empty(structure.Frames);
        Assert.Empty(structure.QuantizationTables);
    }

    [Fact]
    public void Read_ScanComStuffingERst_NaoViramSegmentos()
    {
        var bytes = new JpegBytes().Soi().Header().Scan(4)
            .Raw(0xFF, 0x00, 0x12, 0xFF, 0xD0, 0x34, 0xFF, 0x00, 0xFF, 0xD7, 0x56)
            .Eoi().ToArray();

        var structure = JpegSegmentReader.Read(bytes);

        Assert.Equal(JpegMarker.Eoi, structure.Segments[^1].Marker);
        Assert.DoesNotContain(structure.Segments, s => JpegMarkers.IsRestart(s.MarkerCode));
        Assert.True(structure.HasEndOfImage);
        Assert.Empty(structure.Issues);
    }

    [Fact]
    public void Read_ScanComPreenchimentoAntesDoEoi_EncontraOEoi()
    {
        var builder = new JpegBytes().Soi().Header().Scan(4).Raw(0xFF, 0xFF);
        var eoiOffset = builder.Position;
        var structure = JpegSegmentReader.Read(builder.Eoi().ToArray());

        // O preenchimento faz parte do scan; o EOI fica no último 0xFF.
        Assert.Equal(eoiOffset, structure.EndOfImageOffset);
        Assert.Empty(structure.Issues);
    }

    [Fact]
    public void Read_Progressive_LeVariosScansComTabelasEntreEles()
    {
        var bytes = new JpegBytes().Soi()
            .Segment(0xDB, 65).Segment(0xC2, 15)
            .Segment(0xC4, 29).Scan(6)
            .Segment(0xC4, 29).Scan(6)
            .Scan(6)
            .Eoi().ToArray();

        var structure = JpegSegmentReader.Read(bytes);

        Assert.Equal(
            [0xD8, 0xDB, 0xC2, 0xC4, 0xDA, 0xC4, 0xDA, 0xDA, 0xD9],
            structure.Segments.Select(s => s.MarkerCode));
        Assert.Empty(structure.Issues);
    }

    [Fact]
    public void Read_PreenchimentoEntreSegmentos_MarcadorFicaNoUltimo0xFF()
    {
        var builder = new JpegBytes().Soi().Raw(0xFF, 0xFF);
        var app0Offset = builder.Position;
        var structure = JpegSegmentReader.Read(builder.Segment(0xE0).Scan().Eoi().ToArray());

        Assert.Equal(app0Offset, structure.Segments[1].Offset);
        Assert.Equal(JpegMarker.App0, structure.Segments[1].Marker);
        Assert.Empty(structure.Issues);
    }

    [Fact]
    public void Read_DadosAposOEoi_InformaOTamanhoSemLerOConteudo()
    {
        // Os bytes depois do EOI parecem outro JPEG (como no MPF), mas não são lidos.
        var bytes = new JpegBytes().Soi().Header().Scan().Eoi()
            .Soi().Segment(0xDB, 65).Eoi().ToArray();

        var structure = JpegSegmentReader.Read(bytes);

        Assert.Equal(73, structure.TrailingDataLength);
        Assert.Single(structure.Segments, s => s.Marker == JpegMarker.Eoi);
        Assert.Empty(structure.Issues);
    }

    [Fact]
    public void Read_EoiSemScan_TerminaNoEoi()
    {
        var structure = JpegSegmentReader.Read(new JpegBytes().Soi().Segment(0xE0).Eoi().ToArray());

        Assert.Equal([0xD8, 0xE0, 0xD9], structure.Segments.Select(s => s.MarkerCode));
        Assert.True(structure.HasEndOfImage);
        Assert.Empty(structure.Issues);
    }

    [Fact]
    public void Read_SoiRepetidoERstForaDoScan_SaoRegistradosSemProblema()
    {
        // Avaliar se isso é anômalo é papel das regras (22.4).
        var bytes = new JpegBytes().Soi().Soi().Marker(0xD3).Segment(0xE0).Scan().Eoi().ToArray();

        var structure = JpegSegmentReader.Read(bytes);

        Assert.Equal([0xD8, 0xD8, 0xD3, 0xE0, 0xDA, 0xD9], structure.Segments.Select(s => s.MarkerCode));
        Assert.Empty(structure.Issues);
    }

    // ---- Início do arquivo ----

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xFF })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })] // PNG
    [InlineData(new byte[] { 0x00, 0xFF, 0xD8, 0xFF, 0xD9 })]
    public void Read_SemSoiNoInicio_RegistraOProblemaEPara(byte[] bytes)
    {
        var structure = JpegSegmentReader.Read(bytes);

        var issue = Assert.Single(structure.Issues);
        Assert.Equal(JpegReadIssueKind.MissingStartOfImage, issue.Kind);
        Assert.Equal(0, issue.Offset);
        Assert.Empty(structure.Segments);
        Assert.False(structure.HasEndOfImage);
    }

    // ---- Arquivo truncado ----

    [Fact]
    public void Read_SoSoi_TerminaSemEoi()
    {
        var structure = JpegSegmentReader.Read([0xFF, 0xD8]);

        var issue = Assert.Single(structure.Issues);
        Assert.Equal(JpegReadIssueKind.TruncatedFile, issue.Kind);
        Assert.Equal(2, issue.Offset);
        Assert.False(structure.HasEndOfImage);
    }

    [Fact]
    public void Read_TerminaNoMeioDeUmMarcador_RegistraTruncado()
    {
        var bytes = new JpegBytes().Soi().Segment(0xE0).Raw(0xFF).ToArray();

        var structure = JpegSegmentReader.Read(bytes);

        var issue = Assert.Single(structure.Issues);
        Assert.Equal(JpegReadIssueKind.TruncatedFile, issue.Kind);
        Assert.Equal(bytes.Length - 1, issue.Offset);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 })]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE1, 0x00 })]
    public void Read_TerminaNoCampoDeComprimento_RegistraTruncado(byte[] bytes)
    {
        var structure = JpegSegmentReader.Read(bytes);

        var issue = Assert.Single(structure.Issues);
        Assert.Equal(JpegReadIssueKind.TruncatedFile, issue.Kind);
        Assert.Equal(2, issue.Offset);
        Assert.Equal([0xD8], structure.Segments.Select(s => s.MarkerCode));
    }

    [Fact]
    public void Read_TerminaDentroDosDadosComprimidos_RegistraTruncadoSemEoi()
    {
        var builder = new JpegBytes().Soi().Header().Segment(0xDA, 10);
        var scanStart = builder.Position;
        var bytes = builder.Raw(0x12, 0x34, 0xFF, 0x00, 0x56, 0xFF).ToArray();

        var structure = JpegSegmentReader.Read(bytes);

        var issue = Assert.Single(structure.Issues);
        Assert.Equal(JpegReadIssueKind.TruncatedFile, issue.Kind);
        Assert.Equal(scanStart, issue.Offset);
        Assert.False(structure.HasEndOfImage);
        Assert.Equal(JpegMarker.Sos, structure.Segments[^1].Marker);
    }

    [Fact]
    public void Read_TerminaSemEoiDepoisDeUmSegmento_RegistraTruncado()
    {
        var bytes = new JpegBytes().Soi().Segment(0xE0).ToArray();

        var structure = JpegSegmentReader.Read(bytes);

        var issue = Assert.Single(structure.Issues);
        Assert.Equal(JpegReadIssueKind.TruncatedFile, issue.Kind);
        Assert.Equal(bytes.Length, issue.Offset);
    }

    // ---- Comprimento inválido ----

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Read_ComprimentoMenorQue2_RegistraOSegmentoEPara(int declaredLength)
    {
        var bytes = new JpegBytes().Soi().SegmentHeader(0xE1, declaredLength).Segment(0xE0).Eoi().ToArray();

        var structure = JpegSegmentReader.Read(bytes);

        var issue = Assert.Single(structure.Issues);
        Assert.Equal(JpegReadIssueKind.InvalidSegmentLength, issue.Kind);
        Assert.Equal(2, issue.Offset);
        Assert.Equal((byte)0xE1, issue.MarkerCode);
        Assert.Equal(new JpegSegment(0xE1, 2, declaredLength), structure.Segments[^1]);
        Assert.False(structure.HasEndOfImage);
    }

    [Fact]
    public void Read_ComprimentoQuePassaDoFim_RegistraOSegmentoEPara()
    {
        var bytes = new JpegBytes().Soi().SegmentHeader(0xE1, 1000).Raw(0x11, 0x22).ToArray();

        var structure = JpegSegmentReader.Read(bytes);

        var issue = Assert.Single(structure.Issues);
        Assert.Equal(JpegReadIssueKind.InvalidSegmentLength, issue.Kind);
        Assert.Equal((byte)0xE1, issue.MarkerCode);
        Assert.Equal(new JpegSegment(0xE1, 2, 1000), structure.Segments[^1]);
    }

    [Fact]
    public void Read_SegmentoTerminaExatamenteNoFim_NaoEhComprimentoInvalido()
    {
        var bytes = new JpegBytes().Soi().Segment(0xE0).ToArray();

        var structure = JpegSegmentReader.Read(bytes);

        Assert.DoesNotContain(structure.Issues, i => i.Kind == JpegReadIssueKind.InvalidSegmentLength);
    }

    // ---- Marcador desconhecido ----

    [Fact]
    public void Read_MarcadorDesconhecido_RegistraEContinuaComoSegmentoComComprimento()
    {
        var bytes = new JpegBytes().Soi().Segment(0x42, 6).Segment(0xE0).Scan().Eoi().ToArray();

        var structure = JpegSegmentReader.Read(bytes);

        var issue = Assert.Single(structure.Issues);
        Assert.Equal(JpegReadIssueKind.UnknownMarker, issue.Kind);
        Assert.Equal(2, issue.Offset);
        Assert.Equal((byte)0x42, issue.MarkerCode);

        Assert.Equal(new JpegSegment(0x42, 2, 8), structure.Segments[1]);
        Assert.Null(structure.Segments[1].Marker);
        Assert.True(structure.HasEndOfImage);
    }

    // ---- Bytes soltos ----

    [Fact]
    public void Read_BytesSoltosEntreSegmentos_RegistraPosicaoQuantidadeEMarcadorEContinua()
    {
        var builder = new JpegBytes().Soi().Segment(0xE0);
        var strayStart = builder.Position;
        builder.Raw(0x01, 0x02, 0x03, 0x04, 0x05);
        var resumeOffset = builder.Position;
        var structure = JpegSegmentReader.Read(builder.Segment(0xDB, 65).Scan().Eoi().ToArray());

        var issue = Assert.Single(structure.Issues);
        Assert.Equal(JpegReadIssueKind.UnexpectedData, issue.Kind);
        Assert.Equal(strayStart, issue.Offset);
        Assert.Equal(5, issue.Length);
        Assert.Equal((byte)0xDB, issue.MarkerCode);

        Assert.Contains(new JpegSegment(0xDB, resumeOffset, 67), structure.Segments);
        Assert.True(structure.HasEndOfImage);
    }

    [Fact]
    public void Read_BytesSoltosAntesDoEoi_RecomecaNoEoi()
    {
        // Caso comum de encoder com bug: bytes sobrando logo antes do EOI.
        var builder = new JpegBytes().Soi().Segment(0xE0).Raw(0x00, 0x00);
        var eoiOffset = builder.Position;
        var structure = JpegSegmentReader.Read(builder.Eoi().ToArray());

        var issue = Assert.Single(structure.Issues);
        Assert.Equal(JpegReadIssueKind.UnexpectedData, issue.Kind);
        Assert.Equal(2, issue.Length);
        Assert.Equal((byte)0xD9, issue.MarkerCode);
        Assert.Equal(eoiOffset, structure.EndOfImageOffset);
    }

    [Fact]
    public void Read_BytesSoltosCom0xFFSeguidoDeCodigoDesconhecido_NaoRecomecaNele()
    {
        // 0xFF 0x42 no meio de bytes soltos é mais provável lixo do que um marcador.
        var builder = new JpegBytes().Soi().Segment(0xE0);
        var strayStart = builder.Position;
        builder.Raw(0x01, 0xFF, 0x42, 0x03);
        var structure = JpegSegmentReader.Read(builder.Eoi().ToArray());

        var issue = Assert.Single(structure.Issues);
        Assert.Equal(strayStart, issue.Offset);
        Assert.Equal(4, issue.Length);
        Assert.Equal((byte)0xD9, issue.MarkerCode);
    }

    [Fact]
    public void Read_Stuffing0xFF00ForaDoScan_EhTratadoComoBytesSoltos()
    {
        var builder = new JpegBytes().Soi().Segment(0xE0);
        var strayStart = builder.Position;
        builder.Raw(0xFF, 0x00, 0x07);
        var structure = JpegSegmentReader.Read(builder.Eoi().ToArray());

        var issue = Assert.Single(structure.Issues);
        Assert.Equal(JpegReadIssueKind.UnexpectedData, issue.Kind);
        Assert.Equal(strayStart, issue.Offset);
        Assert.Equal(3, issue.Length);
        Assert.True(structure.HasEndOfImage);
    }

    [Fact]
    public void Read_BytesSoltosAteOFim_RegistraSemMarcadorETerminaSemEoi()
    {
        var builder = new JpegBytes().Soi().Segment(0xE0);
        var strayStart = builder.Position;
        var structure = JpegSegmentReader.Read(builder.Raw(0x01, 0x02, 0x03).ToArray());

        var issue = Assert.Single(structure.Issues);
        Assert.Equal(JpegReadIssueKind.UnexpectedData, issue.Kind);
        Assert.Equal(strayStart, issue.Offset);
        Assert.Equal(3, issue.Length);
        Assert.Null(issue.MarkerCode);
        Assert.False(structure.HasEndOfImage);
    }

    [Fact]
    public void Read_ComprimentoErradoCaiDentroDaMiniatura_RegistraOsMarcadoresDela()
    {
        // Limitação conhecida: um comprimento curto demais no APP1 leva o leitor para dentro da
        // miniatura do EXIF, e o EOI dela é tomado como o da imagem. O UnexpectedData registrado
        // antes é o que permite às regras (22.4) desconfiar do que vem depois.
        var thumbnail = new JpegBytes().Soi().Segment(0xDB, 65).Scan(4).Eoi().ToArray();
        var bytes = new JpegBytes().Soi()
            .SegmentHeader(0xE1, 4).Raw(0x45, 0x78) // declara 4: o segmento "termina" aqui
            .Raw(0x69, 0x66, 0x00, 0x00).Raw(thumbnail)
            .Segment(0xDB, 65).Scan().Eoi().ToArray();

        var structure = JpegSegmentReader.Read(bytes);

        var issue = Assert.Single(structure.Issues);
        Assert.Equal(JpegReadIssueKind.UnexpectedData, issue.Kind);
        Assert.Equal((byte)0xD8, issue.MarkerCode);
        Assert.True(structure.TrailingDataLength > 0);
    }

    // ---- Robustez ----

    [Fact]
    public void Read_BytesAleatorios_NuncaLancaExcecao()
    {
        var random = new Random(22);

        for (var i = 0; i < 2000; i++)
        {
            var bytes = new byte[random.Next(2, 300)];
            random.NextBytes(bytes);
            bytes[0] = 0xFF;
            bytes[1] = 0xD8;

            var structure = JpegSegmentReader.Read(bytes);

            Assert.All(structure.Segments, s => Assert.InRange(s.Offset, 0, bytes.Length - 2));
            Assert.InRange(structure.TrailingDataLength, 0, bytes.Length);
        }
    }
}
