namespace PCPulse.Contracts;

/// <summary>
/// Контекст хоста PCPulse, передаваемый модулю при инициализации.
/// </summary>
public interface IPulseHostContext
{
    /// <summary>
    /// Идентификатор текущего модуля.
    /// </summary>
    string ModuleId { get; }

    /// <summary>
    /// Выделенный каталог пользовательских данных и временных файлов модуля.
    /// </summary>
    string ModuleDataDirectory { get; }

    /// <summary>
    /// Каталог, из которого загружена текущая версия модуля.
    /// </summary>
    string ModuleInstallDirectory { get; }

    /// <summary>
    /// Текущая версия оболочки PCPulse.
    /// </summary>
    Version HostVersion { get; }

    /// <summary>
    /// Текущая версия контракта, поддерживаемая хостом (<c>MAJOR.MINOR</c>).
    /// </summary>
    string ContractVersion { get; }

    /// <summary>
    /// Сервис отображения встроенных диалогов в главном окне PCPulse.
    /// </summary>
    IPulseDialogService Dialogs { get; }

    /// <summary>
    /// Сервис запуска привилегированного helper-исполняемого файла модуля.
    /// </summary>
    IPulseElevationService Elevation { get; }

    /// <summary>
    /// Журнал модуля.
    /// </summary>
    IPulseLogger Logger { get; }

    /// <summary>
    /// Хранилище JSON-настроек модуля.
    /// </summary>
    IPulseSettingsStore Settings { get; }

    /// <summary>
    /// Уведомляет хост о необходимости обновить краткую сводку модуля на странице «Обзор».
    /// </summary>
    void RequestSummaryRefresh();
}
