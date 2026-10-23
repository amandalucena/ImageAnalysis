using ImageAnalysis.Domain.Entities.Common;
using ImageAnalysis.Domain.Enums;

namespace ImageAnalysis.Domain.Entities;

/// <summary>
/// Evidência que sustenta um achado.
/// Um Finding possui várias Evidences (1:N).
/// </summary>
public class Evidence : BaseEntity
{
    public Guid FindingId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public EvidenceStrength Strength { get; private set; }
    public EvidenceDirection Direction { get; private set; }

    // Construtor para materialização pelo ORM.
    private Evidence()
    {
    }

    internal Evidence(Guid findingId, string name, string description, EvidenceStrength strength, EvidenceDirection direction)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("O nome da evidência é obrigatório.", nameof(name));

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("A descrição da evidência é obrigatória.", nameof(description));

        FindingId = findingId;
        Name = name;
        Description = description;
        Strength = strength;
        Direction = direction;
    }
}
