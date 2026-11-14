namespace PCPulse.Contracts;

/// <summary>
/// Изолированное JSON-хранилище настроек модуля в каталоге <see cref="IPulseHostContext.ModuleDataDirectory"/>.
/// </summary>
public interface IPulseSettingsStore
{
    /// <summary>
    /// Загружает объект настроек по безопасному имени ключа (или возвращает <c>null</c>, если ключ ещё не сохранён).
    /// </summary>
    Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Сохраняет объект настроек под заданным безопасным ключом.
    /// </summary>
    Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Удаляет сохранённые настройки по ключу, если они существуют.
    /// </summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
