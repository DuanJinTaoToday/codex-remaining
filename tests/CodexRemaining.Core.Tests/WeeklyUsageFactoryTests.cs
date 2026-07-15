using CodexRemaining.Core.Usage;

namespace CodexRemaining.Core.Tests;

public sealed class WeeklyUsageFactoryTests
{
    [Fact]
    public void FindsWeeklyWindowAndConvertsUsedPercentToRemainingPercent()
    {
        var resetAt = DateTimeOffset.FromUnixTimeSeconds(1_767_225_800);
        var windows = new[]
        {
            new RateLimitWindowData(90, 1_767_222_200, 300),
            new RateLimitWindowData(23, resetAt.ToUnixTimeSeconds(), 10_080),
        };

        var usage = WeeklyUsageFactory.TryCreate(windows);

        Assert.NotNull(usage);
        Assert.Equal(77, usage.RemainingPercent);
        Assert.Equal(resetAt, usage.ResetsAt);
    }

    [Theory]
    [InlineData(79, UsageRisk.Normal)]
    [InlineData(80, UsageRisk.Warning)]
    [InlineData(90, UsageRisk.Warning)]
    [InlineData(91, UsageRisk.Danger)]
    public void ClassifiesRiskFromRemainingPercent(int usedPercent, UsageRisk expectedRisk)
    {
        var windows = new[]
        {
            new RateLimitWindowData(usedPercent, null, 10_080),
        };

        var usage = WeeklyUsageFactory.TryCreate(windows);

        Assert.NotNull(usage);
        Assert.Equal(expectedRisk, usage.Risk);
    }

    [Theory]
    [InlineData(-1, 10_080)]
    [InlineData(101, 10_080)]
    [InlineData(50, 300)]
    public void RejectsWindowsThatCannotRepresentWeeklyUsage(int usedPercent, long durationMins)
    {
        var windows = new[]
        {
            new RateLimitWindowData(usedPercent, null, durationMins),
        };

        Assert.Null(WeeklyUsageFactory.TryCreate(windows));
    }
}
