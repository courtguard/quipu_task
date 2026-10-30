using Atlas.Shared.Markets;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Atlas.Onboarding.Infrastructure.Persistence;

/// <summary>How the service reaches SQL Server.</summary>
public sealed class SqlOptions
{
    /// <summary>
    /// Connection string without a database. The database is chosen per market, and the
    /// credentials are this service's own login, never a shared administrator.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}

/// <summary>
/// Hands out database contexts for a market. Every market has its own database.
/// </summary>
public sealed class MarketDatabases
{
    private readonly string _BaseConnectionString;

    /// <summary>Creates the factory.</summary>
    /// <param name="options">How to reach SQL Server.</param>
    public MarketDatabases(IOptions<SqlOptions> options)
    {
        _BaseConnectionString = options.Value.ConnectionString;
    }

    /// <summary>The connection string for one market's database.</summary>
    /// <param name="market">The market.</param>
    /// <returns>The connection string.</returns>
    public string ConnectionStringFor(MarketProfile market)
    {
        var builder = new SqlConnectionStringBuilder(_BaseConnectionString)
        {
            InitialCatalog = market.Database
        };
        return builder.ConnectionString;
    }

    /// <summary>Opens a new context on a market's database. The caller disposes it.</summary>
    /// <param name="market">The market.</param>
    /// <returns>A new context.</returns>
    public OnboardingDbContext Create(MarketProfile market)
    {
        var options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseSqlServer(ConnectionStringFor(market))
            .Options;
        return new OnboardingDbContext(options);
    }
}
