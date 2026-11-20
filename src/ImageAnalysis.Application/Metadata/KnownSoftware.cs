using System.Text.RegularExpressions;

namespace ImageAnalysis.Application.Metadata;

/// <summary>
/// Padrões de editores de imagem e de geradores de IA, usados pelas regras de achados.
/// A comparação ignora maiúsculas e procura o padrão como palavra inteira dentro do valor,
/// porque Software costuma vir como "Adobe Photoshop 25.0 (Windows)". Palavra inteira evita
/// falsos positivos como "Canva" em "Canvas".
/// Um mesmo valor pode ser editor e gerador de IA (ex.: Photoshop com Firefly).
/// </summary>
public static class KnownSoftware
{
    private static readonly Regex EditorPattern = Build(
        "Photoshop", "GIMP", "Lightroom", "Affinity", "Pixelmator", "Snapseed", "Canva");

    private static readonly Regex AiGeneratorPattern = Build(
        "Midjourney", "DALL-E", "DALL·E", "Stable Diffusion", "Firefly", "ComfyUI");

    // Marcas e linhas de scanners, verificadas em Make e Model. Só nomes exclusivos de scanner:
    // "Canon", "Nikon" e "Epson" sozinhos também são câmeras ou impressoras e ficam de fora.
    private static readonly Regex ScannerPattern = Build(
        "Scanner", "CanoScan", "Coolscan", "Perfection", "OpticFilm", "Plustek", "ScanSnap",
        "Scanjet", "FastFoto", "Flextight", "Imacon", "Pakon", "Noritsu", "DocuMate");

    public static bool IsEditor(string? software) => Matches(software, EditorPattern);

    // Para Make/Model, quando a tag FileSource não existe.
    public static bool IsScanner(string? device) => Matches(device, ScannerPattern);

    public static bool IsAiGenerator(string? software) => Matches(software, AiGeneratorPattern);

    private static bool Matches(string? software, Regex pattern) =>
        !string.IsNullOrWhiteSpace(software) && pattern.IsMatch(software);

    // \b nas pontas: o padrão precisa começar e terminar em limite de palavra.
    private static Regex Build(params string[] patterns) =>
        new(
            $@"\b(?:{string.Join("|", patterns.Select(Regex.Escape))})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
}
