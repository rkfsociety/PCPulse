namespace PCPulse.Contracts;

/// <summary>
/// Журнал событий модуля, записываемый в общий лог PCPulse.
/// </summary>
public interface IPulseLogger
{
    /// <summary>
    /// Записывает информационное сообщение.
    /// </summary>
    void Info(string message);

    /// <summary>
    /// Записывает предупреждение.
    /// </summary>
    void Warn(string message);

    /// <summary>
    /// Записывает ошибку и опциональное исключение.
    /// </summary>
    void Error(string message, Exception? exception = null);
}
