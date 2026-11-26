namespace ImageAnalysis.Application.Jpeg;

/// <summary>
/// Tabela de quantização (DQT). Os 64 coeficientes ficam na ordem natural (linha a linha
/// do bloco 8×8), já convertidos da ordem zigzag em que vêm no arquivo. É a entrada da
/// estimativa de qualidade e da detecção de dupla compressão (#23).
/// </summary>
public sealed record QuantizationTable
{
    public const int CoefficientCount = 64;

    // Os 4 bits do arquivo; a T.81 permite 0–3.
    public byte Id { get; }

    public QuantizationPrecision Precision { get; }

    public IReadOnlyList<ushort> Coefficients { get; }

    public QuantizationTable(byte id, QuantizationPrecision precision, IEnumerable<ushort> coefficients)
    {
        if (id > 0x0F)
            throw new ArgumentOutOfRangeException(nameof(id), "O identificador da tabela tem 4 bits.");

        if (!Enum.IsDefined(precision))
            throw new ArgumentOutOfRangeException(nameof(precision), "Precisão da tabela inválida.");

        ArgumentNullException.ThrowIfNull(coefficients);

        var copy = coefficients.ToArray();
        if (copy.Length != CoefficientCount)
            throw new ArgumentException($"A tabela deve ter {CoefficientCount} coeficientes.", nameof(coefficients));

        // Coeficiente 0 é inválido no arquivo, mas é o que foi lido; o modelo não o rejeita.
        if (precision == QuantizationPrecision.EightBit && copy.Any(c => c > byte.MaxValue))
            throw new ArgumentException("Uma tabela de 8 bits não tem coeficientes acima de 255.", nameof(coefficients));

        Id = id;
        Precision = precision;
        Coefficients = copy;
    }

    // Record compara listas por referência; duas tabelas com os mesmos valores devem ser iguais
    // (ex.: tabelas repetidas no arquivo).
    public bool Equals(QuantizationTable? other) =>
        other is not null
        && Id == other.Id
        && Precision == other.Precision
        && Coefficients.SequenceEqual(other.Coefficients);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Precision);
        foreach (var coefficient in Coefficients)
            hash.Add(coefficient);

        return hash.ToHashCode();
    }
}

/// <summary>
/// Precisão dos coeficientes, pelo campo Pq do DQT.
/// </summary>
public enum QuantizationPrecision
{
    EightBit = 0,
    SixteenBit = 1
}
