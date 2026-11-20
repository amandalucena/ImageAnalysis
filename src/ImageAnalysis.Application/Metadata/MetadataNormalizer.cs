using System.Globalization;
using System.Text.Json;
using ImageAnalysis.Domain.ValueObjects;

namespace ImageAnalysis.Application.Metadata;

/// <summary>
/// Extrai o NormalizedMetadata do dump bruto (formato descrito em RawMetadata).
/// Um campo inválido nunca derruba a normalização: vira null e o restante segue.
/// </summary>
public static class MetadataNormalizer
{
    // Em cada lista, a primeira tag presente vence. O EXIF (IFD0) vem antes do XMP porque é
    // gravado pela câmera, enquanto o XMP costuma ser escrito por software.
    private static readonly string[] MakeTags = ["IFD0:Make", "XMP-tiff:Make"];
    private static readonly string[] ModelTags = ["IFD0:Model", "XMP-tiff:Model"];

    // Aqui não há prioridade: todas as presentes entram, na ordem da lista.
    private static readonly string[] SoftwareTags = ["IFD0:Software", "XMP-tiff:Software", "XMP-xmp:CreatorTool"];

    // Coordenadas, e não qualquer tag GPS: há câmeras que gravam o bloco GPS só com GPSVersionID.
    // Tags Composite (ex.: Composite:GPSPosition) não entram: são deduzidas pelo ExifTool.
    private static readonly string[] GpsCoordinateTags =
        ["GPS:GPSLatitude", "GPS:GPSLongitude", "XMP-exif:GPSLatitude", "XMP-exif:GPSLongitude"];

    private static readonly string[] ThumbnailTags = ["IFD1:ThumbnailImage", "IFD1:ThumbnailLength"];

    // JPEG e BMP usam o grupo File; PNG e GIF, o grupo do próprio formato.
    private static readonly (string Width, string Height)[] DimensionTags =
    [
        ("File:ImageWidth", "File:ImageHeight"),
        ("PNG:ImageWidth", "PNG:ImageHeight"),
        ("GIF:ImageWidth", "GIF:ImageHeight")
    ];

    private const string DeclaredWidthTag = "ExifIFD:ExifImageWidth";
    private const string DeclaredHeightTag = "ExifIFD:ExifImageHeight";
    private const string JfifGroupPrefix = "JFIF:";
    private const string JfifVersionTag = "JFIF:JFIFVersion";
    private const string WarningTag = "ExifTool:Warning";

    // Formato das datas EXIF: sem fuso e com ":" também na data.
    private const string ExifDateFormat = "yyyy:MM:dd HH:mm:ss";

    // Padding comum em campos ASCII do EXIF.
    private static readonly char[] PaddingChars = [' ', '\0', '\t', '\r', '\n'];

    public static NormalizedMetadata Normalize(RawMetadata raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var tags = ReadTags(raw.Json);

        return new NormalizedMetadata
        {
            Make = FirstText(tags, MakeTags),
            Model = FirstText(tags, ModelTags),
            Software = SoftwareTags
                .Select(tag => (Tag: tag, Value: Text(tags, tag)))
                .Where(s => s.Value is not null)
                .Select(s => new SoftwareTag(s.Tag, s.Value!))
                .ToArray(),
            // Cada data com o offset que o EXIF 2.31 define para ela.
            DateTimeOriginal = Date(tags, "ExifIFD:DateTimeOriginal", "ExifIFD:OffsetTimeOriginal"),
            CreateDate = Date(tags, "ExifIFD:CreateDate", "ExifIFD:OffsetTimeDigitized"),
            ModifyDate = Date(tags, "IFD0:ModifyDate", "ExifIFD:OffsetTime"),
            HasJfif = tags.Keys.Any(key => key.StartsWith(JfifGroupPrefix, StringComparison.Ordinal)),
            JfifVersion = Text(tags, JfifVersionTag),
            HasThumbnail = ThumbnailTags.Any(tags.ContainsKey),
            HasGps = GpsCoordinateTags.Any(tag => Text(tags, tag) is not null),
            Dimensions = DimensionTags
                .Select(pair => Dimensions(tags, pair.Width, pair.Height))
                .FirstOrDefault(dimensions => dimensions is not null),
            DeclaredDimensions = Dimensions(tags, DeclaredWidthTag, DeclaredHeightTag),
            Warnings = Warnings(tags)
        };
    }

    private static Dictionary<string, JsonElement> ReadTags(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Array
                || root.GetArrayLength() != 1
                || root[0].ValueKind != JsonValueKind.Object)
                throw new ArgumentException("O dump de metadados não é um array JSON com um único objeto.", nameof(json));

            var tags = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in root[0].EnumerateObject())
                tags.TryAdd(property.Name, property.Value.Clone());

            return tags;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("O dump de metadados não é um JSON válido.", nameof(json), ex);
        }
    }

    private static string? FirstText(Dictionary<string, JsonElement> tags, string[] candidates) =>
        candidates.Select(tag => Text(tags, tag)).FirstOrDefault(value => value is not null);

    // O ExifTool emite como número tudo o que parece número (ex.: JFIFVersion 1.01, um Model "5"),
    // então campos de texto aceitam os dois. O texto do número é mantido como veio ("1.10", não "1.1").
    // Listas e objetos (tags XMP com vários valores) não são texto simples e ficam de fora.
    private static string? Text(Dictionary<string, JsonElement> tags, string tag)
    {
        if (!tags.TryGetValue(tag, out var value))
            return null;

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };

        return Clean(text);
    }

    private static string? Clean(string? text)
    {
        var cleaned = text?.Trim(PaddingChars);
        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
    }

    private static MetadataDate? Date(Dictionary<string, JsonElement> tags, string dateTag, string offsetTag)
    {
        var raw = Text(tags, dateTag);
        if (raw is null)
            return null;

        // DateTimeStyles.None mantém DateTimeKind.Unspecified: o valor não é tratado como UTC
        // nem como horário do servidor.
        DateTime? value = DateTime.TryParseExact(raw, ExifDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

        return new MetadataDate(raw, value, Offset(Text(tags, offsetTag)));
    }

    // Formato "+HH:MM" / "-HH:MM", dentro da faixa de fusos reais (-12:00 a +14:00).
    private static TimeSpan? Offset(string? text)
    {
        if (text is not { Length: 6 } || text[0] is not ('+' or '-') || text[3] != ':')
            return null;

        if (!int.TryParse(text.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(text.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || minutes >= 60)
            return null;

        var offset = new TimeSpan(hours, minutes, 0);
        if (text[0] == '-')
            offset = offset.Negate();

        return offset >= TimeSpan.FromHours(-12) && offset <= TimeSpan.FromHours(14) ? offset : null;
    }

    private static ImageDimensions? Dimensions(Dictionary<string, JsonElement> tags, string widthTag, string heightTag)
    {
        var width = PositiveInt(tags, widthTag);
        var height = PositiveInt(tags, heightTag);

        return width is not null && height is not null ? new ImageDimensions(width.Value, height.Value) : null;
    }

    private static int? PositiveInt(Dictionary<string, JsonElement> tags, string tag)
    {
        var text = Text(tags, tag);

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;
    }

    // Sem a opção -a, o ExifTool mostra um aviso por execução; a lista aceita mais de um
    // para não depender disso.
    private static IReadOnlyList<string> Warnings(Dictionary<string, JsonElement> tags)
    {
        if (!tags.TryGetValue(WarningTag, out var value))
            return [];

        var warnings = value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => Clean(item.GetString()))
            : [Text(tags, WarningTag)];

        return warnings.OfType<string>().ToArray();
    }
}
