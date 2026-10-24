using ImageAnalysis.Domain.Interfaces;
using ImageAnalysis.Infrastructure.Persistence;
using ImageAnalysis.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImageAnalysis.Infrastructure;

/// <summary>
/// Registro das implementações da Infrastructure. A API chama apenas AddInfrastructure,
/// sem conhecer DbContext ou repositórios concretos.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string postgresConnectionString)
    {
        services.AddDbContext<ImageAnalysisDbContext>(options =>
            options.UseNpgsql(postgresConnectionString));

        services.AddScoped<IAnalysisRepository, AnalysisRepository>();

        return services;
    }
}
