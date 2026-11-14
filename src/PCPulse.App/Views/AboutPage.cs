using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PCPulse.App.Services;
using PCPulse.Contracts;
using PCPulse.Core.Modules;
using PCPulse.Core.Platform;

namespace PCPulse.App.Views;

internal sealed class AboutPage : UserControl
{
    public AboutPage(Version hostVersion, PulsePaths paths, ModuleManager moduleManager)
    {
        var stack = new StackPanel
        {
            Margin = new Thickness(28, 24),
            Spacing = 14
        };

        stack.Children.Add(new TextBlock
        {
            Text = "О программе PCPulse",
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
            Foreground = ThemeTokens.TextPrimary
        });

        var infoStack = new StackPanel { Spacing = 8 };
        infoStack.Children.Add(Line($"Версия оболочки: {hostVersion}"));
        infoStack.Children.Add(Line($"Версия контракта модулей: {PulseContractVersion.Current}"));
        infoStack.Children.Add(Line($"Текущая ОС: {PulseContractVersion.GetCurrentPlatform()}"));
        infoStack.Children.Add(Line($"Режим запуска: {(Environment.IsPrivilegedProcess ? "С повышенными правами" : "Обычный пользователь (рекомендуется)")}"));
        infoStack.Children.Add(Line($"Каталог данных: {paths.DataRoot}"));
        infoStack.Children.Add(Line($"Установлено модулей: {moduleManager.Modules.Count}"));

        stack.Children.Add(ThemeTokens.CreateCard(infoStack));
        stack.Children.Add(ThemeTokens.CreateCard(new TextBlock
        {
            Text = "PCPulse объединяет утилиты экосистемы *Pulse (MemPulse, DiskPulse и др.) в одном окне без дочерних окон и без постоянного запроса прав администратора.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeTokens.TextMuted
        }));

        Content = new ScrollViewer { Content = stack };
    }

    private static TextBlock Line(string text) => new()
    {
        Text = text,
        Foreground = ThemeTokens.TextPrimary,
        TextWrapping = TextWrapping.Wrap
    };
}
