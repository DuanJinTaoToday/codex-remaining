using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodexRemaining.App.Infrastructure;
using CodexRemaining.Core.Application;
using CodexRemaining.Core.Codex;
using CodexRemaining.Core.Presentation;

namespace CodexRemaining.App;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly UsageMonitor monitor;
    private readonly LocalLogger logger;
    private readonly AutoStartManager autoStartManager;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private readonly object refreshScheduleLock = new();
    private UsageDisplayModel display;
    private UsageMonitorState? lastState;
    private readonly System.Windows.Threading.DispatcherTimer countdownTimer;
    private bool isExpanded;
    private bool isRefreshing;
    private bool isAutoStartEnabled;
    private int refreshIntervalMinutes;
    private CancellationTokenSource refreshScheduleChanged = new();
    private Task? periodicRefreshTask;

    public MainWindowViewModel(
        UsageMonitor monitor,
        LocalLogger logger,
        AutoStartManager autoStartManager,
        int refreshIntervalMinutes = 5)
    {
        this.monitor = monitor;
        this.logger = logger;
        this.autoStartManager = autoStartManager;
        this.refreshIntervalMinutes = IsSupportedRefreshInterval(refreshIntervalMinutes)
            ? refreshIntervalMinutes
            : 5;
        countdownTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        countdownTimer.Tick += UpdateCountdown;
        countdownTimer.Start();
        isAutoStartEnabled = autoStartManager.IsEnabled;
        display = UsageDisplayModel.FromState(
            new UsageMonitorState(UsageStateKind.Loading, null, null, null),
            TimeZoneInfo.Local);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public UsageDisplayModel Display
    {
        get => display;
        private set => SetProperty(ref display, value);
    }

    public bool IsExpanded
    {
        get => isExpanded;
        set => SetProperty(ref isExpanded, value);
    }

    public bool IsRefreshing
    {
        get => isRefreshing;
        private set => SetProperty(ref isRefreshing, value);
    }

    public bool IsAutoStartEnabled
    {
        get => isAutoStartEnabled;
        private set => SetProperty(ref isAutoStartEnabled, value);
    }

    public int RefreshIntervalMinutes
    {
        get => refreshIntervalMinutes;
        private set => SetProperty(ref refreshIntervalMinutes, value);
    }

    public async Task StartAsync()
    {
        await RefreshAsync();
        periodicRefreshTask = RunPeriodicRefreshAsync(lifetime.Token);
    }

    public async Task RefreshAsync()
    {
        if (!await refreshLock.WaitAsync(TimeSpan.Zero))
        {
            return;
        }

        try
        {
            IsRefreshing = true;
            var state = await monitor.RefreshAsync(lifetime.Token);
            lastState = state;
            Display = UsageDisplayModel.FromState(state, TimeZoneInfo.Local);

            var errors = new[] { state.Error, state.AccountError, state.LocalError, state.ResetCreditsError }
                .Where(error => error is not null)
                .Select(error => error!.Kind)
                .Distinct()
                .ToArray();
            if (errors.Length > 0)
            {
                logger.Info($"刷新部分失败: {string.Join(", ", errors)}");
            }
            else
            {
                logger.Info("刷新成功");
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.Error("刷新发生未预期错误", exception);
            lastState = null;
            Display = UsageDisplayModel.FromState(
                new UsageMonitorState(
                    UsageStateKind.Unavailable,
                    null,
                    null,
                    new UsageReadError(UsageReadErrorKind.Unavailable, "暂时无法获取，请重试。")),
                TimeZoneInfo.Local);
        }
        finally
        {
            IsRefreshing = false;
            refreshLock.Release();
        }
    }

    public void SetAutoStart(bool enabled)
    {
        try
        {
            autoStartManager.SetEnabled(enabled);
            IsAutoStartEnabled = autoStartManager.IsEnabled;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            logger.Error("修改开机自启失败", exception);
            IsAutoStartEnabled = autoStartManager.IsEnabled;
        }
    }

    public void SetRefreshInterval(int minutes)
    {
        if (!IsSupportedRefreshInterval(minutes) || minutes == RefreshIntervalMinutes)
        {
            return;
        }

        CancellationTokenSource previousSchedule;
        lock (refreshScheduleLock)
        {
            RefreshIntervalMinutes = minutes;
            previousSchedule = refreshScheduleChanged;
            refreshScheduleChanged = new CancellationTokenSource();
        }

        previousSchedule.Cancel();
        previousSchedule.Dispose();
    }

    private void UpdateCountdown(object? sender, EventArgs e)
    {
        if (lastState is not null)
            Display = UsageDisplayModel.FromState(lastState, TimeZoneInfo.Local);
    }

    public void Dispose()
    {
        countdownTimer.Stop();
        countdownTimer.Tick -= UpdateCountdown;
        lifetime.Cancel();
        lock (refreshScheduleLock)
        {
            refreshScheduleChanged.Cancel();
            refreshScheduleChanged.Dispose();
        }
        lifetime.Dispose();
    }

    private async Task RunPeriodicRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                CancellationToken scheduleChangedToken;
                TimeSpan interval;
                lock (refreshScheduleLock)
                {
                    scheduleChangedToken = refreshScheduleChanged.Token;
                    interval = TimeSpan.FromMinutes(RefreshIntervalMinutes);
                }

                using var waitSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    scheduleChangedToken);
                try
                {
                    await Task.Delay(interval, waitSource.Token);
                }
                catch (OperationCanceledException) when (scheduleChangedToken.IsCancellationRequested)
                {
                    continue;
                }

                await RefreshAsync();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static bool IsSupportedRefreshInterval(int minutes) =>
        minutes is 1 or 5 or 10;

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
