using System.Text.Json;
using CodexRemaining.Core.Application;

namespace CodexRemaining.App.Infrastructure;

public sealed class JsonUsageSnapshotStore(string filePath, LocalLogger logger)
    : IUsageSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<UsageSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<UsageSnapshot>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            logger.Error("读取用量缓存失败", exception);
            return null;
        }
    }

    public async Task SaveAsync(UsageSnapshot snapshot, CancellationToken cancellationToken)
    {
        var temporaryFile = filePath + ".tmp";

        try
        {
            await using (var stream = File.Create(temporaryFile))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
            }

            File.Move(temporaryFile, filePath, overwrite: true);
        }
        catch (IOException exception)
        {
            logger.Error("保存用量缓存失败", exception);
        }
    }
}
