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
            var total = 0L;

            foreach (var filePath in CollectSessionFiles(todayStartUtc))
            {
                cancellationToken.ThrowIfCancellationRequested();
                total = checked(
                    total + await ReadFileAsync(filePath, today, cancellationToken));
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

    private async Task<long> ReadFileAsync(
        string filePath,
        DateOnly today,
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
        var todayTotal = 0L;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.Contains("\"token_count\"", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
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

                var localDate = DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTime(timestamp, timeProvider.LocalTimeZone).DateTime);
                if (localDate == today)
                {
                    todayTotal = checked(todayTotal + delta);
                }
            }
            catch (JsonException)
            {
            }
        }

        return todayTotal;
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
}
