using System.ComponentModel;
using System.Diagnostics;

namespace ImageAnalysis.Tests.Infrastructure.ExternalTools;

/// <summary>
/// Teste que executa o ExifTool real. Sem o ExifTool no PATH o teste é ignorado,
/// a menos que EXIFTOOL_REQUIRED=true (CI): aí ele roda e falha, para não passar em silêncio.
/// </summary>
public sealed class ExifToolFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> IsInstalled = new(CheckInstalled);

    public ExifToolFactAttribute()
    {
        var required = string.Equals(
            Environment.GetEnvironmentVariable("EXIFTOOL_REQUIRED"), "true", StringComparison.OrdinalIgnoreCase);

        if (!required && !IsInstalled.Value)
            Skip = "ExifTool não encontrado no PATH (defina EXIFTOOL_REQUIRED=true para falhar em vez de ignorar).";
    }

    private static bool CheckInstalled()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("exiftool", "-ver")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            return process is not null && process.WaitForExit(TimeSpan.FromSeconds(10)) && process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
