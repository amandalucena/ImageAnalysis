namespace ImageAnalysis.Application.Jpeg;

/// <summary>
/// Percorre o arquivo JPEG do SOI ao EOI e registra cada marcador com posição e comprimento.
/// Não interpreta o conteúdo dos segmentos (22.3) nem julga o que é anômalo (22.4): Frames e
/// QuantizationTables saem vazios. Não lança exceção pelo conteúdo do arquivo; o que impede ou
/// desvia a leitura vira um JpegReadIssue.
/// </summary>
public static class JpegSegmentReader
{
    private const byte MarkerPrefix = 0xFF;
    private const byte StuffedZero = 0x00;

    public static JpegStructure Read(ReadOnlySpan<byte> data)
    {
        var segments = new List<JpegSegment>();
        var issues = new List<JpegReadIssue>();
        long? endOfImageOffset = null;
        long trailingDataLength = 0;
        byte[] trailingDataPrefix = [];

        if (data.Length < 2 || data[0] != MarkerPrefix || data[1] != (byte)JpegMarker.Soi)
        {
            issues.Add(new JpegReadIssue(
                JpegReadIssueKind.MissingStartOfImage, 0, "O arquivo não começa com o marcador SOI (0xFFD8)."));

            return Build(segments, issues, endOfImageOffset, trailingDataLength, trailingDataPrefix);
        }

        segments.Add(new JpegSegment((byte)JpegMarker.Soi, 0, 0));
        var position = 2;

        // Cada volta avança pelo menos 1 byte (ver os comentários de cada caso), então um arquivo
        // malicioso não prende o laço.
        while (true)
        {
            if (position >= data.Length)
            {
                issues.Add(new JpegReadIssue(
                    JpegReadIssueKind.TruncatedFile, position, "O arquivo terminou sem o marcador EOI."));
                break;
            }

            if (data[position] != MarkerPrefix)
            {
                if (!TrySkipUnexpectedData(data, position, issues, out position))
                    break;

                continue;
            }

            // Bytes 0xFF seguidos são preenchimento permitido; o marcador é o último deles.
            var fillStart = position;
            while (position + 1 < data.Length && data[position + 1] == MarkerPrefix)
                position++;

            if (position + 1 >= data.Length)
            {
                issues.Add(new JpegReadIssue(
                    JpegReadIssueKind.TruncatedFile, fillStart, "O arquivo terminou no meio de um marcador."));
                break;
            }

            var markerOffset = position;
            var code = data[markerOffset + 1];

            // 0xFF 0x00 só existe dentro dos dados comprimidos; aqui é um byte solto. A busca começa
            // depois do 0xFF, então a posição avança.
            if (code == StuffedZero)
            {
                if (!TrySkipUnexpectedData(data, markerOffset, issues, out position))
                    break;

                continue;
            }

            if (JpegMarkers.IsStandalone(code))
            {
                segments.Add(new JpegSegment(code, markerOffset, 0));
                position = markerOffset + 2;

                if (code == (byte)JpegMarker.Eoi)
                {
                    endOfImageOffset = markerOffset;
                    trailingDataLength = data.Length - position;
                    trailingDataPrefix = data.Slice(
                        position, Math.Min(JpegStructure.TrailingDataPrefixLength, data.Length - position)).ToArray();
                    break;
                }

                continue;
            }

            // Códigos desconhecidos são lidos como segmentos com comprimento, como faz a libjpeg:
            // só os marcadores sem dados da T.81 não têm o campo.
            if (!JpegMarkers.IsKnown(code))
            {
                issues.Add(new JpegReadIssue(
                    JpegReadIssueKind.UnknownMarker,
                    markerOffset,
                    $"Marcador desconhecido {FormatMarker(code)}.",
                    markerCode: code));
            }

            if (markerOffset + 3 >= data.Length)
            {
                issues.Add(new JpegReadIssue(
                    JpegReadIssueKind.TruncatedFile,
                    markerOffset,
                    $"O arquivo terminou no campo de comprimento do marcador {FormatMarker(code)}."));
                break;
            }

            var length = (data[markerOffset + 2] << 8) | data[markerOffset + 3];
            segments.Add(new JpegSegment(code, markerOffset, length));

            // Sem um comprimento confiável não há como saber onde começa o próximo marcador.
            if (length < 2)
            {
                issues.Add(new JpegReadIssue(
                    JpegReadIssueKind.InvalidSegmentLength,
                    markerOffset,
                    $"O marcador {FormatMarker(code)} declara comprimento {length}, menor que o mínimo de 2.",
                    markerCode: code));
                break;
            }

            // long: offset + comprimento não estoura, mas o resultado é comparado com o tamanho.
            var segmentEnd = (long)markerOffset + 2 + length;
            if (segmentEnd > data.Length)
            {
                issues.Add(new JpegReadIssue(
                    JpegReadIssueKind.InvalidSegmentLength,
                    markerOffset,
                    $"O marcador {FormatMarker(code)} declara comprimento {length}, que passa do fim do arquivo.",
                    markerCode: code));
                break;
            }

            // Comprimento mínimo 2: avança pelo menos 4 bytes.
            position = (int)segmentEnd;

            if (code == (byte)JpegMarker.Sos)
            {
                var next = FindEndOfScan(data, position);
                if (next < 0)
                {
                    issues.Add(new JpegReadIssue(
                        JpegReadIssueKind.TruncatedFile,
                        position,
                        "O arquivo terminou dentro dos dados comprimidos, sem o marcador EOI."));
                    break;
                }

                position = next;
            }
        }

        return Build(segments, issues, endOfImageOffset, trailingDataLength, trailingDataPrefix);
    }

