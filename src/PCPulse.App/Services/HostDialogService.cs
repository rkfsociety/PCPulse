using PCPulse.Contracts;

namespace PCPulse.App.Services;

public sealed record HostDialogRequest(
    string Title,
    string Message,
    string ConfirmText,
    string? CancelText,
    bool IsDestructive);

/// <summary>
/// Потокобезопасный сервис встроенных диалогов в единственном окне PCPulse.
/// Последовательно отображает диалоги через переданный обработчик оверлея.
/// </summary>
public sealed class HostDialogService : IPulseDialogService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Func<HostDialogRequest, Task<bool>>? _presenter;

    public HostDialogService(Func<HostDialogRequest, Task<bool>>? presenter = null)
    {
        _presenter = presenter;
    }

    public void AttachPresenter(Func<HostDialogRequest, Task<bool>> presenter)
    {
        _presenter = presenter;
    }

    public Task ShowInfoAsync(string title, string message, CancellationToken cancellationToken = default) =>
        PresentAsync(new HostDialogRequest(title, message, "Понятно", null, false), cancellationToken);

    public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) =>
        PresentAsync(new HostDialogRequest(title, message, "Закрыть", null, true), cancellationToken);

    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText = "Продолжить",
        string cancelText = "Отмена",
        bool isDestructive = false,
        CancellationToken cancellationToken = default) =>
        PresentAsync(new HostDialogRequest(title, message, confirmText, cancelText, isDestructive), cancellationToken);

    private async Task<bool> PresentAsync(HostDialogRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var presenter = _presenter;
            if (presenter is null)
                return false;

            return await presenter(request).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
