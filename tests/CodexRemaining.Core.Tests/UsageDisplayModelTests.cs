using CodexRemaining.Core.Application;
using CodexRemaining.Core.Codex;
using CodexRemaining.Core.Presentation;
using CodexRemaining.Core.Usage;

namespace CodexRemaining.Core.Tests;

public sealed class UsageDisplayModelTests
{
    [Theory]
    [InlineData(0, "暂无可用重置卡")]
    [InlineData(3, "到期时间未知")]
    public void DistinguishesNoCardsFromUnknownExpiry(int count, string expiry)
    {
        var state = new UsageMonitorState(UsageStateKind.Fresh, new WeeklyUsage(94, null, UsageRisk.Normal), null, null,
            ResetCreditsKind: UsageStateKind.Fresh, ResetCredits: new ResetCredits(count, null, count == 0));
        var display = UsageDisplayModel.FromState(state, TimeZoneInfo.Utc);
        Assert.Equal($"重置卡 {count} 张", display.ResetCreditsText);
        Assert.Equal(expiry, display.ResetCreditExpiryText);
    }

    [Fact]
    public void CountdownMovesWithoutARefreshAndNeverBecomesNegative()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var state = new UsageMonitorState(UsageStateKind.Fresh, new WeeklyUsage(94, now.AddDays(6).AddHours(18), UsageRisk.Normal), now, null,
            ResetCreditsKind: UsageStateKind.Fresh,
            ResetCredits: new ResetCredits(3, now.AddDays(2).AddHours(17), true));
        var display = UsageDisplayModel.FromState(state, TimeZoneInfo.Utc, now);
        Assert.Equal(94, display.RemainingPercent);
        Assert.Equal("6天18小时后重置", display.ResetText);
        Assert.Equal("周 94%", display.CompactText);
        Assert.Equal("最近一张：2天17小时后到期", display.ResetCreditExpiryText);
        var later = UsageDisplayModel.FromState(state, TimeZoneInfo.Utc, now.AddDays(7));
        Assert.Equal("已到重置时间，待刷新", later.ResetText);
        Assert.Equal("周 94%", later.CompactText);
        Assert.Equal("已到到期时间，待刷新确认", later.ResetCreditExpiryText);
    }

    [Fact]
    public void PartialExpiryDetailsAreQualifiedAndFailedRefreshIsMarkedStale()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new UsageMonitorState(UsageStateKind.Fresh, new WeeklyUsage(94, now.AddMinutes(20), UsageRisk.Normal), now, null,
            ResetCreditsKind: UsageStateKind.Stale,
            ResetCredits: new ResetCredits(3, now.AddHours(2), false),
            ResetCreditsLastSuccessfulAt: now.AddMinutes(-5));
        var display = UsageDisplayModel.FromState(state, TimeZoneInfo.Utc, now);
        Assert.Equal("20分钟后重置", display.ResetText);
        Assert.Equal("已知最早：2小时0分钟后到期", display.ResetCreditExpiryText);
        Assert.Equal("周 94%", display.CompactText);
        Assert.Contains("数据已过期", display.ResetCreditsStatusText);
    }

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

        var display = UsageDisplayModel.FromState(state, ChinaTimeZone, new DateTimeOffset(2026, 7, 15, 12, 30, 0, TimeSpan.Zero));

        Assert.Equal("周 18%", display.CompactText);
        Assert.Equal("6天18小时后重置", display.ResetText);
        Assert.Equal("重置：7月22日 14:30", display.ResetExactText);
        Assert.Equal(18, display.RemainingPercent);
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
