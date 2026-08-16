using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FpaiConnect.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;

namespace FpaiConnect.Infrastructure.Storage;

/// <summary>
/// Blob-backed document store for App Service, where the local filesystem is ephemeral and
/// not shared between instances. Storage paths stay identical in shape to the local provider
/// (yyyy/MM/{guid}{ext}) so a database seeded on one provider still resolves on the other.
/// </summary>
public class AzureBlobFileStorage : IFileStorage
{
    private readonly BlobContainerClient _container;
    private bool _ensured;
    private readonly SemaphoreSlim _ensureLock = new(1, 1);

    public AzureBlobFileStorage(IConfiguration config)
    {
        var containerName = config["Storage:ContainerName"] ?? "documents";
        var connectionString = config["Storage:ConnectionString"];
        var accountUrl = config["Storage:AccountUrl"];

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            // Account-key auth, mainly useful for a local Azurite emulator.
            _container = new BlobContainerClient(connectionString, containerName);
        }
        else if (!string.IsNullOrWhiteSpace(accountUrl))
        {
            // No key anywhere: the App Service's managed identity authenticates directly,
            // the same credential chain used for Entra SQL auth. In production this needs
            // no secret at all — only "Storage Blob Data Contributor" on the storage account.
            _container = new BlobContainerClient(new Uri($"{accountUrl.TrimEnd('/')}/{containerName}"),
                new DefaultAzureCredential());
        }
        else
        {
            throw new InvalidOperationException(
                "Storage:AccountUrl (managed identity) or Storage:ConnectionString (key-based, " +
                "for local development against Azurite) is required when Storage:Provider is AzureBlob.");
        }
    }

    private async Task EnsureContainerAsync(CancellationToken ct)
    {
        if (_ensured) return;
        await _ensureLock.WaitAsync(ct);
        try
        {
            if (_ensured) return;
            // Private access only: documents are served through the API, which re-checks
            // department scope and the confidential flag on every download.
            await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
            _ensured = true;
        }
        finally
        {
            _ensureLock.Release();
        }
    }

    public async Task<StoredFile> SaveAsync(
        Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        await EnsureContainerAsync(ct);

        var extension = Path.GetExtension(fileName);
        if (extension.Length > 20) extension = string.Empty;
        var safeExtension = string.Concat(extension.Where(c => char.IsLetterOrDigit(c) || c == '.'));

        var storagePath =
            $"{DateTime.UtcNow:yyyy}/{DateTime.UtcNow:MM}/{Guid.CreateVersion7():N}{safeExtension}";

        // Hash while uploading rather than buffering the whole file to compute it separately.
        using var sha = SHA256.Create();
        await using var crypto = new CryptoStream(content, sha, CryptoStreamMode.Read, leaveOpen: true);

        var blob = _container.GetBlobClient(storagePath);
        await blob.UploadAsync(crypto, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        }, ct);

        var properties = await blob.GetPropertiesAsync(cancellationToken: ct);
        return new StoredFile(
            storagePath,
            properties.Value.ContentLength,
            Convert.ToHexString(sha.Hash ?? []).ToLowerInvariant());
    }

    public async Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default)
    {
        try
        {
            return await _container.GetBlobClient(storagePath).OpenReadAsync(cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        await _container.GetBlobClient(storagePath)
            .DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);
    }
}
