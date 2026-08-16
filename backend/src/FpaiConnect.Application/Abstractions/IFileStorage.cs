namespace FpaiConnect.Application.Abstractions;

public record StoredFile(string StoragePath, long SizeBytes, string Sha256);

/// <summary>
/// Abstracts document bytes away from the database. Local disk in development,
/// Azure Blob Storage in App Service — swapping providers changes no call sites.
/// </summary>
public interface IFileStorage
{
    Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default);
    Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default);
    Task DeleteAsync(string storagePath, CancellationToken ct = default);
}
