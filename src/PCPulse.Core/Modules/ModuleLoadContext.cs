using System.Reflection;
using System.Runtime.Loader;

namespace PCPulse.Core.Modules;

/// <summary>
/// Изолированный <see cref="AssemblyLoadContext"/> для подключаемого модуля.
/// Сборки контракта и Avalonia всегда берутся из контекста хоста, чтобы типы UI и интерфейсов совпадали.
/// </summary>
internal sealed class ModuleLoadContext : AssemblyLoadContext
{
    private static readonly string[] SharedAssemblyNames =
    [
        "PCPulse.Contracts",
        "Avalonia",
        "MicroCom.Runtime",
        "SkiaSharp",
        "HarfBuzzSharp",
        "Tmds.DBus.Protocol"
    ];

    private static readonly string[] SharedAssemblyPrefixes =
    [
        "Avalonia.",
        "SkiaSharp.",
        "HarfBuzzSharp.",
        "Tmds.DBus."
    ];

    private readonly AssemblyDependencyResolver _resolver;

    public ModuleLoadContext(string moduleId, string entryAssemblyPath)
        : base($"PCPulse.Module.{moduleId}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (!string.IsNullOrEmpty(name) && IsHostSharedAssembly(name))
            return null;

        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is not null ? LoadFromAssemblyPath(assemblyPath) : null;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath is not null ? LoadUnmanagedDllFromPath(libraryPath) : nint.Zero;
    }

    private static bool IsHostSharedAssembly(string name)
    {
        foreach (var exact in SharedAssemblyNames)
        {
            if (name.Equals(exact, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var prefix in SharedAssemblyPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
