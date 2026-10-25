namespace Elbwald.DesktopTools.Contracts.Media.Editing;

/// <summary>
/// Persistent non-destructive edit stack for one stable Media-ID. Revision is used
/// for optimistic concurrency; source image bytes are never stored or rewritten.
/// </summary>
public sealed record ImageEditDocument(
    string MediaItemId,
    int SchemaVersion,
    long Revision,
    IReadOnlyList<ImageEditOperation> Operations,
    DateTimeOffset UpdatedUtc)
{
    public const int CurrentSchemaVersion = 1;

    public static ImageEditDocument Empty(string mediaItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaItemId);

        return new ImageEditDocument(
            mediaItemId,
            CurrentSchemaVersion,
            0,
            [],
            DateTimeOffset.MinValue);
    }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(MediaItemId);
        ArgumentNullException.ThrowIfNull(Operations);

        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Edit-Dokument-Schema {SchemaVersion} wird nicht unterstützt. "
                + $"Erwartet wird {CurrentSchemaVersion}.");
        }

        if (Revision < 0)
        {
            throw new InvalidOperationException(
                "Die Revision eines Edit-Dokuments darf nicht negativ sein.");
        }

        var operationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in Operations)
        {
            ArgumentNullException.ThrowIfNull(operation);
            operation.Validate();

            if (!operationIds.Add(operation.Id))
            {
                throw new InvalidOperationException(
                    $"Edit-Operation-ID '{operation.Id}' ist mehrfach vorhanden.");
            }
        }
    }
}
