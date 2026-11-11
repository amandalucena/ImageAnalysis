using RabbitMQ.Client;

namespace ImageAnalysis.Infrastructure.Messaging;

/// <summary>
/// Fila de análises no RabbitMQ. As mensagens são publicadas no exchange padrão, que
/// entrega direto na fila cujo nome é a routing key.
/// </summary>
internal static class AnalysisQueueTopology
{
    public const string QueueName = "analysis.process";

    // Declarada por quem publica e por quem consome (a operação é idempotente), para que
    // nenhum dos lados dependa de o outro ter iniciado antes. Durable: sobrevive a um
    // restart do broker, junto com as mensagens persistentes.
    public static Task DeclareAsync(IChannel channel, CancellationToken cancellationToken) =>
        channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);
}
