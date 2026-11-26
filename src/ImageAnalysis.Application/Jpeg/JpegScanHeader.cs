namespace ImageAnalysis.Application.Jpeg;

/// <summary>
/// Cabeçalho de um scan (SOS). Num JPEG progressive, a sequência de scans (componentes, faixa
/// espectral e aproximação sucessiva) varia de encoder para encoder.
/// </summary>
public sealed record JpegScanHeader
{
    // Posição do marcador SOS, para relacionar o scan ao segmento.
    public long Offset { get; }

    public IReadOnlyList<JpegScanComponent> Components { get; }

    // Ss e Se: primeiro e último coeficiente do scan. 0–63 num scan sequencial.
    public byte SpectralStart { get; }
    public byte SpectralEnd { get; }

    // Ah e Al: os 4 bits de cada campo.
    public byte SuccessiveApproximationHigh { get; }
    public byte SuccessiveApproximationLow { get; }

    public JpegScanHeader(
        long offset,
        IEnumerable<JpegScanComponent> components,
        byte spectralStart,
        byte spectralEnd,
        byte successiveApproximationHigh,
        byte successiveApproximationLow)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "A posição do scan não pode ser negativa.");

        ArgumentNullException.ThrowIfNull(components);

        var copy = components.ToArray();
        if (copy.Any(c => c is null))
            throw new ArgumentException("A lista de componentes não pode conter itens nulos.", nameof(components));

        if (successiveApproximationHigh > 0x0F)
            throw new ArgumentOutOfRangeException(nameof(successiveApproximationHigh), "O campo Ah tem 4 bits.");

        if (successiveApproximationLow > 0x0F)
            throw new ArgumentOutOfRangeException(nameof(successiveApproximationLow), "O campo Al tem 4 bits.");

        Offset = offset;
        Components = copy;
        SpectralStart = spectralStart;
        SpectralEnd = spectralEnd;
        SuccessiveApproximationHigh = successiveApproximationHigh;
        SuccessiveApproximationLow = successiveApproximationLow;
    }

    // Record compara listas por referência; scans com os mesmos valores devem ser iguais.
    public bool Equals(JpegScanHeader? other) =>
        other is not null
        && Offset == other.Offset
        && Components.SequenceEqual(other.Components)
        && SpectralStart == other.SpectralStart
        && SpectralEnd == other.SpectralEnd
        && SuccessiveApproximationHigh == other.SuccessiveApproximationHigh
        && SuccessiveApproximationLow == other.SuccessiveApproximationLow;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Offset);
        foreach (var component in Components)
            hash.Add(component);
        hash.Add(SpectralStart);
        hash.Add(SpectralEnd);
        hash.Add(SuccessiveApproximationHigh);
        hash.Add(SuccessiveApproximationLow);

        return hash.ToHashCode();
    }
}

/// <summary>
/// Componente de um scan e as tabelas Huffman que usa (os 4 bits de Td e Ta).
/// </summary>
public sealed record JpegScanComponent
{
    public byte Id { get; }
    public byte DcTableId { get; }
    public byte AcTableId { get; }

    public JpegScanComponent(byte id, byte dcTableId, byte acTableId)
    {
        if (dcTableId > 0x0F)
            throw new ArgumentOutOfRangeException(nameof(dcTableId), "O identificador da tabela tem 4 bits.");

        if (acTableId > 0x0F)
            throw new ArgumentOutOfRangeException(nameof(acTableId), "O identificador da tabela tem 4 bits.");

        Id = id;
        DcTableId = dcTableId;
        AcTableId = acTableId;
    }
}
