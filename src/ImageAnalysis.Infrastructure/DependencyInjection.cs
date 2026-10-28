using ImageAnalysis.Domain.Interfaces;
using ImageAnalysis.Infrastructure.Persistence;
using ImageAnalysis.Infrastructure.Persistence.Repositories;
using ImageAnalysis.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImageAnalysis.Infrastructure;

/// <summary>
/// Registro das implementações da Infrastructure. A API chama apenas AddInfrastructure,
/// sem conhecer DbContext ou repositórios concretos.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string postgresConnectionString, string storageRootPath)
    {
        services.AddDbContext<ImageAnalysisDbContext>(options =>
            options.UseNpgsql(postgresConnectionString));

        services.AddScoped<IAnalysisRepository, AnalysisRepository>();

        // Sem estado além da pasta raiz, então uma única instância atende toda a aplicação.
        services.AddSingleton<IImageStorage>(new LocalImageStorage(storageRootPath));

        return services;
    }
}
