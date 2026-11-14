using System.Text;
using PCPulse.Contracts;

namespace PCPulse.Core.Logging;

/// <summary>
/// Потокобезопасный ротируемый журнал событий (по умолчанию до 1 МБ + один архив <c>.1</c>).
/// </summary>
public sealed class PulseFileLogger : IPulseLogger
{
    private readonly string _logFilePath;
    private readonly string _archiveFilePath;
    private readonly string _source;
    private readonly long _maxBytes;
    private readonly Lock _sync = new();

    public PulseFileLogger(string logsDirectory, string source, long maxBytes = 1_048_576)
    {
        if (string.IsNullOrWhiteSpace(logsDirectory))
            throw new ArgumentException("Каталог логов не задан.", nameof(logsDirectory));

        Directory.CreateDirectory(logsDirectory);
        _logFilePath = Path.Combine(logsDirectory, "app.log");
        _archiveFilePath = Path.Combine(logsDirectory, "app.log.1");
        _source = string.IsNullOrWhiteSpace(source) ? "host" : source;
        _maxBytes = Math.Max(64, maxBytes);
    }

    public string LogFilePath => _logFilePath;

    public PulseFileLogger ForSource(string source) =>
        new(Path.GetDirectoryName(_logFilePath)!, source, _maxBytes);

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var line = exception is null
            ? $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{_source}] {message}{Environment.NewLine}"
            : $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{_source}] {message} | {exception}{Environment.NewLine}";

        var bytes = Encoding.UTF8.GetByteCount(line);
        lock (_sync)
        {
            try
            {
                if (File.Exists(_logFilePath))
                {
                    var info = new FileInfo(_logFilePath);
                    if (info.Length + bytes > _maxBytes)
                    {
                        if (File.Exists(_archiveFilePath))
                            File.Delete(_archiveFilePath);
                        File.Move(_logFilePath, _archiveFilePath);
                    }
                }

                File.AppendAllText(_logFilePath, line, Encoding.UTF8);
            }
            catch
            {
                // Ошибка записи в журнал не должна ронять приложение.
            }
        }
    }
}
