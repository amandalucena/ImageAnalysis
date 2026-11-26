using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.ValueObjects;

namespace ImageAnalysis.Application.Jpeg;

/// <summary>
/// Regras que transformam a estrutura JPEG em achados. Funções puras sobre o JpegStructure, como
/// as MetadataFindingRules; cada regra devolve null quando não há nada a relatar.
/// As forças são conservadoras (só Low): a estrutura sozinha raramente distingue edição de um
/// encoder com bug, e os limiares ainda não foram validados com o dataset (CLAUDE.md 24).
/// Qualidade estimada e dupla compressão pelas tabelas de quantização ficam na #23; o
/// cruzamento com os metadados, no Evidence Aggregator (#43).
/// </summary>
public static class JpegStructureFindingRules
{
    // Um arquivo malicioso pode gerar milhares de problemas; cada evidência vira uma linha no
    // banco. As que passam do limite são resumidas numa evidência final.
    public const int MaxEvidencesPerFinding = 20;

    // Valores vindos do arquivo (comentários, identificadores) nas descrições, bem abaixo de
    // Evidence.MaxDescriptionLength.
    public const int MaxFileValueLength = 200;

    // Assinaturas de bibliotecas e editores no COM. Lista inicial, a ampliar com o dataset.
    private static readonly (string Signature, string Encoder)[] CommentSignatures =
    [
        ("gd-jpeg", "a biblioteca GD (libgd)"),
        ("CREATOR:", "uma biblioteca baseada na IJG/libjpeg"),
        ("GIMP", "o GIMP"),
        ("LEAD Technologies", "o LEADTOOLS"),
    ];

    /// <summary>
    /// Aplica todas as regras e junta os achados, numa ordem fixa para que o laudo e os testes
    /// sejam estáveis. Um arquivo sem SOI não é JPEG e não gera achados de estrutura: quem decide
    /// se o analisador se aplica é o JpegStructureAnalyzer (22.5).
    /// </summary>
    public static AnalyzerResult Evaluate(JpegStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);

        if (structure.Issues.Any(i => i.Kind == JpegReadIssueKind.MissingStartOfImage))
            return AnalyzerResult.Empty;

        AnalyzerFinding?[] findings =
        [
            Truncated(structure),
            MalformedStructure(structure),
            UnexpectedMarkers(structure),
            TrailingData(structure),
            EditorSignatures(structure),
            EncodingProfile(structure),
        ];

