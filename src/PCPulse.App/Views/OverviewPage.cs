using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PCPulse.App.Services;
using PCPulse.Contracts;
using PCPulse.Core.Modules;

namespace PCPulse.App.Views;

internal sealed class OverviewPage : UserControl
{
    private readonly ModuleManager _moduleManager;
    private readonly Action<string> _openSection;
    private readonly StackPanel _cardsHost;
    private readonly TextBlock _statsText;

    public OverviewPage(ModuleManager moduleManager, Action<string> openSection)
    {
        _moduleManager = moduleManager;
        _openSection = openSection;

        var title = new TextBlock
        {
            Text = "Обзор экосистемы",
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
            Foreground = ThemeTokens.TextPrimary
        };

        _statsText = new TextBlock
        {
            FontSize = 14,
            Foreground = ThemeTokens.TextMuted,
            TextWrapping = TextWrapping.Wrap
        };

        var refreshBtn = ThemeTokens.CreateSecondaryButton("Обновить сводки");
        refreshBtn.Click += async (_, _) =>
        {
            await _moduleManager.RefreshAllSummariesAsync();
            Refresh();
        };

        var headerRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        var titleStack = new StackPanel { Spacing = 6, Children = { title, _statsText } };
        Grid.SetColumn(titleStack, 0);
        Grid.SetColumn(refreshBtn, 1);
        refreshBtn.VerticalAlignment = VerticalAlignment.Top;
        headerRow.Children.Add(titleStack);
        headerRow.Children.Add(refreshBtn);

        _cardsHost = new StackPanel { Spacing = 12 };

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(28, 24),
                Spacing = 18,
                Children = { headerRow, _cardsHost }
            }
        };

        Refresh();
    }

    public void Refresh()
    {
        var modules = _moduleManager.Modules;
        var loadedCount = modules.Count(m => m.State == ModuleRuntimeState.Loaded);
        var platform = PulseContractVersion.GetCurrentPlatform();
        _statsText.Text = $"Платформа: {platform} • Активных модулей: {loadedCount} • Контракт PCPulse v{PulseContractVersion.Current}";

        _cardsHost.Children.Clear();
        if (modules.Count == 0)
        {
            var emptyStack = new StackPanel { Spacing = 12 };
            emptyStack.Children.Add(new TextBlock
            {
                Text = "Модули ещё не установлены",
                FontSize = 17,
                FontWeight = FontWeight.SemiBold,
                Foreground = ThemeTokens.TextPrimary
            });
            emptyStack.Children.Add(new TextBlock
            {
                Text = "Перейдите в раздел «Модули», чтобы установить MemPulse, DiskPulse или подключить локальный пакет .zip.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeTokens.TextMuted
            });
            var goModules = ThemeTokens.CreatePrimaryButton("Открыть каталог модулей");
            goModules.Click += (_, _) => _openSection("nav:modules");
            emptyStack.Children.Add(goModules);
            _cardsHost.Children.Add(ThemeTokens.CreateCard(emptyStack));
            return;
        }

        foreach (var handle in modules)
        {
            var cardGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 16
            };

            var info = new StackPanel { Spacing = 5 };
            info.Children.Add(new TextBlock
            {
                Text = $"{handle.Record.Name}  v{handle.Record.Version}",
                FontSize = 17,
                FontWeight = FontWeight.SemiBold,
                Foreground = ThemeTokens.TextPrimary
            });

            var statusBrush = handle.State switch
            {
                ModuleRuntimeState.Loaded => handle.LastSummary?.Severity switch
                {
                    PulseStatusSeverity.Warning => ThemeTokens.WarningBrush,
                    PulseStatusSeverity.Attention => ThemeTokens.DangerBrush,
                    _ => ThemeTokens.SuccessBrush
                },
                ModuleRuntimeState.Faulted => ThemeTokens.DangerBrush,
                _ => ThemeTokens.TextMuted
            };

            var statusLine = handle.State switch
            {
                ModuleRuntimeState.Loaded => handle.LastSummary?.Headline ?? "Модуль готов к работе",
                ModuleRuntimeState.Disabled => "Отключён пользователем",
                ModuleRuntimeState.Faulted => $"Ошибка: {handle.ErrorMessage}",
                ModuleRuntimeState.IncompatiblePlatform => $"Несовместим с ОС: {handle.ErrorMessage}",
                ModuleRuntimeState.IncompatibleContract => $"Несовместим с версией контракта: {handle.ErrorMessage}",
                _ => "Неизвестный статус"
            };

            info.Children.Add(new TextBlock
            {
                Text = statusLine,
                Foreground = statusBrush,
                TextWrapping = TextWrapping.Wrap
            });

            if (!string.IsNullOrWhiteSpace(handle.LastSummary?.Details))
            {
                info.Children.Add(new TextBlock
                {
                    Text = handle.LastSummary.Details,
                    Foreground = ThemeTokens.TextMuted,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            Grid.SetColumn(info, 0);
            cardGrid.Children.Add(info);

            if (handle.State == ModuleRuntimeState.Loaded)
            {
                var openBtn = ThemeTokens.CreatePrimaryButton("Открыть");
                openBtn.VerticalAlignment = VerticalAlignment.Center;
                openBtn.Click += (_, _) => _openSection($"module:{handle.Record.Id}");
                Grid.SetColumn(openBtn, 1);
                cardGrid.Children.Add(openBtn);
            }
            else
            {
                var manageBtn = ThemeTokens.CreateSecondaryButton("Управление");
                manageBtn.VerticalAlignment = VerticalAlignment.Center;
                manageBtn.Click += (_, _) => _openSection("nav:modules");
                Grid.SetColumn(manageBtn, 1);
                cardGrid.Children.Add(manageBtn);
            }

            _cardsHost.Children.Add(ThemeTokens.CreateCard(cardGrid));
        }
    }
}
