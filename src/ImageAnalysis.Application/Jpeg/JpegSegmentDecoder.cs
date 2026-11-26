using System.Buffers.Binary;
using System.Text;

namespace ImageAnalysis.Application.Jpeg;

/// <summary>
/// Interpreta o conteúdo dos segmentos localizados pelo JpegSegmentReader: DQT, SOFn, DHT, DRI,
/// SOS, APPn e COM. Como o leitor, só descreve o arquivo. Um segmento malformado vira um
/// MalformedSegment com o que deu para ler dele; como as posições já vieram da leitura, um
/// segmento ruim não atrapalha os outros.
/// </summary>
public static class JpegSegmentDecoder
{
    // Posição na ordem natural (linha a linha do bloco 8×8) de cada coeficiente na ordem zigzag
    // em que vem no DQT (T.81, figura A.6).
    internal static readonly byte[] ZigzagToNatural =
    [
        0, 1, 8, 16, 9, 2, 3, 10,
        17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    ];

    private static readonly (JpegMarker Marker, byte[] Prefix, JpegApplicationKind Kind)[] ApplicationSignatures =
    [
        (JpegMarker.App0, "JFIF\0"u8.ToArray(), JpegApplicationKind.Jfif),
        (JpegMarker.App0, "JFXX\0"u8.ToArray(), JpegApplicationKind.JfifExtension),
        (JpegMarker.App1, "Exif\0"u8.ToArray(), JpegApplicationKind.Exif),
        (JpegMarker.App1, "http://ns.adobe.com/xap/1.0/\0"u8.ToArray(), JpegApplicationKind.Xmp),
        (JpegMarker.App1, "http://ns.adobe.com/xmp/extension/\0"u8.ToArray(), JpegApplicationKind.ExtendedXmp),
        (JpegMarker.App2, "ICC_PROFILE\0"u8.ToArray(), JpegApplicationKind.IccProfile),
        (JpegMarker.App2, "MPF\0"u8.ToArray(), JpegApplicationKind.Mpf),
        (JpegMarker.App13, "Photoshop 3.0\0"u8.ToArray(), JpegApplicationKind.Photoshop),
        // Sem terminador: o "Adobe" é seguido direto pela versão.
        (JpegMarker.App14, "Adobe"u8.ToArray(), JpegApplicationKind.Adobe),
    ];

    public static JpegStructure Decode(ReadOnlySpan<byte> data, JpegStructure read)
    {
        ArgumentNullException.ThrowIfNull(read);

        var context = new DecodeContext(read.Issues);

        foreach (var segment in read.Segments)
        {
            // Sem dados (SOI, EOI, RSTn) ou com o comprimento inválido que encerrou a leitura:
            // nesse caso o conteúdo está incompleto e não é interpretado.
            if (segment.Length < 2 || segment.Offset + 2 + segment.Length > data.Length)
                continue;

            var payload = data.Slice((int)segment.Offset + 4, segment.Length - 2);

            switch (segment.Marker)
            {
                case JpegMarker.Dqt:
                    DecodeQuantizationTables(payload, segment, context);
                    break;
                case JpegMarker.Dht:
                    DecodeHuffmanTables(payload, segment, context);
                    break;
                case JpegMarker.Dri:
                    DecodeRestartInterval(payload, segment, context);
                    break;
                case JpegMarker.Sos:
                    DecodeScanHeader(payload, segment, context);
                    break;
                case JpegMarker.Com:
                    DecodeComment(payload, segment, context);
                    break;
                case { } marker when JpegMarkers.IsStartOfFrame((byte)marker):
                    DecodeFrameHeader(payload, segment, marker, context);
                    break;
                case { } marker when JpegMarkers.IsApplication((byte)marker):
                    DecodeApplicationSegment(payload, segment, marker, context);
                    break;
            }
        }

        return new JpegStructure(
            read.Segments,
            context.Frames,
            context.QuantizationTables,
            read.EndOfImageOffset,
            read.TrailingDataLength,
            context.Issues,
            context.HuffmanTables,
            context.RestartIntervals,
            context.Scans,
            context.ApplicationSegments,
            context.Comments);
    }

    // ---- DQT ----

    // Um DQT pode ter várias tabelas seguidas, cada uma com 1 byte Pq/Tq e 64 coeficientes de
    // 1 byte (Pq = 0) ou 2 bytes (Pq = 1).
    private static void DecodeQuantizationTables(ReadOnlySpan<byte> payload, JpegSegment segment, DecodeContext context)
    {
        if (payload.IsEmpty)
        {
            context.Malformed(segment, "O segmento DQT não contém nenhuma tabela.");
            return;
        }

        var position = 0;
        while (position < payload.Length)
        {
            var precision = payload[position] >> 4;
            var id = (byte)(payload[position] & 0x0F);

            if (precision > 1)
            {
                context.Malformed(segment, $"A tabela de quantização {id} declara precisão inválida ({precision}).");
                return;
            }

            var coefficientSize = precision + 1;
            var tableSize = QuantizationTable.CoefficientCount * coefficientSize;
            if (payload.Length - position - 1 < tableSize)
            {
                context.Malformed(segment, $"A tabela de quantização {id} está incompleta.");
                return;
            }

            var coefficients = payload.Slice(position + 1, tableSize);
            var natural = new ushort[QuantizationTable.CoefficientCount];
            for (var k = 0; k < QuantizationTable.CoefficientCount; k++)
            {
                natural[ZigzagToNatural[k]] = precision == 0
                    ? coefficients[k]
                    : BinaryPrimitives.ReadUInt16BigEndian(coefficients.Slice(k * 2, 2));
            }

            context.QuantizationTables.Add(new QuantizationTable(id, (QuantizationPrecision)precision, natural));
            position += 1 + tableSize;
        }
    }

