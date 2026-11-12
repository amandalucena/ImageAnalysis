using System.Text.Json;
using ImageAnalysis.Application.Exceptions;
using ImageAnalysis.Application.Metadata;

namespace ImageAnalysis.Infrastructure.ExternalTools.ExifTool;

/// <summary>
/// Interpreta o resultado de uma execução do ExifTool com -json -G1.
/// Separado do processo para que as regras sejam testadas sem o ExifTool instalado.
/// </summary>
internal static class ExifToolOutput
{
    // Com -G1, o erro de leitura vem no grupo ExifTool (ex.: "Unknown file type", "File is empty").
    // Avisos vêm em "ExifTool:Warning" com exit code 0 e não são falha.
    private const string ErrorTag = "ExifTool:Error";

    private const int MaxStandardErrorLength = 500;

    public static RawMetadata Interpret(int exitCode, string standardOutput, string standardError)
    {
        if (string.IsNullOrWhiteSpace(standardOutput))
            throw Failed($"O ExifTool terminou com código {exitCode} sem produzir saída.", standardError);

        JsonElement file;
        try
        {
            using var document = JsonDocument.Parse(standardOutput);
            var root = document.RootElement;

            // Um único arquivo é lido por execução (stdin), então o array tem exatamente um objeto.
            if (root.ValueKind != JsonValueKind.Array
                || root.GetArrayLength() != 1
                || root[0].ValueKind != JsonValueKind.Object)
                throw Failed("A saída do ExifTool não é um array JSON com um único objeto.", standardError);

            file = root[0].Clone();
        }
        catch (JsonException ex)
        {
            throw new MetadataExtractionException(
                MetadataExtractionFailure.ToolFailed, "A saída do ExifTool não é um JSON válido.", ex);
        }

        if (file.TryGetProperty(ErrorTag, out var error))
            throw new MetadataExtractionException(
                MetadataExtractionFailure.UnreadableImage, $"O ExifTool não conseguiu ler a imagem: {error}.");

        // Exit code diferente de zero sem um erro de leitura indica falha da própria ferramenta.
        if (exitCode != 0)
            throw Failed($"O ExifTool terminou com código {exitCode}.", standardError);

        return new RawMetadata(standardOutput);
    }

    private static MetadataExtractionException Failed(string message, string standardError)
    {
        var detail = standardError.Trim();
        if (detail.Length > MaxStandardErrorLength)
            detail = detail[..MaxStandardErrorLength] + "...";

        return new MetadataExtractionException(
            MetadataExtractionFailure.ToolFailed,
            detail.Length == 0 ? message : $"{message} stderr: {detail}");
    }
}
