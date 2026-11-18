using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CodexRemaining.Core.Codex;

public sealed class CodexAppServerSessionFactory(string? executablePath = null)
    : IAppServerSessionFactory
{
    public async Task<IAppServerSession> CreateAsync(CancellationToken cancellationToken)
    {
        var resolvedPath = executablePath ?? CodexExecutableLocator.Find();
        if (resolvedPath is null)
        {
            throw new CodexIntegrationException(
                UsageReadErrorKind.CodexNotFound,
                "找不到 codex.exe，请先安装并登录 Codex。");
        }

        var startInfo = CreateStartInfo(resolvedPath);
        var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                throw new CodexIntegrationException(
                    UsageReadErrorKind.Unavailable,
                    "无法启动 Codex app-server。");
            }
        }
        catch (Win32Exception exception)
        {
            process.Dispose();
            throw new CodexIntegrationException(
                UsageReadErrorKind.CodexNotFound,
                "无法启动 codex.exe，请确认 Codex 已正确安装。",
                exception);
        }

        var session = new CodexAppServerSession(process);

        try
        {
            await session.InitializeAsync(cancellationToken);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(string resolvedPath)
    {
        var isCommandScript = string.Equals(
            Path.GetExtension(resolvedPath),
            ".cmd",
            StringComparison.OrdinalIgnoreCase);
        var fileName = isCommandScript
            ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
            : resolvedPath;
        const string appServerArguments = "app-server --stdio --disable plugins";
        var arguments = isCommandScript
            ? $"/d /s /c \"{Path.GetFileName(resolvedPath)} {appServerArguments}\""
            : appServerArguments;

        return new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = isCommandScript
                ? Path.GetDirectoryName(resolvedPath)!
                : Environment.CurrentDirectory,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
        };
    }
}

public static class CodexExecutableLocator
{
    public static string? Find()
    {
        var configuredPath = Environment.GetEnvironmentVariable("CODEX_EXECUTABLE");
        if (IsExecutable(configuredPath))
        {
            return Path.GetFullPath(configuredPath!);
        }

        var desktopCandidate = FindDesktopExecutable();
        if (desktopCandidate is not null)
        {
            return desktopCandidate;
        }

        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var directory in pathDirectories)
        {
            foreach (var fileName in new[] { "codex.cmd", "codex.exe" })
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory.Trim('"'), fileName);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (IsExecutable(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        var windowsAppsCandidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps",
            "codex.exe");

        return IsExecutable(windowsAppsCandidate) ? windowsAppsCandidate : null;
    }

    private static string? FindDesktopExecutable()
    {
        var binDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenAI",
            "Codex",
            "bin");

        try
        {
            if (!Directory.Exists(binDirectory))
            {
                return null;
            }

            foreach (var versionDirectory in Directory
                         .EnumerateDirectories(binDirectory)
                         .OrderByDescending(Directory.GetLastWriteTimeUtc))
            {
                var executable = Path.Combine(versionDirectory, "codex.exe");
                if (IsExecutable(executable))
                {
                    return executable;
                }
            }

            var fallbackExecutable = Path.Combine(binDirectory, "codex.exe");
            return IsExecutable(fallbackExecutable) ? fallbackExecutable : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsExecutable(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path);
}

internal sealed class CodexAppServerSession(Process process) : IAppServerSession
{
    private readonly StreamWriter input = process.StandardInput;
    private readonly StreamReader output = process.StandardOutput;
    private long requestId;
    private bool disposed;

    public Task<JsonElement> CallAsync(string method, CancellationToken cancellationToken) =>
        SendRequestAsync(method, null, includeParams: false, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        try
        {
            input.Close();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (TimeoutException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    internal Task<JsonElement> InitializeAsync(CancellationToken cancellationToken)
    {
        var parameters = new
        {
            clientInfo = new
            {
                name = "codex-remaining",
                title = "Codex Remaining",
                version = "0.3.1",
            },
            capabilities = new
            {
                experimentalApi = true,
            },
        };

        return SendRequestAsync("initialize", parameters, includeParams: true, cancellationToken);
    }

    private async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        bool includeParams,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var id = Interlocked.Increment(ref requestId);
        var request = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["method"] = method,
        };

        if (includeParams)
        {
            request["params"] = parameters;
        }

        await input.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), cancellationToken);
        await input.FlushAsync(cancellationToken);

        while (true)
        {
            var line = await output.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                throw new CodexIntegrationException(
                    UsageReadErrorKind.Unavailable,
                    "Codex app-server 在返回用量前已退出。");
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (!root.TryGetProperty("id", out var responseId) ||
                responseId.ValueKind is not JsonValueKind.Number ||
                responseId.GetInt64() != id)
            {
                continue;
            }

            if (root.TryGetProperty("error", out var error))
            {
                throw CreateProtocolException(error);
            }

            if (!root.TryGetProperty("result", out var result))
            {
                throw new CodexIntegrationException(
                    UsageReadErrorKind.IncompatibleProtocol,
                    "Codex app-server 返回了无法识别的响应。");
            }

            return result.Clone();
        }
    }

    private static CodexIntegrationException CreateProtocolException(JsonElement error)
    {
        var message = error.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString() ?? "Codex app-server 请求失败。"
            : "Codex app-server 请求失败。";
        var normalizedMessage = message.ToLowerInvariant();

        var kind = normalizedMessage.Contains("login", StringComparison.Ordinal) ||
                   normalizedMessage.Contains("auth", StringComparison.Ordinal)
            ? UsageReadErrorKind.NotLoggedIn
            : UsageReadErrorKind.IncompatibleProtocol;

        var displayMessage = kind is UsageReadErrorKind.NotLoggedIn
            ? "请先登录 Codex，然后重试。"
            : "当前 Codex 版本与用量读取协议不兼容。";

        return new CodexIntegrationException(kind, displayMessage);
    }
}
