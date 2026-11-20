namespace ImageAnalysis.Application.Storage;

/// <summary>
/// Chaves no IImageStorage dos arquivos gerados durante o processamento de uma análise.
/// Cada análise tem a própria pasta (analysis-{id}/). As chaves são gravadas no banco
/// (ex.: ImageMetadata.RawMetadataPath), então o formato não deve mudar.
/// </summary>
public static class AnalysisStorageKeys
{
    // Dump bruto do ExifTool, guardado para auditoria.
    public static string RawMetadata(Guid analysisId) => $"{Folder(analysisId)}/metadata.json";

    private static string Folder(Guid analysisId)
    {
        if (analysisId == Guid.Empty)
            throw new ArgumentException("O identificador da análise é obrigatório.", nameof(analysisId));

        return $"analysis-{analysisId:D}";
    }
}
