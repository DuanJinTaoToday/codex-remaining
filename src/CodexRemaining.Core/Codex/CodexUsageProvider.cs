using System.Text.Json;
using System.Globalization;
using CodexRemaining.Core.Usage;

namespace CodexRemaining.Core.Codex;

public interface ICodexUsageProvider
{
    Task<UsageReadResult> ReadAsync(CancellationToken cancellationToken);
}

public interface IAppServerSessionFactory
{
    Task<IAppServerSession> CreateAsync(CancellationToken cancellationToken);
}

public interface IAppServerSession : IAsyncDisposable
{
    Task<JsonElement> CallAsync(string method, CancellationToken cancellationToken);
}

public sealed record UsageReadResult(
    WeeklyUsage? Usage,
    UsageReadError? Error,
    AccountSevenDayUsage? AccountUsage = null,
    UsageReadError? AccountError = null,
    ResetCredits? ResetCredits = null,
    UsageReadError? ResetCreditsError = null)
{
    public bool IsSuccess => Usage is not null && Error is null;

    public static UsageReadResult Success(
        WeeklyUsage usage,
        AccountSevenDayUsage? accountUsage = null) =>
        new(usage, null, accountUsage, null);

    public static UsageReadResult Failure(UsageReadErrorKind kind, string message) =>
        new(
            null,
            new UsageReadError(kind, message),
            null,
            new UsageReadError(kind, message),
            null,
            new UsageReadError(kind, message));
}

public sealed record UsageReadError(UsageReadErrorKind Kind, string Message);

public enum UsageReadErrorKind
{
    CodexNotFound,
    NotLoggedIn,
    Timeout,
    IncompatibleProtocol,
    Unavailable,
}

public sealed class CodexIntegrationException(
    UsageReadErrorKind kind,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public UsageReadErrorKind Kind { get; } = kind;
}

