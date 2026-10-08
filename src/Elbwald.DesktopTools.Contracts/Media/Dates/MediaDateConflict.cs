namespace Elbwald.DesktopTools.Contracts.Media.Dates;

public sealed record MediaDateConflict(
    MediaDateSource PrimarySource,
    MediaDateSource ConflictingSource,
    string Message);
