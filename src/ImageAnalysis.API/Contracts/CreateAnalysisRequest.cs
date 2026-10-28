using System.ComponentModel.DataAnnotations;

namespace ImageAnalysis.API.Contracts;

/// <summary>
/// Corpo do POST /analyses (multipart/form-data).
/// </summary>
public sealed class CreateAnalysisRequest
{
    [Required(ErrorMessage = "O arquivo da imagem é obrigatório.")]
    public IFormFile? File { get; init; }

    // Mesmo limite da coluna Analyses.Name.
    [MaxLength(200, ErrorMessage = "O nome da análise deve ter no máximo 200 caracteres.")]
    public string? Name { get; init; }
}
