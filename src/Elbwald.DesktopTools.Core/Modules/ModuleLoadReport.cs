namespace Elbwald.DesktopTools.Core.Modules;

public sealed class ModuleLoadReport : IModuleLoadReport
{
    private IReadOnlyList<ModuleLoadResult> _results = Array.Empty<ModuleLoadResult>();
    private IReadOnlyList<ModuleLoadResult> _failures = Array.Empty<ModuleLoadResult>();

    public IReadOnlyList<ModuleLoadResult> Results => _results;

    public IReadOnlyList<ModuleLoadResult> Failures => _failures;

    public void Replace(IEnumerable<ModuleLoadResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var snapshot = results.ToArray();

        _results = snapshot;
        _failures = snapshot
            .Where(result => !result.Success)
            .ToArray();
    }
}
