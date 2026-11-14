using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using PCPulse.App.Services;
using PCPulse.Core.GitHub;
using PCPulse.Core.Logging;
using PCPulse.Core.Modules;
using PCPulse.Core.Platform;
using PCPulse.Core.Updates;

namespace PCPulse.App.Views;

internal sealed class MainWindow : Window
{
    private readonly ModuleManager _moduleManager;
    private readonly PulsePaths _paths;
    private readonly HostDialogService _dialogService;
    private readonly HostSettingsStore _settingsStore;
    private readonly PulseFileLogger _logger;
    private readonly GitHubReleaseClient _releaseClient;
    private readonly AppUpdateService _updateService;
    private readonly Version _hostVersion;
    private readonly Action _requestAppExit;

    private readonly StackPanel _navList;
    private readonly ContentControl _contentHost;
    private readonly Border _dialogOverlay;
    private readonly TextBlock _dialogTitle;
    private readonly TextBlock _dialogMessage;
    private readonly Button _dialogConfirmBtn;
    private readonly Button _dialogCancelBtn;
    private readonly Dictionary<string, Button> _navButtons = new(StringComparer.OrdinalIgnoreCase);

    private OverviewPage? _overviewPage;
    private ModulesPage? _modulesPage;
    private SettingsPage? _settingsPage;
    private AboutPage? _aboutPage;
    private HostSettings _settings;
    private string _activeRoute = "nav:overview";
    private bool _forceClosing;

    public MainWindow(
        ModuleManager moduleManager,
        PulsePaths paths,
        HostDialogService dialogService,
        HostSettingsStore settingsStore,
        HostSettings initialSettings,
        PulseFileLogger logger,
        GitHubReleaseClient releaseClient,
        AppUpdateService updateService,
        Version hostVersion,
        Action requestAppExit,
        Action<bool> updateTrayVisibility)
    {
        _moduleManager = moduleManager;
        _paths = paths;
        _dialogService = dialogService;
        _settingsStore = settingsStore;
        _settings = initialSettings;
        _logger = logger;
        _releaseClient = releaseClient;
        _updateService = updateService;
        _hostVersion = hostVersion;
        _requestAppExit = requestAppExit;

        Title = "PCPulse";
        Width = 1160;
        Height = 760;
        MinWidth = 940;
        MinHeight = 620;
        Background = ThemeTokens.WindowBackground;

        try
        {
            using var iconStream = AssetLoader.Open(new Uri("avares://PCPulse/Assets/PCPulse.png"));
            Icon = new WindowIcon(iconStream);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Не удалось загрузить иконку окна: {ex.Message}");
        }

        _navList = new StackPanel { Spacing = 4 };
        var brandBlock = new StackPanel
        {
            Margin = new Thickness(16, 20, 16, 16),
            Spacing = 3,
            Children =
            {
                new TextBlock
                {
                    Text = "PCPulse",
                    FontSize = 22,
                    FontWeight = FontWeight.Bold,
                    Foreground = ThemeTokens.AccentBrush
                },
                new TextBlock
                {
                    Text = $"Экосистема v{_hostVersion}",
                    FontSize = 12,
                    Foreground = ThemeTokens.TextMuted
                }
            }
        };

        var sidebar = new Border
        {
            Width = 240,
            Background = ThemeTokens.SidebarBackground,
            BorderBrush = ThemeTokens.BorderBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new DockPanel
            {
                Children =
                {
                    WithDock(brandBlock, Dock.Top),
                    new ScrollViewer
                    {
                        Padding = new Thickness(10, 0, 10, 14),
                        Content = _navList
                    }
                }
            }
        };

        _contentHost = new ContentControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _dialogTitle = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = ThemeTokens.TextPrimary,
            TextWrapping = TextWrapping.Wrap
        };
        _dialogMessage = new TextBlock
        {
            FontSize = 14,
            Foreground = ThemeTokens.TextMuted,
            TextWrapping = TextWrapping.Wrap
        };
        _dialogCancelBtn = ThemeTokens.CreateSecondaryButton("Отмена");
        _dialogConfirmBtn = ThemeTokens.CreatePrimaryButton("Продолжить");

