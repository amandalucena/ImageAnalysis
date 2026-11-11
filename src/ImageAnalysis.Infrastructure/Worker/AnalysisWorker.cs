using ImageAnalysis.Application.UseCases.ProcessImageAnalysis;
using ImageAnalysis.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ImageAnalysis.Infrastructure.Worker;

/// <summary>
/// Consome a fila de análises e executa o ProcessImageAnalysis para cada mensagem.
/// Roda em segundo plano no processo que o registra (hoje, a API).
/// </summary>
public class AnalysisWorker : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnalysisWorker> _logger;

    public AnalysisWorker(IConnection connection, IServiceScopeFactory scopeFactory, ILogger<AnalysisWorker> logger)
    {
        _connection = connection;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await AnalysisQueueTopology.DeclareAsync(channel, stoppingToken);

        // Uma mensagem por vez: a próxima só é entregue depois do ack da anterior.
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, delivery, stoppingToken);

        // autoAck: false — a mensagem só sai da fila quando o processamento termina.
        await channel.BasicConsumeAsync(AnalysisQueueTopology.QueueName, autoAck: false, consumer, stoppingToken);

        _logger.LogInformation("Worker consumindo a fila {Queue}.", AnalysisQueueTopology.QueueName);

        // O consumo acontece nos eventos do canal; aqui só se espera o encerramento do host.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        if (!AnalysisMessage.TryDeserialize(delivery.Body.Span, out var message))
        {
            // Reprocessar não a tornaria válida.
            _logger.LogError("Mensagem inválida descartada da fila {Queue}.", AnalysisQueueTopology.QueueName);
            await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, CancellationToken.None);
            return;
        }

        try
        {
            // Um escopo (e um DbContext) por mensagem, nunca compartilhado entre análises.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var processImageAnalysis = scope.ServiceProvider.GetRequiredService<ProcessImageAnalysis>();

            var outcome = await processImageAnalysis.ExecuteAsync(
                new ProcessImageAnalysisCommand(message.AnalysisId), stoppingToken);

            _logger.LogInformation("Análise {AnalysisId} tratada pelo Worker: {Outcome}.", message.AnalysisId, outcome);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento do host: sem ack, o broker devolve a mensagem à fila quando o canal fecha.
        }
        catch (Exception ex)
        {
            // Erro inesperado (ex.: banco indisponível). A mensagem volta para a fila uma vez;
            // se falhar de novo na reentrega, é descartada (ainda não há dead-letter queue).
            var requeue = !delivery.Redelivered;
            _logger.LogError(ex, "Erro ao tratar a análise {AnalysisId} (devolvida à fila: {Requeue}).", message.AnalysisId, requeue);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue, CancellationToken.None);
        }
    }
}
