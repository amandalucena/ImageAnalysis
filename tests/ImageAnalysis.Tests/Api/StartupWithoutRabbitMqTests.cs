using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ImageAnalysis.Tests.Api;

/// <summary>
/// A API precisa subir mesmo com o RabbitMQ fora do ar: o Worker tenta consumir em segundo
/// plano, sem derrubar o processo. Não usa Docker; nenhuma dependência é acessada.
/// </summary>
public class StartupWithoutRabbitMqTests
{
    [Fact]
    public async Task ApiSobeEResponde_ComRabbitMqInacessivel()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Porta 1 em localhost: a conexão é recusada imediatamente.
            builder.UseSetting("ConnectionStrings:RabbitMq", "amqp://guest:guest@localhost:1/");
            builder.UseSetting("Storage:RootPath", Path.Combine(Path.GetTempPath(), $"image-analysis-startup-tests-{Guid.NewGuid()}"));
        });

        var response = await factory.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
