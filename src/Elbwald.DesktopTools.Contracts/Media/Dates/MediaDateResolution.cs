namespace Elbwald.DesktopTools.Contracts.Media.Dates;

public sealed class MediaDateResolution
{
    public MediaDateResolution(
        DateTime? value,
        MediaDateConfidence confidence,
        MediaDateSource? primarySource,
        IEnumerable<MediaDateCandidate> candidates,
        IEnumerable<MediaDateComparison> comparisons,
        string explanation)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(comparisons);
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);

        Value = value;
        Confidence = confidence;
        PrimarySource = primarySource;
        Candidates = candidates.ToArray();
        Comparisons = comparisons.ToArray();
        Explanation = explanation;

        Conflicts =
            Comparisons
                .Where(comparison =>
                    (int)comparison.Deviation
                    >= (int)MediaDateDeviationLevel.Noticeable)
                .Select(comparison =>
                    new MediaDateConflict(
                        comparison.PrimarySource,
                        comparison.ComparedSource,
                        comparison.Message))
                .ToArray();
    }

    public DateTime? Value { get; }

    public MediaDateConfidence Confidence { get; }

    public MediaDateSource? PrimarySource { get; }

    public IReadOnlyList<MediaDateCandidate> Candidates { get; }

    public IReadOnlyList<MediaDateComparison> Comparisons { get; }

    public IReadOnlyList<MediaDateConflict> Conflicts { get; }

    public string Explanation { get; }

    public MediaDateDeviationLevel HighestDeviation =>
        Comparisons.Count == 0
            ? MediaDateDeviationLevel.Equivalent
            : Comparisons
                .OrderByDescending(comparison =>
                    (int)comparison.Deviation)
                .First()
                .Deviation;

    public bool IsResolved =>
        Value.HasValue
        && PrimarySource.HasValue;

    public bool HasConflicts =>
        Conflicts.Count > 0;

    public bool NeedsReview =>
        Comparisons.Any(comparison =>
            (int)comparison.Deviation
            >= (int)MediaDateDeviationLevel.Minor);
}
