using ImageAnalysis.Application.Exceptions;
using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.ValueObjects;

namespace ImageAnalysis.Application.Metadata;

/// <summary>
/// Regras que transformam metadados normalizados em achados. Funções puras: não leem
/// arquivo nem chamam ferramenta, o que permite testá-las com NormalizedMetadata montado à mão.
/// Cada regra devolve null quando não há nada a relatar.
/// </summary>
public static class MetadataFindingRules
{
    /// <summary>
    /// Aplica todas as regras e junta os achados. A ordem é fixa para que o laudo e os testes
    /// sejam estáveis; uma regra nova entra aqui.
    /// </summary>
    public static AnalyzerResult Evaluate(NormalizedMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        AnalyzerFinding?[] findings =
        [
            AiGenerator(metadata),
            EditingSoftware(metadata),
            InconsistentDates(metadata),
            StrippedMetadata(metadata),
            MetadataUnavailable(metadata),
            ReadWarnings(metadata)
        ];

        var found = findings.OfType<AnalyzerFinding>().ToArray();
        return found.Length == 0 ? AnalyzerResult.Empty : new AnalyzerResult(found);
    }

    /// <summary>
    /// Software de edição em Software/CreatorTool. Editor é Medium, não High: Lightroom e
    /// apps de celular também fazem ajustes legítimos. Valores divergentes entre as fontes
    /// reforçam o achado (Low), mas sozinhos não o geram.
    /// </summary>
    public static AnalyzerFinding? EditingSoftware(NormalizedMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var evidences = DistinctValues(metadata.Software, KnownSoftware.IsEditor)
            .Select(group => new AnalyzerEvidence(
                MetadataEvidenceNames.EditingSoftwareTag,
                $"{Sources(group)} {Verb(group)} editor de imagem: {FileValue(group.Value)}",
                EvidenceStrength.Medium,
                EvidenceDirection.SupportsManipulation))
            .ToList();

        if (evidences.Count == 0)
            return null;

        if (HasDivergentValues(metadata.Software))
        {
            var sources = string.Join("; ", metadata.Software.Select(tag => $"{tag.Source} = {FileValue(tag.Value)}"));

            evidences.Add(new AnalyzerEvidence(
                MetadataEvidenceNames.SoftwareDivergence,
                $"As fontes de software dos metadados divergem: {sources}",
                EvidenceStrength.Low,
                EvidenceDirection.SupportsManipulation));
        }

        return new AnalyzerFinding(
            MetadataFindingNames.EditingSoftware,
            "Os metadados indicam que a imagem passou por um editor de imagem.",
            evidences);
    }

    /// <summary>
    /// Gerador de imagem por IA em Software/CreatorTool. É o sinal mais direto que os
    /// metadados oferecem (High), mas não prova: metadados podem ser forjados, e quem pondera
    /// isso é o Evidence Aggregator (#43).
    /// </summary>
    public static AnalyzerFinding? AiGenerator(NormalizedMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var evidences = DistinctValues(metadata.Software, KnownSoftware.IsAiGenerator)
            .Select(group => new AnalyzerEvidence(
                MetadataEvidenceNames.AiGeneratorTag,
                $"{Sources(group)} {Verb(group)} gerador de imagem por IA: {FileValue(group.Value)}",
                EvidenceStrength.High,
                EvidenceDirection.SupportsManipulation))
            .ToList();

        if (evidences.Count == 0)
            return null;

        return new AnalyzerFinding(
            MetadataFindingNames.AiGenerator,
            "Os metadados indicam que a imagem foi criada ou alterada por um gerador de imagem por IA.",
            evidences);
    }

    // Um sinal isolado de remoção é comum em apps legítimos; só a combinação vira achado.
    public const int StrippedMetadataMinSignals = 2;

    /// <summary>
    /// Nenhum metadado de captura no arquivo. Registrado como Neutral: ausência de metadata não é,
    /// por si só, evidência de manipulação (CLAUDE.md, seção 9). JFIF, dimensões reais e avisos
    /// descrevem a estrutura do arquivo, não a captura, e não contam aqui.
    /// </summary>
    public static AnalyzerFinding? MetadataUnavailable(NormalizedMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (HasCaptureMetadata(metadata))
            return null;

        return new AnalyzerFinding(
            MetadataFindingNames.MetadataUnavailable,
            "A imagem não contém metadados de captura.",
            [
                new AnalyzerEvidence(
                    MetadataEvidenceNames.MetadataAbsent,
                    "Nenhum metadado de captura foi encontrado (câmera, datas, software, GPS, miniatura). " +
                    "Isso é comum e não indica manipulação: PNG, GIF e BMP normalmente não têm EXIF, " +
                    "capturas de tela também não, e aplicativos de mensagem e redes sociais removem " +
                    "os metadados no envio.",
                    EvidenceStrength.Low,
                    EvidenceDirection.Neutral)
            ]);
    }

