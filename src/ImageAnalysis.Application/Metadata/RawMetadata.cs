namespace ImageAnalysis.Application.Metadata;

/// <summary>
/// Dump bruto dos metadados, exatamente como a ferramenta o produziu (JSON).
/// Avisos de leitura (ex.: estrutura JPEG inconsistente) fazem parte do dump: não são
/// falha da extração e podem ser interpretados como sinal pelas regras de achados.
///
/// Formato (contrato com o MetadataNormalizer): saída do ExifTool com -json -G1, ou seja,
/// um array com um único objeto cujas chaves são "Grupo:Tag" (ex.: "IFD0:Make",
/// "XMP-xmp:CreatorTool", "ExifTool:Warning"). Outra ferramenta precisaria produzir o mesmo formato.
/// </summary>
public sealed record RawMetadata
{
    public string Json { get; }

    public RawMetadata(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("O dump de metadados não pode ser vazio.", nameof(json));

        Json = json;
    }
}
