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
    // Intervalo entre tentativas de começar a consumir enquanto o RabbitMQ não responde.
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnalysisWorker> _logger;

    public AnalysisWorker(RabbitMqConnectionProvider connectionProvider, IServiceScopeFactory scopeFactory, ILogger<AnalysisWorker> logger)
    {
        _connectionProvider = connectionProvider;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Uma exceção que escapasse daqui encerraria o host (e a API junto), então falhas
        // para começar a consumir são registradas e tentadas de novo.
        IChannel channel;
        while (true)
        {
            try
            {
                channel = await StartConsumingAsync(stoppingToken);
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Não foi possível consumir a fila {Queue}; nova tentativa em {RetryDelay}.",
                    AnalysisQueueTopology.QueueName, RetryDelay);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }

        await using (channel)
        {
            // O consumo acontece nos eventos do canal; aqui só se espera o encerramento do host.
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
    }

    private async Task<IChannel> StartConsumingAsync(CancellationToken stoppingToken)
    {
        var connection = await _connectionProvider.GetConnectionAsync(stoppingToken);
        var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        try
        {
            await AnalysisQueueTopology.DeclareAsync(channel, stoppingToken);

            // Uma mensagem por vez: a próxima só é entregue depois do ack da anterior.
            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, delivery, stoppingToken);

            // autoAck: false — a mensagem só sai da fila quando o processamento termina.
            await channel.BasicConsumeAsync(AnalysisQueueTopology.QueueName, autoAck: false, consumer, stoppingToken);
        }
        catch
        {
            await channel.DisposeAsync();
            throw;
        }

        _logger.LogInformation("Worker consumindo a fila {Queue}.", AnalysisQueueTopology.QueueName);
        return channel;
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        if (!AnalysisMessage.TryDeserialize(delivery.Body.Span, out var message))
        {
            // Reprocessar não a tornaria válida.
            _logger.LogError("Mensagem inválida descartada da fila {Queue}.", AnalysisQueueTopology.QueueName);
            await SettleAsync(() => channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, CancellationToken.None), null);
            return;
        }

        ProcessImageAnalysisOutcome outcome;

        try
        {
            // Um escopo (e um DbContext) por mensagem, nunca compartilhado entre análises.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var processImageAnalysis = scope.ServiceProvider.GetRequiredService<ProcessImageAnalysis>();

            outcome = await processImageAnalysis.ExecuteAsync(
                new ProcessImageAnalysisCommand(message.AnalysisId), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento do host: sem ack, o broker devolve a mensagem à fila quando o canal fecha.
            return;
        }
        catch (Exception ex)
        {
            // Erro inesperado (ex.: banco indisponível). A mensagem volta para a fila uma vez;
            // se falhar de novo na reentrega, é descartada (ainda não há dead-letter queue).
            var requeue = !delivery.Redelivered;
            _logger.LogError(ex, "Erro ao tratar a análise {AnalysisId} (devolvida à fila: {Requeue}).", message.AnalysisId, requeue);
            await SettleAsync(() => channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue, CancellationToken.None), message.AnalysisId);
            return;
        }

        // Fora do try: uma falha no ack não é erro de processamento, a análise já foi gravada.
        _logger.LogInformation("Análise {AnalysisId} tratada pelo Worker: {Outcome}.", message.AnalysisId, outcome);
        await SettleAsync(() => channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None), message.AnalysisId);
    }

    // Ack, nack e reject falham se o canal ou a conexão fecharam. Nesse caso o broker reentrega
    // a mensagem, e a idempotência do ProcessImageAnalysis evita reprocessar a análise.
    private async Task SettleAsync(Func<ValueTask> settle, Guid? analysisId)
    {
        try
        {
            await settle();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível confirmar a mensagem da análise {AnalysisId}; ela será reentregue.", analysisId);
        }
    }
}
