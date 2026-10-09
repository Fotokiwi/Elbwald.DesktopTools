namespace Elbwald.DesktopTools.Contracts.Media.Importing;

public sealed record MediaImportPlanIssue(
    string Message,
    string? Path = null);
