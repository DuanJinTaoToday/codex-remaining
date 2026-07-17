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
}
