namespace PCPulse.Core.Platform;

/// <summary>
/// Вычисляет стандартные пользовательские каталоги данных, модулей и журналов PCPulse.
/// </summary>
public sealed class PulsePaths
{
    public PulsePaths(string? rootOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(rootOverride))
        {
            DataRoot = Path.GetFullPath(rootOverride);
            LogsDirectory = Path.Combine(DataRoot, "logs");
        }
        else if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            DataRoot = Path.Combine(localAppData, "PCPulse");
            LogsDirectory = Path.Combine(DataRoot, "Logs");
        }
        else
        {
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrWhiteSpace(dataHome))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                dataHome = Path.Combine(home, ".local", "share");
            }

            var stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            if (string.IsNullOrWhiteSpace(stateHome))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                stateHome = Path.Combine(home, ".local", "state");
            }

            DataRoot = Path.Combine(dataHome, "pcpulse");
            LogsDirectory = Path.Combine(stateHome, "pcpulse", "logs");
        }

        ModulesDirectory = Path.Combine(DataRoot, "modules");
        ModuleDataRoot = Path.Combine(DataRoot, "data");
        TempDirectory = Path.Combine(DataRoot, "temp");
        InstalledModulesFile = Path.Combine(DataRoot, "installed-modules.json");
        PendingDeletesFile = Path.Combine(DataRoot, "pending-deletes.json");
        HostSettingsFile = Path.Combine(DataRoot, "settings.json");
    }

    public string DataRoot { get; }
    public string LogsDirectory { get; }
    public string ModulesDirectory { get; }
    public string ModuleDataRoot { get; }
    public string TempDirectory { get; }
    public string InstalledModulesFile { get; }
    public string PendingDeletesFile { get; }
    public string HostSettingsFile { get; }

    public string GetModuleDataDirectory(string moduleId) =>
        Path.Combine(ModuleDataRoot, moduleId);

    public string GetModuleVersionDirectory(string moduleId, Version version) =>
        Path.Combine(ModulesDirectory, moduleId, version.ToString());

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ModulesDirectory);
        Directory.CreateDirectory(ModuleDataRoot);
        Directory.CreateDirectory(TempDirectory);
    }
}
