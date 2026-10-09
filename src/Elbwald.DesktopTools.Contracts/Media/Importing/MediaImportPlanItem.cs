namespace Elbwald.DesktopTools.Contracts.Media.Importing;

public sealed record MediaImportPlanItem(
    string SourcePath,
    string DestinationPath,
    string RelativePath,
    long Length,
    MediaImportPlanItemState State,
    string Message);
