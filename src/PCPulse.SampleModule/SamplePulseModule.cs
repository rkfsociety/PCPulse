using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PCPulse.Contracts;

namespace PCPulse.SampleModule;

public sealed class SamplePulseModule : IPulseModule
{
    private IPulseHostContext? _context;
    private int _summaryCounter;

    public PulseModuleDescriptor Descriptor { get; } = new(
        Id: "sample-module",
        DisplayName: "Образец модуля",
        Description: "Эталонный модуль для проверки контракта PCPulse 1.0",
        ModuleVersion: new Version(1, 0, 0),
        ContractVersion: PulseContractVersion.Current,
        SupportedPlatforms: PulsePlatform.All,
        RequiresElevationForSomeActions: false);

    public bool WasDisposed { get; private set; }

    public async Task InitializeAsync(IPulseHostContext context, CancellationToken cancellationToken = default)
    {
        _context = context;
        _context.Logger.Info("Эталонный модуль инициализирован.");
        var saved = await _context.Settings.LoadAsync<SampleSettings>("state", cancellationToken).ConfigureAwait(false);
        _summaryCounter = saved?.Counter ?? 0;
    }

    public Control CreateView()
    {
        var title = new TextBlock
        {
            Text = "Эталонный модуль PCPulse",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White
        };

        var pingButton = new Button
        {
            Content = "Проверить диалог и сохранить счётчик",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        pingButton.Click += async (_, _) =>
        {
            if (_context is null) return;
            _summaryCounter++;
            await _context.Settings.SaveAsync("state", new SampleSettings(_summaryCounter));
            _context.RequestSummaryRefresh();
            await _context.Dialogs.ShowInfoAsync("Эталонный модуль", $"Счётчик сохранён: {_summaryCounter}");
        };

        return new UserControl
        {
            Content = new StackPanel
            {
                Spacing = 12,
                Children = { title, pingButton }
            }
        };
    }

    public Task<PulseStatusSummary?> GetSummaryAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<PulseStatusSummary?>(new PulseStatusSummary(
            $"Эталонный модуль активен ({_summaryCounter})",
            $"Версия контракта: {Descriptor.ContractVersion}",
            PulseStatusSeverity.Normal));

    public ValueTask DisposeAsync()
    {
        WasDisposed = true;
        _context?.Logger.Info("Эталонный модуль выгружен.");
        return ValueTask.CompletedTask;
    }

    private sealed record SampleSettings(int Counter);
}
