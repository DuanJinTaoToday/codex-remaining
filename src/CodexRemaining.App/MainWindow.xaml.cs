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
    private const double SnapDistanceInDips = 24;

    private readonly MainWindowViewModel viewModel;
    private readonly AppSettingsStore settingsStore;
    private readonly AppSettings settings;
    private readonly DispatcherTimer fullScreenTimer;
    private bool hiddenForFullScreen;
    private bool placementRestoreQueued;

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
        ApplyCapsuleSize(settings.CapsuleSize);

        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        fullScreenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        fullScreenTimer.Tick += FullScreenTimer_Tick;
        fullScreenTimer.Start();
    }

    public void RestoreToVisibleArea()
    {
        if (settings.Attachment is not null)
        {
            var restored = WindowPlacement.RestoreAttachment(this, settings.Attachment);
            if (restored is not null)
            {
                settings.Attachment = restored;
                SavePosition();
                return;
            }
        }

        settings.Attachment = null;
        WindowPlacement.ConstrainToNearestWorkArea(this);
        SavePosition();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (settings.Attachment is not null)
        {
            RestoreToVisibleArea();
        }
        else if (settings.Left is double left && settings.Top is double top)
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
            settings.Attachment = WindowPlacement.SnapToNearestWorkArea(
                this,
                SnapDistanceInDips);
            SavePosition();
        }
        else
        {
            viewModel.IsExpanded = !viewModel.IsExpanded;
        }
    }

    private async void RefreshMenuItem_Click(object sender, RoutedEventArgs e) =>
        await viewModel.RefreshAsync();

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        AutoStartMenuItem.IsChecked = viewModel.IsAutoStartEnabled;
        SmallSizeMenuItem.IsChecked = settings.CapsuleSize is CapsuleSizePreset.Small;
        MediumSizeMenuItem.IsChecked = settings.CapsuleSize is CapsuleSizePreset.Medium;
        LargeSizeMenuItem.IsChecked = settings.CapsuleSize is CapsuleSizePreset.Large;
        OneMinuteRefreshMenuItem.IsChecked = settings.RefreshIntervalMinutes == 1;
        FiveMinuteRefreshMenuItem.IsChecked = settings.RefreshIntervalMinutes == 5;
        TenMinuteRefreshMenuItem.IsChecked = settings.RefreshIntervalMinutes == 10;
    }

    private void AutoStartMenuItem_Click(object sender, RoutedEventArgs e) =>
        viewModel.SetAutoStart(AutoStartMenuItem.IsChecked);

    private void CapsuleSizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        settings.CapsuleSize = ReferenceEquals(sender, SmallSizeMenuItem)
            ? CapsuleSizePreset.Small
            : ReferenceEquals(sender, LargeSizeMenuItem)
                ? CapsuleSizePreset.Large
                : CapsuleSizePreset.Medium;
        ApplyCapsuleSize(settings.CapsuleSize);
        SavePosition();
        QueueAttachmentRestore();
    }

    private void RefreshIntervalMenuItem_Click(object sender, RoutedEventArgs e)
    {
        settings.RefreshIntervalMinutes = ReferenceEquals(sender, OneMinuteRefreshMenuItem)
            ? 1
            : ReferenceEquals(sender, TenMinuteRefreshMenuItem)
                ? 10
                : 5;
        viewModel.SetRefreshInterval(settings.RefreshIntervalMinutes);
        settingsStore.Save(settings);
    }

    private void ResetPositionMenuItem_Click(object sender, RoutedEventArgs e) => ResetPosition();

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) =>
        System.Windows.Application.Current.Shutdown();

    private void ResetPosition()
    {
        settings.Attachment = WindowPlacement.AttachToPrimaryCorner(
            this,
            CapsuleAnchor.TopRight);
        if (settings.Attachment is null)
        {
            var area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left, area.Right - ActualWidth);
            Top = area.Top;
        }

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

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) =>
        QueueAttachmentRestore();

    private void QueueAttachmentRestore()
    {
        if (!IsLoaded || settings.Attachment is null || placementRestoreQueued)
        {
            return;
        }

        placementRestoreQueued = true;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                placementRestoreQueued = false;
                var restored = WindowPlacement.RestoreAttachment(this, settings.Attachment);
                if (restored is not null)
                {
                    settings.Attachment = restored;
                    SavePosition();
                }
            });
    }

    private void ApplyCapsuleSize(CapsuleSizePreset size)
    {
        var scale = size switch
        {
            CapsuleSizePreset.Small => 0.85,
            CapsuleSizePreset.Large => 1.25,
            _ => 1,
        };
        MinWidth = 104 * scale;
        RootBorder.LayoutTransform = new ScaleTransform(scale, scale);
    }

    private void ApplyTheme()
    {
        var isLight = ThemeDetector.IsLightTheme();
        Resources["CardBackground"] = CreateBrush(isLight ? "#F7FFFFFF" : "#F7202937");
        Resources["PrimaryTextBrush"] = CreateBrush(isLight ? "#FF111827" : "#FFF9FAFB");
        Resources["SecondaryTextBrush"] = CreateBrush(isLight ? "#FF4B5563" : "#FFD1D5DB");
        Resources["ProgressTrackBrush"] = CreateBrush(isLight ? "#FFE5E7EB" : "#FF374151");
        Resources["DividerBrush"] = CreateBrush(isLight ? "#FFD1D5DB" : "#FF4B5563");
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
