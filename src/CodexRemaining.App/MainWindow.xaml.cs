using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexRemaining.App.Infrastructure;
using CodexRemaining.Core.Presentation;
using Microsoft.Win32;

namespace CodexRemaining.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private readonly AppSettingsStore settingsStore;
    private readonly AppSettings settings;
    private readonly DispatcherTimer fullScreenTimer;
    private bool hiddenForFullScreen;

    public MainWindow(
        MainWindowViewModel viewModel,
        AppSettingsStore settingsStore,
        AppSettings settings)
    {
        this.viewModel = viewModel;
        this.settingsStore = settingsStore;
        this.settings = settings;

        InitializeComponent();
        DataContext = viewModel;
        ApplyTheme();

        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        fullScreenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        fullScreenTimer.Tick += FullScreenTimer_Tick;
        fullScreenTimer.Start();
    }

    public void RestoreToVisibleArea()
    {
        var width = ActualWidth > 0 ? ActualWidth : MinWidth;
        var height = ActualHeight > 0 ? ActualHeight : 40;
        var area = WindowPlacement.GetNearestWorkArea(new Rect(Left, Top, width, height));
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - width));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - height));
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (settings.Left is double left && settings.Top is double top)
        {
            Left = left;
            Top = top;
            RestoreToVisibleArea();
            SavePosition();
        }
        else
        {
            ResetPosition();
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left)
        {
            return;
        }

        var startingLeft = Left;
        var startingTop = Top;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var moved = Math.Abs(Left - startingLeft) > 3 || Math.Abs(Top - startingTop) > 3;
        if (moved)
        {
            SavePosition();
        }
        else
        {
            viewModel.IsExpanded = !viewModel.IsExpanded;
        }
    }

    private async void RefreshMenuItem_Click(object sender, RoutedEventArgs e) =>
        await viewModel.RefreshAsync();

    private void ContextMenu_Opened(object sender, RoutedEventArgs e) =>
        AutoStartMenuItem.IsChecked = viewModel.IsAutoStartEnabled;

    private void AutoStartMenuItem_Click(object sender, RoutedEventArgs e) =>
        viewModel.SetAutoStart(AutoStartMenuItem.IsChecked);

    private void ResetPositionMenuItem_Click(object sender, RoutedEventArgs e) => ResetPosition();

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) =>
        System.Windows.Application.Current.Shutdown();

    private void ResetPosition()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left, area.Right - ActualWidth - 24);
        Top = area.Top + 72;
        SavePosition();
    }

    private void SavePosition()
    {
        settings.Left = Left;
        settings.Top = Top;
        settingsStore.Save(settings);
    }

    private void FullScreenTimer_Tick(object? sender, EventArgs e)
    {
        var shouldHide = FullScreenDetector.IsForegroundWindowFullScreen(this);
        if (shouldHide && !hiddenForFullScreen)
        {
            hiddenForFullScreen = true;
            Hide();
        }
        else if (!shouldHide && hiddenForFullScreen)
        {
            hiddenForFullScreen = false;
            Show();
        }
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
        Dispatcher.Invoke(ApplyTheme);

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.Invoke(() =>
        {
            RestoreToVisibleArea();
            SavePosition();
        });

    private void ApplyTheme()
    {
        var isLight = ThemeDetector.IsLightTheme();
        Resources["CardBackground"] = CreateBrush(isLight ? "#F7FFFFFF" : "#F7202937");
        Resources["PrimaryTextBrush"] = CreateBrush(isLight ? "#FF111827" : "#FFF9FAFB");
        Resources["SecondaryTextBrush"] = CreateBrush(isLight ? "#FF4B5563" : "#FFD1D5DB");
    }

    private static SolidColorBrush CreateBrush(string value) =>
        new((Color)ColorConverter.ConvertFromString(value));

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        fullScreenTimer.Stop();
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
        SavePosition();
    }
}

public sealed class UsageVisualStateToBrushConverter : System.Windows.Data.IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        System.Globalization.CultureInfo culture) => value switch
    {
        UsageVisualState.Normal => CreateBrush(37, 99, 235),
        UsageVisualState.Warning => CreateBrush(217, 119, 6),
        UsageVisualState.Danger => CreateBrush(220, 38, 38),
        UsageVisualState.Stale => CreateBrush(107, 114, 128),
        UsageVisualState.Unavailable => CreateBrush(107, 114, 128),
        _ => CreateBrush(75, 85, 99),
    };

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue) =>
        new(Color.FromRgb(red, green, blue));
}
