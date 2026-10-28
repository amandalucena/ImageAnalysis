using ImageAnalysis.Domain.Interfaces;

public class ImageTypeDetector
{
    private readonly IAnalysisRepository _analysisRepository;
    private readonly IImageStorage _imageStorage;

    public ImageTypeDetector(IAnalysisRepository analysisRepository, IImageStorage imageStorage)
    {
        _analysisRepository = analysisRepository;
        _imageStorage = imageStorage;
    }

    //Le os primeiros bytes e devolve o MIME para a extensão do arquivo. Null para tipo desconhecido
    public static string? GetMimeTypeFromBytes(byte[] fileBytes)
    {
        if (fileBytes.Length < 4)
        {
            return null; // Arquivo muito pequeno para determinar o tipo
        }

        // Verifica os primeiros bytes para identificar o tipo de arquivo.
        // JPEG: só a assinatura inicial (SOI + início do primeiro marcador). O fim do arquivo não é
        // verificado porque JPEGs válidos podem ter bytes após o marcador EOI (FF D9).
        if (fileBytes[0] == 0xFF && fileBytes[1] == 0xD8 && fileBytes[2] == 0xFF)
        {
            return "image/jpeg"; // JPEG
        }
        else if (fileBytes[0] == 0x89 && fileBytes[1] == 0x50 && fileBytes[2] == 0x4E && fileBytes[3] == 0x47)
        {
            return "image/png"; // PNG
        }
        else if (fileBytes[0] == 0x47 && fileBytes[1] == 0x49 && fileBytes[2] == 0x46)
        {
            return "image/gif"; // GIF
        }
        else if (fileBytes[0] == 0x42 && fileBytes[1] == 0x4D)
        {
            return "image/bmp"; // BMP
        }

        return null; // Tipo desconhecido
    }

    
}