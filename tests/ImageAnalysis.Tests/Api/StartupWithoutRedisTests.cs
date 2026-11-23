using System.Net;
using ImageAnalysis.Domain.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ImageAnalysis.Tests.Api;

/// <summary>
/// A API precisa subir mesmo com o Redis fora do ar: a conexão só é aberta no primeiro uso do
/// cache. Não usa Docker; nenhuma dependência é acessada além da tentativa de conexão recusada.
/// </summary>
public class StartupWithoutRedisTests
{
    [Fact]
    public async Task ApiSobeEResponde_ComRedisInacessivel()
    {
        await using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CacheResolvidoSemConectar_FalhaSoAoSerUsado()
    {
        await using var factory = CreateFactory();

        // Resolver o cache não abre conexão; a falha aparece só na primeira operação.
        var cache = factory.Services.GetRequiredService<IAnalysisCache>();

        await Assert.ThrowsAnyAsync<Exception>(() => cache.RemoveAllAsync(Guid.NewGuid()));
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Porta 1 em localhost: a conexão é recusada imediatamente. O RabbitMQ também aponta
            // para uma porta recusada, para que o Worker não dependa de um RabbitMQ local.
            builder.UseSetting("ConnectionStrings:Redis", "localhost:1,connectTimeout=1000");
            builder.UseSetting("ConnectionStrings:RabbitMq", "amqp://guest:guest@localhost:1/");
            builder.UseSetting("Storage:RootPath", Path.Combine(Path.GetTempPath(), $"image-analysis-startup-tests-{Guid.NewGuid()}"));
        });
}
