namespace PCPulse.Core.Security;

/// <summary>
/// Проверка и безопасное разрешение относительных путей внутри разрешённого корня.
/// </summary>
public static class PathGuard
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Разрешает относительный путь строго внутри <paramref name="rootDirectory"/>, отклоняя абсолютные пути и выход за пределы корня (<c>..</c>).
    /// </summary>
    public static string ResolveSafeChildPath(string rootDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Корневой каталог не задан.", nameof(rootDirectory));
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new UnauthorizedAccessException("Относительный путь не должен быть пустым.");

        var normalizedRelative = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(normalizedRelative) ||
            normalizedRelative.StartsWith(Path.DirectorySeparatorChar))
        {
            throw new UnauthorizedAccessException($"Запрещён абсолютный путь: «{relativePath}».");
        }

        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        var fullTarget = Path.GetFullPath(Path.Combine(fullRoot, normalizedRelative));
        var expectedPrefix = fullRoot + Path.DirectorySeparatorChar;

        if (!fullTarget.StartsWith(expectedPrefix, PathComparison))
        {
            throw new UnauthorizedAccessException($"Путь «{relativePath}» выходит за пределы разрешённого каталога.");
        }

        return fullTarget;
    }

    /// <summary>
    /// Проверяет, что строковое поле манифеста является безопасным относительным путём внутри пакета модуля.
    /// </summary>
    public static string ValidateManifestRelativePath(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new FormatException($"Поле манифеста «{fieldName}» не должно быть пустым.");

        var trimmed = value.Trim();
        if (trimmed.Contains(':') ||
            trimmed.StartsWith('/') ||
            trimmed.StartsWith('\\') ||
            Path.IsPathRooted(trimmed))
        {
            throw new FormatException($"Поле «{fieldName}» должно быть относительным путём: «{value}».");
        }

        var segments = trimmed.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or ".."))
        {
            throw new FormatException($"Поле «{fieldName}» содержит недопустимые сегменты пути: «{value}».");
        }

        return trimmed.Replace('\\', '/');
    }
}
