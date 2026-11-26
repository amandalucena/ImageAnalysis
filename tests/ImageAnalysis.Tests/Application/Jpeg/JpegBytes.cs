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

    // Segmentos típicos de um JPEG baseline até o SOS (APP0, DQT, SOF0, DHT).
    public JpegBytes Header() =>
        Segment(0xE0, 14).Segment(0xDB, 65).Segment(0xC0, 15).Segment(0xC4, 29);

    // SOS seguido de dados comprimidos sem nenhum 0xFF.
    public JpegBytes Scan(int dataLength = 8) =>
        Segment(0xDA, 10).Raw(Enumerable.Repeat((byte)0x5A, dataLength).ToArray());

    public byte[] ToArray() => [.. _bytes];
}
