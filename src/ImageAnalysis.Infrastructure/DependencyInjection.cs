using ImageAnalysis.Application.Metadata;
using ImageAnalysis.Domain.Interfaces;
using ImageAnalysis.Infrastructure.Caching;
using ImageAnalysis.Infrastructure.ExternalTools.ExifTool;
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
        string redisConnectionString,
        string storageRootPath,
        string rabbitMqConnectionString,
        ExifToolOptions exifToolOptions)
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

        // Conexão Redis compartilhada, aberta na primeira utilização. Registrada por factory para
        // que o container a descarte no encerramento do host.
        services.AddSingleton(_ => new RedisConnectionProvider(redisConnectionString));

        services.AddSingleton<IAnalysisCache, RedisAnalysisCache>();

        // Sem estado: cada extração inicia o seu próprio processo do ExifTool.
        services.AddSingleton<IMetadataExtractor>(new ExifToolMetadataExtractor(exifToolOptions));

        return services;
    }

    // Separado de AddInfrastructure para que o host decida se consome a fila.
    public static IServiceCollection AddAnalysisWorker(this IServiceCollection services)
    {
        services.AddHostedService<AnalysisWorker>();

        return services;
    }
}
