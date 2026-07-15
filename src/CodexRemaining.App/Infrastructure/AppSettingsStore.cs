using System.Text.Json;

namespace CodexRemaining.App.Infrastructure;

public sealed class AppSettings
{
    public double? Left { get; set; }

    public double? Top { get; set; }
}

public sealed class AppSettingsStore(string filePath, LocalLogger logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Load()
    {
        try
        {
            return File.Exists(filePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            logger.Error("读取设置失败", exception);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var temporaryFile = filePath + ".tmp";
            File.WriteAllText(temporaryFile, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryFile, filePath, overwrite: true);
        }
        catch (IOException exception)
        {
            logger.Error("保存设置失败", exception);
        }
    }
}
