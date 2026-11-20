namespace ImageAnalysis.Infrastructure.ExternalTools.ExifTool;

/// <summary>
/// Configuração da seção ExifTool.
/// </summary>
public sealed class ExifToolOptions
{
    // Nome no PATH ou caminho absoluto do executável.
    public string ExecutablePath { get; init; } = "exiftool";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
