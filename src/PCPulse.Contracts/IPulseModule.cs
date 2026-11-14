using Avalonia.Controls;

namespace PCPulse.Contracts;

/// <summary>
/// Точка входа подключаемого модуля экосистемы PCPulse.
/// Реализация должна иметь публичный конструктор без параметров.
/// </summary>
public interface IPulseModule : IAsyncDisposable
{
    /// <summary>
    /// Метаданные модуля.
    /// </summary>
    PulseModuleDescriptor Descriptor { get; }

    /// <summary>
    /// Инициализирует модуль один раз за сессию работы хоста.
    /// </summary>
    Task InitializeAsync(IPulseHostContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Создаёт корневой элемент интерфейса модуля (например, <see cref="UserControl"/>) для отображения внутри единственного окна PCPulse.
    /// Вызывается хостом один раз за сессию после <see cref="InitializeAsync"/>.
    /// Модуль не должен создавать отдельные <see cref="Window"/>.
    /// </summary>
    Control CreateView();

    /// <summary>
    /// Возвращает актуальную краткую сводку состояния модуля для раздела «Обзор», либо <c>null</c>.
    /// </summary>
    Task<PulseStatusSummary?> GetSummaryAsync(CancellationToken cancellationToken = default);
}
