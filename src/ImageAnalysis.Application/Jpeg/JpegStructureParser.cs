namespace ImageAnalysis.Application.Jpeg;

/// <summary>
/// Ponto de entrada da leitura da estrutura JPEG: localiza os segmentos (JpegSegmentReader) e
/// interpreta o conteúdo deles (JpegSegmentDecoder). Os problemas das duas etapas saem juntos em
/// JpegStructure.Issues, primeiro os da leitura.
/// </summary>
public static class JpegStructureParser
{
    public static JpegStructure Parse(ReadOnlySpan<byte> data) =>
        JpegSegmentDecoder.Decode(data, JpegSegmentReader.Read(data));
}