    /// <summary>
    /// EXIF parcial: o arquivo tem um bloco EXIF (datas ou dimensões declaradas), mas faltam os
    /// campos que identificam a câmera — típico de remoção seletiva de tags. Sinal fraco (Low) e só
    /// com pelo menos StrippedMetadataMinSignals sinais. Nunca dispara junto com MetadataUnavailable.
    /// Não se aplica a scanners, que não gravam identidade de câmera nem miniatura.
    /// "editor-without-camera" soma de propósito com EditingSoftware: descreve outra coisa
    /// (a identidade da câmera foi apagada).
    /// </summary>
    public static AnalyzerFinding? StrippedMetadata(NormalizedMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        // Software sozinho não é EXIF: um PNG exportado com só o CreatorTool no XMP nunca teve
        // bloco EXIF, e tratá-lo como "removido" acusaria ausência como manipulação (seção 9).
        if (!HasExifBlock(metadata) || Device(metadata) == CaptureDevice.Scanner)
            return null;

        var cameraMissing = metadata.Make is null && metadata.Model is null;
        var evidences = new List<AnalyzerEvidence>();

        if (cameraMissing)
            evidences.Add(StrippedEvidence(
                MetadataEvidenceNames.CameraIdentityMissing,
                "O arquivo tem metadados EXIF, mas não informa fabricante (Make) nem modelo (Model) da câmera."));

        // Só conta quando o EXIF tem cara de captura por câmera (data de captura e dimensões
        // declaradas). Sem isso, a falta da miniatura acompanha qualquer arquivo que não veio de
        // uma câmera e seria só um reflexo de "câmera ausente", contando o mesmo fato duas vezes.
        if (!metadata.HasThumbnail && metadata.DateTimeOriginal is not null && metadata.DeclaredDimensions is not null)
            evidences.Add(StrippedEvidence(
                MetadataEvidenceNames.ThumbnailMissing,
                "O arquivo tem metadados de captura (DateTimeOriginal e dimensões declaradas), mas não tem " +
                "a miniatura (thumbnail) que câmeras costumam gravar junto com eles."));

        var editor = metadata.Software.FirstOrDefault(tag => KnownSoftware.IsEditor(tag.Value));
        if (cameraMissing && editor is not null)
            evidences.Add(StrippedEvidence(
                MetadataEvidenceNames.EditorWithoutCamera,
                $"{editor.Source} indica editor de imagem ({FileValue(editor.Value)}), mas a identificação da câmera " +
                "não está presente: ela pode ter sido apagada durante a edição."));

        if (evidences.Count < StrippedMetadataMinSignals)
            return null;

        return new AnalyzerFinding(
            MetadataFindingNames.StrippedMetadata,
            "Parte dos metadados parece ter sido removida seletivamente.",
            evidences);
    }

    /// <summary>
    /// Avisos do ExifTool ao ler o arquivo, registrados como fato (Neutral). Não interpreta a
    /// estrutura: isso é do analisador de estrutura JPEG, que faz a própria análise dos marcadores
    /// e não deve reaproveitar estes avisos. O Evidence Aggregator cruza os dois.
    /// </summary>
    public static AnalyzerFinding? ReadWarnings(NormalizedMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (metadata.Warnings.Count == 0)
            return null;

        var evidences = metadata.Warnings.Select(warning => new AnalyzerEvidence(
            MetadataEvidenceNames.ExifToolWarning,
            $"O ExifTool emitiu um aviso ao ler o arquivo: {FileValue(warning)}",
            EvidenceStrength.Low,
            EvidenceDirection.Neutral));

        return new AnalyzerFinding(
            MetadataFindingNames.ReadWarnings,
            "A leitura dos metadados gerou avisos sobre a estrutura do arquivo.",
            evidences);
    }

    // Bloco EXIF de captura: datas (mesmo inválidas, a tag existe) ou dimensões declaradas.
    // Faz parte de HasCaptureMetadata, o que garante que StrippedMetadata (que exige este bloco)
    // nunca dispare junto com MetadataUnavailable.
    private static bool HasExifBlock(NormalizedMetadata metadata) =>
        metadata.DateTimeOriginal is not null
        || metadata.CreateDate is not null
        || metadata.ModifyDate is not null
        || metadata.DeclaredDimensions is not null;

    private static bool HasCaptureMetadata(NormalizedMetadata metadata) =>
        HasExifBlock(metadata)
        || metadata.Make is not null
        || metadata.Model is not null
        || metadata.Software.Count > 0
        || metadata.HasThumbnail
        || metadata.HasGps
        || metadata.CaptureDevice is not null;

