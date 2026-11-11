using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ImageAnalysis.Infrastructure.Messaging;

/// <summary>
/// Conteúdo da mensagem na fila: só o id, em JSON ({"analysisId":"..."}).
/// </summary>
internal sealed record AnalysisMessage(Guid AnalysisId)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);

    public static bool TryDeserialize(ReadOnlySpan<byte> body, [NotNullWhen(true)] out AnalysisMessage? message)
    {
        try
        {
            message = JsonSerializer.Deserialize<AnalysisMessage>(body, JsonOptions);
        }
        catch (JsonException)
        {
            message = null;
        }

        if (message?.AnalysisId == Guid.Empty)
            message = null;

        return message is not null;
    }
}
