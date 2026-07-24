using CodexRemaining.Core.Codex;
using CodexRemaining.Core.Usage;

namespace CodexRemaining.Core.Application;

public interface IUsageSnapshotStore
{
    Task<UsageSnapshot?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(UsageSnapshot snapshot, CancellationToken cancellationToken);
}

public sealed record UsageSnapshot(
    WeeklyUsage? Usage,
    DateTimeOffset? RefreshedAt,
    AccountSevenDayUsage? AccountUsage = null,
    DateTimeOffset? AccountRefreshedAt = null,
    LocalTodayTokenUsage? LocalUsage = null,
    DateTimeOffset? LocalRefreshedAt = null,
    DateOnly? LocalUsageDate = null);

public enum UsageStateKind
{
    Loading,
    Fresh,
    Stale,
    Unavailable,
}

public sealed record UsageMonitorState(
    UsageStateKind Kind,
    WeeklyUsage? Usage,
    DateTimeOffset? LastSuccessfulAt,
    UsageReadError? Error,
    UsageStateKind AccountKind = UsageStateKind.Loading,
    AccountSevenDayUsage? AccountUsage = null,
    DateTimeOffset? AccountLastSuccessfulAt = null,
    UsageReadError? AccountError = null,
    UsageStateKind LocalKind = UsageStateKind.Loading,
    LocalTodayTokenUsage? LocalUsage = null,
    DateTimeOffset? LocalLastSuccessfulAt = null,
    UsageReadError? LocalError = null);

public sealed class UsageMonitor(
    ICodexUsageProvider usageProvider,
    IUsageSnapshotStore snapshotStore,
    TimeProvider timeProvider,
    TimeSpan? requestTimeout = null,
    ILocalTokenUsageProvider? localTokenUsageProvider = null)
{
    public async Task<UsageMonitorState> RefreshAsync(CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(requestTimeout ?? TimeSpan.FromSeconds(15));

        var codexTask = ReadCodexAsync(timeoutSource.Token, cancellationToken);
        var localTask = ReadLocalAsync(timeoutSource.Token, cancellationToken);
        await Task.WhenAll(codexTask, localTask);

        var codexResult = await codexTask;
        var localResult = await localTask;
        var savedSnapshot = await snapshotStore.LoadAsync(cancellationToken);
        var refreshedAt = timeProvider.GetUtcNow();
        var localToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(refreshedAt, timeProvider.LocalTimeZone).DateTime);
        var weekly = Merge(
            codexResult.Usage,
            codexResult.Error,
            savedSnapshot?.Usage,
            savedSnapshot?.RefreshedAt,
            refreshedAt);
        var account = Merge(
            codexResult.AccountUsage,
            codexResult.AccountError,
            savedSnapshot?.AccountUsage,
            savedSnapshot?.AccountRefreshedAt,
            refreshedAt);
        var local = Merge(
            localResult.Usage,
            localResult.Error,
            savedSnapshot?.LocalUsageDate == localToday
                ? savedSnapshot.LocalUsage
                : null,
            savedSnapshot?.LocalUsageDate == localToday
                ? savedSnapshot.LocalRefreshedAt
                : null,
            refreshedAt);

        if (weekly.Kind is UsageStateKind.Fresh ||
            account.Kind is UsageStateKind.Fresh ||
            local.Kind is UsageStateKind.Fresh)
        {
            await snapshotStore.SaveAsync(
                new UsageSnapshot(
                    weekly.Value,
                    weekly.LastSuccessfulAt,
                    account.Value,
                    account.LastSuccessfulAt,
                    local.Value,
                    local.LastSuccessfulAt,
                    local.Value is not null ? localToday : null),
                cancellationToken);
        }

        return new UsageMonitorState(
            weekly.Kind,
            weekly.Value,
            weekly.LastSuccessfulAt,
            weekly.Error,
            account.Kind,
            account.Value,
            account.LastSuccessfulAt,
            account.Error,
            local.Kind,
            local.Value,
            local.LastSuccessfulAt,
            local.Error);
    }

    private async Task<UsageReadResult> ReadCodexAsync(
        CancellationToken timeoutToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await usageProvider.ReadAsync(timeoutToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return UsageReadResult.Failure(
                UsageReadErrorKind.Timeout,
                "读取 Codex 用量超时，请重试。");
        }
    }

    private async Task<LocalTokenUsageReadResult> ReadLocalAsync(
        CancellationToken timeoutToken,
        CancellationToken cancellationToken)
    {
        if (localTokenUsageProvider is null)
        {
            return LocalTokenUsageReadResult.Failure("本机 Token 用量读取器不可用。");
        }

        try
        {
            return await localTokenUsageProvider.ReadAsync(timeoutToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LocalTokenUsageReadResult(
                null,
                new UsageReadError(
                    UsageReadErrorKind.Timeout,
                    "读取本机 Token 用量超时，请重试。"));
        }
    }

    private static MetricState<T> Merge<T>(
        T? currentValue,
        UsageReadError? currentError,
        T? savedValue,
        DateTimeOffset? savedAt,
        DateTimeOffset refreshedAt)
        where T : class
    {
        if (currentValue is not null)
        {
            return new MetricState<T>(
                UsageStateKind.Fresh,
                currentValue,
                refreshedAt,
                null);
        }

        var error = currentError ?? new UsageReadError(
            UsageReadErrorKind.Unavailable,
            "暂时无法获取，请重试。");
        return savedValue is not null
            ? new MetricState<T>(
                UsageStateKind.Stale,
                savedValue,
                savedAt,
                error)
            : new MetricState<T>(
                UsageStateKind.Unavailable,
                null,
                null,
                error);
    }

    private sealed record MetricState<T>(
        UsageStateKind Kind,
        T? Value,
        DateTimeOffset? LastSuccessfulAt,
        UsageReadError? Error)
        where T : class;
}
