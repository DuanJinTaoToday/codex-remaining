using CodexRemaining.Core.Application;
using CodexRemaining.Core.Codex;
using CodexRemaining.Core.Presentation;
using CodexRemaining.Core.Usage;

namespace CodexRemaining.Core.Tests;

public sealed class UsageDisplayModelTests
{
    private static readonly TimeZoneInfo ChinaTimeZone = TimeZoneInfo.CreateCustomTimeZone(
        "China Test",
        TimeSpan.FromHours(8),
        "China Test",
        "China Test");

    [Fact]
    public void FreshUsageUsesLocalResetTimeAndRiskVisual()
    {
        var state = new UsageMonitorState(
            UsageStateKind.Fresh,
            new WeeklyUsage(
                18,
                new DateTimeOffset(2026, 7, 22, 6, 30, 0, TimeSpan.Zero),
                UsageRisk.Warning),
            new DateTimeOffset(2026, 7, 15, 3, 20, 0, TimeSpan.Zero),
            null);

        var display = UsageDisplayModel.FromState(state, ChinaTimeZone);

        Assert.Equal("周 18%", display.CompactText);
        Assert.Equal("重置：7月22日 14:30", display.ResetText);
        Assert.Equal("更新：7月15日 11:20", display.UpdatedText);
        Assert.Equal(UsageVisualState.Warning, display.VisualState);
        Assert.Equal(string.Empty, display.StatusText);
    }

    [Fact]
    public void StaleUsageIsVisuallyStaleInsteadOfUsingRiskColor()
    {
        var error = new UsageReadError(UsageReadErrorKind.Unavailable, "网络暂时不可用。");
        var state = new UsageMonitorState(
            UsageStateKind.Stale,
            new WeeklyUsage(5, null, UsageRisk.Danger),
            new DateTimeOffset(2026, 7, 15, 3, 20, 0, TimeSpan.Zero),
            error);

        var display = UsageDisplayModel.FromState(state, ChinaTimeZone);

        Assert.Equal("周 5%", display.CompactText);
        Assert.Equal("数据已过期", display.StatusText);
        Assert.Equal("网络暂时不可用。", display.DetailText);
        Assert.Equal(UsageVisualState.Stale, display.VisualState);
    }
}
