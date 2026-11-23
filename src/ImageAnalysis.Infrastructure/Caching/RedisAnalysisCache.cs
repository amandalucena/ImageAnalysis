using System.Text.Json;
using System.Text.RegularExpressions;
using ImageAnalysis.Domain.Interfaces;
using StackExchange.Redis;

namespace ImageAnalysis.Infrastructure.Caching;

/// <summary>
/// IAnalysisCache sobre Redis (CLAUDE.md 14). Cada entrada fica em analysis:{id}:{entry}, com o
/// valor serializado em JSON e sempre com TTL. Um set de índice em analysis:{id}:_entries guarda
/// os nomes das entradas da análise, para que o RemoveAllAsync apague tudo sem varrer o keyspace
/// (KEYS/SCAN). Gravação, remoção e limpeza rodam em scripts Lua, então entrada e índice nunca
/// ficam dessincronizados por uma falha no meio da operação.
/// </summary>
public sealed partial class RedisAnalysisCache : IAnalysisCache
{
    // Sem ELA ainda, não há medida de quanto os dados intermediários precisam durar; 6h cobre com
    // folga uma análise em execução e impede que entradas de análises interrompidas fiquem para sempre.
    public static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromHours(6);

    // O índice tem o TTL da entrada mais longa já gravada, nunca menor: ele não pode expirar
    // antes das entradas que lista, ou o RemoveAllAsync deixaria entradas para trás.
    // PTTL devolve -1 (sem TTL) ou -2 (não existe) para um índice novo, menores que qualquer TTL.
    private const string SetScript = """
        redis.call('SET', KEYS[1], ARGV[1], 'PX', ARGV[2])
        redis.call('SADD', KEYS[2], ARGV[3])
        if redis.call('PTTL', KEYS[2]) < tonumber(ARGV[2]) then
            redis.call('PEXPIRE', KEYS[2], ARGV[2])
        end
        """;

    private const string RemoveScript = """
        redis.call('DEL', KEYS[1])
        redis.call('SREM', KEYS[2], ARGV[1])
        """;

    // As chaves das entradas são montadas dentro do script a partir do índice. Entradas que já
    // expiraram continuam listadas no índice; o DEL de uma chave inexistente não tem efeito.
    private const string RemoveAllScript = """
        local entries = redis.call('SMEMBERS', KEYS[1])
        for _, entry in ipairs(entries) do
            redis.call('DEL', ARGV[1] .. entry)
        end
        redis.call('DEL', KEYS[1])
        """;

    // O índice usa "_entries", fora do formato permitido para nomes de entrada, para que nenhuma
    // entrada possa sobrescrevê-lo.
    private const string IndexSuffix = "_entries";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly RedisConnectionProvider _connectionProvider;

    public RedisAnalysisCache(RedisConnectionProvider connectionProvider)
    {
        _connectionProvider = connectionProvider;
    }

    public async Task SetAsync<T>(
        Guid analysisId, string entry, T value, TimeSpan? timeToLive = null, CancellationToken cancellationToken = default)
    {
        ValidateEntry(entry);

        var ttl = timeToLive ?? DefaultTimeToLive;
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeToLive), "O TTL deve ser positivo.");

        // Arredonda para cima: um TTL abaixo de 1 ms viraria PX 0, que o Redis rejeita.
        var ttlMilliseconds = (long)Math.Ceiling(ttl.TotalMilliseconds);
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);

        var database = await _connectionProvider.GetDatabaseAsync(cancellationToken);
        await database.ScriptEvaluateAsync(
            SetScript,
            [EntryKey(analysisId, entry), IndexKey(analysisId)],
            [payload, ttlMilliseconds, entry]);
    }

    public async Task<T?> GetAsync<T>(Guid analysisId, string entry, CancellationToken cancellationToken = default)
    {
        ValidateEntry(entry);

        var database = await _connectionProvider.GetDatabaseAsync(cancellationToken);
        var payload = await database.StringGetAsync(EntryKey(analysisId, entry));

        return payload.IsNull
            ? default
            : JsonSerializer.Deserialize<T>((byte[])payload!, SerializerOptions);
    }

    public async Task RemoveAsync(Guid analysisId, string entry, CancellationToken cancellationToken = default)
    {
        ValidateEntry(entry);

        var database = await _connectionProvider.GetDatabaseAsync(cancellationToken);
        await database.ScriptEvaluateAsync(
            RemoveScript,
            [EntryKey(analysisId, entry), IndexKey(analysisId)],
            [entry]);
    }

    public async Task RemoveAllAsync(Guid analysisId, CancellationToken cancellationToken = default)
    {
        var database = await _connectionProvider.GetDatabaseAsync(cancellationToken);
        await database.ScriptEvaluateAsync(
            RemoveAllScript,
            [IndexKey(analysisId)],
            [KeyPrefix(analysisId)]);
    }

    private static string KeyPrefix(Guid analysisId) => $"analysis:{analysisId}:";

    private static string EntryKey(Guid analysisId, string entry) => KeyPrefix(analysisId) + entry;

    private static string IndexKey(Guid analysisId) => KeyPrefix(analysisId) + IndexSuffix;

    // Nomes curtos e previsíveis (ex.: "ela", "regions", "ela-stats"): sem ":" para não criar
    // níveis extras na chave e sem "_" para não colidir com o índice.
    private static void ValidateEntry(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!EntryPattern().IsMatch(entry))
            throw new ArgumentException(
                "O nome da entrada deve ter apenas letras minúsculas, dígitos, '-' ou '.', começando por letra ou dígito.",
                nameof(entry));
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{0,63}$")]
    private static partial Regex EntryPattern();
}
