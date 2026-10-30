using System.Net;
using Atlas.Onboarding.Infrastructure.Provisioning;
using Atlas.Onboarding.IntegrationTests.Support;
using Microsoft.Data.SqlClient;

namespace Atlas.Onboarding.IntegrationTests;

/// <summary>
/// Each service runs with its own login, which can do its job and nothing else. No shared
/// administrator account is involved in reading, changing or erasing a customer record, and
/// neither service can alter or read the access log.
/// </summary>
public sealed class PrivilegeTests : ApiTestBase, IDisposable
{
    private readonly ServiceLogin _Onboarding = NewLogin("atlas_t_o_");
    private readonly ServiceLogin _Review = NewLogin("atlas_t_r_");
    private bool _Provisioned;

    // The base API runs no worker, so only the API started with the restricted login does work.
    public PrivilegeTests(AtlasFixture fixture)
        : base(fixture, new Dictionary<string, string> { ["Processing:WorkerEnabled"] = "false" })
    {
    }

    private static ServiceLogin NewLogin(string prefix)
    {
        return new ServiceLogin(prefix + Guid.NewGuid().ToString("N")[..8],
            "Test_Pw_1!" + Guid.NewGuid().ToString("N"));
    }

    private async Task ProvisionAsync(params string[] markets)
    {
        foreach (var code in markets)
        {
            await MarketDatabaseProvisioner.ProvisionAsync(TestSql.AdminConnectionString!,
                Fixture.Market(code), _Onboarding, _Review, CancellationToken.None);
        }

        _Provisioned = true;
    }

    private static string ConnectionString(ServiceLogin login, string? database = null)
    {
        var builder = new SqlConnectionStringBuilder(TestSql.AdminConnectionString)
        {
            IntegratedSecurity = false,
            UserID = login.Name,
            Password = login.Password
        };
        if (database is not null)
        {
            builder.InitialCatalog = database;
        }

        return builder.ConnectionString;
    }

    private async Task<object?> RunAsync(ServiceLogin login, string sql, string market = "MB")
    {
        await using var connection = new SqlConnection(
            ConnectionString(login, Fixture.Databases[market]));
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
        command.CommandText = $"DROP LOGIN [{_Onboarding.Name}]; DROP LOGIN [{_Review.Name}];";
        command.ExecuteNonQuery();
    }

    [SqlFact]
    public async Task The_onboarding_login_can_read_and_write_the_onboarding_tables()
    {
        await ProvisionAsync("MB");

        Assert.NotNull(await RunAsync(_Onboarding, "SELECT COUNT(*) FROM dbo.Applications"));
        await RunAsync(_Onboarding, """
            INSERT INTO dbo.Jobs (ApplicationId, NextAttemptAtUtc, Attempts)
            VALUES (NEWID(), SYSUTCDATETIME(), 0);
            DELETE FROM dbo.Jobs;
            """);
    }

    [SqlFact]
    public async Task Neither_login_can_change_the_schema()
    {
        await ProvisionAsync("MB");

        foreach (var login in new[] { _Onboarding, _Review })
        {
            foreach (var sql in new[]
                     {
                         "CREATE TABLE dbo.Intruder (Id int)",
                         "DROP TABLE dbo.Applications",
                         "ALTER TABLE dbo.Applications ADD Extra int",
                         "CREATE LOGIN intruder WITH PASSWORD = 'Intruder_1!x'"
                     })
            {
                await Assert.ThrowsAsync<SqlException>(() => RunAsync(login, sql));
            }
        }
    }

    [SqlFact]
    public async Task Neither_login_can_read_alter_or_erase_the_access_log()
    {
        await ProvisionAsync("MB");

        foreach (var login in new[] { _Onboarding, _Review })
        {
            await RunAsync(login, """
                INSERT INTO dbo.AccessLog (Id, ApplicationId, Actor, Action, OccurredAtUtc)
                VALUES (NEWID(), NEWID(), 'test', 'TEST', SYSUTCDATETIME());
                """);
            foreach (var sql in new[]
                     {
                         "SELECT COUNT(*) FROM dbo.AccessLog",
                         "UPDATE dbo.AccessLog SET Actor = 'someone else'",
                         "DELETE FROM dbo.AccessLog"
                     })
            {
                await Assert.ThrowsAsync<SqlException>(() => RunAsync(login, sql));
            }
        }
    }

