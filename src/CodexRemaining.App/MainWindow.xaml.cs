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
    private bool isDragging;
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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // The first measure can still use the native minimum size from window creation.
        InvalidateMeasure();
        UpdateLayout();
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
            isDragging = true;
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        finally
        {
            isDragging = false;
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
        if (!IsLoaded || isDragging || settings.Attachment is null || placementRestoreQueued)
        {
            return;
        }

        placementRestoreQueued = true;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                placementRestoreQueued = false;
                if (isDragging || settings.Attachment is not { } attachment)
                {
                    return;
                }

                var restored = WindowPlacement.RestoreAttachment(this, attachment);
                if (restored is not null)
                {
                    settings.Attachment = restored;
                    SavePosition();
                }
            });
    }

    private void ApplyCapsuleSize(CapsuleSizePreset size)
    {
        var (compactWidth, expandedWidth, compactPadding, expandedPadding, sectionGap,
            compactValueFont, secondaryFont, headerFont, heroFont, tokenFont) = size switch
        {
            CapsuleSizePreset.Small => (96d, 304d, 5d, 11d, 7d, 13d, 11d, 14d, 25d, 16d),
            CapsuleSizePreset.Large => (128d, 410d, 8d, 17d, 11d, 18d, 14d, 18d, 34d, 21d),
            _ => (110d, 352d, 6d, 14d, 8d, 15d, 12d, 15d, 29d, 18d),
        };

        Resources["CompactWidth"] = compactWidth;
        Resources["ExpandedWidth"] = expandedWidth;
        Resources["CompactPadding"] = new Thickness(0, compactPadding, 0, compactPadding);
        Resources["ExpandedPadding"] = new Thickness(expandedPadding);
        Resources["SectionMargin"] = new Thickness(0, sectionGap, 0, 0);
        Resources["CompactValueFontSize"] = compactValueFont;
        Resources["SecondaryFontSize"] = secondaryFont;
        Resources["HeaderFontSize"] = headerFont;
        Resources["HeroFontSize"] = heroFont;
        Resources["TokenFontSize"] = tokenFont;
    }

    private void ApplyTheme()
    {
        var isLight = ThemeDetector.IsLightTheme();
        Resources["CompactBackground"] = CreateBrush(isLight ? "#FCFDFE" : "#171F2D");
        Resources["CardBackground"] = CreateBrush(isLight ? "#FCFDFE" : "#192335");
        Resources["PrimaryTextBrush"] = CreateBrush(isLight ? "#202631" : "#F1F3F5");
        Resources["SecondaryTextBrush"] = CreateBrush(isLight ? "#565F6D" : "#A6B5CC");
        Resources["ProgressTrackBrush"] = CreateBrush(isLight ? "#DBE2EA" : "#293951");
        Resources["CardBorderBrush"] = CreateBrush(isLight ? "#D6DEE8" : "#34435C");
        Resources["SurfaceBrush"] = CreateBrush(isLight ? "#F0F3F7" : "#233149");
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
        UsageVisualState.Normal when parameter is "Outline" => CreateBrush(81, 113, 166),
        UsageVisualState.Normal when parameter is "Dot" => CreateBrush(103, 155, 255),
        UsageVisualState.Normal => CreateBrush(54, 118, 237),
        UsageVisualState.Warning => CreateBrush(218, 138, 45),
        UsageVisualState.Danger => CreateBrush(215, 80, 92),
        UsageVisualState.Stale => CreateBrush(125, 132, 144),
        UsageVisualState.Unavailable => CreateBrush(125, 132, 144),
        _ => CreateBrush(149, 156, 166),
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
