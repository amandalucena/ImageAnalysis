using System.ComponentModel.DataAnnotations;

namespace ImageAnalysis.API.Contracts;

/// <summary>
/// Corpo do POST /analyses (multipart/form-data).
/// </summary>
public sealed class CreateAnalysisRequest
{
    [Required(ErrorMessage = "O arquivo da imagem é obrigatório.")]
    public IFormFile? File { get; init; }

    // O limite de tamanho é validado no caso de uso, depois de remover os espaços das pontas.
    public string? Name { get; init; }
}
