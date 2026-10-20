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
    string LocalStatusText,
    bool HasWeeklyUsage)
{
    public int RemainingPercent => HasWeeklyUsage ? 100 - UsedPercent : 0;
    public string ResetExactText { get; init; } = string.Empty;
    public string ResetCreditsText { get; init; } = "重置卡 --";
    public string ResetCreditExpiryText { get; init; } = "到期时间未知";
    public string ResetCreditExpiryExactText { get; init; } = string.Empty;
    public string ResetCreditsStatusText { get; init; } = string.Empty;

    public static UsageDisplayModel FromState(
        UsageMonitorState state,
        TimeZoneInfo timeZone,
        DateTimeOffset? now = null)
    {
        var currentTime = now ?? DateTimeOffset.UtcNow;
        var display = CreateUsageDisplay(state, timeZone, currentTime);
        var credits = state.ResetCredits;
        var countText = credits is null ? "重置卡 --" : $"重置卡 {credits.AvailableCount} 张";
        var expiryText = "到期时间未知";
        if (credits is { AvailableCount: 0 })
        {
            expiryText = "暂无可用重置卡";
        }
        else if (credits?.NearestExpiresAt is DateTimeOffset expiresAt)
        {
            var remaining = expiresAt - currentTime;
            var prefix = credits.HasCompleteExpiryDetails ? "最近一张" : "已知最早";
            expiryText = remaining <= TimeSpan.Zero
                ? "已到到期时间，待刷新确认"
                : $"{prefix}：{FormatDuration(remaining)}后到期";

        }

        var creditStatus = StatusFor(state.ResetCreditsKind, state.ResetCreditsLastSuccessfulAt, timeZone);
        return display with
        {
            ResetExactText = state.Usage?.ResetsAt is DateTimeOffset resetAt
                ? $"重置：{FormatLocal(resetAt, timeZone)}" : "重置：未知",
            ResetCreditsText = countText,
            ResetCreditExpiryText = expiryText,
            ResetCreditsStatusText = creditStatus,
            ResetCreditExpiryExactText = credits?.NearestExpiresAt is DateTimeOffset expiry
                ? $"到期：{FormatLocal(expiry, timeZone)}" : "到期时间未知",
        };
    }

    private static UsageDisplayModel CreateUsageDisplay(
        UsageMonitorState state,
        TimeZoneInfo timeZone,
        DateTimeOffset now)
    {
        if (state.Usage is null)
        {
            var hasAnyUsage = state.AccountUsage is not null || state.LocalUsage is not null || state.ResetCredits is not null;
            var visualState = hasAnyUsage
                ? state.AccountKind is UsageStateKind.Stale ||
                  state.LocalKind is UsageStateKind.Stale || state.ResetCreditsKind is UsageStateKind.Stale
                    ? UsageVisualState.Stale
                    : UsageVisualState.Normal
                : state.Kind is UsageStateKind.Loading ||
                  state.AccountKind is UsageStateKind.Loading ||
                  state.LocalKind is UsageStateKind.Loading
                    ? UsageVisualState.Loading
                    : UsageVisualState.Unavailable;
            return new UsageDisplayModel(
                "周 --",
                string.Empty,
                FormatUpdatedText(state, timeZone),
                StatusFor(state.Kind, state.LastSuccessfulAt, timeZone),
                FirstError(state),
                visualState,
                0,
                "-- 已用",
                "-- 剩余",
                FormatTokens(state.AccountUsage?.Tokens),
                FormatExactTokens(state.AccountUsage?.Tokens),
                FormatThroughDate(state.AccountUsage?.ThroughDate),
                StatusFor(
                    state.AccountKind,
                    state.AccountLastSuccessfulAt,
                    timeZone),
                FormatTokens(state.LocalUsage?.Tokens),
                FormatExactTokens(state.LocalUsage?.Tokens),
                StatusFor(
                    state.LocalKind,
                    state.LocalLastSuccessfulAt,
                    timeZone),
                false);
        }

        var resetText = state.Usage.ResetsAt is DateTimeOffset resetAt
            ? resetAt > now ? $"{FormatDuration(resetAt - now)}后重置" : "已到重置时间，待刷新"
            : "重置时间未知";
        var isStale = state.Kind is UsageStateKind.Stale;
        var usedPercent = 100 - state.Usage.RemainingPercent;

        return new UsageDisplayModel(
            $"周 {state.Usage.RemainingPercent}%",
            resetText,
            FormatUpdatedText(state, timeZone),
            isStale
                ? StatusFor(state.Kind, state.LastSuccessfulAt, timeZone)
                : string.Empty,
            FirstError(state),
            isStale ? UsageVisualState.Stale : MapRisk(state.Usage.Risk),
            usedPercent,
            $"已用 {usedPercent}%",
            $"{state.Usage.RemainingPercent}% 剩余",
            FormatTokens(state.AccountUsage?.Tokens),
            FormatExactTokens(state.AccountUsage?.Tokens),
            FormatThroughDate(state.AccountUsage?.ThroughDate),
            StatusFor(
                state.AccountKind,
                state.AccountLastSuccessfulAt,
                timeZone),
            FormatTokens(state.LocalUsage?.Tokens),
            FormatExactTokens(state.LocalUsage?.Tokens),
            StatusFor(
                state.LocalKind,
                state.LocalLastSuccessfulAt,
                timeZone),
            true);
    }

    private static string FormatDuration(TimeSpan remaining)
    {
        if (remaining.TotalDays >= 1)
            return $"{(int)remaining.TotalDays}天{remaining.Hours}小时";
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}小时{remaining.Minutes}分钟";
        return remaining.TotalMinutes >= 1 ? $"{(int)remaining.TotalMinutes}分钟" : "不到1分钟";
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
                state.ResetCreditsLastSuccessfulAt,
            }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty()
            .Max();
        return refreshedAt == default
            ? string.Empty
            : $"更新：{FormatLocal(refreshedAt, timeZone)}";
    }

    private static string StatusFor(
        UsageStateKind kind,
        DateTimeOffset? lastSuccessfulAt,
        TimeZoneInfo timeZone) => kind switch
    {
        UsageStateKind.Loading => "正在获取",
        UsageStateKind.Stale when lastSuccessfulAt is DateTimeOffset value =>
            $"数据已过期 · {FormatLocal(value, timeZone)}",
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
        state.ResetCreditsError?.Message ??
        string.Empty;

    private static UsageVisualState MapRisk(UsageRisk risk) => risk switch
    {
        UsageRisk.Normal => UsageVisualState.Normal,
        UsageRisk.Warning => UsageVisualState.Warning,
        UsageRisk.Danger => UsageVisualState.Danger,
        _ => UsageVisualState.Unavailable,
    };
}
