using ImageAnalysis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ImageAnalysis.Tests.Infrastructure.Persistence;

/// <summary>
/// Sobe um PostgreSQL descartável (Testcontainers) e aplica as migrations,
/// compartilhado entre os testes da coleção "Postgres". Requer Docker em execução.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    // Cada chamada cria um contexto novo, sem cache do change tracker,
    // para que as leituras venham de fato do banco.
    public ImageAnalysisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ImageAnalysisDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        return new ImageAnalysisDbContext(options);
    }
}

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
}
