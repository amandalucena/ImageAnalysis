using ImageAnalysis.Domain.Interfaces;
using ImageAnalysis.Infrastructure.Messaging;
using ImageAnalysis.Infrastructure.Persistence;
using ImageAnalysis.Infrastructure.Persistence.Repositories;
using ImageAnalysis.Infrastructure.Storage;
using ImageAnalysis.Infrastructure.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImageAnalysis.Infrastructure;

/// <summary>
/// Registro das implementações da Infrastructure. A API chama apenas AddInfrastructure,
/// sem conhecer DbContext ou repositórios concretos.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string postgresConnectionString,
        string storageRootPath,
        string rabbitMqConnectionString)
    {
        services.AddDbContext<ImageAnalysisDbContext>(options =>
            options.UseNpgsql(postgresConnectionString));

        services.AddScoped<IAnalysisRepository, AnalysisRepository>();

        // Sem estado além da pasta raiz, então uma única instância atende toda a aplicação.
        services.AddSingleton<IImageStorage>(new LocalImageStorage(storageRootPath));

        // Conexão RabbitMQ compartilhada, aberta na primeira utilização. Os canais é que são
        // abertos por uso: um por publicação e um para o Worker.
        services.AddSingleton(new RabbitMqConnectionProvider(rabbitMqConnectionString));

        services.AddSingleton<IAnalysisQueue, RabbitMqAnalysisQueue>();

        return services;
    }

    // Separado de AddInfrastructure para que o host decida se consome a fila.
    public static IServiceCollection AddAnalysisWorker(this IServiceCollection services)
    {
        services.AddHostedService<AnalysisWorker>();

        return services;
    }
}
