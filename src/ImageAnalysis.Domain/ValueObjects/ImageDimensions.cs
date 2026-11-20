namespace ImageAnalysis.Domain.ValueObjects;

/// <summary>
/// Dimensões da imagem em pixels, como lidas do arquivo.
/// </summary>
public sealed record ImageDimensions
{
    public int Width { get; }
    public int Height { get; }

    public ImageDimensions(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "A largura da imagem deve ser maior que zero.");

        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "A altura da imagem deve ser maior que zero.");

        Width = width;
        Height = height;
    }
}