    // ---- SOFn ----

    // P (1), Y (2), X (2), Nf (1) e 3 bytes por componente.
    private static void DecodeFrameHeader(
        ReadOnlySpan<byte> payload, JpegSegment segment, JpegMarker marker, DecodeContext context)
    {
        if (payload.Length < 6)
        {
            context.Malformed(segment, $"O cabeçalho de frame {FormatMarker(segment)} está incompleto.");
            return;
        }

        var componentCount = payload[5];
        var expectedLength = 6 + 3 * componentCount;
        if (payload.Length < expectedLength)
        {
            context.Malformed(
                segment,
                $"O cabeçalho de frame {FormatMarker(segment)} declara {componentCount} componentes, mas não tem espaço para todos.");
            return;
        }

        // Bytes a mais não impedem a leitura do cabeçalho, mas são registrados.
        if (payload.Length > expectedLength)
        {
            context.Malformed(
                segment,
                $"O cabeçalho de frame {FormatMarker(segment)} tem {payload.Length - expectedLength} bytes além dos componentes declarados.");
        }

        var components = new JpegFrameComponent[componentCount];
        for (var i = 0; i < componentCount; i++)
        {
            var c = payload.Slice(6 + 3 * i, 3);
            components[i] = new JpegFrameComponent(c[0], (byte)(c[1] >> 4), (byte)(c[1] & 0x0F), c[2]);
        }

        context.Frames.Add(new JpegFrameHeader(
            marker,
            precision: payload[0],
            height: BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(1, 2)),
            width: BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(3, 2)),
            components));
    }

    // ---- DHT ----

    // Um DHT pode ter várias tabelas seguidas, cada uma com 1 byte Tc/Th, 16 contagens e os
    // símbolos (tantos quanto a soma das contagens).
    private static void DecodeHuffmanTables(ReadOnlySpan<byte> payload, JpegSegment segment, DecodeContext context)
    {
        if (payload.IsEmpty)
        {
            context.Malformed(segment, "O segmento DHT não contém nenhuma tabela.");
            return;
        }

        var position = 0;
        while (position < payload.Length)
        {
            var tableClass = payload[position] >> 4;
            var id = (byte)(payload[position] & 0x0F);

            if (tableClass > 1)
            {
                context.Malformed(segment, $"A tabela Huffman {id} declara classe inválida ({tableClass}).");
                return;
            }

            if (payload.Length - position - 1 < HuffmanTable.CountsLength)
            {
                context.Malformed(segment, $"A tabela Huffman {id} está incompleta.");
                return;
            }

            var counts = payload.Slice(position + 1, HuffmanTable.CountsLength);
            var symbolCount = 0;
            foreach (var count in counts)
                symbolCount += count;

            var symbolsStart = position + 1 + HuffmanTable.CountsLength;
            if (payload.Length - symbolsStart < symbolCount)
            {
                context.Malformed(segment, $"A tabela Huffman {id} declara {symbolCount} símbolos, mas não tem espaço para todos.");
                return;
            }

            context.HuffmanTables.Add(new HuffmanTable(
                (HuffmanTableClass)tableClass, id, counts.ToArray(), payload.Slice(symbolsStart, symbolCount).ToArray()));

            position = symbolsStart + symbolCount;
        }
    }

    // ---- DRI ----

    private static void DecodeRestartInterval(ReadOnlySpan<byte> payload, JpegSegment segment, DecodeContext context)
    {
        if (payload.Length != 2)
            context.Malformed(segment, $"O segmento DRI deveria ter 2 bytes de conteúdo, mas tem {payload.Length}.");

        if (payload.Length >= 2)
            context.RestartIntervals.Add(BinaryPrimitives.ReadUInt16BigEndian(payload));
    }

    // ---- SOS ----

    // Ns (1), 2 bytes por componente, Ss (1), Se (1) e Ah/Al (1).
    private static void DecodeScanHeader(ReadOnlySpan<byte> payload, JpegSegment segment, DecodeContext context)
    {
        if (payload.IsEmpty)
        {
            context.Malformed(segment, "O cabeçalho do scan está vazio.");
            return;
        }

        var componentCount = payload[0];
        var expectedLength = 1 + 2 * componentCount + 3;
        if (payload.Length < expectedLength)
        {
            context.Malformed(
                segment, $"O cabeçalho do scan declara {componentCount} componentes, mas não tem espaço para todos.");
            return;
        }

        if (payload.Length > expectedLength)
        {
            context.Malformed(
                segment, $"O cabeçalho do scan tem {payload.Length - expectedLength} bytes além dos campos declarados.");
        }

        var components = new JpegScanComponent[componentCount];
        for (var i = 0; i < componentCount; i++)
        {
            var c = payload.Slice(1 + 2 * i, 2);
            components[i] = new JpegScanComponent(c[0], (byte)(c[1] >> 4), (byte)(c[1] & 0x0F));
        }

        var tail = payload.Slice(1 + 2 * componentCount, 3);
        context.Scans.Add(new JpegScanHeader(
            segment.Offset, components, tail[0], tail[1], (byte)(tail[2] >> 4), (byte)(tail[2] & 0x0F)));
    }

    // ---- APPn ----

    private static void DecodeApplicationSegment(
        ReadOnlySpan<byte> payload, JpegSegment segment, JpegMarker marker, DecodeContext context)
    {
        var kind = JpegApplicationKind.Unknown;
        var prefixLength = 0;
        foreach (var signature in ApplicationSignatures)
        {
            if (signature.Marker == marker && payload.StartsWith(signature.Prefix))
            {
                kind = signature.Kind;
                prefixLength = signature.Prefix.Length;
                break;
            }
        }

        var application = new JpegApplicationSegment(marker, segment.Offset, segment.Length, kind, ReadIdentifier(payload));
        var header = payload[prefixLength..];

        application = kind switch
        {
            JpegApplicationKind.Jfif => application with { Jfif = ReadJfif(header, segment, context) },
            JpegApplicationKind.IccProfile => application with { IccProfileChunk = ReadIccChunk(header, segment, context) },
            JpegApplicationKind.Adobe => application with { Adobe = ReadAdobe(header, segment, context) },
            _ => application,
        };

        context.ApplicationSegments.Add(application);
    }

    // Versão (2), unidade (1), densidade X e Y (2 + 2) e tamanho da miniatura (1 + 1).
    private static JfifHeader? ReadJfif(ReadOnlySpan<byte> header, JpegSegment segment, DecodeContext context)
    {
        if (header.Length < 9)
        {
            context.Malformed(segment, "O cabeçalho JFIF está incompleto.");
            return null;
        }

        return new JfifHeader(
            header[0],
            header[1],
            header[2],
            BinaryPrimitives.ReadUInt16BigEndian(header.Slice(3, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(header.Slice(5, 2)),
            header[7],
            header[8]);
    }

    private static IccProfileChunk? ReadIccChunk(ReadOnlySpan<byte> header, JpegSegment segment, DecodeContext context)
    {
        if (header.Length < 2)
        {
            context.Malformed(segment, "O cabeçalho do perfil ICC está incompleto.");
            return null;
        }

        return new IccProfileChunk(header[0], header[1]);
    }

    // Versão (2), flags0 (2), flags1 (2) e transform (1).
    private static AdobeHeader? ReadAdobe(ReadOnlySpan<byte> header, JpegSegment segment, DecodeContext context)
    {
        if (header.Length < 7)
        {
            context.Malformed(segment, "O cabeçalho Adobe está incompleto.");
            return null;
        }

        return new AdobeHeader(
            BinaryPrimitives.ReadUInt16BigEndian(header.Slice(0, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(header.Slice(2, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(header.Slice(4, 2)),
            header[6]);
    }

    // Texto ASCII imprimível do início do conteúdo, até o primeiro byte fora dessa faixa.
    private static string? ReadIdentifier(ReadOnlySpan<byte> payload)
    {
        var length = 0;
        while (length < payload.Length && length < JpegApplicationSegment.MaxIdentifierLength
            && payload[length] is >= 0x20 and <= 0x7E)
        {
            length++;
        }

        return length == 0 ? null : Encoding.ASCII.GetString(payload[..length]);
    }

    // ---- COM ----

    private static void DecodeComment(ReadOnlySpan<byte> payload, JpegSegment segment, DecodeContext context)
    {
        var text = Encoding.Latin1.GetString(payload[..Math.Min(payload.Length, JpegComment.MaxTextLength)]).TrimEnd('\0');

        context.Comments.Add(new JpegComment(segment.Offset, text, payload.Length));
    }

    private static string FormatMarker(JpegSegment segment) => $"0xFF{segment.MarkerCode:X2}";

    private sealed class DecodeContext(IEnumerable<JpegReadIssue> readIssues)
    {
        public List<JpegReadIssue> Issues { get; } = [.. readIssues];
        public List<JpegFrameHeader> Frames { get; } = [];
        public List<QuantizationTable> QuantizationTables { get; } = [];
        public List<HuffmanTable> HuffmanTables { get; } = [];
        public List<ushort> RestartIntervals { get; } = [];
        public List<JpegScanHeader> Scans { get; } = [];
        public List<JpegApplicationSegment> ApplicationSegments { get; } = [];
        public List<JpegComment> Comments { get; } = [];

        public void Malformed(JpegSegment segment, string description) =>
            Issues.Add(new JpegReadIssue(
                JpegReadIssueKind.MalformedSegment, segment.Offset, description, markerCode: segment.MarkerCode));
    }
}
