using ImageAnalysis.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace ImageAnalysis.Tests.Api;

/// <summary>
/// Sobe a API em memória, com o Worker, um PostgreSQL e um RabbitMQ descartáveis
/// (Testcontainers) e uma pasta temporária como storage, compartilhada entre os testes da
/// coleção "Api". Requer Docker.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .Build();

    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management")
        .Build();

    public string StorageRootPath { get; } = Path.Combine(Path.GetTempPath(), $"image-analysis-api-tests-{Guid.NewGuid()}");

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_container.StartAsync(), _rabbitMqContainer.StartAsync());

        var options = new DbContextOptionsBuilder<ImageAnalysisDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        await using var dbContext = new ImageAnalysisDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    // UseSetting (e não ConfigureAppConfiguration) porque o Program.cs lê a configuração
    // antes do Build, e só as configurações aplicadas assim já estão visíveis nesse momento.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", _container.GetConnectionString());
        builder.UseSetting("ConnectionStrings:RabbitMq", _rabbitMqContainer.GetConnectionString());
        builder.UseSetting("Storage:RootPath", StorageRootPath);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _container.DisposeAsync();
        await _rabbitMqContainer.DisposeAsync();

        if (Directory.Exists(StorageRootPath))
            Directory.Delete(StorageRootPath, recursive: true);
    }
}

[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
}
