namespace Elbwald.DesktopTools.Contracts.Media.Sorting;

public sealed record MediaSortDateDecision(
    MediaSortDateDecisionKind Kind,
    DateTime? Value = null,
    string? Label = null)
{
    public static MediaSortDateDecision UseDate(
        DateTime value,
        string? label = null) =>
        new(
            MediaSortDateDecisionKind.UseDate,
            value,
            label);

    public static MediaSortDateDecision SortAsUnclear() =>
        new(MediaSortDateDecisionKind.SortAsUnclear);

    public static MediaSortDateDecision Skip() =>
        new(MediaSortDateDecisionKind.Skip);
}
