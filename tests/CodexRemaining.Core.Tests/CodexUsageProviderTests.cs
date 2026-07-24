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
    public async Task ReturnsAccountSevenDayUsageFromDailyBuckets()
    {
        using var rateLimits = JsonDocument.Parse("""
            {
              "rateLimits": {
                "secondary": {
                  "usedPercent": 25,
                  "resetsAt": 1767225800,
                  "windowDurationMins": 10080
                }
              }
            }
            """);
        using var tokenUsage = JsonDocument.Parse("""
            {
              "summary": {},
              "dailyUsageBuckets": [
                { "startDate": "2026-07-15", "tokens": 999 },
                { "startDate": "2026-07-17", "tokens": 100 },
                { "startDate": "2026-07-19", "tokens": 300 },
                { "startDate": "2026-07-23", "tokens": 700 }
              ]
            }
            """);
        var provider = new CodexUsageProvider(
            new StubSessionFactory(method =>
                method is "account/usage/read"
                    ? tokenUsage.RootElement.Clone()
                    : rateLimits.RootElement.Clone()));

        var result = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(1_100, result.AccountUsage?.Tokens);
        Assert.Equal(new DateOnly(2026, 7, 23), result.AccountUsage?.ThroughDate);
        Assert.Null(result.AccountError);
    }

    [Fact]
    public async Task KeepsWeeklyUsageWhenAccountTokenUsageFails()
    {
        using var rateLimits = JsonDocument.Parse("""
            {
              "rateLimits": {
                "secondary": {
                  "usedPercent": 25,
                  "resetsAt": 1767225800,
                  "windowDurationMins": 10080
                }
              }
            }
            """);
        var provider = new CodexUsageProvider(
            new StubSessionFactory(method =>
                method is "account/usage/read"
                    ? throw new CodexIntegrationException(
                        UsageReadErrorKind.IncompatibleProtocol,
                        "账号 Token 接口不可用。")
                    : rateLimits.RootElement.Clone()));

        var result = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(75, result.Usage?.RemainingPercent);
        Assert.Null(result.Error);
        Assert.Null(result.AccountUsage);
        Assert.Equal(UsageReadErrorKind.IncompatibleProtocol, result.AccountError?.Kind);
    }

    [Fact]
    public async Task KeepsAccountTokenUsageWhenWeeklyUsageFails()
    {
        using var invalidRateLimits = JsonDocument.Parse("""{ "rateLimits": {} }""");
        using var tokenUsage = JsonDocument.Parse("""
            {
              "summary": {},
              "dailyUsageBuckets": [
                { "startDate": "2026-07-23", "tokens": 700 }
              ]
            }
            """);
        var provider = new CodexUsageProvider(
            new StubSessionFactory(method =>
                method is "account/usage/read"
                    ? tokenUsage.RootElement.Clone()
                    : invalidRateLimits.RootElement.Clone()));

        var result = await provider.ReadAsync(CancellationToken.None);

        Assert.Null(result.Usage);
        Assert.Equal(UsageReadErrorKind.IncompatibleProtocol, result.Error?.Kind);
        Assert.Equal(700, result.AccountUsage?.Tokens);
        Assert.Null(result.AccountError);
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

    private sealed class StubSessionFactory : IAppServerSessionFactory
    {
        private readonly Func<string, JsonElement> responseFactory;

        public StubSessionFactory(JsonElement response)
            : this(_ => response)
        {
        }

        public StubSessionFactory(Func<string, JsonElement> responseFactory)
        {
            this.responseFactory = responseFactory;
        }

        public Task<IAppServerSession> CreateAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IAppServerSession>(new StubSession(responseFactory));
    }

    private sealed class StubSession(Func<string, JsonElement> responseFactory) : IAppServerSession
    {
        public Task<JsonElement> CallAsync(string method, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(method));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingSessionFactory(Exception exception) : IAppServerSessionFactory
    {
        public Task<IAppServerSession> CreateAsync(CancellationToken cancellationToken) =>
            Task.FromException<IAppServerSession>(exception);
    }
}
