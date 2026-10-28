using ImageAnalysis.Domain.Interfaces;

namespace ImageAnalysis.Infrastructure.Storage;

/// <summary>
/// Storage em disco local. As chaves são relativas à pasta raiz configurada em Storage:RootPath,
/// o que permite trocar por um object storage em nuvem sem alterar quem usa IImageStorage.
/// </summary>
public class LocalImageStorage : IImageStorage
{
    private readonly string _rootPath;

    public LocalImageStorage(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("A pasta raiz do storage é obrigatória.", nameof(rootPath));

        _rootPath = Path.GetFullPath(rootPath);
    }

    public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var fullPath = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // CreateNew falha se o arquivo já existir: a imagem original nunca é sobrescrita,
        // sem a janela de corrida de verificar a existência antes de criar.
        await using var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

        try
        {
            await content.CopyToAsync(file, cancellationToken);
        }
        catch
        {
            // Não deixa um arquivo incompleto que bloquearia uma nova tentativa com a mesma chave.
            await file.DisposeAsync();
            File.Delete(fullPath);
            throw;
        }
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(ResolvePath(key)));

    // Impede que uma chave como "../outro" ou um caminho absoluto escape da pasta raiz.
    private string ResolvePath(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A chave do arquivo é obrigatória.", nameof(key));

        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, key));
        var relativePath = Path.GetRelativePath(_rootPath, fullPath);

        if (relativePath == "." || relativePath.StartsWith("..") || Path.IsPathRooted(relativePath))
            throw new ArgumentException($"A chave '{key}' aponta para fora do storage.", nameof(key));

        return fullPath;
    }
}
