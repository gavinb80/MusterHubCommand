using Azure.Identity;
using Azure.Storage.Blobs;

namespace MusterHubCommand.Api.Services;

// Where incident attachment bytes live. The DB row (IncidentAttachment) is
// the metadata and authorisation anchor; this is just the byte store. Ported
// verbatim from MusterHubSkills.Api/Services/FileStorage.cs -- same
// interface, same two implementations, only the container name differs.
public interface IFileStorage
{
    Task SaveAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default);
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
}

// Dev/test default: plain files under a configurable root. Paths are
// server-generated GUID-based (never user input), so no traversal
// concerns, but normalise-and-check anyway.
public class LocalFileStorage(string rootPath) : IFileStorage
{
    private string Resolve(string path)
    {
        var full = Path.GetFullPath(Path.Combine(rootPath, path));
        if (!full.StartsWith(Path.GetFullPath(rootPath), StringComparison.Ordinal))
            throw new InvalidOperationException("Attachment path escapes the storage root.");
        return full;
    }

    public async Task SaveAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var full = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var file = File.Create(full);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var full = Resolve(path);
        return Task.FromResult<Stream?>(File.Exists(full) ? File.OpenRead(full) : null);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var full = Resolve(path);
        if (File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }
}

// Prod: Azure Blob Storage through the App Service's managed identity
// (Storage Blob Data Contributor on the account) -- no connection string,
// no new secret, same identity posture as the Key Vault references.
public class AzureBlobFileStorage(string blobEndpoint, string containerName) : IFileStorage
{
    private readonly BlobContainerClient container =
        new BlobServiceClient(new Uri(blobEndpoint), new DefaultAzureCredential()).GetBlobContainerClient(containerName);

    public async Task SaveAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await container.GetBlobClient(path).UploadAsync(content, overwrite: true, cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var blob = container.GetBlobClient(path);
        if (!await blob.ExistsAsync(cancellationToken)) return null;
        return await blob.OpenReadAsync(cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        await container.DeleteBlobIfExistsAsync(path, cancellationToken: cancellationToken);
    }
}