    [SqlFact]
    public async Task The_review_login_sees_only_the_review_queue()
    {
        await ProvisionAsync("MB");

        Assert.NotNull(await RunAsync(_Review, "SELECT COUNT(*) FROM dbo.ReviewQueue"));
        foreach (var sql in new[]
                 {
                     "SELECT COUNT(*) FROM dbo.Applications",
                     "SELECT COUNT(*) FROM dbo.Documents",
                     "UPDATE dbo.Applications SET Status = 4",
                     "DELETE FROM dbo.Applications",
                     "SELECT COUNT(*) FROM dbo.ReviewDecisions"
                 })
        {
            await Assert.ThrowsAsync<SqlException>(() => RunAsync(_Review, sql));
        }
    }

    [SqlFact]
    public async Task Only_the_onboarding_login_can_run_the_retention_purge()
    {
        await ProvisionAsync("MB");
        const string PURGE_NOTHING =
            "EXEC dbo.PurgeExpiredApplications @cutoff = '1900-01-01'";

        Assert.Equal(0, await RunAsync(_Onboarding, PURGE_NOTHING));
        await Assert.ThrowsAsync<SqlException>(() => RunAsync(_Review, PURGE_NOTHING));
    }

    [SqlFact]
    public async Task The_review_login_sees_whether_an_application_exists_but_not_whose_it_is()
    {
        await ProvisionAsync("MB");

        Assert.NotNull(await RunAsync(_Review, "SELECT COUNT(*) FROM dbo.ApplicationDirectory"));
        await Assert.ThrowsAsync<SqlException>(
            () => RunAsync(_Review, "SELECT FirstName FROM dbo.ApplicationDirectory"));
    }

    [SqlFact]
    public async Task A_login_cannot_open_the_database_of_a_market_it_was_not_given()
    {
        await ProvisionAsync("MB");

        await Assert.ThrowsAsync<SqlException>(
            () => RunAsync(_Onboarding, "SELECT COUNT(*) FROM dbo.Applications", "MA"));
        await Assert.ThrowsAsync<SqlException>(
            () => RunAsync(_Review, "SELECT COUNT(*) FROM dbo.ReviewQueue", "MA"));
    }

    [SqlFact]
    public async Task The_whole_flow_works_with_only_the_restricted_logins()
    {
        await ProvisionAsync(AtlasFixture.MarketCodes);
        using var onboarding = Fixture.CreateApi(new Dictionary<string, string>
        {
            ["Sql:ConnectionString"] = ConnectionString(_Onboarding)
        });
        using var review = Fixture.CreateReviewApi(new Dictionary<string, string>
        {
            ["Sql:ConnectionString"] = ConnectionString(_Review)
        });
        using var client = onboarding.CreateClient();
        using var officer = await StaffClient.SignInAsync(Fixture, review, "officer-1",
            "compliance_officer", "MB");

        var approved = await client.SubmitAsync(Applicant.For("MB"));
        var referred = await client.SubmitAsync(
            Applicant.For("MB", c => c.LastName = "Possible-Match"));
        Assert.Equal(HttpStatusCode.Created, approved.Status);
        Assert.Equal("APPROVED", approved.State);
        Assert.Equal("UNDER_REVIEW", referred.State);

        Assert.Equal(HttpStatusCode.OK, (await officer.GetAsync($"/reviews/{referred.Id}")).Status);
        Assert.Equal(HttpStatusCode.Accepted,
            (await officer.DecideAsync(referred.Id, "APPROVE")).Status);
        await EventuallyAsync(async () =>
            (await client.GetStatusAsync(referred.Id, referred.Token)).State == "APPROVED",
            "the reviewed application to be approved");
    }
}
