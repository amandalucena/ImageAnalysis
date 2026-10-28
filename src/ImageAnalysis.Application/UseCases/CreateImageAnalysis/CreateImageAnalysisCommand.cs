namespace ImageAnalysis.Application.UseCases.CreateImageAnalysis;

/// <summary>
/// Entrada do CreateImageAnalysis. Recebe o conteúdo como Stream, sem depender do
/// IFormFile do ASP.NET: a conversão do upload HTTP é responsabilidade da API.
/// </summary>
/// <param name="Name">Nome opcional da análise informado pelo usuário.</param>
/// <param name="OriginalFileName">Nome do arquivo como foi enviado; usado só para auditoria.</param>
/// <param name="Content">Conteúdo do arquivo enviado.</param>
public sealed record CreateImageAnalysisCommand(string? Name, string OriginalFileName, Stream Content);
