using CodexRemaining.Core.Usage;

namespace CodexRemaining.Core.Tests;

public sealed class AccountSevenDayUsageFactoryTests
{
    [Fact]
    public void SumsSevenCalendarDaysEndingAtLatestAvailableDate()
    {
        var buckets = new[]
        {
            new AccountDailyTokenBucket(new DateOnly(2026, 7, 15), 99),
            new AccountDailyTokenBucket(new DateOnly(2026, 7, 17), 100),
            new AccountDailyTokenBucket(new DateOnly(2026, 7, 19), 300),
            new AccountDailyTokenBucket(new DateOnly(2026, 7, 23), 700),
        };

        var usage = AccountSevenDayUsageFactory.TryCreate(buckets);

        Assert.NotNull(usage);
        Assert.Equal(1_100, usage.Tokens);
        Assert.Equal(new DateOnly(2026, 7, 23), usage.ThroughDate);
    }
}
