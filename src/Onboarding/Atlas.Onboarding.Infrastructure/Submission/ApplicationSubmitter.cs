using System.Security.Cryptography;
using Atlas.Onboarding.Domain.Applications;
using Atlas.Onboarding.Infrastructure.Jobs;
using Atlas.Onboarding.Infrastructure.Persistence;
using Atlas.Onboarding.Infrastructure.Processing;
using Atlas.Onboarding.Infrastructure.Security;
using Atlas.Onboarding.Infrastructure.Storage;
using Atlas.Shared.Markets;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Atlas.Onboarding.Infrastructure.Submission;

/// <summary>An image submitted with an application.</summary>
/// <param name="Kind">What it shows.</param>
/// <param name="Content">The image bytes.</param>
public sealed record SubmittedDocument(DocumentKind Kind, byte[] Content);

/// <summary>A validated application to record.</summary>
/// <param name="Market">The applicant's market.</param>
/// <param name="Applicant">The applicant's details.</param>
/// <param name="Documents">The submitted images.</param>
/// <param name="IdempotencyKey">Makes a resubmission recognisable.</param>
/// <param name="Fingerprint">SHA-256 of the submitted request.</param>
public sealed record SubmissionRequest(
    MarketProfile Market,
    ApplicantDetails Applicant,
    IReadOnlyList<SubmittedDocument> Documents,
    string IdempotencyKey,
    byte[] Fingerprint);

/// <summary>What recording a submission led to.</summary>
public enum SubmissionOutcome
{
    /// <summary>A new application was recorded and queued.</summary>
    Created,

    /// <summary>This exact request was submitted before; that application is returned.</summary>
    Replayed,

    /// <summary>The idempotency key was used before for a different request.</summary>
    IdempotencyKeyReused,

    /// <summary>The applicant already has an application that is still live.</summary>
    ApplicantAlreadyActive
}

/// <summary>The result of recording a submission.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="ApplicationId">
/// The application, for <see cref="SubmissionOutcome.Created" /> and
/// <see cref="SubmissionOutcome.Replayed" />. Deliberately absent when the applicant already
/// has one: telling a caller that someone applied would disclose that person's data.
/// </param>
public sealed record SubmissionResult(SubmissionOutcome Outcome, Guid? ApplicationId = null);

/// <summary>
/// Records a new application: stores the images, then the application and its job in one
/// transaction. It copes with the same request arriving twice, even at the same moment on two
/// replicas, because the database's unique indexes decide the winner.
/// </summary>
public sealed class ApplicationSubmitter
{
    private const int UNIQUE_INDEX_VIOLATION = 2601;
    private const int UNIQUE_CONSTRAINT_VIOLATION = 2627;

    private readonly MarketDatabases _Databases;
    private readonly IDocumentStore _Documents;
    private readonly ApplicantHasher _Hasher;
    private readonly ProcessingSignal _Signal;
    private readonly TimeProvider _Time;
    private readonly ILogger<ApplicationSubmitter> _Logger;

    /// <summary>Creates the submitter.</summary>
    public ApplicationSubmitter(MarketDatabases databases, IDocumentStore documents,
        ApplicantHasher hasher, ProcessingSignal signal, TimeProvider time,
        ILogger<ApplicationSubmitter> logger)
    {
        _Databases = databases;
        _Documents = documents;
        _Hasher = hasher;
        _Signal = signal;
        _Time = time;
        _Logger = logger;
    }

