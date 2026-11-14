using PCPulse.Contracts;

namespace PCPulse.Core.Catalog;

public sealed record CatalogModuleEntry(
    string Id,
    string Name,
    string Description,
    string Repository,
    PulsePlatform Platforms);
