using ImageAnalysis.Application.UseCases.CreateImageAnalysis;
using ImageAnalysis.Application.UseCases.GetAnalysisStatus;
using Microsoft.Extensions.DependencyInjection;

namespace ImageAnalysis.Application;

/// <summary>
/// Registro dos casos de uso. A API chama apenas AddApplication.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Scoped porque dependem do IAnalysisRepository, que usa o DbContext da requisição.
        services.AddScoped<CreateImageAnalysis>();
        services.AddScoped<GetAnalysisStatus>();

        return services;
    }
}
