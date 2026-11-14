namespace PCPulse.Contracts;

/// <summary>
/// Сервис отображения диалогов внутри главного окна PCPulse (без открытия дополнительных окон).
/// </summary>
public interface IPulseDialogService
{
    /// <summary>
    /// Показывает информационное сообщение во встроенном оверлее окна.
    /// </summary>
    Task ShowInfoAsync(string title, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Показывает сообщение об ошибке во встроенном оверлее окна.
    /// </summary>
    Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Запрашивает подтверждение действия у пользователя во встроенном оверлее окна.
    /// </summary>
    /// <returns><c>true</c>, если пользователь подтвердил действие; иначе <c>false</c>.</returns>
    Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText = "Продолжить",
        string cancelText = "Отмена",
        bool isDestructive = false,
        CancellationToken cancellationToken = default);
}