        var dialogCard = new Border
        {
            MaxWidth = 460,
            MinWidth = 340,
            Background = ThemeTokens.CardBackground,
            BorderBrush = ThemeTokens.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(22),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    _dialogTitle,
                    _dialogMessage,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { _dialogCancelBtn, _dialogConfirmBtn }
                    }
                }
            }
        };

        _dialogOverlay = new Border
        {
            Background = Brush.Parse("#B0060A0F"),
            IsVisible = false,
            Child = dialogCard
        };

        var layoutGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("240,*")
        };
        Grid.SetColumn(sidebar, 0);
        Grid.SetColumn(_contentHost, 1);
        layoutGrid.Children.Add(sidebar);
        layoutGrid.Children.Add(_contentHost);

        var rootGrid = new Grid();
        rootGrid.Children.Add(layoutGrid);
        rootGrid.Children.Add(_dialogOverlay);
        Content = rootGrid;

        _dialogService.AttachPresenter(ShowDialogOverlayAsync);
        _moduleManager.ModulesChanged += () => Dispatcher.UIThread.Post(() =>
        {
            RebuildNavigation();
            _overviewPage?.Refresh();
            _modulesPage?.RebuildList();
        });
        _moduleManager.ModuleSummaryInvalidated += _ => Dispatcher.UIThread.Post(async () =>
        {
            await _moduleManager.RefreshAllSummariesAsync();
            _overviewPage?.Refresh();
        });

        Closing += (_, args) =>
        {
            if (!_forceClosing && _settings.ShowTrayIcon && _settings.MinimizeToTrayOnClose)
            {
                args.Cancel = true;
                Hide();
                return;
            }

            if (!_forceClosing)
            {
                _forceClosing = true;
                _requestAppExit();
            }
        };

        RebuildNavigation();
        Navigate("nav:overview");

        void ApplySettings(HostSettings updated)
        {
            _settings = updated;
            updateTrayVisibility(updated.ShowTrayIcon);
        }

        _settingsPage = new SettingsPage(
            _paths,
            _settingsStore,
            _settings,
            _dialogService,
            _logger,
            _updateService,
            _hostVersion,
            ApplySettings,
            () =>
            {
                _forceClosing = true;
                _requestAppExit();
            });
    }

    public void AllowClose() => _forceClosing = true;

    public bool VerifyStartupSmokeTest() =>
        _navList.Children.Count >= 4 &&
        _contentHost.Content is not null &&
        _dialogOverlay.Parent is Grid;

    private void RebuildNavigation()
    {
        _navList.Children.Clear();
        _navButtons.Clear();

        AddNavButton("nav:overview", "Обзор");

        var installedModules = _moduleManager.Modules
            .Where(m => m.Record.Enabled)
            .ToList();

        if (installedModules.Count > 0)
        {
            _navList.Children.Add(new TextBlock
            {
                Text = "МОДУЛИ",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = ThemeTokens.TextMuted,
                Margin = new Thickness(12, 10, 12, 4)
            });

            foreach (var handle in installedModules)
            {
                var label = handle.State == ModuleRuntimeState.Faulted
                    ? $"{handle.Record.Name} (!)"
                    : handle.Record.Name;
                AddNavButton($"module:{handle.Record.Id}", label);
            }
        }

        _navList.Children.Add(new Border
        {
            Height = 1,
            Background = ThemeTokens.BorderBrush,
            Margin = new Thickness(8, 10)
        });

        AddNavButton("nav:modules", "Модули");
        AddNavButton("nav:settings", "Настройки");
        AddNavButton("nav:about", "О программе");

        UpdateNavHighlight();
    }

    private void AddNavButton(string route, string label)
    {
        var btn = new Button
        {
            Content = label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 9),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(0),
            Foreground = ThemeTokens.TextPrimary,
            Background = Brushes.Transparent
        };
        btn.Click += (_, _) => Navigate(route);
        _navButtons[route] = btn;
        _navList.Children.Add(btn);
    }

    private void UpdateNavHighlight()
    {
        foreach (var (route, btn) in _navButtons)
        {
            var active = string.Equals(route, _activeRoute, StringComparison.OrdinalIgnoreCase);
            btn.Background = active ? ThemeTokens.AccentSoftBrush : Brushes.Transparent;
            btn.Foreground = active ? ThemeTokens.AccentBrush : ThemeTokens.TextPrimary;
            btn.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
        }
    }

    private void Navigate(string route)
    {
        _activeRoute = route;
        UpdateNavHighlight();

        if (route == "nav:overview")
        {
            _overviewPage ??= new OverviewPage(_moduleManager, Navigate);
            _overviewPage.Refresh();
            _contentHost.Content = _overviewPage;
            return;
        }

        if (route == "nav:modules")
        {
            _modulesPage ??= new ModulesPage(_moduleManager, _paths, _dialogService, _logger, _releaseClient);
            _modulesPage.RebuildList();
            _contentHost.Content = _modulesPage;
            return;
        }

        if (route == "nav:settings")
        {
            _contentHost.Content = _settingsPage;
            return;
        }

        if (route == "nav:about")
        {
            _aboutPage = new AboutPage(_hostVersion, _paths, _moduleManager);
            _contentHost.Content = _aboutPage;
            return;
        }

        if (route.StartsWith("module:", StringComparison.OrdinalIgnoreCase))
        {
            var moduleId = route["module:".Length..];
            var view = _moduleManager.GetOrCreateView(moduleId);
            if (view is not null)
            {
                _contentHost.Content = view;
                return;
            }

            var handle = _moduleManager.GetLoadedModule(moduleId);
            var errorStack = new StackPanel
            {
                Margin = new Thickness(28, 24),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Модуль «{handle?.Record.Name ?? moduleId}» недоступен",
                        FontSize = 20,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = ThemeTokens.DangerBrush
                    },
                    new TextBlock
                    {
                        Text = handle?.ErrorMessage ?? "Модуль не загружен или отключён.",
                        Foreground = ThemeTokens.TextPrimary,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            };
            var goModules = ThemeTokens.CreateSecondaryButton("Перейти в Модули");
            goModules.Click += (_, _) => Navigate("nav:modules");
            errorStack.Children.Add(goModules);
            _contentHost.Content = new ScrollViewer { Content = errorStack };
        }
    }

    private Task<bool> ShowDialogOverlayAsync(HostDialogRequest request)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(() =>
        {
            _dialogTitle.Text = request.Title;
            _dialogMessage.Text = request.Message;
            _dialogConfirmBtn.Content = request.ConfirmText;
            _dialogConfirmBtn.Background = request.IsDestructive ? ThemeTokens.DangerBrush : ThemeTokens.AccentBrush;

            if (string.IsNullOrEmpty(request.CancelText))
            {
                _dialogCancelBtn.IsVisible = false;
            }
            else
            {
                _dialogCancelBtn.Content = request.CancelText;
                _dialogCancelBtn.IsVisible = true;
            }

            void CleanupAndResolve(bool result)
            {
                _dialogConfirmBtn.Click -= OnConfirm;
                _dialogCancelBtn.Click -= OnCancel;
                _dialogOverlay.IsVisible = false;
                tcs.TrySetResult(result);
            }

            void OnConfirm(object? s, Avalonia.Interactivity.RoutedEventArgs e) => CleanupAndResolve(true);
            void OnCancel(object? s, Avalonia.Interactivity.RoutedEventArgs e) => CleanupAndResolve(false);

            _dialogConfirmBtn.Click += OnConfirm;
            _dialogCancelBtn.Click += OnCancel;
            _dialogOverlay.IsVisible = true;
        });

        return tcs.Task;
    }

    private static Control WithDock(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        return control;
    }
}
