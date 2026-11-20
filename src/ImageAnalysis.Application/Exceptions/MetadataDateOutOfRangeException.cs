using ImageAnalysis.Application.Metadata;

namespace ImageAnalysis.Application.Exceptions;

/// <summary>
/// Uma data dos metadados não pode ser convertida para UTC porque o resultado sai do intervalo
/// representável por DateTime (ex.: "0001:01:01 00:00:00" com offset +05:00). Acontece com
/// arquivos forjados ou corrompidos. As regras tratam a data como não comparável e registram
/// o fato no laudo, em vez de deixar a análise inteira falhar.
/// </summary>
public class MetadataDateOutOfRangeException : Exception
{
    public MetadataDate Date { get; }

    public MetadataDateOutOfRangeException(MetadataDate date)
        : base($"A data '{date.Raw}' com offset {date.Offset} sai do intervalo de DateTime ao converter para UTC.")
    {
        Date = date;
    }
}
