using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCPulse.Contracts;

namespace PCPulse.Core.Catalog;

/// <summary>
/// Загружает встроенный реестр официальных модулей экосистемы PCPulse.
/// </summary>
public static class ModuleCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<CatalogModuleEntry> LoadBuiltIn()
    {
        var assembly = typeof(ModuleCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream("PCPulse.Core.Catalog.catalog.json")
            ?? throw new InvalidOperationException("Встроенный ресурс catalog.json не найден.");

        var items = JsonSerializer.Deserialize<List<CatalogItemDto>>(stream, JsonOptions) ?? [];
        var result = new List<CatalogModuleEntry>(items.Count);

        foreach (var item in items)
        {
            var platforms = PulsePlatform.None;
            if (item.Platforms is not null)
            {
                foreach (var p in item.Platforms)
                {
                    if (string.Equals(p, "windows", StringComparison.OrdinalIgnoreCase))
                        platforms |= PulsePlatform.Windows;
                    else if (string.Equals(p, "linux", StringComparison.OrdinalIgnoreCase))
                        platforms |= PulsePlatform.Linux;
                }
            }

            result.Add(new CatalogModuleEntry(
                item.Id ?? string.Empty,
                item.Name ?? string.Empty,
                item.Description ?? string.Empty,
                item.Repository ?? string.Empty,
                platforms));
        }

        return result;
    }

    private sealed class CatalogItemDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("repository")] public string? Repository { get; set; }
        [JsonPropertyName("platforms")] public List<string>? Platforms { get; set; }
    }
}
