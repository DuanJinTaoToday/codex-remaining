using CodexRemaining.Core.Codex;
using System.Text.Json;

namespace CodexRemaining.Core.Tests;

public sealed class CodexSessionTokenUsageProviderTests
{
    [Fact]
    public async Task CountsOnlyTodaysCumulativeTokenDeltas()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"CodexRemaining.SessionUsage.Tests-{Guid.NewGuid():N}");
        var sessions = Path.Combine(directory, "sessions", "2026", "07", "24");
        Directory.CreateDirectory(sessions);

        try
        {
            await File.WriteAllLinesAsync(
                Path.Combine(sessions, "rollout-test.jsonl"),
                new[]
                {
                    TokenCount("2026-07-23T15:59:00Z", 100),
                    TokenCount("2026-07-23T16:01:00Z", 250),
                    TokenCount("2026-07-23T16:02:00Z", 250),
                    TokenCount("2026-07-24T02:00:00Z", 400),
                });
            var provider = new CodexSessionTokenUsageProvider(
                directory,
                new FixedTimeProvider(
                    new DateTimeOffset(2026, 7, 24, 11, 0, 0, TimeSpan.FromHours(8))));

            var result = await provider.ReadAsync(CancellationToken.None);

            Assert.Equal(300, result.Usage?.Tokens);
            Assert.Null(result.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReadsAnActiveSessionFileWhileCodexIsWritingIt()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"CodexRemaining.SessionUsage.Tests-{Guid.NewGuid():N}");
        var sessions = Path.Combine(directory, "sessions", "2026", "07", "24");
        Directory.CreateDirectory(sessions);
        var filePath = Path.Combine(sessions, "rollout-active.jsonl");

        try
        {
            await File.WriteAllLinesAsync(
                filePath,
                new[] { TokenCount("2026-07-24T02:00:00Z", 400) });
            await using var activeWriter = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite);
            var provider = new CodexSessionTokenUsageProvider(
                directory,
                new FixedTimeProvider(
                    new DateTimeOffset(2026, 7, 24, 11, 0, 0, TimeSpan.FromHours(8))));

            var result = await provider.ReadAsync(CancellationToken.None);

            Assert.Equal(400, result.Usage?.Tokens);
            Assert.Null(result.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExcludesParentReplayFromSubagentButCountsNewSubagentUsage()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"CodexRemaining.SessionUsage.Tests-{Guid.NewGuid():N}");
        var sessions = Path.Combine(directory, "sessions", "2026", "07", "24");
        Directory.CreateDirectory(sessions);
        const string parentId = "11111111-1111-1111-1111-111111111111";
        const string childId = "22222222-2222-2222-2222-222222222222";

        try
        {
            await File.WriteAllLinesAsync(
                Path.Combine(sessions, $"rollout-parent-{parentId}.jsonl"),
                new[]
                {
                    SessionMeta("2026-07-24T02:00:00Z", parentId),
                    TokenCount("2026-07-24T02:01:00Z", 100),
                    TokenCount("2026-07-24T02:02:00Z", 200),
                    """{"timestamp":"2026-07-24T02:10:00Z","type":"turn_context","payload":{}}""",
                });
            await File.WriteAllLinesAsync(
                Path.Combine(sessions, $"rollout-child-{childId}.jsonl"),
                new[]
                {
                    SessionMeta("2026-07-24T02:05:00Z", childId, parentId),
                    TokenCount("2026-07-24T02:06:00Z", 100),
                    TokenCount("2026-07-24T02:07:00Z", 200),
                    TokenCount("2026-07-24T02:08:00Z", 350),
                });
            var provider = new CodexSessionTokenUsageProvider(
                directory,
                new FixedTimeProvider(
                    new DateTimeOffset(2026, 7, 24, 11, 0, 0, TimeSpan.FromHours(8))));

            var result = await provider.ReadAsync(CancellationToken.None);

            Assert.Equal(350, result.Usage?.Tokens);
            Assert.Null(result.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string TokenCount(string timestamp, long totalTokens) =>
        JsonSerializer.Serialize(new
        {
            timestamp,
            type = "event_msg",
            payload = new
            {
                type = "token_count",
                info = new
                {
                    total_token_usage = new
                    {
                        input_tokens = totalTokens - 10,
                        cached_input_tokens = 0,
                        output_tokens = 10,
                        total_tokens = totalTokens,
                    },
                },
            },
        });

    private static string SessionMeta(string timestamp, string id, string? parentId = null) =>
        JsonSerializer.Serialize(new
        {
            timestamp,
            type = "session_meta",
            payload = new
            {
                id,
                forked_from_id = parentId,
                source = "vscode",
            },
        });

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone(
            "China Test",
            TimeSpan.FromHours(8),
            "China Test",
            "China Test");
    }
}
