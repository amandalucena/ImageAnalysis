namespace ImageAnalysis.Application.Jpeg;

/// <summary>
/// Marcador encontrado no arquivo e a posição do segmento. Guarda o código bruto para
/// registrar também marcadores desconhecidos; Marker é null nesses casos.
/// </summary>
public sealed record JpegSegment
{
    public byte MarkerCode { get; }

    // Posição do byte 0xFF que abre o marcador.
    public long Offset { get; }

    // Valor do campo de comprimento, que inclui os próprios 2 bytes. 0 nos marcadores sem
    // segmento de dados (SOI, EOI, RSTn, TEM). Não inclui os dados comprimidos depois do SOS.
    public int Length { get; }

    public JpegSegment(byte markerCode, long offset, int length)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "A posição do segmento não pode ser negativa.");

        // O campo tem 16 bits; valores 0 e 1 são inválidos no arquivo, mas descrevem o que
        // foi lido. Quem lê registra o problema; o modelo só não aceita o que o campo não comporta.
        if (length is < 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(length), "O comprimento do segmento deve caber em 16 bits.");

        if (JpegMarkers.IsStandalone(markerCode) && length != 0)
            throw new ArgumentException("Marcadores sem segmento de dados não têm comprimento.", nameof(length));

        MarkerCode = markerCode;
        Offset = offset;
        Length = length;
    }

    public JpegMarker? Marker => JpegMarkers.IsKnown(MarkerCode) ? (JpegMarker)MarkerCode : null;
}
