using ImageAnalysis.Domain.Entities.Common;
using ImageAnalysis.Domain.Enums;

namespace ImageAnalysis.Domain.Entities;

/// <summary>
/// Achado produzido por um analisador durante uma análise.
/// Uma Analysis possui vários Findings (1:N).
/// </summary>
public class Finding : BaseEntity
{
    public Guid AnalysisId { get; private set; }
    public string Name { get; private set; } = null!;
    public SourceFindingType SourceFindingType { get; private set; }
    public string Description { get; private set; } = null!;

    private readonly List<Evidence> _evidences = new();
    public IReadOnlyCollection<Evidence> Evidences => _evidences.AsReadOnly();

    // Construtor para materialização pelo ORM.
    private Finding()
    {
    }

    internal Finding(Guid analysisId, string name, SourceFindingType sourceFindingType, string description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("O nome do achado é obrigatório.", nameof(name));

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("A descrição do achado é obrigatória.", nameof(description));

        if (!Enum.IsDefined(sourceFindingType))
            throw new ArgumentOutOfRangeException(nameof(sourceFindingType), "Analisador de origem inválido.");

        AnalysisId = analysisId;
        Name = name;
        SourceFindingType = sourceFindingType;
        Description = description;
    }

    // Interno: a Analysis é a raiz do agregado e valida o status antes de chamar.
    internal Evidence AddEvidence(string name, string description, EvidenceStrength strength, EvidenceDirection direction)
    {
        var evidence = new Evidence(Id, name, description, strength, direction);
        _evidences.Add(evidence);
        return evidence;
    }
}
