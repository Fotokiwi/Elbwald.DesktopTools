namespace Elbwald.DesktopTools.Contracts.Media.Companions;

public sealed record MediaCompanionProjection(
    string SourcePath,
    string ProjectedDestinationPath,
    MediaCompanionKind Kind,
    bool DestinationAlreadyExists);
