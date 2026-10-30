using System.Security.Cryptography;
using Atlas.Onboarding.Domain.Applications;
using Atlas.Onboarding.Infrastructure.Persistence;
using Atlas.Onboarding.Infrastructure.Providers;
using Atlas.Onboarding.Infrastructure.Storage;
using Atlas.Shared.Markets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atlas.Onboarding.Infrastructure.Processing;

/// <summary>What a processing attempt achieved.</summary>
public enum ProcessingOutcome
{
    /// <summary>
    /// Nothing more to do for now: the application is final, or waiting for a person
    /// (a compliance officer or a branch visit).
    /// </summary>
    Finished,

    /// <summary>It could not go further, typically because a provider did not answer.</summary>
    RetryLater
}

/// <summary>
/// Moves one application as far as it can go. It is the only place that carries out the
/// workflow, whether this is the first attempt, a retry after an outage, or a resumption after a
/// crash. Every step records its result before the next begins, and every call to an outside
/// system can safely be repeated, so stopping at any point loses nothing.
/// </summary>
public sealed class ApplicationProcessor
{
    private readonly MarketDatabases _Databases;
    private readonly MarketRegistry _Markets;
    private readonly IDocumentStore _Documents;
    private readonly IIdentityVerifier _Identity;
    private readonly IScreeningService _Screening;
    private readonly ICoreBanking _CoreBanking;
    private readonly TimeProvider _Time;
    private readonly ProcessingOptions _Options;
    private readonly ILogger<ApplicationProcessor> _Logger;

    /// <summary>Creates the processor.</summary>
    public ApplicationProcessor(MarketDatabases databases, MarketRegistry markets,
        IDocumentStore documents, IIdentityVerifier identity, IScreeningService screening,
        ICoreBanking coreBanking, TimeProvider time, IOptions<ProcessingOptions> options,
        ILogger<ApplicationProcessor> logger)
    {
        _Databases = databases;
        _Markets = markets;
        _Documents = documents;
        _Identity = identity;
        _Screening = screening;
        _CoreBanking = coreBanking;
        _Time = time;
        _Options = options.Value;
        _Logger = logger;
    }

    /// <summary>Works on an application until it is finished or cannot go further.</summary>
    /// <param name="marketCode">The application's market.</param>
    /// <param name="applicationId">The application.</param>
    /// <param name="cancellationToken">Cancels the work, for example at shutdown.</param>
    /// <returns>Whether the job is done or must be retried.</returns>
    /// <exception cref="DbUpdateConcurrencyException">
    /// Someone else changed the application meanwhile; the caller retries.
    /// </exception>
    public async Task<ProcessingOutcome> ProcessAsync(string marketCode, Guid applicationId,
        CancellationToken cancellationToken)
    {
        var market = _Markets.Find(marketCode)
                     ?? throw new InvalidOperationException($"Unknown market {marketCode}.");
        await using var context = _Databases.Create(market);
        var application = await context.Applications.Include(a => a.Documents)
            .SingleOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
        if (application is null)
        {
            return ProcessingOutcome.Finished;
        }

        if (application.Status == ApplicationStatus.Processing)
        {
            var next = await VerifyAsync(context, market, application, cancellationToken);
            if (next is not null)
            {
                return next.Value;
            }
        }

        return application.Status == ApplicationStatus.Fulfilling
            ? await FulfilAsync(context, market, application, cancellationToken)
            : ProcessingOutcome.Finished;
    }

