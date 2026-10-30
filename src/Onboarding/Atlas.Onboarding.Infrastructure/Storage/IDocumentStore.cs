using Atlas.Shared.Markets;

namespace Atlas.Onboarding.Infrastructure.Storage;

/// <summary>
/// Where identity documents and selfies are kept. Each market has its own container so that the
/// images stay in the applicant's country.
/// </summary>
public interface IDocumentStore
{
    /// <summary>Stores an image.</summary>
    /// <param name="market">The market, which decides the container.</param>
    /// <param name="blobName">The name to store it under.</param>
    /// <param name="content">The image bytes.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task SaveAsync(MarketProfile market, string blobName, ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);

    /// <summary>Reads an image back.</summary>
    /// <param name="market">The market, which decides the container.</param>
    /// <param name="blobName">The name it was stored under.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The image bytes.</returns>
    Task<byte[]> ReadAsync(MarketProfile market, string blobName,
        CancellationToken cancellationToken);

    /// <summary>Deletes an image. Deleting one that does not exist is not an error.</summary>
    /// <param name="market">The market, which decides the container.</param>
    /// <param name="blobName">The name it was stored under.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task DeleteAsync(MarketProfile market, string blobName, CancellationToken cancellationToken);
}
