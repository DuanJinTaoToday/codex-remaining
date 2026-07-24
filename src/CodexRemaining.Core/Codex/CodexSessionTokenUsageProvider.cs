using System.Globalization;
using System.Text.Json;

namespace CodexRemaining.Core.Codex;

public sealed record LocalTodayTokenUsage(long Tokens);

public sealed record LocalTokenUsageReadResult(
    LocalTodayTokenUsage? Usage,
    UsageReadError? Error)
{
    public static LocalTokenUsageReadResult Success(long tokens) =>
        new(new LocalTodayTokenUsage(tokens), null);

    public static LocalTokenUsageReadResult Failure(string message) =>
        new(null, new UsageReadError(UsageReadErrorKind.Unavailable, message));
}

public interface ILocalTokenUsageProvider
{
    Task<LocalTokenUsageReadResult> ReadAsync(CancellationToken cancellationToken);
}

public sealed class CodexSessionTokenUsageProvider(
    string? codexDirectory = null,
    TimeProvider? timeProvider = null) : ILocalTokenUsageProvider
{
    private readonly string codexDirectory = codexDirectory ?? ResolveCodexDirectory();
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<LocalTokenUsageReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var today = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(
                    timeProvider.GetUtcNow(),
                    timeProvider.LocalTimeZone).DateTime);
            var todayStartUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(
                    today.ToDateTime(TimeOnly.MinValue),
                    DateTimeKind.Unspecified),
                timeProvider.LocalTimeZone);
            var sessions = new List<ParsedSession>();

            foreach (var filePath in CollectSessionFiles(todayStartUtc))
            {
                cancellationToken.ThrowIfCancellationRequested();
                sessions.Add(await ReadFileAsync(filePath, cancellationToken));
            }

            var sessionsById = sessions
                .Where(session => session.Id is not null)
                .GroupBy(session => session.Id!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            var total = 0L;
            foreach (var session in sessions)
            {
                var replayPrefix = FindReplayPrefix(session, sessionsById);
                foreach (var tokenEvent in session.Events.Skip(replayPrefix))
                {
                    var localDate = DateOnly.FromDateTime(
                        TimeZoneInfo.ConvertTime(
                            tokenEvent.Timestamp,
                            timeProvider.LocalTimeZone).DateTime);
                    if (localDate == today)
                    {
                        total = checked(total + tokenEvent.Delta);
                    }
                }
            }

            return LocalTokenUsageReadResult.Success(total);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or OverflowException)
        {
            return LocalTokenUsageReadResult.Failure(
                "暂时无法读取本机 Codex Token 用量，请重试。");
        }
    }

    private IEnumerable<string> CollectSessionFiles(DateTime todayStartUtc)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddFiles(
            Path.Combine(codexDirectory, "sessions"),
            SearchOption.AllDirectories,
            todayStartUtc,
            files);
        AddFiles(
            Path.Combine(codexDirectory, "archived_sessions"),
            SearchOption.TopDirectoryOnly,
            todayStartUtc,
            files);
        return files.Values;
    }

    private static void AddFiles(
        string directory,
        SearchOption searchOption,
        DateTime todayStartUtc,
        IDictionary<string, string> files)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(directory, "*.jsonl", searchOption))
        {
            if (File.GetLastWriteTimeUtc(filePath) < todayStartUtc)
            {
                continue;
            }

            var fileName = Path.GetFileName(filePath);
            if (!files.TryGetValue(fileName, out var existing) ||
                File.GetLastWriteTimeUtc(filePath) > File.GetLastWriteTimeUtc(existing))
            {
                files[fileName] = filePath;
            }
        }
    }

    private static int FindReplayPrefix(
        ParsedSession session,
        IReadOnlyDictionary<string, ParsedSession> sessionsById)
    {
        if (session.ParentId is null ||
            session.StartedAt is null ||
            !sessionsById.TryGetValue(session.ParentId, out var parent))
        {
            return 0;
        }

        var parentSignatures = parent.Events
            .Where(tokenEvent => tokenEvent.Timestamp <= session.StartedAt)
            .Select(tokenEvent => tokenEvent.Signature)
            .ToArray();
        var parentOffset = 0;
        var matched = 0;
        foreach (var tokenEvent in session.Events)
        {
            var relativeMatch = Array.FindIndex(
                parentSignatures,
                parentOffset,
                signature => signature == tokenEvent.Signature);
            if (relativeMatch < 0)
            {
                break;
            }

            parentOffset = relativeMatch + 1;
            matched++;
        }

        return matched;
    }

    private async Task<ParsedSession> ReadFileAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
        using var reader = new StreamReader(stream);
        long? previousTotal = null;
        string? id = null;
        string? parentId = null;
        DateTimeOffset? startedAt = null;
        var events = new List<TokenEvent>();

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.Contains("\"token_count\"", StringComparison.Ordinal) &&
                !line.Contains("\"session_meta\"", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (TryReadSessionMeta(root, out var metaId, out var metaParentId, out var metaStartedAt))
                {
                    id ??= metaId;
                    parentId ??= metaParentId;
                    startedAt ??= metaStartedAt;
                    continue;
                }

                if (!TryReadTokenCount(root, out var total, out var isCumulative) ||
                    !TryReadTimestamp(root, out var timestamp))
                {
                    continue;
                }

                var delta = isCumulative
                    ? previousTotal is long previous && total >= previous
                        ? total - previous
                        : total
                    : total;
                if (isCumulative)
                {
                    previousTotal = total;
                }

                events.Add(new TokenEvent(
                    timestamp,
                    delta,
                    ReadTokenSignature(root)));
            }
            catch (JsonException)
            {
            }
        }

        return new ParsedSession(id, parentId, startedAt, events);
    }

    private static bool TryReadSessionMeta(
        JsonElement root,
        out string? id,
        out string? parentId,
        out DateTimeOffset? startedAt)
    {
        id = null;
        parentId = null;
        startedAt = null;
        if (!root.TryGetProperty("type", out var type) ||
            type.GetString() is not "session_meta" ||
            !root.TryGetProperty("payload", out var payload))
        {
            return false;
        }

        id = ReadNonEmptyString(payload, "id") ??
             ReadNonEmptyString(payload, "session_id");
        parentId = ReadNonEmptyString(payload, "forked_from_id");
        if (parentId is null &&
            payload.TryGetProperty("source", out var source) &&
            source.ValueKind is JsonValueKind.Object &&
            source.TryGetProperty("subagent", out var subagent) &&
            subagent.ValueKind is JsonValueKind.Object &&
            subagent.TryGetProperty("thread_spawn", out var spawn))
        {
            parentId = ReadNonEmptyString(spawn, "parent_thread_id");
        }

        if (TryReadTimestamp(root, out var timestamp))
        {
            startedAt = timestamp;
        }

        return true;
    }

    private static string? ReadNonEmptyString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind is JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()
            : null;

    private static string ReadTokenSignature(JsonElement root)
    {
        var info = root.GetProperty("payload").GetProperty("info");
        var total = info.TryGetProperty("total_token_usage", out var totalUsage)
            ? totalUsage.GetRawText()
            : string.Empty;
        var latest = info.TryGetProperty("last_token_usage", out var latestUsage)
            ? latestUsage.GetRawText()
            : string.Empty;
        return $"{total}|{latest}";
    }

    private static bool TryReadTokenCount(
        JsonElement root,
        out long total,
        out bool isCumulative)
    {
        total = 0;
        isCumulative = false;
        if (!root.TryGetProperty("type", out var rootType) ||
            rootType.GetString() is not "event_msg" ||
            !root.TryGetProperty("payload", out var payload) ||
            !payload.TryGetProperty("type", out var payloadType) ||
            payloadType.GetString() is not "token_count" ||
            !payload.TryGetProperty("info", out var info) ||
            info.ValueKind is JsonValueKind.Null)
        {
            return false;
        }

        if (info.TryGetProperty("total_token_usage", out var cumulative))
        {
            isCumulative = true;
            return TryReadTotal(cumulative, out total);
        }

        return info.TryGetProperty("last_token_usage", out var latest) &&
               TryReadTotal(latest, out total);
    }

    private static bool TryReadTotal(JsonElement usage, out long total)
    {
        if (usage.TryGetProperty("total_tokens", out var totalElement) &&
            totalElement.TryGetInt64(out total) &&
            total >= 0)
        {
            return true;
        }

        total = 0;
        if (!usage.TryGetProperty("input_tokens", out var inputElement) ||
            !inputElement.TryGetInt64(out var input) ||
            !usage.TryGetProperty("output_tokens", out var outputElement) ||
            !outputElement.TryGetInt64(out var output) ||
            input < 0 ||
            output < 0)
        {
            return false;
        }

        total = checked(input + output);
        return true;
    }

    private static bool TryReadTimestamp(JsonElement root, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return root.TryGetProperty("timestamp", out var timestampElement) &&
               DateTimeOffset.TryParse(
                   timestampElement.GetString(),
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal,
                   out timestamp);
    }

    private static string ResolveCodexDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        return !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(configured)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex");
    }

    private sealed record TokenEvent(
        DateTimeOffset Timestamp,
        long Delta,
        string Signature);

    private sealed record ParsedSession(
        string? Id,
        string? ParentId,
        DateTimeOffset? StartedAt,
        IReadOnlyList<TokenEvent> Events);
}
