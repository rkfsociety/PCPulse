using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PCPulse.App.Services;
using PCPulse.Contracts;
using PCPulse.Core.Catalog;
using PCPulse.Core.GitHub;
using PCPulse.Core.Logging;
using PCPulse.Core.Modules;
using PCPulse.Core.Platform;

namespace PCPulse.App.Views;

internal sealed class ModulesPage : UserControl
{
    private readonly ModuleManager _moduleManager;
    private readonly PulsePaths _paths;
    private readonly HostDialogService _dialogs;
    private readonly PulseFileLogger _logger;
    private readonly GitHubReleaseClient _releaseClient;
    private readonly IReadOnlyList<CatalogModuleEntry> _catalog;
    private readonly Dictionary<string, ModuleReleaseResolution> _remoteCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly StackPanel _listHost;
    private readonly TextBlock _statusText;
    private bool _isBusy;

    public ModulesPage(
        ModuleManager moduleManager,
        PulsePaths paths,
        HostDialogService dialogs,
        PulseFileLogger logger,
        GitHubReleaseClient releaseClient)
    {
        _moduleManager = moduleManager;
        _paths = paths;
        _dialogs = dialogs;
        _logger = logger;
        _releaseClient = releaseClient;
        _catalog = ModuleCatalog.LoadBuiltIn();

        var title = new TextBlock
        {
            Text = "Модули экосистемы",
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
            Foreground = ThemeTokens.TextPrimary
        };

        _statusText = new TextBlock
        {
            Text = "Выберите модули для установки, включения или обновления из GitHub Releases.",
            Foreground = ThemeTokens.TextMuted,
            TextWrapping = TextWrapping.Wrap
        };

        var checkBtn = ThemeTokens.CreatePrimaryButton("Проверить релизы");
        checkBtn.Click += async (_, _) => await RefreshCatalogAsync();

        var installZipBtn = ThemeTokens.CreateSecondaryButton("Установить из локального ZIP…");
        installZipBtn.Click += async (_, _) => await InstallFromLocalZipAsync();

        var actionsRow = new WrapPanel { Orientation = Orientation.Horizontal };
        actionsRow.Children.Add(checkBtn);
        checkBtn.Margin = new Thickness(0, 0, 10, 8);
        actionsRow.Children.Add(installZipBtn);
        installZipBtn.Margin = new Thickness(0, 0, 10, 8);

        _listHost = new StackPanel { Spacing = 12 };

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(28, 24),
                Spacing = 16,
                Children = { title, _statusText, actionsRow, _listHost }
            }
        };

        RebuildList();
    }

    public void RebuildList()
    {
        _listHost.Children.Clear();

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _catalog)
        {
            seenIds.Add(entry.Id);
            var installed = _moduleManager.GetLoadedModule(entry.Id);
            _remoteCache.TryGetValue(entry.Id, out var remote);
            _listHost.Children.Add(BuildModuleCard(entry.Id, entry.Name, entry.Description, entry.Repository, entry.Platforms, installed, remote));
        }

        foreach (var handle in _moduleManager.Modules)
        {
            if (seenIds.Contains(handle.Record.Id))
                continue;
            _listHost.Children.Add(BuildModuleCard(
                handle.Record.Id,
                handle.Record.Name,
                handle.Record.Description,
                "Локальный пакет",
                handle.Manifest?.SupportedPlatforms ?? PulsePlatform.All,
                handle,
                remote: null));
        }
    }

    private Border BuildModuleCard(
        string id,
        string name,
        string description,
        string sourceLabel,
        PulsePlatform supportedPlatforms,
        LoadedModuleHandle? installed,
        ModuleReleaseResolution? remote)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 16
        };

        var left = new StackPanel { Spacing = 5 };
        left.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Foreground = ThemeTokens.TextPrimary
        });
        left.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = ThemeTokens.TextMuted,
            TextWrapping = TextWrapping.Wrap
        });
        left.Children.Add(new TextBlock
        {
            Text = $"Источник: {sourceLabel} • ОС: {supportedPlatforms}",
            FontSize = 12,
            Foreground = ThemeTokens.TextMuted
        });

        var statusLine = BuildStatusLine(installed, remote, out var statusBrush);
        left.Children.Add(new TextBlock
        {
            Text = statusLine,
            Foreground = statusBrush,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        });

        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var buttons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (installed is null)
        {
            if (remote?.Status == ModuleAvailabilityStatus.CompatibleAvailable)
            {
                var installBtn = ThemeTokens.CreatePrimaryButton($"Установить v{remote.Manifest!.Version}");
                installBtn.IsEnabled = !_isBusy;
                installBtn.Click += async (_, _) => await InstallRemoteModuleAsync(id, remote);
                buttons.Children.Add(installBtn);
            }
            else
            {
                var checkOneBtn = ThemeTokens.CreateSecondaryButton("Найти релиз");
                checkOneBtn.IsEnabled = !_isBusy;
                checkOneBtn.Click += async (_, _) => await CheckSingleAsync(id);
                buttons.Children.Add(checkOneBtn);
            }
        }
        else
        {
            if (remote?.Status == ModuleAvailabilityStatus.CompatibleAvailable &&
                Version.TryParse(installed.Record.Version, out var curVer) &&
                remote.Manifest!.Version > curVer)
            {
                var updateBtn = ThemeTokens.CreatePrimaryButton($"Обновить до v{remote.Manifest.Version}");
                updateBtn.Margin = new Thickness(0, 0, 8, 6);
                updateBtn.IsEnabled = !_isBusy;
                updateBtn.Click += async (_, _) => await InstallRemoteModuleAsync(id, remote);
                buttons.Children.Add(updateBtn);
            }

            var toggleBtn = ThemeTokens.CreateSecondaryButton(installed.Record.Enabled ? "Отключить" : "Включить");
            toggleBtn.Margin = new Thickness(0, 0, 8, 6);
            toggleBtn.IsEnabled = !_isBusy;
            toggleBtn.Click += async (_, _) => await ToggleModuleAsync(id, !installed.Record.Enabled);
            buttons.Children.Add(toggleBtn);

            var removeBtn = ThemeTokens.CreateDangerButton("Удалить");
            removeBtn.Margin = new Thickness(0, 0, 0, 6);
            removeBtn.IsEnabled = !_isBusy;
            removeBtn.Click += async (_, _) => await RemoveModuleAsync(id, name);
            buttons.Children.Add(removeBtn);
        }

        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return ThemeTokens.CreateCard(grid);
    }

    private static string BuildStatusLine(
        LoadedModuleHandle? installed,
        ModuleReleaseResolution? remote,
        out IBrush brush)
    {
        if (installed is not null)
        {
            if (installed.State == ModuleRuntimeState.Faulted)
            {
                brush = ThemeTokens.DangerBrush;
                return $"Установлен v{installed.Record.Version}, ошибка: {installed.ErrorMessage}";
            }

            if (remote?.Status == ModuleAvailabilityStatus.CompatibleAvailable &&
                Version.TryParse(installed.Record.Version, out var cur) &&
                remote.Manifest!.Version > cur)
            {
                brush = ThemeTokens.AccentBrush;
                return $"Установлен v{installed.Record.Version} • Доступно обновление v{remote.Manifest.Version}";
            }

            brush = installed.Record.Enabled ? ThemeTokens.SuccessBrush : ThemeTokens.TextMuted;
            return installed.Record.Enabled
                ? $"Установлен v{installed.Record.Version} (активен)"
                : $"Установлен v{installed.Record.Version} (отключён)";
        }

        if (remote is null)
        {
            brush = ThemeTokens.TextMuted;
            return "Не установлен (нажмите «Проверить релизы»)";
        }

        switch (remote.Status)
        {
            case ModuleAvailabilityStatus.CompatibleAvailable:
                brush = ThemeTokens.AccentBrush;
                return $"Готов к установке: v{remote.Manifest!.Version}";
            case ModuleAvailabilityStatus.IncompatiblePlatform:
                brush = ThemeTokens.WarningBrush;
                return $"Несовместим с ОС: {remote.Reason}";
            case ModuleAvailabilityStatus.IncompatibleContract:
                brush = ThemeTokens.WarningBrush;
                return $"Несовместим с версией контракта: {remote.Reason}";
            default:
                brush = ThemeTokens.TextMuted;
                return remote.Reason ?? "Совместимый модульный релиз не найден.";
        }
    }

    private async Task RefreshCatalogAsync()
    {
        if (_isBusy) return;
        _isBusy = true;
        _statusText.Text = "Проверяю доступные релизы модулей на GitHub…";
        RebuildList();

        try
        {
            foreach (var entry in _catalog)
            {
                try
                {
                    var resolution = await _releaseClient.FindLatestCompatibleModuleAsync(entry);
                    _remoteCache[entry.Id] = resolution;
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Не удалось проверить релизы {entry.Repository}: {ex.Message}");
                    _remoteCache[entry.Id] = new ModuleReleaseResolution(
                        ModuleAvailabilityStatus.NoModuleReleases,
                        null,
                        null,
                        null,
                        null,
                        $"Ошибка проверки GitHub: {ex.Message}",
                        null);
                }
            }

            _statusText.Text = "Проверка релизов завершена.";
        }
        finally
        {
            _isBusy = false;
            RebuildList();
        }
    }

    private async Task CheckSingleAsync(string moduleId)
    {
        var entry = _catalog.FirstOrDefault(e => e.Id.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        if (entry is null || _isBusy)
            return;

        _isBusy = true;
        _statusText.Text = $"Проверяю релизы {entry.Name}…";
        RebuildList();
        try
        {
            _remoteCache[entry.Id] = await _releaseClient.FindLatestCompatibleModuleAsync(entry);
            _statusText.Text = $"Проверка {entry.Name} завершена.";
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка проверки релиза {entry.Id}.", ex);
            await _dialogs.ShowErrorAsync("Ошибка проверки релиза", ex.Message);
        }
        finally
        {
            _isBusy = false;
            RebuildList();
        }
    }

    private async Task InstallRemoteModuleAsync(string moduleId, ModuleReleaseResolution resolution)
    {
        if (_isBusy) return;
        _isBusy = true;
        _statusText.Text = $"Скачиваю и проверяю пакет {moduleId}…";
        RebuildList();

        string? downloadedZip = null;
        try
        {
            downloadedZip = await _releaseClient.DownloadAndVerifyModuleZipAsync(resolution, _paths.TempDirectory);
            var record = await _moduleManager.InstallFromZipAsync(downloadedZip);
            _statusText.Text = $"Модуль {record.Name} v{record.Version} установлен.";
            await _dialogs.ShowInfoAsync("Установка модуля", $"Модуль {record.Name} v{record.Version} успешно установлен и активирован.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка установки модуля {moduleId}.", ex);
            await _dialogs.ShowErrorAsync("Ошибка установки модуля", ex.Message);
        }
        finally
        {
            if (downloadedZip is not null && File.Exists(downloadedZip))
            {
                try { File.Delete(downloadedZip); } catch { }
            }
            _isBusy = false;
            RebuildList();
        }
    }

    private async Task InstallFromLocalZipAsync()
    {
        if (_isBusy) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите ZIP-пакет модуля PCPulse",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Пакет модуля PCPulse (*.zip)") { Patterns = ["*.zip"] }
            ]
        });

        var localPath = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath))
            return;

        _isBusy = true;
        _statusText.Text = "Устанавливаю локальный пакет модуля…";
        RebuildList();
        try
        {
            var record = await _moduleManager.InstallFromZipAsync(localPath);
            _statusText.Text = $"Модуль {record.Name} v{record.Version} установлен из ZIP.";
            await _dialogs.ShowInfoAsync("Установка из ZIP", $"Модуль {record.Name} v{record.Version} успешно подключён.");
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка установки локального ZIP модуля.", ex);
            await _dialogs.ShowErrorAsync("Не удалось установить модуль", ex.Message);
        }
        finally
        {
            _isBusy = false;
            RebuildList();
        }
    }

    private async Task ToggleModuleAsync(string moduleId, bool enabled)
    {
        if (_isBusy) return;
        _isBusy = true;
        try
        {
            await _moduleManager.SetModuleEnabledAsync(moduleId, enabled);
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка переключения модуля {moduleId}.", ex);
            await _dialogs.ShowErrorAsync("Ошибка модуля", ex.Message);
        }
        finally
        {
            _isBusy = false;
            RebuildList();
        }
    }

    private async Task RemoveModuleAsync(string moduleId, string name)
    {
        if (_isBusy) return;
        var confirmed = await _dialogs.ConfirmAsync(
            $"Удалить {name}?",
            $"Модуль «{name}» будет отключён и удалён из PCPulse. Его файлы данных в профиле пользователя сохранятся.",
            confirmText: "Удалить",
            cancelText: "Отмена",
            isDestructive: true);
        if (!confirmed)
            return;

        _isBusy = true;
        try
        {
            await _moduleManager.UninstallModuleAsync(moduleId);
            _statusText.Text = $"Модуль «{name}» удалён.";
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка удаления модуля {moduleId}.", ex);
            await _dialogs.ShowErrorAsync("Ошибка удаления", ex.Message);
        }
        finally
        {
            _isBusy = false;
            RebuildList();
        }
    }
}
