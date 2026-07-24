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
        Assert.Equal("数据已过期 · 7月15日 11:20", display.StatusText);
        Assert.Equal("网络暂时不可用。", display.DetailText);
        Assert.Equal(UsageVisualState.Stale, display.VisualState);
    }

    [Fact]
    public void ExpandedUsageFormatsIndependentAccountAndLocalTokenStates()
    {
        var state = new UsageMonitorState(
            UsageStateKind.Fresh,
            new WeeklyUsage(
                66,
                new DateTimeOffset(2026, 7, 25, 4, 18, 0, TimeSpan.Zero),
                UsageRisk.Normal),
            new DateTimeOffset(2026, 7, 24, 2, 55, 0, TimeSpan.Zero),
            null,
            UsageStateKind.Stale,
            new AccountSevenDayUsage(29_147_000, new DateOnly(2026, 7, 23)),
            new DateTimeOffset(2026, 7, 24, 2, 50, 0, TimeSpan.Zero),
            new UsageReadError(UsageReadErrorKind.Unavailable, "账号统计延迟。"),
            UsageStateKind.Fresh,
            new LocalTodayTokenUsage(2_861_234),
            new DateTimeOffset(2026, 7, 24, 2, 55, 0, TimeSpan.Zero),
            null);

        var display = UsageDisplayModel.FromState(state, ChinaTimeZone);

        Assert.Equal(34, display.UsedPercent);
        Assert.Equal("66% 剩余", display.RemainingText);
        Assert.Equal("2914.7 万", display.AccountSevenDayText);
        Assert.Equal("29,147,000 Tokens", display.AccountSevenDayExactText);
        Assert.Equal("截至：7月23日", display.AccountThroughText);
        Assert.Equal("数据已过期 · 7月24日 10:50", display.AccountStatusText);
        Assert.Equal("286.1 万", display.LocalTodayText);
        Assert.Equal("2,861,234 Tokens", display.LocalTodayExactText);
        Assert.Equal(string.Empty, display.LocalStatusText);
    }

    [Fact]
    public void KeepsPanelAvailableAndHidesUnknownProgressWhenWeeklyIsUnavailable()
    {
        var state = new UsageMonitorState(
            UsageStateKind.Unavailable,
            null,
            null,
            new UsageReadError(UsageReadErrorKind.Unavailable, "周窗口不可用。"),
            UsageStateKind.Fresh,
            new AccountSevenDayUsage(1_000, new DateOnly(2026, 7, 23)),
            new DateTimeOffset(2026, 7, 24, 2, 50, 0, TimeSpan.Zero),
            null,
            UsageStateKind.Fresh,
            new LocalTodayTokenUsage(500),
            new DateTimeOffset(2026, 7, 24, 2, 55, 0, TimeSpan.Zero),
            null);

        var display = UsageDisplayModel.FromState(state, ChinaTimeZone);

        Assert.False(display.HasWeeklyUsage);
        Assert.NotEqual(UsageVisualState.Unavailable, display.VisualState);
        Assert.Equal("-- 已用", display.UsedText);
    }
}