    /// <summary>Records the submission, unless it duplicates an earlier one.</summary>
    /// <param name="request">The validated submission.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What happened, and the application when there is one.</returns>
    public async Task<SubmissionResult> SubmitAsync(SubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var market = request.Market;
        var applicantHash = _Hasher.Hash(market.Code, request.Applicant);

        var earlier = await FindEarlierAsync(request, applicantHash, cancellationToken);
        if (earlier is not null)
        {
            return earlier;
        }

        var id = Guid.NewGuid();
        var stored = await StoreDocumentsAsync(request, id, cancellationToken);
        try
        {
            var now = _Time.GetUtcNow().UtcDateTime;
            var application = Application.Submit(id, market.Code, request.Applicant,
                applicantHash, request.IdempotencyKey, request.Fingerprint, now);
            foreach (var document in stored)
            {
                application.AddDocument(document);
            }

            await using var context = _Databases.Create(market);
            context.Applications.Add(application);
            context.Jobs.Add(new ProcessingJob(id, now));
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Lost a race with an identical or competing request. Nothing of ours was saved.
            await DeleteDocumentsAsync(market, stored);
            var winner = await FindEarlierAsync(request, applicantHash, cancellationToken);
            return winner ?? throw new InvalidOperationException(
                "A uniqueness conflict occurred but no earlier application was found.", ex);
        }
        catch
        {
            await DeleteDocumentsAsync(market, stored);
            throw;
        }

        _Signal.Notify();
        return new SubmissionResult(SubmissionOutcome.Created, id);
    }

    private async Task<SubmissionResult?> FindEarlierAsync(SubmissionRequest request,
        byte[] applicantHash, CancellationToken cancellationToken)
    {
        await using var context = _Databases.Create(request.Market);

        // One query, not two. With two, the same request arriving twice at once could slip
        // between them: the first finds no matching key, the other request commits, and the
        // second then mistakes it for a different request from the same person.
        var matches = await context.Applications.AsNoTracking()
            .Where(a => a.IdempotencyKey == request.IdempotencyKey
                        || (a.ApplicantHash == applicantHash
                            && a.Status != ApplicationStatus.Rejected
                            && a.Status != ApplicationStatus.Expired))
            .Select(a => new { a.Id, a.IdempotencyKey, a.RequestFingerprint })
            .ToListAsync(cancellationToken);

        var sameKey = matches.FirstOrDefault(m => m.IdempotencyKey == request.IdempotencyKey);
        if (sameKey is not null)
        {
            return CryptographicOperations.FixedTimeEquals(sameKey.RequestFingerprint,
                request.Fingerprint)
                ? new SubmissionResult(SubmissionOutcome.Replayed, sameKey.Id)
                : new SubmissionResult(SubmissionOutcome.IdempotencyKeyReused);
        }

        return matches.Count > 0
            ? new SubmissionResult(SubmissionOutcome.ApplicantAlreadyActive)
            : null;
    }

    private async Task<List<ApplicationDocument>> StoreDocumentsAsync(SubmissionRequest request,
        Guid applicationId, CancellationToken cancellationToken)
    {
        var now = _Time.GetUtcNow().UtcDateTime;
        var stored = new List<ApplicationDocument>();
        try
        {
            foreach (var submitted in request.Documents)
            {
                var documentId = Guid.NewGuid();
                var blobName = $"{applicationId:N}/{documentId:N}";
                await _Documents.SaveAsync(request.Market, blobName, submitted.Content,
                    cancellationToken);
                stored.Add(new ApplicationDocument(documentId, applicationId, submitted.Kind,
                    blobName, SHA256.HashData(submitted.Content), submitted.Content.Length, now));
            }

            return stored;
        }
        catch
        {
            await DeleteDocumentsAsync(request.Market, stored);
            throw;
        }
    }

    private async Task DeleteDocumentsAsync(MarketProfile market,
        IEnumerable<ApplicationDocument> documents)
    {
        foreach (var document in documents)
        {
            try
            {
                await _Documents.DeleteAsync(market, document.BlobName, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // An orphaned image is a leftover, not a reason to fail the request.
                _Logger.LogError("Could not remove an orphaned document: {Kind}",
                    ex.GetType().Name);
            }
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is SqlException
        {
            Number: UNIQUE_INDEX_VIOLATION or UNIQUE_CONSTRAINT_VIOLATION
        };
    }
}
