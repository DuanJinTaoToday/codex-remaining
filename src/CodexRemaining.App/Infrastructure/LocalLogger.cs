using System.Globalization;

namespace CodexRemaining.App.Infrastructure;

public sealed class LocalLogger
{
    private readonly string logDirectory;
    private readonly object sync = new();

    public LocalLogger(string logDirectory)
    {
        this.logDirectory = logDirectory;
        Directory.CreateDirectory(logDirectory);
        DeleteExpiredLogs();
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string category, Exception exception) =>
        Write("ERROR", $"{category}: {exception.GetType().Name}: {exception.Message}");

    private void Write(string level, string message)
    {
        var sanitized = message.Replace('\r', ' ').Replace('\n', ' ');
        var line = $"{DateTimeOffset.Now:O} [{level}] {sanitized}{Environment.NewLine}";
        var file = Path.Combine(logDirectory, $"{DateTime.Now:yyyy-MM-dd}.log");

        lock (sync)
        {
            File.AppendAllText(file, line);
        }
    }

    private void DeleteExpiredLogs()
    {
        var cutoff = DateTime.Today.AddDays(-7);
        foreach (var file in Directory.EnumerateFiles(logDirectory, "*.log"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (DateTime.TryParseExact(
                    name,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date) &&
                date < cutoff)
            {
                File.Delete(file);
            }
        }
    }
}
