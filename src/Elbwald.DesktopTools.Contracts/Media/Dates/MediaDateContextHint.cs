namespace Elbwald.DesktopTools.Contracts.Media.Dates;

public sealed record MediaDateContextHint(
    MediaDateContextHintKind Kind,
    MediaDateContextStrength Strength,
    string DirectoryPath,
    string CameraLabel,
    MediaDateSource PrimarySource,
    MediaDateSource ComparedSource,
    TimeSpan DominantDifference,
    int MatchingFileCount,
    int ComparableFileCount,
    int OutlierCount,
    double CoverageRatio,
    string Message);
