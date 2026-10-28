namespace ImageAnalysis.API.Contracts;

/// <summary>
/// Limites do upload de imagens. Constantes porque são usadas em atributos
/// ([RequestSizeLimit]/[RequestFormLimits]), que não aceitam valores de configuração.
/// </summary>
public static class UploadLimits
{
    public const long MaxImageSizeInBytes = 20 * 1024 * 1024;

    // Folga para os cabeçalhos do multipart e o campo de nome, para que uma imagem de até
    // 20 MB não seja barrada pelo tamanho total da requisição antes da validação do arquivo.
    public const long MaxRequestSizeInBytes = MaxImageSizeInBytes + 1024 * 1024;
}