    // Registra os bytes que não começam um marcador e recomeça no próximo marcador conhecido.
    // Devolve false quando não há mais nenhum até o fim do arquivo.
    private static bool TrySkipUnexpectedData(
        ReadOnlySpan<byte> data, int start, List<JpegReadIssue> issues, out int resumeAt)
    {
        var next = FindNextMarker(data, start + 1);

        if (next < 0)
        {
            issues.Add(new JpegReadIssue(
                JpegReadIssueKind.UnexpectedData,
                start,
                $"{data.Length - start} bytes que não formam um marcador até o fim do arquivo.",
                length: data.Length - start));

            resumeAt = data.Length;
            return false;
        }

        var code = data[next + 1];
        issues.Add(new JpegReadIssue(
            JpegReadIssueKind.UnexpectedData,
            start,
            $"{next - start} bytes ignorados antes do marcador {FormatMarker(code)}.",
            length: next - start,
            markerCode: code));

        resumeAt = next;
        return true;
    }

    // Próximo 0xFF seguido de um código conhecido. Códigos desconhecidos não contam: no meio de
    // bytes soltos, 0xFF seguido de um valor qualquer é muito mais provável que um marcador real.
    private static int FindNextMarker(ReadOnlySpan<byte> data, int from)
    {
        for (var i = from; i + 1 < data.Length; i++)
        {
            if (data[i] == MarkerPrefix && JpegMarkers.IsKnown(data[i + 1]))
                return i;
        }

        return -1;
    }

    // Percorre os dados comprimidos depois do SOS e devolve a posição do marcador que os encerra
    // (outro SOS ou DHT no progressive, ou o EOI), ou -1 se o arquivo acabar antes.
    // 0xFF 0x00 (stuffing), RST0–RST7 e 0xFF de preenchimento fazem parte do scan.
    private static int FindEndOfScan(ReadOnlySpan<byte> data, int from)
    {
        for (var i = from; i + 1 < data.Length; i++)
        {
            if (data[i] != MarkerPrefix)
                continue;

            var next = data[i + 1];
            if (next == StuffedZero || JpegMarkers.IsRestart(next))
            {
                i++;
                continue;
            }

            if (next == MarkerPrefix)
                continue;

            return i;
        }

        return -1;
    }

    private static string FormatMarker(byte code) => $"0xFF{code:X2}";

    private static JpegStructure Build(
        List<JpegSegment> segments,
        List<JpegReadIssue> issues,
        long? endOfImageOffset,
        long trailingDataLength,
        byte[] trailingDataPrefix) =>
        new(segments, [], [], endOfImageOffset, trailingDataLength, issues, trailingDataPrefix: trailingDataPrefix);
}
