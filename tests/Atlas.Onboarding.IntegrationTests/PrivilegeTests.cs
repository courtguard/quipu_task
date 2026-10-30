using System.Net;
using Atlas.Onboarding.Infrastructure.Provisioning;
using Atlas.Onboarding.IntegrationTests.Support;
using Microsoft.Data.SqlClient;

namespace Atlas.Onboarding.IntegrationTests;

/// <summary>
/// The onboarding service runs with its own login, which can use the onboarding tables and
/// nothing else. No shared administrator account is involved in reading, changing or erasing a
/// customer record.
/// </summary>
public sealed class PrivilegeTests : ApiTestBase, IDisposable
{
    private readonly string _Login = "atlas_t_" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _Password = "Atlas_Test_1!" + Guid.NewGuid().ToString("N");
    private bool _Provisioned;

    // The base API runs no worker, so only the API started with the restricted login does work.
    public PrivilegeTests(AtlasFixture fixture)
        : base(fixture, new Dictionary<string, string> { ["Processing:WorkerEnabled"] = "false" })
    {
    }

    private async Task ProvisionAsync(params string[] markets)
    {
        var login = new ServiceLogin(_Login, _Password);
        foreach (var code in markets)
        {
            await MarketDatabaseProvisioner.ProvisionAsync(TestSql.AdminConnectionString!,
                Fixture.Market(code), login, CancellationToken.None);
        }

        _Provisioned = true;
    }

    private string ConnectionString(string? database = null)
    {
        var builder = new SqlConnectionStringBuilder(TestSql.AdminConnectionString)
        {
            IntegratedSecurity = false,
            UserID = _Login,
            Password = _Password
        };
        if (database is not null)
        {
            builder.InitialCatalog = database;
        }

        return builder.ConnectionString;
    }

    private async Task<object?> RunAsync(string database, string sql)
    {
        await using var connection = new SqlConnection(ConnectionString(database));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    public new void Dispose()
    {
        base.Dispose();
        if (!_Provisioned)
        {
            return;
        }

        SqlConnection.ClearAllPools();
        using var connection = new SqlConnection(TestSql.AdminConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"DROP LOGIN [{_Login}]";
        command.ExecuteNonQuery();
    }

    [SqlFact]
    public async Task The_login_can_read_and_write_the_onboarding_tables()
    {
        await ProvisionAsync("MB");
        var database = Fixture.Databases["MB"];

        Assert.NotNull(await RunAsync(database, "SELECT COUNT(*) FROM dbo.Applications"));
        await RunAsync(database, """
            INSERT INTO dbo.Jobs (ApplicationId, NextAttemptAtUtc, Attempts)
            VALUES (NEWID(), SYSUTCDATETIME(), 0);
            DELETE FROM dbo.Jobs;
            """);
    }

    [SqlFact]
    public async Task The_login_cannot_change_the_schema()
    {
        await ProvisionAsync("MB");
        var database = Fixture.Databases["MB"];

        foreach (var sql in new[]
                 {
                     "CREATE TABLE dbo.Intruder (Id int)",
                     "DROP TABLE dbo.Applications",
                     "ALTER TABLE dbo.Applications ADD Extra int",
                     "CREATE LOGIN intruder WITH PASSWORD = 'Intruder_1!x'"
                 })
        {
            await Assert.ThrowsAsync<SqlException>(() => RunAsync(database, sql));
        }
    }

    [SqlFact]
    public async Task The_login_cannot_open_the_database_of_a_market_it_was_not_given()
    {
        await ProvisionAsync("MB");

        await Assert.ThrowsAsync<SqlException>(
            () => RunAsync(Fixture.Databases["MA"], "SELECT COUNT(*) FROM dbo.Applications"));
    }

    [SqlFact]
    public async Task The_whole_flow_works_with_only_the_restricted_login()
    {
        await ProvisionAsync(AtlasFixture.MarketCodes);
        using var restricted = Fixture.CreateApi(new Dictionary<string, string>
        {
            ["Sql:ConnectionString"] = ConnectionString()
        });
        using var client = restricted.CreateClient();

        var approved = await client.SubmitAsync(Applicant.For("MB"));
        var review = await client.SubmitAsync(
            Applicant.For("MB", c => c.LastName = "Possible-Match"));

        Assert.Equal(HttpStatusCode.Created, approved.Status);
        Assert.Equal("APPROVED", approved.State);
        Assert.Equal("UNDER_REVIEW", review.State);
    }
}
