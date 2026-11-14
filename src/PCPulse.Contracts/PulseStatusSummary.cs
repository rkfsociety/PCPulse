namespace PCPulse.Contracts;

/// <summary>
/// Уровень важности краткой сводки модуля на главной странице.
/// </summary>
public enum PulseStatusSeverity
{
    /// <summary>
    /// Штатное состояние.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Информационное уведомление.
    /// </summary>
    Info = 1,

    /// <summary>
    /// Предупреждение, требующее внимания пользователя.
    /// </summary>
    Warning = 2,

    /// <summary>
    /// Требуется действие или выявлена проблема.
    /// </summary>
    Attention = 3
}

/// <summary>
/// Краткая сводка состояния модуля для отображения в разделе «Обзор».
/// </summary>
/// <param name="Headline">Основная строка состояния (например, «ОЗУ: 54 %»).</param>
/// <param name="Details">Дополнительные пояснения.</param>
/// <param name="Severity">Уровень важности.</param>
public sealed record PulseStatusSummary(
    string Headline,
    string? Details = null,
    PulseStatusSeverity Severity = PulseStatusSeverity.Normal);
