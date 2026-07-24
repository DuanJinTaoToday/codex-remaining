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
    UsageReadError? AccountError = null)
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
        var weeklyTask = CreateAndReadWeeklyAsync(cancellationToken);
        var accountTask = CreateAndReadAccountAsync(cancellationToken);
        await Task.WhenAll(weeklyTask, accountTask);
        var weekly = await weeklyTask;
        var account = await accountTask;
        return new UsageReadResult(
            weekly.Value,
            weekly.Error,
            account.Value,
            account.Error);
    }

    private async Task<MetricRead<WeeklyUsage>> CreateAndReadWeeklyAsync(
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(requestTimeout ?? TimeSpan.FromSeconds(10));
        try
        {
            await using var session = await sessionFactory.CreateAsync(timeoutSource.Token);
            return await ReadWeeklyAsync(session, timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure<WeeklyUsage>(
                UsageReadErrorKind.Timeout,
                "读取一周用量超时，请重试。");
        }
        catch (Exception exception)
        {
            return MapFailure<WeeklyUsage>(
                exception,
                "暂时无法读取一周用量，请重试。");
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

    private static async Task<MetricRead<WeeklyUsage>> ReadWeeklyAsync(
        IAppServerSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await session.CallAsync("account/rateLimits/read", cancellationToken);
            var usage = WeeklyUsageFactory.TryCreate(ReadWindows(response));
            return usage is not null
                ? new MetricRead<WeeklyUsage>(usage, null)
                : new MetricRead<WeeklyUsage>(
                    null,
                    new UsageReadError(
                        UsageReadErrorKind.IncompatibleProtocol,
                        "Codex 未返回可识别的一周用量窗口。"));
        }
        catch (CodexIntegrationException exception)
        {
            return new MetricRead<WeeklyUsage>(
                null,
                new UsageReadError(exception.Kind, exception.Message));
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            return new MetricRead<WeeklyUsage>(
                null,
                new UsageReadError(
                    UsageReadErrorKind.IncompatibleProtocol,
                    "当前 Codex 版本与一周用量协议不兼容。"));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            return new MetricRead<WeeklyUsage>(
                null,
                new UsageReadError(
                    UsageReadErrorKind.Unavailable,
                    "暂时无法读取一周用量，请重试。"));
        }
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
        if (!response.TryGetProperty("rateLimits", out var rateLimits))
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
