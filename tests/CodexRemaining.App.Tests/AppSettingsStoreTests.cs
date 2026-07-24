using CodexRemaining.App.Infrastructure;

namespace CodexRemaining.App.Tests;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public void SavesAndLoadsCapsuleSizeAndAttachment()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"CodexRemaining.Tests-{Guid.NewGuid():N}");

        try
        {
            var store = new AppSettingsStore(
                Path.Combine(directory, "settings.json"),
                new LocalLogger(Path.Combine(directory, "logs")));
            var expected = new AppSettings
            {
                CapsuleSize = CapsuleSizePreset.Large,
                RefreshIntervalMinutes = 10,
                Attachment = new CapsuleAttachmentSettings
                {
                    MonitorDeviceName = @"\\.\DISPLAY2",
                    MonitorCenterX = 3200,
                    MonitorCenterY = 720,
                    Anchor = CapsuleAnchor.BottomRight,
                    EdgeOffsetRatio = 0,
                },
            };

            store.Save(expected);
            var actual = store.Load();

            Assert.Equal(CapsuleSizePreset.Large, actual.CapsuleSize);
            Assert.Equal(10, actual.RefreshIntervalMinutes);
            Assert.Equal(expected.Attachment, actual.Attachment);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void UsesFiveMinutesWhenSavedRefreshIntervalIsUnsupported()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"CodexRemaining.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            File.WriteAllText(settingsPath, """{ "RefreshIntervalMinutes": 3 }""");
            var store = new AppSettingsStore(
                settingsPath,
                new LocalLogger(Path.Combine(directory, "logs")));

            var settings = store.Load();

            Assert.Equal(5, settings.RefreshIntervalMinutes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
