using System.ComponentModel;
using System.Diagnostics;
using CodexRemaining.Core.Codex;

namespace CodexRemaining.App.Infrastructure;

public static class CodexVersionReader
{
    public static async Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        var executable = CodexExecutableLocator.Find();
        if (executable is null)
        {
            return null;
        }

        var isCommandScript = string.Equals(
            Path.GetExtension(executable),
            ".cmd",
            StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo
        {
            FileName = isCommandScript
                ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
                : executable,
            Arguments = isCommandScript
                ? $"/d /s /c \"{Path.GetFileName(executable)} --version\""
                : "--version",
            WorkingDirectory = isCommandScript
                ? Path.GetDirectoryName(executable)!
                : Environment.CurrentDirectory,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return null;
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var output = (await standardOutput).Trim();
            if (string.IsNullOrWhiteSpace(output))
            {
                output = (await standardError).Trim();
            }

            return string.IsNullOrWhiteSpace(output)
                ? null
                : output.Split('\n', StringSplitOptions.TrimEntries)[0];
        }
        catch (Exception exception) when (
            exception is IOException or
                InvalidOperationException or
                UnauthorizedAccessException or
                OperationCanceledException or
                Win32Exception)
        {
            return null;
        }
    }
}
