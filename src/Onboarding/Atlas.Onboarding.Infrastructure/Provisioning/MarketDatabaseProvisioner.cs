using System.Text.RegularExpressions;
using Atlas.Onboarding.Infrastructure.Persistence;
using Atlas.Shared.Markets;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Onboarding.Infrastructure.Provisioning;

/// <summary>A SQL login a service connects with.</summary>
/// <param name="Name">The login and user name.</param>
/// <param name="Password">The login's password.</param>
public sealed record ServiceLogin(string Name, string Password);

/// <summary>
/// Creates a market's database and schema, and gives each service its own login with only the
/// permissions it needs. It runs with administrator rights, once, outside the services: the
/// running services hold no such rights, so no shared or generic credential can read, change or
/// erase customer records.
/// </summary>
public static partial class MarketDatabaseProvisioner
{
    // The contract with the Review API: the only part of the applications it can see. It shows
    // cases waiting for a person, with what a reviewer needs, and nothing else. Statuses 1 and 2
    // are PendingComplianceReview and AwaitingBranchActivation.
    private const string REVIEW_QUEUE_VIEW = """
        CREATE OR ALTER VIEW dbo.ReviewQueue AS
        SELECT a.Id AS ApplicationId,
               a.Market,
               CASE a.Status WHEN 1 THEN 'REVIEW' WHEN 2 THEN 'BRANCH' END AS Stage,
               a.FirstName, a.LastName, a.DateOfBirth, a.Nationality,
               a.NationalId, a.PassportNumber,
               CASE a.DocumentResult WHEN 0 THEN 'VALID' WHEN 1 THEN 'INVALID'
                                     WHEN 2 THEN 'INCONCLUSIVE' END AS DocumentResult,
               a.FaceMatch, a.Confidence, a.ScreeningCaseId,
               a.CreatedAtUtc AS SubmittedAtUtc
        FROM dbo.Applications a
        WHERE a.Status IN (1, 2);
        """;

    // The onboarding service owns the applications; it may only add to the access log.
    private const string ONBOARDING_GRANTS = """
        GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Applications TO [{0}];
        GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Documents TO [{0}];
        GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Jobs TO [{0}];
        GRANT SELECT, UPDATE ON dbo.ReviewDecisions TO [{0}];
        GRANT INSERT ON dbo.AccessLog TO [{0}];
        """;

    // The Review API sees the queue, records decisions and wakes the worker. It can change no
    // application and read no log.
    private const string REVIEW_GRANTS = """
        GRANT SELECT ON dbo.ReviewQueue TO [{0}];
        GRANT INSERT ON dbo.ReviewDecisions TO [{0}];
        GRANT SELECT, INSERT ON dbo.Jobs TO [{0}];
        GRANT INSERT ON dbo.AccessLog TO [{0}];
        """;

    /// <summary>
    /// Creates the market's database, schema and review view if missing, then the service
    /// logins that are given. Safe to run again.
    /// </summary>
    /// <param name="adminConnectionString">An administrator's connection to the server.</param>
    /// <param name="market">The market.</param>
    /// <param name="onboardingLogin">The onboarding service's login, or null for none.</param>
    /// <param name="reviewLogin">The Review API's login, or null for none.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public static async Task ProvisionAsync(string adminConnectionString, MarketProfile market,
        ServiceLogin? onboardingLogin, ServiceLogin? reviewLogin,
        CancellationToken cancellationToken)
    {
        RequireSafeName(market.Database);
        await EnsureDatabaseAsync(adminConnectionString, market.Database, cancellationToken);
        await EnsureSchemaAsync(adminConnectionString, market.Database, cancellationToken);
        await ExecuteAsync(WithDatabase(adminConnectionString, market.Database),
            REVIEW_QUEUE_VIEW, cancellationToken);

        if (onboardingLogin is not null)
        {
            await EnsureLoginAsync(adminConnectionString, market.Database, onboardingLogin,
                ONBOARDING_GRANTS, cancellationToken);
        }

        if (reviewLogin is not null)
        {
            await EnsureLoginAsync(adminConnectionString, market.Database, reviewLogin,
                REVIEW_GRANTS, cancellationToken);
        }
    }

    /// <summary>Drops a market's database. Used by tests to clean up after themselves.</summary>
    /// <param name="adminConnectionString">An administrator's connection to the server.</param>
    /// <param name="database">The database to drop.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public static async Task DropDatabaseAsync(string adminConnectionString, string database,
        CancellationToken cancellationToken)
    {
        RequireSafeName(database);
        SqlConnection.ClearAllPools();
        await ExecuteAsync(WithDatabase(adminConnectionString, "master"), $"""
            IF DB_ID(N'{database}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{database}];
            END
            """, cancellationToken);
    }

    private static Task EnsureDatabaseAsync(string admin, string database,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(WithDatabase(admin, "master"),
            $"IF DB_ID(N'{database}') IS NULL CREATE DATABASE [{database}];", cancellationToken);
    }

    private static async Task EnsureSchemaAsync(string admin, string database,
        CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseSqlServer(WithDatabase(admin, database)).Options;
        await using var context = new OnboardingDbContext(options);

        // The database was just created empty, so this builds the whole schema. It does not
        // alter an existing one: versioned migrations are a known gap.
        await context.Database.EnsureCreatedAsync(cancellationToken);
    }

    private static async Task EnsureLoginAsync(string admin, string database, ServiceLogin login,
        string grants, CancellationToken cancellationToken)
    {
        RequireSafeName(login.Name);
        var password = login.Password.Replace("'", "''");
        await ExecuteAsync(WithDatabase(admin, "master"), $"""
            IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{login.Name}')
                CREATE LOGIN [{login.Name}] WITH PASSWORD = N'{password}';
            """, cancellationToken);

        await ExecuteAsync(WithDatabase(admin, database), $"""
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'{login.Name}')
                CREATE USER [{login.Name}] FOR LOGIN [{login.Name}];
            {string.Format(grants, login.Name)}
            """, cancellationToken);
    }

    private static string WithDatabase(string connectionString, string database)
    {
        return new SqlConnectionStringBuilder(connectionString) { InitialCatalog = database }
            .ConnectionString;
    }

    private static async Task ExecuteAsync(string connectionString, string sql,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Names are put into SQL text, which cannot be parameterised, so they are restricted.
    private static void RequireSafeName(string name)
    {
        if (!SafeName().IsMatch(name))
        {
            throw new ArgumentException(
                "Names must start with a letter and contain only letters, digits and underscores.");
        }
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.None, 100)]
    private static partial Regex SafeName();
}
