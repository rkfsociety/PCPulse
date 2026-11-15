using System.Diagnostics;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PCPulse.App.Services;
using PCPulse.Core.Logging;
using PCPulse.Core.Platform;
using PCPulse.Core.Updates;

namespace PCPulse.App.Views;

internal sealed class SettingsPage : UserControl
{
    private readonly PulsePaths _paths;
    private readonly HostSettingsStore _settingsStore;
    private readonly HostDialogService _dialogs;
    private readonly PulseFileLogger _logger;
    private readonly AppUpdateService _updateService;
    private readonly Version _hostVersion;
    private readonly Action<HostSettings> _onSettingsSaved;
    private readonly Action _requestAppExit;

    private readonly CheckBox _showTrayCheck;
    private readonly CheckBox _minimizeToTrayCheck;
    private readonly TextBlock _updateStatusText;
    private HostSettings _current;

    public SettingsPage(
        PulsePaths paths,
        HostSettingsStore settingsStore,
        HostSettings initialSettings,
        HostDialogService dialogs,
        PulseFileLogger logger,
        AppUpdateService updateService,
        Version hostVersion,
        Action<HostSettings> onSettingsSaved,
        Action requestAppExit)
    {
        _paths = paths;
        _settingsStore = settingsStore;
        _current = initialSettings;
        _dialogs = dialogs;
        _logger = logger;
        _updateService = updateService;
        _hostVersion = hostVersion;
        _onSettingsSaved = onSettingsSaved;
        _requestAppExit = requestAppExit;

        var title = new TextBlock
        {
            Text = "Настройки PCPulse",
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
            Foreground = ThemeTokens.TextPrimary
        };

        _showTrayCheck = new CheckBox
        {
            Content = "Показывать значок PCPulse в системном трее",
            IsChecked = _current.ShowTrayIcon,
            Foreground = ThemeTokens.TextPrimary
        };

        _minimizeToTrayCheck = new CheckBox
        {
            Content = "Сворачивать окно в трей при закрытии (вместо выхода)",
            IsChecked = _current.MinimizeToTrayOnClose,
            Foreground = ThemeTokens.TextPrimary
        };

        var saveBtn = ThemeTokens.CreatePrimaryButton("Сохранить настройки");
        saveBtn.Click += async (_, _) => await SaveSettingsAsync();

        var trayCard = ThemeTokens.CreateCard(new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Поведение окна и трея", FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = ThemeTokens.TextPrimary },
                _showTrayCheck,
                _minimizeToTrayCheck,
                saveBtn
            }
        });

        _updateStatusText = new TextBlock
        {
            Text = $"Текущая версия PCPulse: {_hostVersion}",
            Foreground = ThemeTokens.TextMuted,
            TextWrapping = TextWrapping.Wrap
        };

        var checkUpdateBtn = ThemeTokens.CreateSecondaryButton("Проверить обновления");
        checkUpdateBtn.Click += async (_, _) => await CheckAndApplyUpdateAsync();

        var openDataBtn = ThemeTokens.CreateSecondaryButton("Открыть папку данных и логов");
        openDataBtn.Click += (_, _) => OpenDirectory(_paths.DataRoot);

        var systemCard = ThemeTokens.CreateCard(new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Обновления и диагностика", FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = ThemeTokens.TextPrimary },
                _updateStatusText,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children = { checkUpdateBtn, openDataBtn }
                }
            }
        });

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(28, 24),
                Spacing = 16,
                Children = { title, trayCard, systemCard }
            }
        };
    }

    private async Task SaveSettingsAsync()
    {
        _current = new HostSettings(
            ShowTrayIcon: _showTrayCheck.IsChecked == true,
            MinimizeToTrayOnClose: _minimizeToTrayCheck.IsChecked == true);

        await _settingsStore.SaveAsync(_current);
        _onSettingsSaved(_current);
        await _dialogs.ShowInfoAsync("Настройки", "Настройки PCPulse сохранены.");
    }

    private async Task CheckAndApplyUpdateAsync()
    {
        _updateStatusText.Text = "Проверяю последний релиз PCPulse на GitHub…";
        try
        {
            var check = await _updateService.CheckForUpdateAsync(_hostVersion, preferInstallerOnWindows: IsPerUserInstalledOnWindows());
            _updateStatusText.Text = check.Message;

            if (!check.IsUpdateAvailable)
            {
                await _dialogs.ShowInfoAsync("Обновления", check.Message);
                return;
            }

            if (check.Asset is null)
            {
                await _dialogs.ShowInfoAsync("Доступно обновление", check.Message);
                return;
            }

            var confirmed = await _dialogs.ConfirmAsync(
                $"Доступна версия {check.LatestVersion}",
                $"Установлена версия {_hostVersion}. Скачать и установить {check.Asset.FileName} с проверкой SHA-256? Приложение перезапустится автоматически.",
                confirmText: "Обновить и перезапустить",
                cancelText: "Позже");
            if (!confirmed)
                return;

            _updateStatusText.Text = $"Скачиваю {check.Asset.FileName}…";
            var packagePath = await _updateService.DownloadAndVerifyAssetAsync(check.Asset, _paths.TempDirectory);
            LaunchSelfUpdateScript(packagePath);
            _requestAppExit();
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка проверки или установки обновления PCPulse.", ex);
            _updateStatusText.Text = $"Ошибка обновления: {ex.Message}";
            await _dialogs.ShowErrorAsync("Ошибка обновления", ex.Message);
        }
    }

    private static bool IsPerUserInstalledOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        var exeDir = AppContext.BaseDirectory;
        var localPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PCPulse");
        return Path.GetFullPath(exeDir).StartsWith(Path.GetFullPath(localPrograms), StringComparison.OrdinalIgnoreCase);
    }

    private void LaunchSelfUpdateScript(string packagePath)
    {
        var updateWorkDir = Path.Combine(_paths.TempDirectory, "self-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateWorkDir);

        if (OperatingSystem.IsWindows())
        {
            var scriptPath = Path.Combine(updateWorkDir, "apply-update.ps1");
            ExtractEmbeddedText("PCPulse.App.Scripts.apply-update.ps1", scriptPath, includeUtf8Bom: true);

            var currentExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PCPulse.exe");
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-WindowStyle");
            psi.ArgumentList.Add("Hidden");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(scriptPath);
            psi.ArgumentList.Add("-ProcessId");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            psi.ArgumentList.Add("-PackagePath");
            psi.ArgumentList.Add(packagePath);
            psi.ArgumentList.Add("-TargetExePath");
            psi.ArgumentList.Add(currentExe);
            psi.ArgumentList.Add("-LogPath");
            psi.ArgumentList.Add(_logger.LogFilePath);
            Process.Start(psi);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            var scriptPath = Path.Combine(updateWorkDir, "apply-update.sh");
            ExtractEmbeddedText("PCPulse.App.Scripts.apply-update.sh", scriptPath, includeUtf8Bom: false);
            var targetDir = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(scriptPath);
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            psi.ArgumentList.Add(packagePath);
            psi.ArgumentList.Add(targetDir);
            psi.ArgumentList.Add(_logger.LogFilePath);
            Process.Start(psi);
        }
    }

    private static void ExtractEmbeddedText(string resourceName, string targetPath, bool includeUtf8Bom)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Не найден встроенный скрипт обновления: {resourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd();
        File.WriteAllText(targetPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: includeUtf8Bom));
    }

    private void OpenDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start(new ProcessStartInfo { FileName = "xdg-open", ArgumentList = { path }, UseShellExecute = false });
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Не удалось открыть каталог {path}: {ex.Message}");
        }
    }
}
