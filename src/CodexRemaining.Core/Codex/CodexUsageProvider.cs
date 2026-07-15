using System.Text.Json;
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

public sealed record UsageReadResult(WeeklyUsage? Usage, UsageReadError? Error)
{
    public bool IsSuccess => Usage is not null && Error is null;

    public static UsageReadResult Success(WeeklyUsage usage) => new(usage, null);

    public static UsageReadResult Failure(UsageReadErrorKind kind, string message) =>
        new(null, new UsageReadError(kind, message));
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

public sealed class CodexUsageProvider(IAppServerSessionFactory sessionFactory) : ICodexUsageProvider
{
    public async Task<UsageReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var session = await sessionFactory.CreateAsync(cancellationToken);
            var response = await session.CallAsync("account/rateLimits/read", cancellationToken);
            var windows = ReadWindows(response);
            var usage = WeeklyUsageFactory.TryCreate(windows);

            return usage is not null
                ? UsageReadResult.Success(usage)
                : UsageReadResult.Failure(
                    UsageReadErrorKind.IncompatibleProtocol,
                    "Codex 未返回可识别的一周用量窗口。");
        }
        catch (CodexIntegrationException exception)
        {
            return UsageReadResult.Failure(exception.Kind, exception.Message);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            return UsageReadResult.Failure(
                UsageReadErrorKind.IncompatibleProtocol,
                "当前 Codex 版本与用量读取协议不兼容。");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            return UsageReadResult.Failure(
                UsageReadErrorKind.Unavailable,
                "暂时无法读取 Codex 用量，请重试。");
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
}