public sealed class CodexUsageProvider(
    IAppServerSessionFactory sessionFactory,
    TimeSpan? requestTimeout = null) : ICodexUsageProvider
{
    public async Task<UsageReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        var weeklyTask = CreateAndReadLimitsAsync(cancellationToken);
        var accountTask = CreateAndReadAccountAsync(cancellationToken);
        await Task.WhenAll(weeklyTask, accountTask);
        var weekly = await weeklyTask;
        var account = await accountTask;
        return new UsageReadResult(
            weekly.Weekly.Value,
            weekly.Weekly.Error,
            account.Value,
            account.Error,
            weekly.Credits.Value,
            weekly.Credits.Error);
    }

    private async Task<LimitsRead> CreateAndReadLimitsAsync(
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(requestTimeout ?? TimeSpan.FromSeconds(10));
        try
        {
            await using var session = await sessionFactory.CreateAsync(timeoutSource.Token);
            var response = await session.CallAsync("account/rateLimits/read", timeoutSource.Token);
            return new LimitsRead(
                ParseMetric(() => WeeklyUsageFactory.TryCreate(ReadWindows(response)), "一周用量窗口"),
                ParseMetric(() => ReadResetCredits(response), "重置卡信息"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var error = new UsageReadError(UsageReadErrorKind.Timeout, "读取额度和重置卡超时，请重试。");
            return new LimitsRead(new(null, error), new(null, error));
        }
        catch (Exception exception)
        {
            var failure = MapFailure<WeeklyUsage>(
                exception,
                "暂时无法读取额度和重置卡，请重试。");
            return new LimitsRead(failure, new(null, failure.Error));
        }
    }

    private async Task<MetricRead<AccountSevenDayUsage>> CreateAndReadAccountAsync(
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(requestTimeout ?? TimeSpan.FromSeconds(10));
        try
        {
            await using var session = await sessionFactory.CreateAsync(timeoutSource.Token);
            return await ReadAccountAsync(session, timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure<AccountSevenDayUsage>(
                UsageReadErrorKind.Timeout,
                "读取账号 Token 用量超时，请重试。");
        }
        catch (Exception exception)
        {
            return MapFailure<AccountSevenDayUsage>(
                exception,
                "暂时无法读取账号 Token 用量，请重试。");
        }
    }

    private sealed record LimitsRead(
        MetricRead<WeeklyUsage> Weekly,
        MetricRead<ResetCredits> Credits);

    private static MetricRead<T> ParseMetric<T>(Func<T?> parse, string name) where T : class
    {
        try
        {
            var value = parse();
            return value is not null
                ? new(value, null)
                : Failure<T>(UsageReadErrorKind.IncompatibleProtocol, $"Codex 未返回可识别的{name}。");
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or FormatException or OverflowException or ArgumentOutOfRangeException)
        {
            return Failure<T>(UsageReadErrorKind.IncompatibleProtocol, $"Codex 返回的{name}格式不兼容。");
        }
    }

    private static ResetCredits? ReadResetCredits(JsonElement response)
    {
        if (!response.TryGetProperty("rateLimitResetCredits", out var credits) ||
            credits.ValueKind is not JsonValueKind.Object ||
            !credits.TryGetProperty("availableCount", out var countElement) ||
            !countElement.TryGetInt32(out var count) || count < 0)
        {
            return null;
        }

        if (count == 0)
        {
            return new ResetCredits(0, null, true);
        }

        var expirations = new List<DateTimeOffset>();
        var availableRows = 0;
        var unknownExpiry = false;
        if (credits.TryGetProperty("credits", out var rows) && rows.ValueKind is JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                if (!row.TryGetProperty("status", out var status) || status.GetString() != "available")
                {
                    continue;
                }
                availableRows++;
                if (ReadNullableInt64(row, "expiresAt") is long expiresAt)
                {
                    expirations.Add(DateTimeOffset.FromUnixTimeSeconds(expiresAt));
                }
                else
                {
                    unknownExpiry = true;
                }
            }
        }

        return new ResetCredits(count,
            expirations.Count > 0 ? expirations.Min() : null,
            availableRows == count && !unknownExpiry);
    }

    private static async Task<MetricRead<AccountSevenDayUsage>> ReadAccountAsync(
        IAppServerSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await session.CallAsync("account/usage/read", cancellationToken);
            var usage = ReadAccountUsage(response);
            return usage is not null
                ? new MetricRead<AccountSevenDayUsage>(usage, null)
                : new MetricRead<AccountSevenDayUsage>(
                    null,
                    new UsageReadError(
                        UsageReadErrorKind.IncompatibleProtocol,
                        "Codex 未返回可识别的账号 Token 用量。"));
        }
        catch (CodexIntegrationException exception)
        {
            return new MetricRead<AccountSevenDayUsage>(
                null,
                new UsageReadError(exception.Kind, exception.Message));
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            return new MetricRead<AccountSevenDayUsage>(
                null,
                new UsageReadError(
                    UsageReadErrorKind.IncompatibleProtocol,
                    "当前 Codex 版本与账号 Token 用量协议不兼容。"));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            return new MetricRead<AccountSevenDayUsage>(
                null,
                new UsageReadError(
                    UsageReadErrorKind.Unavailable,
                    "暂时无法读取账号 Token 用量，请重试。"));
        }
    }

    private static IEnumerable<RateLimitWindowData> ReadWindows(JsonElement response)
    {
        JsonElement rateLimits;
        if (response.TryGetProperty("rateLimitsByLimitId", out var byId) &&
            byId.ValueKind is JsonValueKind.Object &&
            byId.TryGetProperty("codex", out var codexLimits) &&
            codexLimits.ValueKind is JsonValueKind.Object)
        {
            rateLimits = codexLimits;
        }
        else if (!response.TryGetProperty("rateLimits", out rateLimits) ||
                 rateLimits.ValueKind is not JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var propertyName in new[] { "primary", "secondary" })
        {
            if (!rateLimits.TryGetProperty(propertyName, out var window) ||
                window.ValueKind is JsonValueKind.Null)
            {
                continue;
            }

            if (!window.TryGetProperty("usedPercent", out var usedPercent))
            {
                continue;
            }

            yield return new RateLimitWindowData(
                usedPercent.GetInt32(),
                ReadNullableInt64(window, "resetsAt"),
                ReadNullableInt64(window, "windowDurationMins"));
        }
    }

    private static long? ReadNullableInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind is JsonValueKind.Number
            ? property.GetInt64()
            : null;

    private static AccountSevenDayUsage? ReadAccountUsage(JsonElement response)
    {
        if (!response.TryGetProperty("dailyUsageBuckets", out var buckets) ||
            buckets.ValueKind is not JsonValueKind.Array)
        {
            return null;
        }

        var values = new List<AccountDailyTokenBucket>();
        foreach (var bucket in buckets.EnumerateArray())
        {
            if (!bucket.TryGetProperty("startDate", out var dateElement) ||
                !DateOnly.TryParseExact(
                    dateElement.GetString(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date) ||
                !bucket.TryGetProperty("tokens", out var tokensElement) ||
                !tokensElement.TryGetInt64(out var tokens))
            {
                continue;
            }

            values.Add(new AccountDailyTokenBucket(date, tokens));
        }

        return AccountSevenDayUsageFactory.TryCreate(values);
    }

    private static MetricRead<T> MapFailure<T>(
        Exception exception,
        string unavailableMessage)
        where T : class =>
        exception switch
        {
            CodexIntegrationException integration =>
                Failure<T>(integration.Kind, integration.Message),
            JsonException or InvalidOperationException or FormatException or OverflowException =>
                Failure<T>(
                    UsageReadErrorKind.IncompatibleProtocol,
                    "当前 Codex 版本与用量读取协议不兼容。"),
            IOException or UnauthorizedAccessException or ObjectDisposedException =>
                Failure<T>(UsageReadErrorKind.Unavailable, unavailableMessage),
            _ => throw exception,
        };

    private static MetricRead<T> Failure<T>(
        UsageReadErrorKind kind,
        string message)
        where T : class =>
        new(null, new UsageReadError(kind, message));

    private sealed record MetricRead<T>(T? Value, UsageReadError? Error)
        where T : class;
}
