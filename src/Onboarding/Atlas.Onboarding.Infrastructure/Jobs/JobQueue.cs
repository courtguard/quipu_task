using System.Data;
using System.Data.Common;
using Atlas.Onboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Onboarding.Infrastructure.Jobs;

/// <summary>A job a worker has taken hold of.</summary>
/// <param name="ApplicationId">The application to work on.</param>
/// <param name="Attempts">How many times it has been retried so far.</param>
public sealed record ClaimedJob(Guid ApplicationId, int Attempts);

/// <summary>
/// The queue of work on applications, kept in each market's own database. Workers on any number
/// of replicas can take from it at the same time without taking the same job.
/// </summary>
public static class JobQueue
{
    private const string CLAIM_SQL = """
        WITH due AS (
            SELECT TOP (@max) *
            FROM dbo.Jobs WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE NextAttemptAtUtc <= @now
              AND (LockedUntilUtc IS NULL OR LockedUntilUtc <= @now)
            ORDER BY NextAttemptAtUtc
        )
        UPDATE due
        SET LockedUntilUtc = @lockedUntil, LockedBy = @worker
        OUTPUT inserted.ApplicationId, inserted.Attempts;
        """;

    /// <summary>
    /// Takes hold of due jobs. A job held by a worker that has since crashed becomes due again
    /// when its hold runs out. <c>READPAST</c> makes concurrent workers skip rows that another
    /// worker is claiming instead of waiting for them.
    /// </summary>
    /// <param name="context">The market's database.</param>
    /// <param name="worker">Names the claiming worker.</param>
    /// <param name="now">The current time, in UTC.</param>
    /// <param name="lease">How long the worker may hold the jobs.</param>
    /// <param name="max">The most jobs to take.</param>
    /// <param name="cancellationToken">Cancels the claim.</param>
    /// <returns>The jobs now held by this worker.</returns>
    public static async Task<IReadOnlyList<ClaimedJob>> ClaimDueAsync(
        OnboardingDbContext context, string worker, DateTime now, TimeSpan lease, int max,
        CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = CLAIM_SQL;
            AddParameter(command, "@max", DbType.Int32, max);
            AddParameter(command, "@now", DbType.DateTime2, now);
            AddParameter(command, "@lockedUntil", DbType.DateTime2, now + lease);
            AddParameter(command, "@worker", DbType.String, worker);

            var claimed = new List<ClaimedJob>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                claimed.Add(new ClaimedJob(reader.GetGuid(0), reader.GetInt32(1)));
            }

            return claimed;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Removes a job whose application needs no more work for now.</summary>
    /// <param name="context">The market's database.</param>
    /// <param name="applicationId">The application.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public static Task CompleteAsync(OnboardingDbContext context, Guid applicationId,
        CancellationToken cancellationToken)
    {
        return context.Jobs.Where(j => j.ApplicationId == applicationId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Releases a job and makes it due again later.</summary>
    /// <param name="context">The market's database.</param>
    /// <param name="applicationId">The application.</param>
    /// <param name="attempts">The attempt count after this failure.</param>
    /// <param name="nextAttemptAtUtc">When the work is next due, in UTC.</param>
    /// <param name="error">What went wrong, without personal data.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public static Task RescheduleAsync(OnboardingDbContext context, Guid applicationId,
        int attempts, DateTime nextAttemptAtUtc, string error, CancellationToken cancellationToken)
    {
        var shortError = error.Length > 200 ? error[..200] : error;
        return context.Jobs.Where(j => j.ApplicationId == applicationId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Attempts, attempts)
                .SetProperty(j => j.NextAttemptAtUtc, nextAttemptAtUtc)
                .SetProperty(j => j.LockedUntilUtc, (DateTime?)null)
                .SetProperty(j => j.LockedBy, (string?)null)
                .SetProperty(j => j.LastError, shortError), cancellationToken);
    }

    /// <summary>
    /// Queues an application again, for example after a compliance officer has decided. Does
    /// nothing if it is already queued.
    /// </summary>
    /// <param name="context">The market's database.</param>
    /// <param name="applicationId">The application.</param>
    /// <param name="now">The current time, in UTC.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public static Task EnqueueIfMissingAsync(OnboardingDbContext context, Guid applicationId,
        DateTime now, CancellationToken cancellationToken)
    {
        return context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.Jobs (ApplicationId, NextAttemptAtUtc, Attempts)
            SELECT {applicationId}, {now}, 0
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Jobs WHERE ApplicationId = {applicationId})
            """, cancellationToken);
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
