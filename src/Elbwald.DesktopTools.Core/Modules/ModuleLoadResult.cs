namespace Elbwald.DesktopTools.Core.Modules;

public sealed record ModuleLoadResult(
    string ModuleDirectory,
    string? ModuleId,
    bool Success,
    string Message);
