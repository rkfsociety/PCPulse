using System.Diagnostics;
using System.Text;
using PCPulse.Contracts;
using PCPulse.Core.Modules;
using PCPulse.Core.Security;

namespace PCPulse.Core.Elevation;

/// <summary>
/// Запускает исключительно helper-исполняемый файл, объявленный в <c>module.json</c> текущего модуля.
/// </summary>
public sealed class ProcessElevationService : IPulseElevationService
{
    private readonly string _moduleInstallDirectory;
    private readonly ModuleManifest _manifest;

    public ProcessElevationService(string moduleInstallDirectory, ModuleManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleInstallDirectory);
        ArgumentNullException.ThrowIfNull(manifest);
        _moduleInstallDirectory = Path.GetFullPath(moduleInstallDirectory);
        _manifest = manifest;
    }

    public bool IsElevated => Environment.IsPrivilegedProcess;

    public async Task<int> RunElevatedHelperAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        var platform = PulseContractVersion.GetCurrentPlatform();
        var psi = BuildStartInfo(args, platform);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Не удалось запустить helper-процесс модуля.");

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    internal ProcessStartInfo BuildStartInfo(IReadOnlyList<string> args, PulsePlatform platform)
    {
        var relativeHelper = _manifest.GetHelperForPlatform(platform);
        if (string.IsNullOrWhiteSpace(relativeHelper))
        {
            throw new InvalidOperationException(
                $"Модуль «{_manifest.Id}» не объявил helperExecutable для платформы {platform} в module.json.");
        }

        var fullHelperPath = PathGuard.ResolveSafeChildPath(_moduleInstallDirectory, relativeHelper);
        if (!File.Exists(fullHelperPath))
        {
            throw new FileNotFoundException(
                $"Объявленный helper-файл модуля «{_manifest.Id}» не найден.",
                fullHelperPath);
        }

        if (platform == PulsePlatform.Windows)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fullHelperPath,
                WorkingDirectory = _moduleInstallDirectory,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = BuildWindowsArguments(args)
            };
            if (!IsElevated)
                psi.Verb = "runas";
            return psi;
        }

        if (platform == PulsePlatform.Linux)
        {
            var psi = new ProcessStartInfo
            {
                WorkingDirectory = _moduleInstallDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (IsElevated)
            {
                psi.FileName = fullHelperPath;
            }
            else
            {
                psi.FileName = "pkexec";
                psi.ArgumentList.Add(fullHelperPath);
            }

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            return psi;
        }

        throw new PlatformNotSupportedException($"Платформа {platform} не поддерживается для elevated helper.");
    }

    private static string BuildWindowsArguments(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        for (var i = 0; i < args.Count; i++)
        {
            if (i > 0)
                sb.Append(' ');
            sb.Append(QuoteWindowsArg(args[i]));
        }
        return sb.ToString();
    }

    private static string QuoteWindowsArg(string arg)
    {
        if (string.IsNullOrEmpty(arg))
            return "\"\"";
        if (!arg.Any(c => char.IsWhiteSpace(c) || c == '"'))
            return arg;

        var sb = new StringBuilder();
        sb.Append('"');
        var backslashes = 0;
        foreach (var ch in arg)
        {
            if (ch == '\\')
            {
                backslashes++;
            }
            else if (ch == '"')
            {
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
                backslashes = 0;
            }
            else
            {
                if (backslashes > 0)
                {
                    sb.Append('\\', backslashes);
                    backslashes = 0;
                }
                sb.Append(ch);
            }
        }

        if (backslashes > 0)
            sb.Append('\\', backslashes * 2);
        sb.Append('"');
        return sb.ToString();
    }
}
