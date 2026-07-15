namespace CodexRemaining.App.Infrastructure;

public sealed record AppPaths(
    string DataDirectory,
    string LogDirectory,
    string SettingsFile,
    string SnapshotFile)
{
    public static AppPaths Create()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexRemaining");
        var logDirectory = Path.Combine(dataDirectory, "logs");

        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(logDirectory);

        return new AppPaths(
            dataDirectory,
            logDirectory,
            Path.Combine(dataDirectory, "settings.json"),
            Path.Combine(dataDirectory, "usage-cache.json"));
    }
}
