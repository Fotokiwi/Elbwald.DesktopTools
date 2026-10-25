namespace Elbwald.DesktopTools.Contracts.Media.Editing;

/// <summary>
/// Request for a bounded editor preview. DocumentOverride is intended for an in-memory,
/// not-yet-persisted edit stack while sliders or crop handles are being manipulated.
/// </summary>
public sealed record ImageEditPreviewRequest(
    string MediaItemId,
    int MaxEdge = 2048,
    ImageEditDocument? DocumentOverride = null)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(MediaItemId);

        if (MaxEdge <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxEdge),
                "Die maximale Preview-Kantenlänge muss größer als 0 sein.");
        }

        if (DocumentOverride is null)
        {
            return;
        }

        DocumentOverride.Validate();
        if (!string.Equals(
                DocumentOverride.MediaItemId,
                MediaItemId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Das übergebene Edit-Dokument gehört zu einer anderen Media-ID.",
                nameof(DocumentOverride));
        }
    }
}