    private static AnalyzerEvidence StrippedEvidence(string name, string description) =>
        new(name, description, EvidenceStrength.Low, EvidenceDirection.SupportsManipulation);

    // Tags gravadas pela câmera em instantes levemente diferentes podem divergir em 1-2 s.
    public static readonly TimeSpan DateTolerance = TimeSpan.FromSeconds(2);

    // Celulares e apps regravam o arquivo logo após a captura; abaixo disso não é edição.
    public static readonly TimeSpan ModifiedAfterCaptureThreshold = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Datas inconsistentes entre DateTimeOriginal, CreateDate e ModifyDate. Datas ausentes ou
    /// inválidas não são comparadas: ausência não é evidência (CLAUDE.md, seção 9).
    /// Em fotos escaneadas, DateTimeOriginal é a data da foto e CreateDate a do scan, então a
    /// diferença entre elas é esperada (Neutral) e a referência de captura passa a ser a CreateDate.
    /// </summary>
    public static AnalyzerFinding? InconsistentDates(NormalizedMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var device = Device(metadata);
        var evidences = new List<AnalyzerEvidence>();
        var notComparable = new List<(string Tag, MetadataDate Date)>();

        // (b) CreateDate x DateTimeOriginal
        var createVsOriginal = Difference(
            ("CreateDate", metadata.CreateDate), ("DateTimeOriginal", metadata.DateTimeOriginal), notComparable);
        if (createVsOriginal is { } diff && diff.Duration() > DateTolerance)
            evidences.Add(CreateOriginalEvidence(metadata, device));

        // (a) e (c) ModifyDate x captura
        var (captureTag, capture) = CaptureReference(metadata, device);
        if (Difference(("ModifyDate", metadata.ModifyDate), (captureTag, capture), notComparable) is { } sinceCapture)
        {
            if (sinceCapture < -DateTolerance)
                evidences.Add(new AnalyzerEvidence(
                    MetadataEvidenceNames.ModifyBeforeCapture,
                    $"ModifyDate ({metadata.ModifyDate!.Raw}) é anterior a {captureTag} ({capture!.Raw}): " +
                    "o arquivo não pode ter sido modificado antes de ser capturado.",
                    EvidenceStrength.Medium,
                    EvidenceDirection.SupportsManipulation));
            else if (sinceCapture > ModifiedAfterCaptureThreshold)
                evidences.Add(new AnalyzerEvidence(
                    MetadataEvidenceNames.ModifiedAfterCapture,
                    $"ModifyDate ({metadata.ModifyDate!.Raw}) é posterior a {captureTag} ({capture!.Raw}): " +
                    "o arquivo foi regravado depois da captura, o que pode indicar edição.",
                    EvidenceStrength.Low,
                    EvidenceDirection.SupportsManipulation));
        }

        // Datas que não puderam ser comparadas ficam registradas no laudo, uma vez cada.
        foreach (var (tag, date) in notComparable.DistinctBy(entry => entry.Tag))
            evidences.Add(new AnalyzerEvidence(
                MetadataEvidenceNames.DateNotComparable,
                $"{tag} ({date.Raw}, offset {FormatOffset(date.Offset!.Value)}) não pôde ser comparada: " +
                "convertida para UTC, ela sai do intervalo de datas representável. Isso costuma indicar " +
                "um valor forjado ou corrompido.",
                EvidenceStrength.Low,
                EvidenceDirection.Neutral));

        if (evidences.Count == 0)
            return null;

        return new AnalyzerFinding(
            MetadataFindingNames.InconsistentDates,
            "As datas registradas nos metadados não são consistentes entre si.",
            evidences);
    }

    private static AnalyzerEvidence CreateOriginalEvidence(NormalizedMetadata metadata, CaptureDevice? device)
    {
        var dates = $"CreateDate ({metadata.CreateDate!.Raw}) difere de DateTimeOriginal ({metadata.DateTimeOriginal!.Raw})";

        return device switch
        {
            CaptureDevice.Scanner => new AnalyzerEvidence(
                MetadataEvidenceNames.ScannedOriginalDate,
                $"{dates}. A imagem vem de um scanner: DateTimeOriginal é a data da foto original e " +
                "CreateDate a do scan, então a diferença é esperada e não indica manipulação.",
                EvidenceStrength.Low,
                EvidenceDirection.Neutral),
            CaptureDevice.Camera => new AnalyzerEvidence(
                MetadataEvidenceNames.CreateOriginalMismatch,
                $"{dates}. Em câmeras digitais as duas datas são iguais.",
                EvidenceStrength.Medium,
                EvidenceDirection.SupportsManipulation),
            _ => new AnalyzerEvidence(
                MetadataEvidenceNames.CreateOriginalMismatch,
                $"{dates}. Os metadados não informam se a imagem veio de câmera ou de scanner: em fotos " +
                "escaneadas essa diferença é esperada (data da foto original x data do scan).",
                EvidenceStrength.Low,
                EvidenceDirection.SupportsManipulation)
        };
    }