    // Returns null when the application has been cleared and should go on to fulfilment.
    private async Task<ProcessingOutcome?> VerifyAsync(OnboardingDbContext context,
        MarketProfile market, Application application, CancellationToken cancellationToken)
    {
        var now = _Time.GetUtcNow().UtcDateTime;
        if (application.HasOutlived(now, TimeSpan.FromHours(_Options.MaxProcessingAgeHours)))
        {
            application.Expire(now);
            await context.SaveChangesAsync(cancellationToken);
            _Logger.LogWarning("Application {ApplicationId} expired without a decision",
                application.Id);
            return ProcessingOutcome.Finished;
        }

        // Identity and screening do not depend on each other, so they run side by side. Each
        // result is recorded as soon as it is known, so a retry only repeats what is missing.
        var identity = application.Identity is null
            ? CallIdentityAsync(market, application, cancellationToken)
            : Task.FromResult<ProviderResult<IdentityVerification>?>(null);
        var screening = application.Screening is null
            ? CallScreeningAsync(market, application, cancellationToken)
            : Task.FromResult<ProviderResult<ScreeningOutcome>?>(null);
        await Task.WhenAll(identity, screening);

        now = _Time.GetUtcNow().UtcDateTime;
        if (identity.Result is { IsSuccess: true } identityResult)
        {
            application.RecordIdentity(identityResult.Value.Check,
                identityResult.Value.IdentificationId, now);
        }

        if (screening.Result is { IsSuccess: true } screeningResult)
        {
            application.RecordScreening(screeningResult.Value.Check, screeningResult.Value.CaseId,
                now);
        }

        application.ApplyVerification(market, now);
        await context.SaveChangesAsync(cancellationToken);
        _Logger.LogInformation("Application {ApplicationId} is {Status}", application.Id,
            application.Status);

        return application.Status switch
        {
            ApplicationStatus.Processing => ProcessingOutcome.RetryLater,
            ApplicationStatus.Fulfilling => null,
            _ => ProcessingOutcome.Finished
        };
    }

    private async Task<ProviderResult<IdentityVerification>?> CallIdentityAsync(
        MarketProfile market, Application application, CancellationToken cancellationToken)
    {
        var document = application.Documents.FirstOrDefault(d => d.Kind != DocumentKind.Selfie);
        var selfie = application.Documents.FirstOrDefault(d => d.Kind == DocumentKind.Selfie);
        if (document is null || selfie is null)
        {
            throw new InvalidOperationException("The application has no document or selfie.");
        }

        var documentBytes = await ReadAsync(market, document, cancellationToken);
        var selfieBytes = await ReadAsync(market, selfie, cancellationToken);
        return await _Identity.VerifyAsync(market, document.Kind, documentBytes, selfieBytes,
            cancellationToken);
    }

    private async Task<ProviderResult<ScreeningOutcome>?> CallScreeningAsync(
        MarketProfile market, Application application, CancellationToken cancellationToken)
    {
        return await _Screening.ScreenAsync(market, application.Applicant, cancellationToken);
    }

    private async Task<byte[]> ReadAsync(MarketProfile market, ApplicationDocument document,
        CancellationToken cancellationToken)
    {
        var bytes = await _Documents.ReadAsync(market, document.BlobName, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), document.Sha256))
        {
            throw new InvalidOperationException("A stored document does not match its hash.");
        }

        return bytes;
    }

    private async Task<ProcessingOutcome> FulfilAsync(OnboardingDbContext context,
        MarketProfile market, Application application, CancellationToken cancellationToken)
    {
        var name = application.Applicant.FullName;

        // The keys make each call safe to repeat: after a crash or a timeout the same call
        // returns the same account or card order, and never creates a second one.
        if (application.AccountId is null)
        {
            var account = await _CoreBanking.OpenAccountAsync(market, $"{application.Id}:account",
                name, cancellationToken);
            if (!account.IsSuccess)
            {
                return ProcessingOutcome.RetryLater;
            }

            application.RecordAccount(account.Value, _Time.GetUtcNow().UtcDateTime);
            await context.SaveChangesAsync(cancellationToken);
        }

        if (application.CardOrderId is null)
        {
            var card = await _CoreBanking.OrderCardAsync(market, $"{application.Id}:card",
                application.AccountId!, name, cancellationToken);
            if (!card.IsSuccess)
            {
                // The account stays: it is never closed because the card could not be ordered.
                return ProcessingOutcome.RetryLater;
            }

            application.RecordCardOrder(card.Value, _Time.GetUtcNow().UtcDateTime);
            await context.SaveChangesAsync(cancellationToken);
        }

        application.Approve(_Time.GetUtcNow().UtcDateTime);
        await context.SaveChangesAsync(cancellationToken);
        _Logger.LogInformation("Application {ApplicationId} is {Status}", application.Id,
            application.Status);
        return ProcessingOutcome.Finished;
    }
}
