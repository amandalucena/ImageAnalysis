using ImageAnalysis.API.Contracts;
using ImageAnalysis.Application.UseCases.CreateImageAnalysis;
using ImageAnalysis.Application.UseCases.GetAnalysisStatus;
using Microsoft.AspNetCore.Mvc;

namespace ImageAnalysis.API.Controllers;

/// <summary>
/// Criação e consulta de análises. Só traduz HTTP para os casos de uso: a validação do
/// conteúdo da imagem e as regras ficam na Application e no Domain.
/// </summary>
[ApiController]
[Route("analyses")]
public class AnalysesController : ControllerBase
{
    // 202: a análise foi registrada, mas o processamento acontece depois (Worker).
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadLimits.MaxRequestSizeInBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimits.MaxRequestSizeInBytes)]
    [ProducesResponseType<CreateAnalysisResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Create(
        [FromForm] CreateAnalysisRequest request,
        [FromServices] CreateImageAnalysis createImageAnalysis,
        CancellationToken cancellationToken)
    {
        // [Required] já garante o arquivo; o ! informa isso ao compilador.
        var file = request.File!;

        if (file.Length > UploadLimits.MaxImageSizeInBytes)
            return Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Arquivo muito grande.",
                detail: $"O arquivo deve ter no máximo {UploadLimits.MaxImageSizeInBytes / (1024 * 1024)} MB.");

        await using var content = file.OpenReadStream();
        var result = await createImageAnalysis.ExecuteAsync(
            new CreateImageAnalysisCommand(request.Name, file.FileName, content),
            cancellationToken);

        return AcceptedAtAction(
            nameof(GetById),
            new { id = result.AnalysisId },
            new CreateAnalysisResponse(result.AnalysisId));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AnalysisStatusResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromServices] GetAnalysisStatus getAnalysisStatus,
        CancellationToken cancellationToken)
    {
        var result = await getAnalysisStatus.ExecuteAsync(new GetAnalysisStatusQuery(id), cancellationToken);

        return result is null ? NotFound() : Ok(result);
    }
}
