using Microsoft.Data.SqlClient;

namespace Atlas.Onboarding.IntegrationTests.Support;

/// <summary>
/// Finds a SQL Server to test against: the one named in <c>ATLAS_TEST_SQL</c>, else the Docker
/// SQL Server from the platform compose file, else the Windows LocalDB.
/// </summary>
public static class TestSql
{
    private static readonly string[] _Candidates =
    {
        "Server=localhost,1433;User Id=sa;Password=Local_Dev_Password_1;"
        + "TrustServerCertificate=True;Connect Timeout=3",
        @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=True;"
        + "Connect Timeout=15"
    };

    private static readonly List<string> _Failures = new();
    private static readonly Lazy<string?> _AdminConnectionString = new(Find);

    /// <summary>An administrator's connection, or null when no SQL Server is reachable.</summary>
    public static string? AdminConnectionString => _AdminConnectionString.Value;

    /// <summary>Whether a SQL Server can be used.</summary>
    public static bool IsAvailable => AdminConnectionString is not null;

    /// <summary>Why each candidate could not be used, to explain a skipped run.</summary>
    public static string WhyUnavailable => string.Join(" | ", _Failures);

    private static string? Find()
    {
        var configured = Environment.GetEnvironmentVariable("ATLAS_TEST_SQL");
        var candidates = string.IsNullOrWhiteSpace(configured)
            ? _Candidates
            : new[] { configured };
        return candidates.FirstOrDefault(CanConnect);
    }

    private static bool CanConnect(string connectionString)
    {
        try
        {
            using var connection = new SqlConnection(connectionString);
            connection.Open();
            return true;
        }
        catch (Exception ex)
        {
            // Whatever the reason, this server cannot be used. Keep it to explain the skip.
            _Failures.Add($"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            return false;
        }
    }
}

/// <summary>A test that needs SQL Server, and is skipped when there is none.</summary>
public sealed class SqlFactAttribute : FactAttribute
{
    /// <summary>Marks the test as skipped when no SQL Server is reachable.</summary>
    public SqlFactAttribute()
    {
        if (!TestSql.IsAvailable)
        {
            Skip = "No SQL Server reachable. Start platform/docker-compose.yml, or set "
                   + "ATLAS_TEST_SQL. Tried: " + TestSql.WhyUnavailable;
        }
    }
}
