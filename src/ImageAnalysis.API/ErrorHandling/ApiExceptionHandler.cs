using ImageAnalysis.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ImageAnalysis.API.ErrorHandling;

/// <summary>
/// Converte exceções conhecidas em respostas ProblemDetails. As demais seguem para o
/// tratamento padrão (500 sem detalhes internos); o detalhe técnico fica só no log.
/// </summary>
public class ApiExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;

    public ApiExceptionHandler(IProblemDetailsService problemDetailsService)
    {
        _problemDetailsService = problemDetailsService;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            InvalidImageException => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Imagem inválida.",
                Detail = exception.Message,
            },

            // Lançada pelo Kestrel, ex.: corpo acima do [RequestSizeLimit] (413).
            BadHttpRequestException badRequest => new ProblemDetails
            {
                Status = badRequest.StatusCode,
                Title = badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? "Arquivo muito grande."
                    : "Requisição inválida.",
            },

            _ => null,
        };

        if (problem is null)
            return false;

        httpContext.Response.StatusCode = problem.Status!.Value;

        var written = await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });

        // O writer padrão recusa quando o Accept do cliente não inclui JSON (ex.: text/html).
        // Retornar false faria o middleware tratar a exceção como não tratada e responder 500;
        // a resposta é escrita como JSON mesmo assim, mantendo o status correto.
        if (!written)
            await httpContext.Response.WriteAsJsonAsync(
                problem, options: null, contentType: "application/problem+json", cancellationToken);

        return true;
    }
}
