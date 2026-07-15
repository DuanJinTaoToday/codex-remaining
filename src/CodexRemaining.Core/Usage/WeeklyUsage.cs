namespace CodexRemaining.Core.Usage;

public sealed record RateLimitWindowData(
    int UsedPercent,
    long? ResetsAtUnixSeconds,
    long? WindowDurationMins);

public sealed record WeeklyUsage(
    int RemainingPercent,
    DateTimeOffset? ResetsAt,
    UsageRisk Risk);

public enum UsageRisk
{
    Normal,
    Warning,
    Danger,
}

public static class WeeklyUsageFactory
{
    private const long WeeklyWindowDurationMins = 10_080;

    public static WeeklyUsage? TryCreate(IEnumerable<RateLimitWindowData> windows)
    {
        var weeklyWindow = windows.FirstOrDefault(
            window => window.WindowDurationMins == WeeklyWindowDurationMins);

        if (weeklyWindow is null || weeklyWindow.UsedPercent is < 0 or > 100)
        {
            return null;
        }

        DateTimeOffset? resetsAt = weeklyWindow.ResetsAtUnixSeconds is long unixSeconds
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
            : null;

        var remainingPercent = 100 - weeklyWindow.UsedPercent;
        var risk = remainingPercent switch
        {
            > 20 => UsageRisk.Normal,
            >= 10 => UsageRisk.Warning,
            _ => UsageRisk.Danger,
        };

        return new WeeklyUsage(remainingPercent, resetsAt, risk);
    }
}
