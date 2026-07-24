using CodexRemaining.Core.Application;
using CodexRemaining.Core.Codex;
using CodexRemaining.Core.Usage;

namespace CodexRemaining.Core.Tests;

public sealed class UsageMonitorTests
{
    [Fact]
    public async Task SuccessfulRefreshProducesFreshState()
    {
        var resetAt = new DateTimeOffset(2026, 7, 22, 14, 30, 0, TimeSpan.FromHours(8));
        var usage = new WeeklyUsage(64, resetAt, UsageRisk.Normal);
        var refreshedAt = new DateTimeOffset(2026, 7, 15, 11, 30, 0, TimeSpan.FromHours(8));
        var monitor = new UsageMonitor(
            new StubUsageProvider(UsageReadResult.Success(usage)),
            new MemoryUsageSnapshotStore(),
            new FixedTimeProvider(refreshedAt));

        var state = await monitor.RefreshAsync(CancellationToken.None);

        Assert.Equal(UsageStateKind.Fresh, state.Kind);
        Assert.Equal(usage, state.Usage);
        Assert.Equal(refreshedAt, state.LastSuccessfulAt);
        Assert.Null(state.Error);
    }

    [Fact]
    public async Task FailedRefreshWithSavedSnapshotProducesStaleState()
    {
        var usage = new WeeklyUsage(
            18,
            new DateTimeOffset(2026, 7, 22, 14, 30, 0, TimeSpan.FromHours(8)),
            UsageRisk.Warning);
        var lastSuccessfulAt = new DateTimeOffset(
            2026,
            7,
            15,
            11,
            20,
            0,
            TimeSpan.FromHours(8));
        var savedSnapshot = new UsageSnapshot(usage, lastSuccessfulAt);
        var error = new UsageReadError(UsageReadErrorKind.NotLoggedIn, "请先登录 Codex。");
        var monitor = new UsageMonitor(
            new StubUsageProvider(UsageReadResult.Failure(error.Kind, error.Message)),
            new MemoryUsageSnapshotStore(savedSnapshot),
            TimeProvider.System);

        var state = await monitor.RefreshAsync(CancellationToken.None);

        Assert.Equal(UsageStateKind.Stale, state.Kind);
        Assert.Equal(usage, state.Usage);
        Assert.Equal(lastSuccessfulAt, state.LastSuccessfulAt);
        Assert.Equal(error, state.Error);
    }

    [Fact]
    public async Task TimedOutRefreshProducesUnavailableState()
    {
        var monitor = new UsageMonitor(
            new NeverCompletingUsageProvider(),
            new MemoryUsageSnapshotStore(),
            TimeProvider.System,
            TimeSpan.FromMilliseconds(20));

        var state = await monitor.RefreshAsync(CancellationToken.None);

        Assert.Equal(UsageStateKind.Unavailable, state.Kind);
        Assert.Equal(UsageReadErrorKind.Timeout, state.Error?.Kind);
    }

    [Fact]
    public async Task RefreshesSourcesIndependentlyAndKeepsOnlyFailedSourceStale()
    {
        var previousAt = new DateTimeOffset(2026, 7, 24, 10, 0, 0, TimeSpan.FromHours(8));
        var refreshedAt = previousAt.AddMinutes(5);
        var previousSnapshot = new UsageSnapshot(
            new WeeklyUsage(60, null, UsageRisk.Normal),
            previousAt,
            new AccountSevenDayUsage(1_000, new DateOnly(2026, 7, 23)),
            previousAt,
            new LocalTodayTokenUsage(200),
            previousAt);
        var accountError = new UsageReadError(
            UsageReadErrorKind.Unavailable,
            "账号 Token 用量暂时不可用。");
        var monitor = new UsageMonitor(
            new StubUsageProvider(
                new UsageReadResult(
                    new WeeklyUsage(55, null, UsageRisk.Normal),
                    null,
                    null,
                    accountError)),
            new MemoryUsageSnapshotStore(previousSnapshot),
            new FixedTimeProvider(refreshedAt),
            localTokenUsageProvider: new StubLocalTokenUsageProvider(
                LocalTokenUsageReadResult.Success(500)));

        var state = await monitor.RefreshAsync(CancellationToken.None);

        Assert.Equal(UsageStateKind.Fresh, state.Kind);
        Assert.Equal(55, state.Usage?.RemainingPercent);
        Assert.Equal(UsageStateKind.Stale, state.AccountKind);
        Assert.Equal(1_000, state.AccountUsage?.Tokens);
        Assert.Equal(accountError, state.AccountError);
        Assert.Equal(UsageStateKind.Fresh, state.LocalKind);
        Assert.Equal(500, state.LocalUsage?.Tokens);
    }

    private sealed class StubUsageProvider(UsageReadResult result) : ICodexUsageProvider
    {
        public Task<UsageReadResult> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class NeverCompletingUsageProvider : ICodexUsageProvider
    {
        public async Task<UsageReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
    }

    private sealed class StubLocalTokenUsageProvider(LocalTokenUsageReadResult result)
        : ILocalTokenUsageProvider
    {
        public Task<LocalTokenUsageReadResult> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class MemoryUsageSnapshotStore(UsageSnapshot? initialSnapshot = null)
        : IUsageSnapshotStore
    {
        private UsageSnapshot? snapshot = initialSnapshot;

        public Task<UsageSnapshot?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);

        public Task SaveAsync(UsageSnapshot value, CancellationToken cancellationToken)
        {
            snapshot = value;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone =>
            TimeZoneInfo.CreateCustomTimeZone("Test", now.Offset, "Test", "Test");
    }
}
