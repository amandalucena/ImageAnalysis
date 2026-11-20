using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.ValueObjects;

namespace ImageAnalysis.Domain.Interfaces;

/// <summary>
/// Contrato comum dos analisadores (metadata, JPEG, ELA, C2PA, IA...).
/// Um analisador apenas produz evidências: não acessa o banco nem repositórios
/// e não altera a Analysis. Quem aplica o resultado no agregado e persiste é a
/// Application (CLAUDE.md, seção 8.1), o que permite executar analisadores em paralelo.
/// </summary>
public interface IImageAnalyzer
{
    // Origem gravada em cada Finding produzido por este analisador.
    SourceFindingType Source { get; }

    Task<AnalyzerResult> AnalyzeAsync(AnalyzerContext context, CancellationToken cancellationToken = default);
}
