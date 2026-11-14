namespace PCPulse.Contracts;

/// <summary>
/// Сервис запуска привилегированного helper-процесса, объявленного в <c>module.json</c>.
/// </summary>
public interface IPulseElevationService
{
    /// <summary>
    /// Возвращает <c>true</c>, если текущий процесс уже работает с правами администратора или root.
    /// </summary>
    bool IsElevated { get; }

    /// <summary>
    /// Запускает helper-исполняемый файл текущего модуля, объявленный в <c>module.json</c>, с повышением прав (UAC / <c>pkexec</c>).
    /// </summary>
    /// <param name="args">Аргументы командной строки, передаваемые helper-процессу.</param>
    /// <param name="cancellationToken">Токен отмены ожидания.</param>
    /// <returns>Код возврата helper-процесса.</returns>
    Task<int> RunElevatedHelperAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default);
}
