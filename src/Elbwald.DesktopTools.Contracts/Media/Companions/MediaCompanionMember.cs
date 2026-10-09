namespace Elbwald.DesktopTools.Contracts.Media.Companions;

public sealed record MediaCompanionMember(
    MediaAnalyzedFile File,
    MediaCompanionKind Kind,
    string CompanionStem,
    string? AssociatedImageFileName);
