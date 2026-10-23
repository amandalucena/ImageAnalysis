namespace ImageAnalysis.Domain.Entities.Common;

/// <summary>
/// Base das entidades do domínio: identificador e data de criação.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
}
