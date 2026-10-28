using HealthChecks.UI.Client;
using ImageAnalysis.API.ErrorHandling;
using ImageAnalysis.Application;
using ImageAnalysis.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Erros no formato ProblemDetails (RFC 7807), sem expor detalhes internos.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres não configurada.");
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("ConnectionStrings:Redis não configurada.");
var rabbitMqConnectionString = builder.Configuration.GetConnectionString("RabbitMq")
    ?? throw new InvalidOperationException("ConnectionStrings:RabbitMq não configurada.");
var configuredStorageRootPath = builder.Configuration["Storage:RootPath"];
if (string.IsNullOrWhiteSpace(configuredStorageRootPath))
    throw new InvalidOperationException("Storage:RootPath não configurado.");

// Caminho relativo é resolvido a partir do content root (pasta do projeto), e não do diretório
// em que o processo foi iniciado, para que API e Worker apontem para a mesma pasta.
var storageRootPath = Path.GetFullPath(configuredStorageRootPath, builder.Environment.ContentRootPath);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(postgresConnectionString, storageRootPath);

// Conexão RabbitMQ compartilhada (resolvida na primeira utilização).
builder.Services.AddSingleton<IConnection>(_ =>
    new ConnectionFactory { Uri = new Uri(rabbitMqConnectionString) }
        .CreateConnectionAsync().GetAwaiter().GetResult());

builder.Services.AddHealthChecks()
    .AddNpgSql(postgresConnectionString, name: "postgres", tags: ["ready"])
    .AddRedis(redisConnectionString, name: "redis", tags: ["ready"])
    .AddRabbitMQ(name: "rabbitmq", tags: ["ready"]);

var app = builder.Build();

app.Logger.LogInformation("Storage de imagens em {StorageRootPath}", storageRootPath);

// Primeiro do pipeline, para capturar exceções de todas as etapas seguintes.
app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// Liveness: a API está de pé (não verifica dependências).
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

// Readiness: PostgreSQL, Redis e RabbitMQ acessíveis.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.Run();

// Expõe a classe gerada pelos top-level statements para o WebApplicationFactory dos testes.
public partial class Program;
