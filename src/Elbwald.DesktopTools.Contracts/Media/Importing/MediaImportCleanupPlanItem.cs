namespace Elbwald.DesktopTools.Contracts.Media.Importing;

public sealed record MediaImportCleanupPlanItem(
    string SourcePath,
    string DestinationPath,
    string RelativePath,
    long Length,
    string Sha256Hex);
