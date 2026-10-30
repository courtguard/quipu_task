using Atlas.Onboarding.IntegrationTests.Support;

namespace Atlas.Onboarding.IntegrationTests;

public class EnvironmentTests
{
    [Fact]
    public void Reports_which_sql_server_the_end_to_end_tests_use()
    {
        // Not a failure when there is none: the end-to-end tests are skipped, with the reason.
        Console.WriteLine(TestSql.IsAvailable
            ? "End-to-end tests run against a SQL Server."
            : "End-to-end tests are skipped: " + TestSql.WhyUnavailable);
        Assert.True(true);
    }
}
