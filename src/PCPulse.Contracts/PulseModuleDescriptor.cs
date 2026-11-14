namespace PCPulse.Contracts;

/// <summary>
/// Описание модуля экосистемы PCPulse.
/// </summary>
/// <param name="Id">Уникальный строковый идентификатор в нижнем регистре (например, <c>mempulse</c>).</param>
/// <param name="DisplayName">Отображаемое название модуля в интерфейсе.</param>
/// <param name="Description">Краткое описание возможностей модуля.</param>
/// <param name="ModuleVersion">Версия модуля.</param>
/// <param name="ContractVersion">Целевая версия контракта PCPulse в формате <c>MAJOR.MINOR</c> (например, <c>1.0</c>).</param>
/// <param name="SupportedPlatforms">Поддерживаемые операционные системы.</param>
/// <param name="RequiresElevationForSomeActions">Признак того, что отдельные действия модуля требуют повышенных прав через helper.</param>
public sealed record PulseModuleDescriptor(
    string Id,
    string DisplayName,
    string Description,
    Version ModuleVersion,
    string ContractVersion,
    PulsePlatform SupportedPlatforms,
    bool RequiresElevationForSomeActions = false);
