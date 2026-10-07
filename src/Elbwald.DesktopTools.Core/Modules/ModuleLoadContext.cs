using System.Reflection;
using System.Runtime.Loader;

namespace Elbwald.DesktopTools.Core.Modules;

internal sealed class ModuleLoadContext : AssemblyLoadContext
{
    private static readonly string[] SharedAssemblyNames =
    [
        "Elbwald.DesktopTools.Contracts",
        "Elbwald.DesktopTools.UI",
        "CommunityToolkit.Mvvm"
    ];

    private readonly AssemblyDependencyResolver _resolver;

    public ModuleLoadContext(string moduleAssemblyPath)
        : base($"Module:{Path.GetFileNameWithoutExtension(moduleAssemblyPath)}", isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(moduleAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (ShouldUseDefaultContext(assemblyName))
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
        }

        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);

        return assemblyPath is null
            ? null
            : LoadFromAssemblyPath(assemblyPath);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);

        return libraryPath is null
            ? nint.Zero
            : LoadUnmanagedDllFromPath(libraryPath);
    }

    private static bool ShouldUseDefaultContext(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return SharedAssemblyNames.Contains(name, StringComparer.OrdinalIgnoreCase)
               || name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("Microsoft.Extensions.", StringComparison.OrdinalIgnoreCase);
    }
}
