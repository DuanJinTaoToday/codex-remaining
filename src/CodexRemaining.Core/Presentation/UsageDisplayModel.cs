using CodexRemaining.Core.Application;
using CodexRemaining.Core.Usage;

namespace CodexRemaining.Core.Presentation;

public enum UsageVisualState
{
    Normal,
    Warning,
    Danger,
    Stale,
    Unavailable,
    Loading,
}

public sealed record UsageDisplayModel(
    string CompactText,
    string ResetText,
    string UpdatedText,
    string StatusText,
    string DetailText,
    UsageVisualState VisualState)
{
    public static UsageDisplayModel FromState(
        UsageMonitorState state,
        TimeZoneInfo timeZone)
    {
        if (state.Usage is null)
        {
            return new UsageDisplayModel(
                "周 --",
                string.Empty,
                string.Empty,
                state.Kind is UsageStateKind.Loading ? "正在获取用量" : "暂时无法获取",
                state.Error?.Message ?? string.Empty,
                state.Kind is UsageStateKind.Loading
                    ? UsageVisualState.Loading
                    : UsageVisualState.Unavailable);
        }

        var resetText = state.Usage.ResetsAt is DateTimeOffset resetAt
            ? $"重置：{FormatLocal(resetAt, timeZone)}"
            : "重置：未知";
        var updatedText = state.LastSuccessfulAt is DateTimeOffset refreshedAt
            ? $"更新：{FormatLocal(refreshedAt, timeZone)}"
            : string.Empty;
        var isStale = state.Kind is UsageStateKind.Stale;

        return new UsageDisplayModel(
            $"周 {state.Usage.RemainingPercent}%",
            resetText,
            updatedText,
            isStale ? "数据已过期" : string.Empty,
            state.Error?.Message ?? string.Empty,
            isStale ? UsageVisualState.Stale : MapRisk(state.Usage.Risk));
    }

    private static string FormatLocal(DateTimeOffset value, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(value, timeZone);
        return $"{local.Month}月{local.Day}日 {local:HH:mm}";
    }

    private static UsageVisualState MapRisk(UsageRisk risk) => risk switch
    {
        UsageRisk.Normal => UsageVisualState.Normal,
        UsageRisk.Warning => UsageVisualState.Warning,
        UsageRisk.Danger => UsageVisualState.Danger,
        _ => UsageVisualState.Unavailable,
    };
}
