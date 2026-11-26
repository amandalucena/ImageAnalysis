namespace ImageAnalysis.Tests.Application.Jpeg;

/// <summary>
/// Monta arquivos JPEG byte a byte para os testes do leitor. Os segmentos não precisam ter
/// conteúdo válido: o leitor da 22.2 só olha marcadores e comprimentos.
/// </summary>
internal sealed class JpegBytes
{
    private readonly List<byte> _bytes = [];

    public int Position => _bytes.Count;

    public JpegBytes Soi() => Marker(0xD8);

    public JpegBytes Eoi() => Marker(0xD9);

    // Marcador sem segmento de dados.
    public JpegBytes Marker(byte code)
    {
        _bytes.Add(0xFF);
        _bytes.Add(code);
        return this;
    }

    // Segmento com o campo de comprimento correto (dados + 2).
    public JpegBytes Segment(byte code, int dataLength = 4)
    {
        Marker(code);
        var length = dataLength + 2;
        _bytes.Add((byte)(length >> 8));
        _bytes.Add((byte)length);
        _bytes.AddRange(Enumerable.Repeat((byte)0x11, dataLength));
        return this;
    }

    // Segmento com o conteúdo informado e o campo de comprimento correto.
    public JpegBytes SegmentWith(byte code, params byte[] payload)
    {
        Marker(code);
        var length = payload.Length + 2;
        _bytes.Add((byte)(length >> 8));
        _bytes.Add((byte)length);
        _bytes.AddRange(payload);
        return this;
    }

    // Cabeçalho do segmento com um comprimento qualquer, sem os dados.
    public JpegBytes SegmentHeader(byte code, int declaredLength)
    {
        Marker(code);
        _bytes.Add((byte)(declaredLength >> 8));
        _bytes.Add((byte)declaredLength);
        return this;
    }

    public JpegBytes Raw(params byte[] bytes)
    {
        _bytes.AddRange(bytes);
        return this;
    }

    // Segmentos típicos de um JPEG baseline até o SOS, com conteúdo válido para o decoder:
    // APP0 JFIF (14 bytes), DQT com 1 tabela (65), SOF0 16×16 com 3 componentes (15) e
    // DHT com a tabela DC de luminância do Anexo K (29).
    public JpegBytes Header() =>
        SegmentWith(0xE0, [.. "JFIF\0"u8, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00])
            .SegmentWith(0xDB, [0x00, .. Enumerable.Repeat((byte)0x10, 64)])
            .SegmentWith(0xC0, 0x08, 0x00, 0x10, 0x00, 0x10, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x00, 0x03, 0x11, 0x00)
            .SegmentWith(0xC4, [0x00, 0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]);

    // SOS com 3 componentes (10 bytes) seguido de dados comprimidos sem nenhum 0xFF.
    public JpegBytes Scan(int dataLength = 8) =>
        SegmentWith(0xDA, 0x03, 0x01, 0x00, 0x02, 0x11, 0x03, 0x11, 0x00, 0x3F, 0x00)
            .Raw(Enumerable.Repeat((byte)0x5A, dataLength).ToArray());

    public byte[] ToArray() => [.. _bytes];
}
