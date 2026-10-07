namespace Elbwald.DesktopTools.Core.Modules;

public interface IModuleLoadReport
{
    IReadOnlyList<ModuleLoadResult> Results { get; }

    IReadOnlyList<ModuleLoadResult> Failures { get; }

    void Replace(IEnumerable<ModuleLoadResult> results);
}
