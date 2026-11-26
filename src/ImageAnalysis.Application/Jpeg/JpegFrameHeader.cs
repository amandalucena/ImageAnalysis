namespace ImageAnalysis.Application.Jpeg;

/// <summary>
/// Cabeçalho de frame (SOFn). Os valores são os do arquivo, mesmo fora do que a T.81 permite
/// (ex.: precisão 9 num baseline): avaliar se fazem sentido é papel das regras, não do modelo.
/// </summary>
public sealed record JpegFrameHeader
{
    public JpegMarker Marker { get; }

    // Bits por amostra.
    public byte Precision { get; }

    // 0 é válido no arquivo: o número de linhas vem depois, num segmento DNL.
    public ushort Height { get; }
    public ushort Width { get; }

    public IReadOnlyList<JpegFrameComponent> Components { get; }

    public JpegFrameHeader(
        JpegMarker marker, byte precision, ushort height, ushort width, IEnumerable<JpegFrameComponent> components)
    {
        if (!JpegMarkers.IsStartOfFrame((byte)marker))
            throw new ArgumentException("O cabeçalho de frame deve vir de um marcador SOF.", nameof(marker));

        ArgumentNullException.ThrowIfNull(components);

        var copy = components.ToArray();
        if (copy.Any(c => c is null))
            throw new ArgumentException("A lista de componentes não pode conter itens nulos.", nameof(components));

        Marker = marker;
        Precision = precision;
        Height = height;
        Width = width;
        Components = copy;
    }

    public bool IsBaseline => Marker == JpegMarker.Sof0;

    public bool IsProgressive => Marker is JpegMarker.Sof2 or JpegMarker.Sof6 or JpegMarker.Sof10 or JpegMarker.Sof14;

    public bool IsLossless => Marker is JpegMarker.Sof3 or JpegMarker.Sof7 or JpegMarker.Sof11 or JpegMarker.Sof15;

    public bool IsHierarchical =>
        Marker is JpegMarker.Sof5 or JpegMarker.Sof6 or JpegMarker.Sof7
            or JpegMarker.Sof13 or JpegMarker.Sof14 or JpegMarker.Sof15;

    public bool IsArithmetic => Marker >= JpegMarker.Sof9;

    // Record compara listas por referência; dois cabeçalhos com os mesmos valores devem ser iguais.
    public bool Equals(JpegFrameHeader? other) =>
        other is not null
        && Marker == other.Marker
        && Precision == other.Precision
        && Height == other.Height
        && Width == other.Width
        && Components.SequenceEqual(other.Components);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Marker);
        hash.Add(Precision);
        hash.Add(Height);
        hash.Add(Width);
        foreach (var component in Components)
            hash.Add(component);

        return hash.ToHashCode();
    }
}

/// <summary>
/// Componente do frame (ex.: Y, Cb, Cr). Os fatores de amostragem são os 4 bits do arquivo
/// (a T.81 permite 1–4) e definem o subsampling de croma, ex.: Y 2×2 com Cb/Cr 1×1 é 4:2:0.
/// </summary>
public sealed record JpegFrameComponent
{
    public byte Id { get; }
    public byte HorizontalSampling { get; }
    public byte VerticalSampling { get; }
    public byte QuantizationTableId { get; }

    public JpegFrameComponent(byte id, byte horizontalSampling, byte verticalSampling, byte quantizationTableId)
    {
        if (horizontalSampling > 0x0F)
            throw new ArgumentOutOfRangeException(nameof(horizontalSampling), "O fator de amostragem tem 4 bits.");

        if (verticalSampling > 0x0F)
            throw new ArgumentOutOfRangeException(nameof(verticalSampling), "O fator de amostragem tem 4 bits.");

        Id = id;
        HorizontalSampling = horizontalSampling;
        VerticalSampling = verticalSampling;
        QuantizationTableId = quantizationTableId;
    }
}
