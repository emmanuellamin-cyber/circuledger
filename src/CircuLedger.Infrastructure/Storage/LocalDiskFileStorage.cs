using System.Security.Cryptography;
using CircuLedger.Application.Common.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CircuLedger.Infrastructure.Storage;

public sealed class FileStorageOptions
{
    public const string Section = "Storage";

    /// <summary>Absolute path, or a path relative to the content root. In Docker this is a named volume.</summary>
    public string LocalRoot { get; set; } = "App_Data/uploads";
}

/// <summary>Stores files under generated keys (never user-supplied names). Replace with a blob/S3 store later.</summary>
public sealed class LocalDiskFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalDiskFileStorage(IOptions<FileStorageOptions> options, IHostEnvironment environment)
    {
        _root = Path.GetFullPath(Path.IsPathRooted(options.Value.LocalRoot)
            ? options.Value.LocalRoot
            : Path.Combine(environment.ContentRootPath, options.Value.LocalRoot));
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredFile> SaveAsync(Stream content, string extension, string contentType, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var key = $"{now:yyyy}/{now:MM}/{Guid.CreateVersion7():N}{extension}";
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long size = 0;
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                sha.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                size += read;
            }
        }
        return new StoredFile(key, size, Convert.ToHexStringLower(sha.GetHashAndReset()), contentType);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        Stream? stream = File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string Resolve(string key)
    {
        var full = Path.GetFullPath(Path.Combine(_root, key));
        if (!full.StartsWith(_root, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid storage key.");
        return full;
    }
}
