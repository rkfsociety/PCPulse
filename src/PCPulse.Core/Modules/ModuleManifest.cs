using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PCPulse.Contracts;
using PCPulse.Core.Security;

namespace PCPulse.Core.Modules;

/// <summary>
/// Описание и строгая валидация файла <c>module.json</c>.
/// </summary>
public sealed partial class ModuleManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private ModuleManifest(
        string id,
        string name,
        string description,
        Version version,
        string contractVersion,
        PulsePlatform supportedPlatforms,
        string entryAssembly,
        string entryType,
        string? windowsHelper,
        string? linuxHelper)
    {
        Id = id;
        Name = name;
        Description = description;
        Version = version;
        ContractVersion = contractVersion;
        SupportedPlatforms = supportedPlatforms;
        EntryAssembly = entryAssembly;
        EntryType = entryType;
        WindowsHelper = windowsHelper;
        LinuxHelper = linuxHelper;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public Version Version { get; }
    public string ContractVersion { get; }
    public PulsePlatform SupportedPlatforms { get; }
    public string EntryAssembly { get; }
    public string EntryType { get; }
    public string? WindowsHelper { get; }
    public string? LinuxHelper { get; }

    public bool HasAnyHelper => !string.IsNullOrEmpty(WindowsHelper) || !string.IsNullOrEmpty(LinuxHelper);

    public string? GetHelperForPlatform(PulsePlatform platform) => platform switch
    {
        PulsePlatform.Windows => WindowsHelper,
        PulsePlatform.Linux => LinuxHelper,
        _ => null
    };

    public static ModuleManifest Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new FormatException("Манифест module.json пуст.");

        ModuleManifestDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ModuleManifestDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Некорректный JSON в module.json: {ex.Message}", ex);
        }

        if (dto is null)
            throw new FormatException("Не удалось разобрать module.json.");

        if (string.IsNullOrWhiteSpace(dto.Id) || !ModuleIdRegex().IsMatch(dto.Id))
            throw new FormatException($"Некорректный id модуля «{dto.Id}». Ожидается строчная латиница, цифры и дефис (2–49 символов).");

        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new FormatException("Поле name в module.json обязательно.");

        if (string.IsNullOrWhiteSpace(dto.Version) || !Version.TryParse(dto.Version.Trim(), out var parsedVersion))
            throw new FormatException($"Некорректная версия модуля «{dto.Version}».");

        if (string.IsNullOrWhiteSpace(dto.ContractVersion))
            throw new FormatException("Поле contractVersion в module.json обязательно.");

        // Проверяем только формат MAJOR.MINOR; совместимость с текущим хостом проверяется отдельно.
        var contractParts = dto.ContractVersion.Trim().Split('.');
        if (contractParts.Length != 2 ||
            !int.TryParse(contractParts[0], out var maj) || maj < 0 ||
            !int.TryParse(contractParts[1], out var min) || min < 0)
        {
            throw new FormatException($"Некорректный формат contractVersion «{dto.ContractVersion}». Ожидается MAJOR.MINOR.");
        }

        if (dto.Platforms is null || dto.Platforms.Count == 0)
            throw new FormatException("Поле platforms в module.json должно содержать хотя бы одну ОС.");

        var platforms = PulsePlatform.None;
        foreach (var raw in dto.Platforms)
        {
            switch (raw?.Trim().ToLowerInvariant())
            {
                case "windows":
                    platforms |= PulsePlatform.Windows;
                    break;
                case "linux":
                    platforms |= PulsePlatform.Linux;
                    break;
                default:
                    throw new FormatException($"Неизвестная платформа в module.json: «{raw}».");
            }
        }

        var entryAssembly = PathGuard.ValidateManifestRelativePath(dto.EntryAssembly, "entryAssembly");
        if (string.IsNullOrWhiteSpace(dto.EntryType))
            throw new FormatException("Поле entryType в module.json обязательно.");

        string? winHelper = null;
        string? linuxHelper = null;
        if (dto.HelperExecutable is not null)
        {
            if (!string.IsNullOrWhiteSpace(dto.HelperExecutable.Windows))
                winHelper = PathGuard.ValidateManifestRelativePath(dto.HelperExecutable.Windows, "helperExecutable.windows");
            if (!string.IsNullOrWhiteSpace(dto.HelperExecutable.Linux))
                linuxHelper = PathGuard.ValidateManifestRelativePath(dto.HelperExecutable.Linux, "helperExecutable.linux");
        }

        return new ModuleManifest(
            dto.Id,
            dto.Name.Trim(),
            dto.Description?.Trim() ?? string.Empty,
            parsedVersion,
            $"{maj}.{min}",
            platforms,
            entryAssembly,
            dto.EntryType.Trim(),
            winHelper,
            linuxHelper);
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{1,48}$")]
    private static partial Regex ModuleIdRegex();

    private sealed class ModuleManifestDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("contractVersion")] public string? ContractVersion { get; set; }
        [JsonPropertyName("platforms")] public List<string>? Platforms { get; set; }
        [JsonPropertyName("entryAssembly")] public string? EntryAssembly { get; set; }
        [JsonPropertyName("entryType")] public string? EntryType { get; set; }
        [JsonPropertyName("helperExecutable")] public HelperDto? HelperExecutable { get; set; }
    }

    private sealed class HelperDto
    {
        [JsonPropertyName("windows")] public string? Windows { get; set; }
        [JsonPropertyName("linux")] public string? Linux { get; set; }
    }
}
