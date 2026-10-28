using ImageAnalysis.Domain.Entities.Common;
using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.ValueObjects;

namespace ImageAnalysis.Domain.Entities;

/// <summary>
/// Análise completa de uma imagem e raiz do agregado: controla o ciclo de vida
/// (Pending → Processing → Completed/Failed) e é a única porta de entrada para
/// ImageMetadata, Findings, Evidences, AnalysisResult e AnalysisFailure.
/// </summary>
public class Analysis : BaseEntity
{
    // Opcional: informado pelo usuário no upload.
    public string? Name { get; private set; }
    public AnalysisStatus Status { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? FinishedAt { get; private set; }

    // Imagem original já salva no storage antes da criação da análise.
    public ImageFile Image { get; private set; } = null!;

    // Preenchido pelo Metadata Engine durante o processamento.
    public ImageMetadata? Metadata { get; private set; }

    private readonly List<Finding> _findings = new();
    public IReadOnlyCollection<Finding> Findings => _findings.AsReadOnly();

    // Preenchido apenas quando a análise é concluída com sucesso.
    public AnalysisResult? Result { get; private set; }

    // Preenchido apenas quando a análise falha.
    public AnalysisFailure? Failure { get; private set; }

    // Construtor para materialização pelo ORM.
    private Analysis()
    {
    }

    public Analysis(string? name, ImageFile image)
    {
        ArgumentNullException.ThrowIfNull(image);

        // Texto em branco é tratado como ausência de nome.
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Image = image;
        Status = AnalysisStatus.Pending;
    }

    public void Start()
    {
        EnsureStatus(AnalysisStatus.Pending);

        Status = AnalysisStatus.Processing;
        StartedAt = DateTime.UtcNow;
    }

    public ImageMetadata SetMetadata(string rawMetadataPath, int width, int height)
    {
        EnsureStatus(AnalysisStatus.Processing);

        if (Metadata is not null)
            throw new InvalidOperationException("Os metadados desta análise já foram definidos.");

        Metadata = new ImageMetadata(Id, rawMetadataPath, width, height);
        return Metadata;
    }

    public Finding AddFinding(string name, SourceFindingType sourceFindingType, string description)
    {
        EnsureStatus(AnalysisStatus.Processing);

        var finding = new Finding(Id, name, sourceFindingType, description);
        _findings.Add(finding);
        return finding;
    }

    public Evidence AddEvidence(Guid findingId, string name, string description, EvidenceStrength strength, EvidenceDirection direction)
    {
        EnsureStatus(AnalysisStatus.Processing);

        var finding = _findings.SingleOrDefault(f => f.Id == findingId)
            ?? throw new InvalidOperationException($"O achado {findingId} não pertence a esta análise.");

        return finding.AddEvidence(name, description, strength, direction);
    }

    public AnalysisResult Complete(AnalysisResultStatus status, ConfidenceLevel confidence, string summary, string? limitations = null)
    {
        EnsureStatus(AnalysisStatus.Processing);

        Result = new AnalysisResult(Id, status, confidence, summary, limitations);
        Finish(AnalysisStatus.Completed);
        return Result;
    }

    // Pode falhar antes de começar (ex.: erro ao publicar na fila ou imagem ausente no storage).
    // StartedAt nulo indica que a falha ocorreu antes do processamento.
    public void Fail(AnalysisFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        if (Status is AnalysisStatus.Completed or AnalysisStatus.Failed)
            throw new InvalidOperationException(
                $"Operação inválida: a análise já foi finalizada com status {Status}.");

        Failure = failure;
        Finish(AnalysisStatus.Failed);
    }

    // A validação do status de origem fica em Complete() e Fail().
    private void Finish(AnalysisStatus finalStatus)
    {
        Status = finalStatus;
        FinishedAt = DateTime.UtcNow;
    }

    private void EnsureStatus(AnalysisStatus expected)
    {
        if (Status != expected)
            throw new InvalidOperationException(
                $"Operação inválida: a análise está em {Status}, mas deveria estar em {expected}.");
    }
}