    // FileSource vence; sem ele, Make/Model de scanner conhecido.
    private static CaptureDevice? Device(NormalizedMetadata metadata) =>
        metadata.CaptureDevice
        ?? (KnownSoftware.IsScanner(metadata.Make) || KnownSoftware.IsScanner(metadata.Model)
            ? CaptureDevice.Scanner
            : null);

    // Câmera: DateTimeOriginal. Scanner: CreateDate (momento do scan). Cai para a outra se faltar.
    private static (string Tag, MetadataDate? Date) CaptureReference(NormalizedMetadata metadata, CaptureDevice? device)
    {
        var original = ("DateTimeOriginal", metadata.DateTimeOriginal);
        var create = ("CreateDate", metadata.CreateDate);

        var (first, second) = device == CaptureDevice.Scanner ? (create, original) : (original, create);
        return first.Item2?.Value is not null ? first : second;
    }

    // later - earlier. null se alguma não tem valor ou não é comparável. Com offset nas duas,
    // compara em UTC; senão compara sem fuso, porque a câmera grava todas no mesmo relógio local.
    // Uma data que sai do intervalo ao converter para UTC vai para notComparable.
    private static TimeSpan? Difference(
        (string Tag, MetadataDate? Date) later,
        (string Tag, MetadataDate? Date) earlier,
        List<(string Tag, MetadataDate Date)> notComparable)
    {
        if (later.Date?.Value is not { } laterValue || earlier.Date?.Value is not { } earlierValue)
            return null;

        if (later.Date.Offset is null || earlier.Date.Offset is null)
            return laterValue - earlierValue;

        try
        {
            return ToUtc(later.Date) - ToUtc(earlier.Date);
        }
        catch (MetadataDateOutOfRangeException ex)
        {
            notComparable.Add((ReferenceEquals(ex.Date, later.Date) ? later.Tag : earlier.Tag, ex.Date));
            return null;
        }
    }

    /// <exception cref="MetadataDateOutOfRangeException">O resultado sai do intervalo de DateTime.</exception>
    private static DateTime ToUtc(MetadataDate date)
    {
        // Em ticks para checar o intervalo antes: DateTime - TimeSpan lança ArgumentOutOfRangeException
        // genérica, que não distingue este caso de um bug.
        var utcTicks = date.Value!.Value.Ticks - date.Offset!.Value.Ticks;
        if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
            throw new MetadataDateOutOfRangeException(date);

        return new DateTime(utcTicks, DateTimeKind.Utc);
    }

    private static string FormatOffset(TimeSpan offset) =>
        $"{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm}";

    // Diferença só de maiúsculas não conta como divergência (o normalizador já tira o padding).
    private static bool HasDivergentValues(IReadOnlyList<SoftwareTag> software) =>
        software
            .Select(tag => tag.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Skip(1)
            .Any();

    // Tamanho máximo de um valor vindo do arquivo dentro de uma descrição. Com até três fontes
    // de software por evidência, a descrição fica bem abaixo de Evidence.MaxDescriptionLength.
    public const int MaxFileValueLength = 200;

    // Valores vindos do arquivo são controlados por quem enviou a imagem e podem ter qualquer
    // tamanho; sem corte, a descrição estouraria a coluna no banco e a análise nunca seria gravada.
    private static string FileValue(string value) =>
        value.Length <= MaxFileValueLength ? value : string.Concat(value.AsSpan(0, MaxFileValueLength), "…");

    // Agrupa tags com o mesmo valor (sem diferenciar maiúsculas): o Photoshop grava o mesmo texto
    // em IFD0:Software e XMP-xmp:CreatorTool, e isso é um fato só, não dois.
    private static IEnumerable<SoftwareGroup> DistinctValues(IReadOnlyList<SoftwareTag> software, Func<string, bool> matches) =>
        software
            .Where(tag => matches(tag.Value))
            .GroupBy(tag => tag.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SoftwareGroup(group.First().Value, group.Select(tag => tag.Source).ToArray()));

    private static string Sources(SoftwareGroup group) => string.Join(", ", group.Sources);

    private static string Verb(SoftwareGroup group) => group.Sources.Count == 1 ? "indica" : "indicam";

    private sealed record SoftwareGroup(string Value, IReadOnlyList<string> Sources);
}
