using ImageAnalysis.Domain.Enums;

namespace ImageAnalysis.Domain.ValueObjects;

/// <summary>
/// Saída de um analisador. Achados e evidências chegam como dados; a Application
/// os transforma em Finding/Evidence pelos métodos da Analysis (raiz do agregado).
/// Um resultado sem achados é válido: o analisador não encontrou nada a relatar.
/// </summary>
public sealed record AnalyzerResult
{
    public static AnalyzerResult Empty { get; } = new(Array.Empty<AnalyzerFinding>());

    public IReadOnlyList<AnalyzerFinding> Findings { get; }

    public AnalyzerResult(IEnumerable<AnalyzerFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var copy = findings.ToArray();
        if (copy.Any(f => f is null))
            throw new ArgumentException("A lista de achados não pode conter itens nulos.", nameof(findings));

        Findings = copy;
    }
}

/// <summary>
/// Achado produzido por um analisador, ainda fora do agregado.
/// As regras espelham as do Finding para que um resultado inválido falhe no analisador,
/// e não só quando a Application o aplicar.
/// </summary>
public sealed record AnalyzerFinding
{
    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<AnalyzerEvidence> Evidences { get; }

    public AnalyzerFinding(string name, string description, IEnumerable<AnalyzerEvidence> evidences)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("O nome do achado é obrigatório.", nameof(name));

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("A descrição do achado é obrigatória.", nameof(description));

        ArgumentNullException.ThrowIfNull(evidences);

        var copy = evidences.ToArray();
        if (copy.Any(e => e is null))
            throw new ArgumentException("A lista de evidências não pode conter itens nulos.", nameof(evidences));

        Name = name;
        Description = description;
        Evidences = copy;
    }
}

/// <summary>
/// Evidência que sustenta um AnalyzerFinding.
/// </summary>
public sealed record AnalyzerEvidence
{
    public string Name { get; }
    public string Description { get; }
    public EvidenceStrength Strength { get; }
    public EvidenceDirection Direction { get; }

    public AnalyzerEvidence(string name, string description, EvidenceStrength strength, EvidenceDirection direction)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("O nome da evidência é obrigatório.", nameof(name));

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("A descrição da evidência é obrigatória.", nameof(description));

        if (!Enum.IsDefined(strength))
            throw new ArgumentOutOfRangeException(nameof(strength), "Força da evidência inválida.");

        if (!Enum.IsDefined(direction))
            throw new ArgumentOutOfRangeException(nameof(direction), "Direção da evidência inválida.");

        Name = name;
        Description = description;
        Strength = strength;
        Direction = direction;
    }
}
