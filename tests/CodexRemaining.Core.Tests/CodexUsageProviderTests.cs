using System.Text.Json;
using CodexRemaining.Core.Codex;

namespace CodexRemaining.Core.Tests;

public sealed class CodexUsageProviderTests
{
    [Fact]
    public async Task ReturnsWeeklyUsageFromCodexRateLimits()
    {
        const string response = """
            {
              "rateLimits": {
                "primary": {
                  "usedPercent": 8,
                  "resetsAt": 1767222200,
                  "windowDurationMins": 300
                },
                "secondary": {
                  "usedPercent": 25,
                  "resetsAt": 1767225800,
                  "windowDurationMins": 10080
                }
              }
            }
            """;
        using var document = JsonDocument.Parse(response);
        var provider = new CodexUsageProvider(
            new StubSessionFactory(document.RootElement.Clone()));

        var result = await provider.ReadAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Usage);
        Assert.Equal(75, result.Usage.RemainingPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_767_225_800), result.Usage.ResetsAt);
    }

    [Fact]
    public async Task ReturnsActionableErrorWhenCodexIsNotLoggedIn()
    {
        var provider = new CodexUsageProvider(
            new ThrowingSessionFactory(
                new CodexIntegrationException(
                    UsageReadErrorKind.NotLoggedIn,
                    "请先登录 Codex。")));

        var result = await provider.ReadAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Usage);
        Assert.Equal(UsageReadErrorKind.NotLoggedIn, result.Error?.Kind);
        Assert.Equal("请先登录 Codex。", result.Error?.Message);
    }

    [Fact]
    public async Task ReturnsProtocolErrorWhenRateLimitShapeIsInvalid()
    {
        const string response = """
            {
              "rateLimits": {
                "secondary": {
                  "usedPercent": "unknown",
                  "windowDurationMins": 10080
                }
              }
            }
            """;
        using var document = JsonDocument.Parse(response);
        var provider = new CodexUsageProvider(
            new StubSessionFactory(document.RootElement.Clone()));

        var result = await provider.ReadAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageReadErrorKind.IncompatibleProtocol, result.Error?.Kind);
    }

    private sealed class StubSessionFactory(JsonElement response) : IAppServerSessionFactory
    {
        public Task<IAppServerSession> CreateAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IAppServerSession>(new StubSession(response));
    }

    private sealed class StubSession(JsonElement response) : IAppServerSession
    {
        public Task<JsonElement> CallAsync(string method, CancellationToken cancellationToken) =>
            Task.FromResult(response);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingSessionFactory(Exception exception) : IAppServerSessionFactory
    {
        public Task<IAppServerSession> CreateAsync(CancellationToken cancellationToken) =>
            Task.FromException<IAppServerSession>(exception);
    }
}
