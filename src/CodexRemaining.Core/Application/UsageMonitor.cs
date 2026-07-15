using CodexRemaining.Core.Codex;
using CodexRemaining.Core.Usage;

namespace CodexRemaining.Core.Application;

public interface IUsageSnapshotStore
{
    Task<UsageSnapshot?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(UsageSnapshot snapshot, CancellationToken cancellationToken);
}

public sealed record UsageSnapshot(WeeklyUsage Usage, DateTimeOffset RefreshedAt);

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
    UsageReadError? Error);

public sealed class UsageMonitor(
    ICodexUsageProvider usageProvider,
    IUsageSnapshotStore snapshotStore,
    TimeProvider timeProvider,
    TimeSpan? requestTimeout = null)
{
    public async Task<UsageMonitorState> RefreshAsync(CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(requestTimeout ?? TimeSpan.FromSeconds(15));

        UsageReadResult result;
        try
        {
            result = await usageProvider.ReadAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = UsageReadResult.Failure(
                UsageReadErrorKind.Timeout,
                "读取 Codex 用量超时，请重试。");
        }

        if (!result.IsSuccess)
        {
            var savedSnapshot = await snapshotStore.LoadAsync(cancellationToken);
            if (savedSnapshot is not null)
            {
                return new UsageMonitorState(
                    UsageStateKind.Stale,
                    savedSnapshot.Usage,
                    savedSnapshot.RefreshedAt,
                    result.Error);
            }

            return new UsageMonitorState(
                UsageStateKind.Unavailable,
                null,
                null,
                result.Error);
        }

        var refreshedAt = timeProvider.GetUtcNow();
        var snapshot = new UsageSnapshot(result.Usage!, refreshedAt);
        await snapshotStore.SaveAsync(snapshot, cancellationToken);

        return new UsageMonitorState(
            UsageStateKind.Fresh,
            snapshot.Usage,
            snapshot.RefreshedAt,
            null);
    }
}
