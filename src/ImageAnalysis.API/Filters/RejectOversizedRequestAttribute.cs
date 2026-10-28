using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace ImageAnalysis.API.Filters;

/// <summary>
/// Responde 413 quando o Content-Length declarado passa do limite, antes da leitura do formulário.
/// Sem isso, o MVC captura a falha do limite do multipart durante o model binding e o
/// [ApiController] a devolve como 400 de validação.
/// Requisições sem Content-Length (chunked) continuam barradas pelos limites do formulário.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RejectOversizedRequestAttribute : Attribute, IResourceFilter
{
    private readonly long _maxRequestSizeInBytes;

    public RejectOversizedRequestAttribute(long maxRequestSizeInBytes)
    {
        _maxRequestSizeInBytes = maxRequestSizeInBytes;
    }

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (context.HttpContext.Request.ContentLength is not long contentLength || contentLength <= _maxRequestSizeInBytes)
            return;

        // ProblemDetailsFactory para seguir o mesmo formato das demais respostas (type e traceId).
        var problem = context.HttpContext.RequestServices
            .GetRequiredService<ProblemDetailsFactory>()
            .CreateProblemDetails(
                context.HttpContext,
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Arquivo muito grande.",
                detail: $"A requisição deve ter no máximo {_maxRequestSizeInBytes / (1024 * 1024)} MB.");

        context.Result = new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status413PayloadTooLarge,
            ContentTypes = { "application/problem+json" },
        };
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
