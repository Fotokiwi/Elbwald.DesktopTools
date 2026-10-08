using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Media.Dates;

namespace Elbwald.DesktopTools.Contracts.Media.Sorting;

public sealed record MediaSortOptions
{
    public static MediaSortOptions Default { get; } = new();

    public MediaSortRule Rule { get; init; } =
        MediaSortRule.YearMonth;

    public FileOperationKind OperationKind { get; init; } =
        FileOperationKind.Copy;

    public MediaDateConfidence MinimumDateConfidence { get; init; } =
        MediaDateConfidence.High;
}
