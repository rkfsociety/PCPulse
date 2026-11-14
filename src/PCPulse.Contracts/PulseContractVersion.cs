using System.Globalization;

namespace PCPulse.Contracts;

/// <summary>
/// Вспомогательные проверки версии контракта и текущей платформы.
/// </summary>
public static class PulseContractVersion
{
    /// <summary>
    /// Текущая поддерживаемая версия контракта в формате <c>MAJOR.MINOR</c>.
    /// </summary>
    public const string Current = "1.0";

    /// <summary>
    /// Старший компонент текущей версии контракта.
    /// </summary>
    public const int CurrentMajor = 1;

    /// <summary>
    /// Младший компонент текущей версии контракта.
    /// </summary>
    public const int CurrentMinor = 0;

    /// <summary>
    /// Проверяет совместимость версии контракта модуля с текущей версией хоста.
    /// Совместимыми считаются версии формата <c>MAJOR.MINOR</c>, где <c>MAJOR == CurrentMajor</c> и <c>MINOR &lt;= CurrentMinor</c>.
    /// </summary>
    public static bool IsCompatible(string? moduleContractVersion, out string? errorReason)
    {
        if (string.IsNullOrWhiteSpace(moduleContractVersion))
        {
            errorReason = "Версия контракта модуля не указана.";
            return false;
        }

        var parts = moduleContractVersion.Trim().Split('.', StringSplitOptions.None);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            errorReason = $"Некорректный формат версии контракта «{moduleContractVersion}». Ожидается MAJOR.MINOR.";
            return false;
        }

        if (major != CurrentMajor)
        {
            errorReason = $"Несовместимая основная версия контракта модуля {major}.{minor} (хост поддерживает {CurrentMajor}.x).";
            return false;
        }

        if (minor > CurrentMinor)
        {
            errorReason = $"Модуль требует более новую версию контракта {major}.{minor} (хост поддерживает до {Current}).";
            return false;
        }

        errorReason = null;
        return true;
    }

    /// <summary>
    /// Возвращает текущую операционную систему как <see cref="PulsePlatform"/>.
    /// </summary>
    public static PulsePlatform GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
            return PulsePlatform.Windows;
        if (OperatingSystem.IsLinux())
            return PulsePlatform.Linux;
        return PulsePlatform.None;
    }
}
