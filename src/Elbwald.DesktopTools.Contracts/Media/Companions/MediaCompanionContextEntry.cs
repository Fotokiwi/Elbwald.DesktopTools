using Elbwald.DesktopTools.Contracts.Media.Dates;

namespace Elbwald.DesktopTools.Contracts.Media.Companions;

public sealed record MediaCompanionContextEntry(
    MediaAnalyzedFile File,
    MediaDateResolution? DateResolution,
    string? PlannedRelativeDirectory,
    string? PlannedDestinationPath);
