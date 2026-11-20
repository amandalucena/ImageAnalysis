using ImageAnalysis.Application.Metadata;
using ImageAnalysis.Application.UseCases.CreateImageAnalysis;
using ImageAnalysis.Application.UseCases.GetAnalysisFindings;
using ImageAnalysis.Application.UseCases.GetAnalysisStatus;
using ImageAnalysis.Application.UseCases.ProcessImageAnalysis;
using ImageAnalysis.Domain.Interfaces;
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
        services.AddScoped<GetAnalysisFindings>();
        services.AddScoped<ProcessImageAnalysis>();

        // O concreto é pedido por quem precisa do dump e dos metadados normalizados (7.5);
        // a orquestração dos analisadores (#48) recebe a lista de IImageAnalyzer.
        // As duas formas resolvem a mesma instância dentro do escopo.
        services.AddScoped<MetadataAnalyzer>();
        services.AddScoped<IImageAnalyzer>(sp => sp.GetRequiredService<MetadataAnalyzer>());

        return services;
    }
}
