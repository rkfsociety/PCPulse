namespace PCPulse.Contracts;

/// <summary>
/// Целевые операционные системы, поддерживаемые модулем PCPulse.
/// </summary>
[Flags]
public enum PulsePlatform
{
    /// <summary>
    /// Не задано ни одной поддерживаемой платформы.
    /// </summary>
    None = 0,

    /// <summary>
    /// Microsoft Windows.
    /// </summary>
    Windows = 1,

    /// <summary>
    /// Настольный Linux (X11 / Wayland).
    /// </summary>
    Linux = 2,

    /// <summary>
    /// Все поддерживаемые платформы (Windows и Linux).
    /// </summary>
    All = Windows | Linux
}