        var found = findings.OfType<AnalyzerFinding>().ToArray();
        return found.Length == 0 ? AnalyzerResult.Empty : new AnalyzerResult(found);
    }

    /// <summary>
    /// Arquivo que termina antes do EOI. Neutral: indica falha de integridade (download
    /// incompleto, arquivo recuperado), não edição.
    /// </summary>
    public static AnalyzerFinding? Truncated(JpegStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);

        var evidences = structure.Issues
            .Where(i => i.Kind == JpegReadIssueKind.TruncatedFile)
            .Select(i => new AnalyzerEvidence(
                JpegStructureEvidenceNames.TruncatedFile,
                $"{i.Description} (posição {i.Offset}).",
                EvidenceStrength.Low,
                EvidenceDirection.Neutral))
            .ToList();

        // A leitura também para sem EOI por comprimento inválido ou bytes soltos até o fim.
        if (evidences.Count == 0 && !structure.HasEndOfImage)
        {
            evidences.Add(new AnalyzerEvidence(
                JpegStructureEvidenceNames.MissingEndOfImage,
                "A leitura da estrutura terminou antes de encontrar o marcador EOI.",
                EvidenceStrength.Low,
                EvidenceDirection.Neutral));
        }

        if (evidences.Count == 0)
            return null;

        return new AnalyzerFinding(
            JpegStructureFindingNames.Truncated,
            "O arquivo JPEG termina antes do marcador de fim da imagem (EOI).",
            Limit(evidences));
    }

    /// <summary>
    /// Problemas de leitura da estrutura. Bytes soltos no meio do arquivo apoiam manipulação
    /// (Low): edição feita direto nos bytes sem ajustar os segmentos produz isso. Bytes soltos
    /// logo antes do EOI e os demais problemas são Neutral, porque encoders com bug são comuns.
    /// </summary>
    public static AnalyzerFinding? MalformedStructure(JpegStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);

        var evidences = structure.Issues
            .Select(MalformedEvidence)
            .OfType<AnalyzerEvidence>()
            .ToList();

        if (evidences.Count == 0)
            return null;

        return new AnalyzerFinding(
            JpegStructureFindingNames.MalformedStructure,
            "A estrutura do arquivo JPEG tem segmentos malformados ou dados fora dos segmentos.",
            Limit(evidences));
    }

    private static AnalyzerEvidence? MalformedEvidence(JpegReadIssue issue) => issue.Kind switch
    {
        JpegReadIssueKind.UnexpectedData => new AnalyzerEvidence(
            JpegStructureEvidenceNames.UnexpectedData,
            issue.MarkerCode is { } code
                ? $"{issue.Length} bytes fora de qualquer segmento na posição {issue.Offset}, antes do marcador {FormatMarker(code)}."
                : $"{issue.Length} bytes fora de qualquer segmento na posição {issue.Offset}, até o fim do arquivo.",
            EvidenceStrength.Low,
            // No meio do arquivo é o que uma edição nos bytes deixa; antes do EOI ou até o fim,
            // é mais provável bug do encoder ou arquivo corrompido.
            issue.MarkerCode is { } marker && marker != (byte)JpegMarker.Eoi
                ? EvidenceDirection.SupportsManipulation
                : EvidenceDirection.Neutral),

        JpegReadIssueKind.InvalidSegmentLength => new AnalyzerEvidence(
            JpegStructureEvidenceNames.InvalidSegmentLength,
            $"{issue.Description} (posição {issue.Offset}).",
            EvidenceStrength.Low,
            EvidenceDirection.Neutral),

        JpegReadIssueKind.MalformedSegment => new AnalyzerEvidence(
            JpegStructureEvidenceNames.MalformedSegment,
            $"{issue.Description} (posição {issue.Offset}).",
            EvidenceStrength.Low,
            EvidenceDirection.Neutral),

        JpegReadIssueKind.UnknownMarker => new AnalyzerEvidence(
            JpegStructureEvidenceNames.UnknownMarker,
            $"{issue.Description} (posição {issue.Offset}).",
            EvidenceStrength.Low,
            EvidenceDirection.Neutral),

        // TruncatedFile fica no jpeg.truncated; MissingStartOfImage nem chega às regras.
        _ => null,
    };

    /// <summary>
    /// Marcadores em posição ou quantidade que nenhum encoder comum produz. Apoiam manipulação
    /// (Low), exceto quando aparecem depois de bytes soltos: aí a leitura pode ter recomeçado
    /// dentro de outro segmento (ex.: a miniatura do EXIF), e a evidência é Neutral.
    /// </summary>
    public static AnalyzerFinding? UnexpectedMarkers(JpegStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);

        var evidences = new List<AnalyzerEvidence>();
        var segments = structure.Segments;

        foreach (var soi in segments.Where(s => s.Marker == JpegMarker.Soi).Skip(1))
        {
            evidences.Add(MarkerEvidence(
                structure, JpegStructureEvidenceNames.RepeatedStartOfImage, soi.Offset,
                $"O marcador de início da imagem (SOI) aparece de novo na posição {soi.Offset}."));
        }

        // Só o JPEG hierárquico (com DHP) tem vários frames.
        var frames = segments.Where(s => JpegMarkers.IsStartOfFrame(s.MarkerCode)).ToArray();
        if (frames.Length > 1 && !segments.Any(s => s.Marker == JpegMarker.Dhp))
        {
            evidences.Add(MarkerEvidence(
                structure, JpegStructureEvidenceNames.MultipleFrames, frames[1].Offset,
                $"O arquivo tem {frames.Length} cabeçalhos de frame (SOF) sem ser um JPEG hierárquico."));
        }

        // O leitor não registra os RST de dentro do scan; os que estão nos segmentos estão fora.
        foreach (var restart in segments.Where(s => JpegMarkers.IsRestart(s.MarkerCode)))
        {
            evidences.Add(MarkerEvidence(
                structure, JpegStructureEvidenceNames.RestartOutsideScan, restart.Offset,
                $"Marcador de reinício {FormatMarker(restart.MarkerCode)} fora dos dados comprimidos, na posição {restart.Offset}."));
        }

        var firstFrame = frames.FirstOrDefault();
        var firstScan = segments.FirstOrDefault(s => s.Marker == JpegMarker.Sos);
        if (firstScan is not null && (firstFrame is null || firstScan.Offset < firstFrame.Offset))
        {
            evidences.Add(MarkerEvidence(
                structure, JpegStructureEvidenceNames.ScanBeforeFrame, firstScan.Offset,
                $"O início do scan (SOS) na posição {firstScan.Offset} vem antes de qualquer cabeçalho de frame (SOF)."));
        }

        if (structure.HasEndOfImage && firstScan is null)
        {
            evidences.Add(MarkerEvidence(
                structure, JpegStructureEvidenceNames.ImageWithoutScan, structure.EndOfImageOffset!.Value,
                "O arquivo chega ao fim da imagem (EOI) sem nenhum scan com dados da imagem."));
        }

        if (structure.Frames.FirstOrDefault() is { } frame)
        {
            var defined = structure.QuantizationTables.Select(t => t.Id).ToHashSet();
            var offset = firstFrame?.Offset ?? 0;
            foreach (var id in frame.Components.Select(c => c.QuantizationTableId).Distinct().Where(id => !defined.Contains(id)))
            {
                evidences.Add(MarkerEvidence(
                    structure, JpegStructureEvidenceNames.UndefinedQuantizationTable, offset,
                    $"O frame usa a tabela de quantização {id}, que não é definida em nenhum segmento DQT."));
            }
        }

        if (evidences.Count == 0)
            return null;

        return new AnalyzerFinding(
            JpegStructureFindingNames.UnexpectedMarkers,
            "A estrutura do arquivo JPEG tem marcadores fora da ordem ou da quantidade esperada.",
            Limit(evidences));
    }

    private static AnalyzerEvidence MarkerEvidence(JpegStructure structure, string name, long offset, string description)
    {
        var afterResync = structure.Issues.Any(i => i.Kind == JpegReadIssueKind.UnexpectedData && i.Offset < offset);

        return afterResync
            ? new AnalyzerEvidence(
                name,
                $"{description} Ocorre depois de bytes fora dos segmentos, então a leitura deste trecho pode ter recomeçado dentro de outro segmento.",
                EvidenceStrength.Low,
                EvidenceDirection.Neutral)
            : new AnalyzerEvidence(name, description, EvidenceStrength.Low, EvidenceDirection.SupportsManipulation);
    }

    /// <summary>
    /// Bytes depois do EOI, classificados pelo início. Neutral quando é o esperado: a segunda
    /// imagem declarada no MPF, o vídeo de uma foto com movimento ou preenchimento com zeros.
    /// Uma imagem não declarada ou um conteúdo desconhecido apoiam manipulação (Low): podem ser
    /// dados anexados ou escondidos.
    /// </summary>
    public static AnalyzerFinding? TrailingData(JpegStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);

        if (structure.TrailingDataLength == 0)
            return null;

        var prefix = structure.TrailingDataPrefix;
        var size = $"{structure.TrailingDataLength} bytes depois do fim da imagem (EOI)";
        var hasMpf = structure.ApplicationSegments.Any(a => a.Kind == JpegApplicationKind.Mpf);

        AnalyzerEvidence evidence;
        if (prefix is [0xFF, 0xD8, ..])
        {
            evidence = hasMpf
                ? new AnalyzerEvidence(
                    JpegStructureEvidenceNames.MpfImage,
                    $"{size} começam com outra imagem JPEG, declarada no segmento MPF (comum em celulares).",
                    EvidenceStrength.Low,
                    EvidenceDirection.Neutral)
                : new AnalyzerEvidence(
                    JpegStructureEvidenceNames.AppendedJpeg,
                    $"{size} começam com outra imagem JPEG, sem segmento MPF que a declare.",
                    EvidenceStrength.Low,
                    EvidenceDirection.SupportsManipulation);
        }
        else if (prefix.Count >= 8 && prefix[4] == 'f' && prefix[5] == 't' && prefix[6] == 'y' && prefix[7] == 'p')
        {
            evidence = new AnalyzerEvidence(
                JpegStructureEvidenceNames.AppendedVideo,
                $"{size} começam com um vídeo MP4/MOV, como nas fotos com movimento de celulares.",
                EvidenceStrength.Low,
                EvidenceDirection.Neutral);
        }
        else if (prefix.All(b => b == 0x00))
        {
            evidence = new AnalyzerEvidence(
                JpegStructureEvidenceNames.ZeroPadding,
                $"{size} começam com bytes zero, como no preenchimento gravado por algumas câmeras.",
                EvidenceStrength.Low,
                EvidenceDirection.Neutral);
        }
        else
        {
            evidence = new AnalyzerEvidence(
                JpegStructureEvidenceNames.UnknownTrailingData,
                $"{size} com conteúdo não reconhecido (início: {Convert.ToHexString(prefix.ToArray())}).",
                EvidenceStrength.Low,
                EvidenceDirection.SupportsManipulation);
        }

        return new AnalyzerFinding(
            JpegStructureFindingNames.TrailingData,
            "O arquivo tem dados depois do marcador de fim da imagem (EOI).",
            [evidence]);
    }

    /// <summary>
    /// Sinais de que a imagem foi gravada por uma biblioteca ou editor, e não direto pela câmera:
    /// comentário com assinatura conhecida, recursos do Photoshop (APP13) e segmento Adobe (APP14).
    /// Low, mais fraco que o software de edição nos metadados: recodificar também acontece em
    /// redimensionamentos legítimos (ex.: publicação na web).
    /// </summary>
    public static AnalyzerFinding? EditorSignatures(JpegStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);

        var evidences = new List<AnalyzerEvidence>();

        foreach (var comment in structure.Comments)
        {
            var match = CommentSignatures.FirstOrDefault(s => comment.Text.Contains(s.Signature, StringComparison.OrdinalIgnoreCase));
            if (match.Signature is null)
                continue;

            evidences.Add(new AnalyzerEvidence(
                JpegStructureEvidenceNames.EncoderComment,
                $"O comentário do arquivo indica gravação por {match.Encoder}: {FileValue(comment.Text)}",
                EvidenceStrength.Low,
                EvidenceDirection.SupportsManipulation));
        }

        if (structure.ApplicationSegments.Any(a => a.Kind == JpegApplicationKind.Photoshop))
        {
            evidences.Add(new AnalyzerEvidence(
                JpegStructureEvidenceNames.PhotoshopResources,
                "O arquivo tem um bloco de recursos do Photoshop (APP13), gravado por softwares da Adobe e por editores de IPTC.",
                EvidenceStrength.Low,
                EvidenceDirection.SupportsManipulation));
        }

        if (structure.ApplicationSegments.Any(a => a.Kind == JpegApplicationKind.Adobe))
        {
            evidences.Add(new AnalyzerEvidence(
                JpegStructureEvidenceNames.AdobeSegment,
                "O arquivo tem um segmento Adobe (APP14), gravado por softwares da Adobe e por algumas bibliotecas.",
                EvidenceStrength.Low,
                EvidenceDirection.SupportsManipulation));
        }

        if (evidences.Count == 0)
            return null;

        return new AnalyzerFinding(
            JpegStructureFindingNames.EditorSignatures,
            "A estrutura do arquivo tem sinais de gravação por um editor ou biblioteca de imagens.",
            Limit(evidences));
    }

    /// <summary>
    /// Como a imagem foi codificada. Só fatos, todos Neutral: isoladamente não indicam nada, mas o
    /// Evidence Aggregator (#43) pode cruzá-los com os metadados (ex.: EXIF de câmera com
    /// codificação progressive e tabelas Huffman otimizadas).
    /// </summary>
    public static AnalyzerFinding? EncodingProfile(JpegStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);

        if (structure.Frames.FirstOrDefault() is not { } frame)
            return null;

        var evidences = new List<AnalyzerEvidence>
        {
            Fact(JpegStructureEvidenceNames.FrameProcess, FrameProcess(frame)),
            Fact(JpegStructureEvidenceNames.ChromaSubsampling, ChromaSubsampling(frame)),
        };

        if (frame.IsProgressive)
        {
            evidences.Add(Fact(
                JpegStructureEvidenceNames.ProgressiveScans,
                $"A imagem progressive é gravada em {structure.Scans.Count} scans."));
        }

        if (!frame.IsArithmetic)
            evidences.Add(Fact(JpegStructureEvidenceNames.HuffmanTables, HuffmanTables(structure)));

        foreach (var interval in structure.RestartIntervals.Distinct())
        {
            evidences.Add(Fact(
                JpegStructureEvidenceNames.RestartInterval,
                $"Intervalo de reinício (DRI) de {interval} blocos."));
        }

        foreach (var jfif in structure.ApplicationSegments.Select(a => a.Jfif).OfType<JfifHeader>())
        {
            evidences.Add(Fact(
                JpegStructureEvidenceNames.JfifVersion,
                $"Cabeçalho JFIF versão {jfif.MajorVersion}.{jfif.MinorVersion:D2}."));
        }

        foreach (var adobe in structure.ApplicationSegments.Select(a => a.Adobe).OfType<AdobeHeader>())
        {
            evidences.Add(Fact(
                JpegStructureEvidenceNames.AdobeTransform,
                $"Segmento Adobe com transformação de cor {AdobeTransform(adobe.Transform)}."));
        }

        foreach (var comment in structure.Comments.Where(c => !string.IsNullOrWhiteSpace(c.Text)))
        {
            evidences.Add(Fact(
                JpegStructureEvidenceNames.Comment,
                $"Comentário do arquivo: {FileValue(comment.Text)}"));
        }

        return new AnalyzerFinding(
            JpegStructureFindingNames.EncodingProfile,
            "Características da codificação JPEG da imagem.",
            Limit(evidences));
    }

    private static string FrameProcess(JpegFrameHeader frame)
    {
        var process = frame.IsLossless ? "lossless"
            : frame.IsProgressive ? "progressive"
            : frame.IsBaseline ? "baseline"
            : "sequencial estendida";

        var hierarchical = frame.IsHierarchical ? " hierárquica" : "";
        var coding = frame.IsArithmetic ? "aritmética" : "Huffman";

        return $"Codificação {process}{hierarchical} com {coding}, {frame.Precision} bits por amostra, "
            + $"{frame.Width} × {frame.Height} pixels e {frame.Components.Count} "
            + (frame.Components.Count == 1 ? "componente." : "componentes.");
    }

    private static string ChromaSubsampling(JpegFrameHeader frame)
    {
        var components = frame.Components;
        if (components.Count == 1)
            return "Imagem em tons de cinza (1 componente), sem subamostragem de cor.";

        var raw = string.Join(", ", components.Select(c => $"{c.Id}: {c.HorizontalSampling}×{c.VerticalSampling}"));

        if (components.Count == 3
            && components[1].HorizontalSampling == components[2].HorizontalSampling
            && components[1].VerticalSampling == components[2].VerticalSampling
            && components[1].HorizontalSampling > 0
            && components[1].VerticalSampling > 0
            && components[0].HorizontalSampling % components[1].HorizontalSampling == 0
            && components[0].VerticalSampling % components[1].VerticalSampling == 0)
        {
            var label = (components[0].HorizontalSampling / components[1].HorizontalSampling,
                    components[0].VerticalSampling / components[1].VerticalSampling) switch
            {
                (1, 1) => "4:4:4",
                (2, 1) => "4:2:2",
                (2, 2) => "4:2:0",
                (1, 2) => "4:4:0",
                (4, 1) => "4:1:1",
                (4, 2) => "4:1:0",
                _ => null,
            };

            if (label is not null)
                return $"Subamostragem de cor {label} (amostragem por componente {raw}).";
        }

        return $"Subamostragem de cor fora dos padrões comuns (amostragem por componente {raw}).";
    }

    private static string HuffmanTables(JpegStructure structure)
    {
        var tables = structure.HuffmanTables;
        if (tables.Count == 0)
            return "O arquivo não define tabelas Huffman; o decoder usa as do Anexo K da T.81, como nos quadros de vídeo MJPEG.";

        var optimized = tables.Count(t => !StandardHuffmanTables.IsStandard(t));
        return optimized == 0
            ? "As tabelas Huffman são as padrão do Anexo K da T.81, sem otimização pelo encoder."
            : $"As tabelas Huffman foram otimizadas pelo encoder ({optimized} de {tables.Count} diferentes das do Anexo K da T.81).";
    }

    private static string AdobeTransform(byte transform) => transform switch
    {
        0 => "0 (sem transformação: RGB ou CMYK)",
        1 => "1 (YCbCr)",
        2 => "2 (YCCK)",
        _ => $"{transform} (valor desconhecido)",
    };

    private static AnalyzerEvidence Fact(string name, string description) =>
        new(name, description, EvidenceStrength.Low, EvidenceDirection.Neutral);

    // Mantém as primeiras evidências e resume o resto numa evidência final.
    private static IReadOnlyList<AnalyzerEvidence> Limit(List<AnalyzerEvidence> evidences)
    {
        if (evidences.Count <= MaxEvidencesPerFinding)
            return evidences;

        var kept = evidences.Take(MaxEvidencesPerFinding - 1).ToList();
        kept.Add(new AnalyzerEvidence(
            JpegStructureEvidenceNames.MoreOccurrences,
            $"Mais {evidences.Count - kept.Count} ocorrências não listadas.",
            EvidenceStrength.Low,
            EvidenceDirection.Neutral));

        return kept;
    }

    private static string FormatMarker(byte code) => $"0xFF{code:X2}";

    private static string FileValue(string value) =>
        value.Length <= MaxFileValueLength ? value : string.Concat(value.AsSpan(0, MaxFileValueLength), "…");
}
