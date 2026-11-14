namespace PCPulse.Core.Modules;

public sealed record InstalledModuleRecord(
    string Id,
    string Name,
    string Description,
    string Version,
    string ContractVersion,
    string InstallDirectory,
    bool Enabled);
