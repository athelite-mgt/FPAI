using FpaiConnect.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;

namespace FpaiConnect.Infrastructure.Storage;

/// <summary>
/// Disk-backed document store used in development and single-instance hosting.
/// Files are foldered by date and given a random name, so a hostile FileName can never
/// escape the root or collide with an existing document.
/// </summary>
public class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IConfiguration config)
    {
        _root = config["Storage:LocalRoot"]
                ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "documents");
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName);
        if (extension.Length > 20) extension = string.Empty;
        // Never trust the client filename for path construction.
        var safeExtension = string.Concat(extension.Where(c => char.IsLetterOrDigit(c) || c == '.'));

        var relativeDir = Path.Combine(DateTime.UtcNow.ToString("yyyy"), DateTime.UtcNow.ToString("MM"));
        var absoluteDir = Path.Combine(_root, relativeDir);
        Directory.CreateDirectory(absoluteDir);

        var storedName = $"{Guid.CreateVersion7():N}{safeExtension}";
        var absolutePath = Path.Combine(absoluteDir, storedName);

        using var sha = SHA256.Create();
        long size;
        await using (var fs = File.Create(absolutePath))
        await using (var crypto = new CryptoStream(fs, sha, CryptoStreamMode.Write))
        {
            await content.CopyToAsync(crypto, ct);
            await crypto.FlushFinalBlockAsync(ct);
            size = fs.Length;
        }

        var relativePath = Path.Combine(relativeDir, storedName).Replace('\\', '/');
        return new StoredFile(relativePath, size, Convert.ToHexString(sha.Hash!).ToLowerInvariant());
    }

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default)
    {
        var absolute = Resolve(storagePath);
        if (absolute is null || !File.Exists(absolute)) return Task.FromResult<Stream?>(null);
        return Task.FromResult<Stream?>(File.OpenRead(absolute));
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        var absolute = Resolve(storagePath);
        if (absolute is not null && File.Exists(absolute)) File.Delete(absolute);
        return Task.CompletedTask;
    }

    /// <summary>Resolves a stored path, returning null if it would escape the storage root.</summary>
    private string? Resolve(string storagePath)
    {
        var candidate = Path.GetFullPath(Path.Combine(_root, storagePath));
        var rootFull = Path.GetFullPath(_root);
        return candidate.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal) || candidate == rootFull
            ? candidate
            : null;
    }
}
