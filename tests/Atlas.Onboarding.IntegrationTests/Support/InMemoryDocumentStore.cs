using System.Collections.Concurrent;
using Atlas.Onboarding.Infrastructure.Storage;
using Atlas.Shared.Markets;

namespace Atlas.Onboarding.IntegrationTests.Support;

/// <summary>
/// A stand-in for blob storage that also lets a test see where documents were put. The real
/// Azure Blob implementation needs Azurite (Docker).
/// </summary>
public sealed class InMemoryDocumentStore : IDocumentStore
{
    private readonly ConcurrentDictionary<(string Container, string Blob), byte[]> _Blobs = new();

    /// <summary>The containers that currently hold documents.</summary>
    public IEnumerable<string> Containers => _Blobs.Keys.Select(k => k.Container).Distinct();

    /// <summary>How many documents a container holds.</summary>
    public int CountIn(string container) => _Blobs.Keys.Count(k => k.Container == container);

    /// <inheritdoc />
    public Task SaveAsync(MarketProfile market, string blobName, ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        _Blobs[(market.BlobContainer, blobName)] = content.ToArray();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<byte[]> ReadAsync(MarketProfile market, string blobName,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_Blobs[(market.BlobContainer, blobName)]);
    }

    /// <inheritdoc />
    public Task DeleteAsync(MarketProfile market, string blobName,
        CancellationToken cancellationToken)
    {
        _Blobs.TryRemove((market.BlobContainer, blobName), out _);
        return Task.CompletedTask;
    }
}
