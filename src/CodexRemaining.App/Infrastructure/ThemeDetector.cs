using Microsoft.Win32;

namespace CodexRemaining.App.Infrastructure;

public static class ThemeDetector
{
    public static bool IsLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }
}
