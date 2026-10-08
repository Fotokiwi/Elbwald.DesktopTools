using Elbwald.DesktopTools.Contracts.Media.Dates;

namespace Elbwald.DesktopTools.Contracts.Media.Sorting;

public sealed record MediaSortPlanItem(
    int OperationIndex,
    MediaFile File,
    DateTime CapturedAt,
    MediaDateResolution DateResolution,
    string RelativeDirectory,
    string DestinationPath);
