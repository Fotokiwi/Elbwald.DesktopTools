namespace Elbwald.DesktopTools.App.Services;

public sealed class MediaExportSelectionContext
{
    private readonly object _gate = new();
    private IReadOnlyList<string> _mediaItemIds = Array.Empty<string>();

    public void SetSelection(IEnumerable<string> mediaItemIds)
    {
        ArgumentNullException.ThrowIfNull(mediaItemIds);
        var snapshot = mediaItemIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        lock (_gate)
        {
            _mediaItemIds = snapshot;
        }
    }

    public IReadOnlyList<string> GetSelection()
    {
        lock (_gate)
        {
            return _mediaItemIds.ToArray();
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _mediaItemIds.Count;
            }
        }
    }
}
