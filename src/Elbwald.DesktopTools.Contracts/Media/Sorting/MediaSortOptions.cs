using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Media.Dates;

namespace Elbwald.DesktopTools.Contracts.Media.Sorting;

public sealed record MediaSortOptions
{
    public static MediaSortOptions Default { get; } = new();

    public MediaSortRule Rule { get; init; } =
        MediaSortRule.YearMonth;

    /// <summary>
    /// Relative Zielstruktur. Freier Text und dieselben Platzhalter wie beim Umbenennen sind erlaubt.
    /// Pfadsegmente werden mit / getrennt, z. B. {year}/{month}/{camera}.
    /// </summary>
    public string PathPattern { get; init; } =
        "{year}/{month}";

    /// <summary>
    /// Optionale, bereits aufgelöste Elbwald-Metadaten je normalisiertem Quellpfad.
    /// Der Planner bleibt synchron und führt selbst keine Datenbankzugriffe aus.
    /// </summary>
    public IReadOnlyDictionary<string, MediaSortMetadataContext> MetadataBySourcePath { get; init; } =
        new Dictionary<string, MediaSortMetadataContext>(StringComparer.Ordinal);

    public FileOperationKind OperationKind { get; init; } =
        FileOperationKind.Copy;

    public MediaDateConfidence MinimumDateConfidence { get; init; } =
        MediaDateConfidence.High;
}
