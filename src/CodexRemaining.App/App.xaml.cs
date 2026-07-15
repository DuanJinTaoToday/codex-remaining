using System.Windows;
using CodexRemaining.App.Infrastructure;
using CodexRemaining.Core.Application;
using CodexRemaining.Core.Codex;

namespace CodexRemaining.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceCoordinator? singleInstance;
    private MainWindowViewModel? viewModel;
    private LocalLogger? logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        singleInstance = new SingleInstanceCoordinator();
        if (!singleInstance.TryAcquire())
        {
            singleInstance.SignalExistingInstance();
            Shutdown();
            return;
        }

        var paths = AppPaths.Create();
        logger = new LocalLogger(paths.LogDirectory);
        logger.Info("应用启动");

        var settingsStore = new AppSettingsStore(paths.SettingsFile, logger);
        var settings = settingsStore.Load();
        var snapshotStore = new JsonUsageSnapshotStore(paths.SnapshotFile, logger);
        var provider = new CodexUsageProvider(new CodexAppServerSessionFactory());
        var monitor = new UsageMonitor(provider, snapshotStore, TimeProvider.System);
        var autoStartManager = new AutoStartManager(Environment.ProcessPath!);

        viewModel = new MainWindowViewModel(monitor, logger, autoStartManager);
        var window = new MainWindow(viewModel, settingsStore, settings);
        MainWindow = window;

        singleInstance.StartListening(() =>
        {
            Dispatcher.Invoke(async () =>
            {
                window.RestoreToVisibleArea();
                window.Show();
                window.Activate();
                await viewModel.RefreshAsync();
            });
        });

        window.Show();
        await viewModel.StartAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        viewModel?.Dispose();
        singleInstance?.Dispose();
        logger?.Info("应用退出");
        base.OnExit(e);
    }
}
