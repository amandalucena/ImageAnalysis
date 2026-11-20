using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ImageAnalysis.Application.Exceptions;
using ImageAnalysis.Application.Metadata;

namespace ImageAnalysis.Infrastructure.ExternalTools.ExifTool;

/// <summary>
/// Extrai os metadados executando o ExifTool como processo filho, um por imagem.
/// A imagem é enviada pelo stdin: não depende de um caminho em disco (funciona igual com
/// object storage), não cria arquivo temporário e nenhum dado do usuário vira argumento.
/// </summary>
public sealed class ExifToolMetadataExtractor : IMetadataExtractor
{
    // -json:        um array com um objeto por arquivo.
    // -G1:          prefixa cada tag com o grupo de origem (IFD0:Software, XMP-xmp:CreatorTool),
    //               para que a mesma tag em grupos diferentes não colapse numa chave só.
    // --System:all: lendo do stdin, o grupo System descreve o pipe (FileSize 0, permissões), não a imagem.
    // -:            lê a imagem do stdin.
    // Sem -fast, que pularia os dados depois do fim do JPEG (trailers), relevantes para a análise.
    private static readonly string[] Arguments = ["-json", "-G1", "--System:all", "-"];

    private const int CopyBufferSize = 81920;

    private readonly ExifToolOptions _options;

    public ExifToolMetadataExtractor(ExifToolOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ExecutablePath))
            throw new ArgumentException("O caminho do ExifTool é obrigatório.", nameof(options));

        if (options.Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "O tempo limite do ExifTool deve ser maior que zero.");

        _options = options;
    }

    public async Task<RawMetadata> ExtractAsync(Stream image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process { StartInfo = CreateStartInfo() };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new MetadataExtractionException(
                MetadataExtractionFailure.ToolUnavailable,
                $"Não foi possível executar o ExifTool em '{_options.ExecutablePath}'.",
                ex);
        }

        using var timeout = new CancellationTokenSource(_options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            // stdout e stderr são lidos enquanto o stdin é escrito: se o ExifTool enchesse o
            // buffer de saída enquanto ainda esperamos para ler, os dois processos travariam.
            var standardOutput = process.StandardOutput.ReadToEndAsync(linked.Token);
            var standardError = process.StandardError.ReadToEndAsync(linked.Token);

            await WriteImageAsync(process, image, linked.Token);
            await Task.WhenAll(standardOutput, standardError);
            await process.WaitForExitAsync(linked.Token);

            return ExifToolOutput.Interpret(process.ExitCode, standardOutput.Result, standardError.Result);
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            throw new MetadataExtractionException(
                MetadataExtractionFailure.Timeout,
                $"O ExifTool excedeu o tempo limite de {_options.Timeout.TotalSeconds:0.###} s.",
                ex);
        }
        catch
        {
            // Cancelamento de quem chamou (ex.: encerramento do Worker) ou falha ao ler a imagem.
            Kill(process);
            throw;
        }
    }

    private ProcessStartInfo CreateStartInfo()
    {
        var startInfo = new ProcessStartInfo(_options.ExecutablePath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in Arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    // Copia manualmente para separar as falhas: erro ao ler a imagem propaga (não pode virar
    // um dump de metade do arquivo); erro ao escrever no stdin só indica que o ExifTool parou
    // de ler, e o resultado vem do exit code e da saída.
    private static async Task WriteImageAsync(Process process, Stream image, CancellationToken cancellationToken)
    {
        var standardInput = process.StandardInput.BaseStream;
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);

        try
        {
            int read;
            while ((read = await image.ReadAsync(buffer, cancellationToken)) > 0)
            {
                try
                {
                    await standardInput.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                catch (IOException)
                {
                    // O ExifTool encerrou antes de ler tudo (ex.: tipo desconhecido já no cabeçalho).
                    return;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);

            // Fechar o stdin sinaliza o fim do arquivo; sem isso o ExifTool espera mais bytes.
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // Pipe já fechado pelo ExifTool.
            }
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            // Árvore inteira: no Windows o exiftool.exe é um lançador que inicia o Perl.
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Terminou entre a verificação e o Kill.
        }
        catch (Win32Exception)
        {
            // Já em encerramento.
        }
    }
}
