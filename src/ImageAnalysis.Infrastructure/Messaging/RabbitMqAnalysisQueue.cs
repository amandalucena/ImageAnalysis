using ImageAnalysis.Domain.Interfaces;
using RabbitMQ.Client;

namespace ImageAnalysis.Infrastructure.Messaging;

public class RabbitMqAnalysisQueue : IAnalysisQueue
{
    private readonly IConnection _connection;

    public RabbitMqAnalysisQueue(IConnection connection)
    {
        _connection = connection;
    }

    public async Task PublishAsync(Guid analysisId, CancellationToken cancellationToken = default)
    {
        // Um canal por publicação: canais não devem ser compartilhados entre requisições
        // concorrentes, e o custo de abrir um é pequeno perto do upload da imagem.
        // Com confirmações rastreadas, BasicPublishAsync só retorna depois que o broker aceita
        // a mensagem e lança exceção se ela for recusada ou, com mandatory, não chegar a uma fila.
        await using var channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        await AnalysisQueueTopology.DeclareAsync(channel, cancellationToken);

        var properties = new BasicProperties
        {
            // Gravada em disco pelo broker, junto com a fila durável.
            Persistent = true,
            ContentType = "application/json",
            MessageId = analysisId.ToString(),
        };

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: AnalysisQueueTopology.QueueName,
            mandatory: true,
            basicProperties: properties,
            body: new AnalysisMessage(analysisId).Serialize(),
            cancellationToken: cancellationToken);
    }
}
