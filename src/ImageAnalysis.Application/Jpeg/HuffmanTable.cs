namespace ImageAnalysis.Application.Jpeg;

/// <summary>
/// Tabela Huffman (DHT) completa: as 16 contagens (quantos códigos de cada tamanho, de 1 a 16
/// bits) e os símbolos na ordem do arquivo. Guardada inteira para que as regras (22.4) saibam se
/// é a tabela padrão do Anexo K da T.81 ou uma tabela otimizada pelo encoder.
/// </summary>
public sealed record HuffmanTable
{
    public const int CountsLength = 16;

    public HuffmanTableClass Class { get; }

    // Os 4 bits do arquivo; a T.81 permite 0–3.
    public byte Id { get; }

    public IReadOnlyList<byte> Counts { get; }

    public IReadOnlyList<byte> Symbols { get; }

    public HuffmanTable(HuffmanTableClass tableClass, byte id, IEnumerable<byte> counts, IEnumerable<byte> symbols)
    {
        if (!Enum.IsDefined(tableClass))
            throw new ArgumentOutOfRangeException(nameof(tableClass), "Classe da tabela inválida.");

        if (id > 0x0F)
            throw new ArgumentOutOfRangeException(nameof(id), "O identificador da tabela tem 4 bits.");

        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(symbols);

        var countsCopy = counts.ToArray();
        if (countsCopy.Length != CountsLength)
            throw new ArgumentException($"A tabela deve ter {CountsLength} contagens.", nameof(counts));

        var symbolsCopy = symbols.ToArray();
        if (symbolsCopy.Length != countsCopy.Sum(c => c))
            throw new ArgumentException("A quantidade de símbolos deve ser a soma das contagens.", nameof(symbols));

        Class = tableClass;
        Id = id;
        Counts = countsCopy;
        Symbols = symbolsCopy;
    }

    // Record compara listas por referência; tabelas com os mesmos valores devem ser iguais.
    public bool Equals(HuffmanTable? other) =>
        other is not null
        && Class == other.Class
        && Id == other.Id
        && Counts.SequenceEqual(other.Counts)
        && Symbols.SequenceEqual(other.Symbols);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Class);
        hash.Add(Id);
        foreach (var count in Counts)
            hash.Add(count);
        foreach (var symbol in Symbols)
            hash.Add(symbol);

        return hash.ToHashCode();
    }
}

/// <summary>
/// Classe da tabela Huffman, pelo campo Tc do DHT.
/// </summary>
public enum HuffmanTableClass
{
    Dc = 0,
    Ac = 1
}
