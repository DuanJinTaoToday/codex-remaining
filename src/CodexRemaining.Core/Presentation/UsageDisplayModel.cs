using CodexRemaining.Core.Application;
using CodexRemaining.Core.Usage;
using System.Globalization;

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
    UsageVisualState VisualState,
    int UsedPercent,
    string UsedText,
    string RemainingText,
    string AccountSevenDayText,
    string AccountSevenDayExactText,
    string AccountThroughText,
    string AccountStatusText,
    string LocalTodayText,
    string LocalTodayExactText,
    string LocalStatusText)
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
                FormatUpdatedText(state, timeZone),
                StatusFor(state.Kind),
                FirstError(state),
                state.Kind is UsageStateKind.Loading
                    ? UsageVisualState.Loading
                    : UsageVisualState.Unavailable,
                0,
                "已用 --",
                "-- 剩余",
                FormatTokens(state.AccountUsage?.Tokens),
                FormatExactTokens(state.AccountUsage?.Tokens),
                FormatThroughDate(state.AccountUsage?.ThroughDate),
                StatusFor(state.AccountKind),
                FormatTokens(state.LocalUsage?.Tokens),
                FormatExactTokens(state.LocalUsage?.Tokens),
                StatusFor(state.LocalKind));
        }

        var resetText = state.Usage.ResetsAt is DateTimeOffset resetAt
            ? $"重置：{FormatLocal(resetAt, timeZone)}"
            : "重置：未知";
        var isStale = state.Kind is UsageStateKind.Stale;
        var usedPercent = 100 - state.Usage.RemainingPercent;

        return new UsageDisplayModel(
            $"周 {state.Usage.RemainingPercent}%",
            resetText,
            FormatUpdatedText(state, timeZone),
            isStale ? "数据已过期" : string.Empty,
            FirstError(state),
            isStale ? UsageVisualState.Stale : MapRisk(state.Usage.Risk),
            usedPercent,
            $"已用 {usedPercent}%",
            $"{state.Usage.RemainingPercent}% 剩余",
            FormatTokens(state.AccountUsage?.Tokens),
            FormatExactTokens(state.AccountUsage?.Tokens),
            FormatThroughDate(state.AccountUsage?.ThroughDate),
            StatusFor(state.AccountKind),
            FormatTokens(state.LocalUsage?.Tokens),
            FormatExactTokens(state.LocalUsage?.Tokens),
            StatusFor(state.LocalKind));
    }

    private static string FormatLocal(DateTimeOffset value, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(value, timeZone);
        return $"{local.Month}月{local.Day}日 {local:HH:mm}";
    }

    private static string FormatUpdatedText(
        UsageMonitorState state,
        TimeZoneInfo timeZone)
    {
        var refreshedAt = new[]
            {
                state.LastSuccessfulAt,
                state.AccountLastSuccessfulAt,
                state.LocalLastSuccessfulAt,
            }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty()
            .Max();
        return refreshedAt == default
            ? string.Empty
            : $"更新：{FormatLocal(refreshedAt, timeZone)}";
    }

    private static string StatusFor(UsageStateKind kind) => kind switch
    {
        UsageStateKind.Loading => "正在获取",
        UsageStateKind.Stale => "数据已过期",
        UsageStateKind.Unavailable => "暂时无法获取",
        _ => string.Empty,
    };

    private static string FormatTokens(long? tokens)
    {
        if (tokens is null)
        {
            return "--";
        }

        return tokens.Value switch
        {
            >= 100_000_000 =>
                $"{(tokens.Value / 100_000_000d).ToString("F1", CultureInfo.InvariantCulture)} 亿",
            >= 10_000 =>
                $"{(tokens.Value / 10_000d).ToString("F1", CultureInfo.InvariantCulture)} 万",
            _ => tokens.Value.ToString("N0", CultureInfo.InvariantCulture),
        };
    }

    private static string FormatExactTokens(long? tokens) =>
        tokens is long value
            ? $"{value.ToString("N0", CultureInfo.InvariantCulture)} Tokens"
            : string.Empty;

    private static string FormatThroughDate(DateOnly? throughDate) =>
        throughDate is DateOnly value
            ? $"截至：{value.Month}月{value.Day}日"
            : string.Empty;

    private static string FirstError(UsageMonitorState state) =>
        state.Error?.Message ??
        state.AccountError?.Message ??
        state.LocalError?.Message ??
        string.Empty;

    private static UsageVisualState MapRisk(UsageRisk risk) => risk switch
    {
        UsageRisk.Normal => UsageVisualState.Normal,
        UsageRisk.Warning => UsageVisualState.Warning,
        UsageRisk.Danger => UsageVisualState.Danger,
        _ => UsageVisualState.Unavailable,
    };
}
