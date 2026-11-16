using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using PCPulse.App.Services;
using PCPulse.App.Views;
using PCPulse.Core.GitHub;
using PCPulse.Core.Logging;
using PCPulse.Core.Modules;
using PCPulse.Core.Platform;
using PCPulse.Core.Updates;

namespace PCPulse.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        AppBuilder.Configure<PulseApplication>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
}

internal sealed class PulseApplication : Application
{
    private TrayIcon? _trayIcon;
    private ModuleManager? _moduleManager;
    private HttpClient? _httpClient;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var isSmokeTest = Environment.GetCommandLineArgs()
                .Contains("--startup-smoke-test", StringComparer.Ordinal);

            var paths = new PulsePaths();
            paths.EnsureCreated();
            var logger = new PulseFileLogger(paths.LogsDirectory, "host");
            var hostVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);
            hostVersion = new Version(hostVersion.Major, hostVersion.Minor, Math.Max(0, hostVersion.Build));

            var dialogService = new HostDialogService();
            var settingsStore = new HostSettingsStore(paths);
            var settings = settingsStore.LoadAsync().GetAwaiter().GetResult();

            _httpClient = GitHubReleaseClient.CreateDefaultHttpClient(hostVersion);
            var githubToken = GitHubCliAuth.GetTokenAsync().GetAwaiter().GetResult();
            var releaseClient = new GitHubReleaseClient(_httpClient, githubToken);
            var updateService = new AppUpdateService(_httpClient, githubToken);

            _moduleManager = new ModuleManager(paths, hostVersion, dialogService, logger);
            if (!isSmokeTest)
            {
                var manager = _moduleManager;
                Dispatcher.UIThread.Post(async () =>
                {
                    try
                    {
                        await manager.InitializeAndLoadAllAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.Error("Ошибка начальной загрузки модулей.", ex);
                    }
                });
            }

            MainWindow? mainWindow = null;

            void ExitApp()
            {
                mainWindow?.AllowClose();
                desktop.Shutdown(0);
            }

            void SetTrayVisible(bool visible)
            {
                if (isSmokeTest)
                    return;

                if (visible)
                    EnsureTrayIcon(mainWindow, ExitApp, logger);
                else
                    DisposeTrayIcon();
            }

            mainWindow = new MainWindow(
                _moduleManager,
                paths,
                dialogService,
                settingsStore,
                settings,
                logger,
                releaseClient,
                updateService,
                hostVersion,
                ExitApp,
                SetTrayVisible);

            desktop.MainWindow = mainWindow;
            if (!isSmokeTest && settings.ShowTrayIcon)
                EnsureTrayIcon(mainWindow, ExitApp, logger);

            desktop.Exit += (_, _) =>
            {
                DisposeTrayIcon();
                _moduleManager?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _httpClient?.Dispose();
            };

            if (isSmokeTest)
            {
                if (!mainWindow.VerifyStartupSmokeTest())
                    throw new InvalidOperationException("Smoke-тест запуска главного окна PCPulse не пройден.");

                logger.Info("Проверка запуска PCPulse пройдена");
                Dispatcher.UIThread.Post(() => desktop.Shutdown(0), DispatcherPriority.Background);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void EnsureTrayIcon(MainWindow? mainWindow, Action exitApp, PulseFileLogger logger)
    {
        if (_trayIcon is not null)
            return;

        try
        {
            WindowIcon? icon = null;
            using (var stream = AssetLoader.Open(new Uri("avares://PCPulse/Assets/PCPulse.png")))
            {
                icon = new WindowIcon(stream);
            }

            var menu = new NativeMenu();
            var openItem = new NativeMenuItem("Открыть PCPulse");
            openItem.Click += (_, _) => ShowMainWindow(mainWindow);
            var exitItem = new NativeMenuItem("Выход");
            exitItem.Click += (_, _) => exitApp();
            menu.Add(openItem);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(exitItem);

            _trayIcon = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "PCPulse",
                Menu = menu,
                IsVisible = true
            };
            _trayIcon.Clicked += (_, _) => ShowMainWindow(mainWindow);
        }
        catch (Exception ex)
        {
            logger.Warn($"Не удалось инициализировать значок в трее: {ex.Message}");
        }
    }

    private static void ShowMainWindow(MainWindow? mainWindow)
    {
        if (mainWindow is null) return;
        if (!mainWindow.IsVisible)
            mainWindow.Show();
        if (mainWindow.WindowState == WindowState.Minimized)
            mainWindow.WindowState = WindowState.Normal;
        mainWindow.Activate();
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null) return;
        try
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
        }
        catch { }
        _trayIcon = null;
    }
}
