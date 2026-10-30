using System.Collections.Concurrent;
using Atlas.Shared.Markets;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace Atlas.Onboarding.Infrastructure.Storage;

/// <summary>How the service reaches blob storage.</summary>
public sealed class DocumentStoreOptions
{
    /// <summary>
    /// The storage connection string. The default is the Azurite emulator from the platform
    /// compose file, which uses Microsoft's public development account.
    /// </summary>
    public string ConnectionString { get; set; } = "UseDevelopmentStorage=true";
}

/// <summary>
/// Stores images in Azure Blob Storage (or the Azurite emulator), one container per market.
/// </summary>
public sealed class AzureBlobDocumentStore : IDocumentStore
{
    private readonly BlobServiceClient _Service;
    private readonly ConcurrentDictionary<string, bool> _EnsuredContainers = new();

    /// <summary>Creates the store.</summary>
    /// <param name="options">How to reach blob storage.</param>
    public AzureBlobDocumentStore(IOptions<DocumentStoreOptions> options)
    {
        _Service = new BlobServiceClient(options.Value.ConnectionString);
    }

    /// <inheritdoc />
    public async Task SaveAsync(MarketProfile market, string blobName,
        ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var container = await ContainerAsync(market, cancellationToken);
        await container.GetBlobClient(blobName)
            .UploadAsync(BinaryData.FromBytes(content), overwrite: true, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<byte[]> ReadAsync(MarketProfile market, string blobName,
        CancellationToken cancellationToken)
    {
        var container = await ContainerAsync(market, cancellationToken);
        var download = await container.GetBlobClient(blobName)
            .DownloadContentAsync(cancellationToken);
        return download.Value.Content.ToArray();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(MarketProfile market, string blobName,
        CancellationToken cancellationToken)
    {
        var container = await ContainerAsync(market, cancellationToken);
        await container.GetBlobClient(blobName)
            .DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    private async Task<BlobContainerClient> ContainerAsync(MarketProfile market,
        CancellationToken cancellationToken)
    {
        var container = _Service.GetBlobContainerClient(market.BlobContainer);
        if (!_EnsuredContainers.ContainsKey(market.BlobContainer))
        {
            await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            _EnsuredContainers[market.BlobContainer] = true;
        }

        return container;
    }
}
