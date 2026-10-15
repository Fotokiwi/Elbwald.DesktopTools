using Elbwald.DesktopTools.Contracts.LibraryHealth;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.MediaIndex;

namespace Elbwald.DesktopTools.Core.LibraryHealth;

public sealed class LibraryHealthIndexInspector : ILibraryHealthIndexInspector
{
    private const int PageSize = 500;
    private readonly IMediaIndexService _mediaIndexService;

    public LibraryHealthIndexInspector(IMediaIndexService mediaIndexService)
    {
        ArgumentNullException.ThrowIfNull(mediaIndexService);
        _mediaIndexService = mediaIndexService;
    }

    public async Task<LibraryHealthIndexSummary> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        var summary = await _mediaIndexService.GetSummaryAsync(cancellationToken);

        if (summary.ItemCount <= 0)
        {
            return new LibraryHealthIndexSummary(
                0,
                0,
                0,
                0,
                summary.LastIndexedUtc);
        }

        var offset = 0;
        var inspected = 0;
        var present = 0;
        var orphaned = 0;
        var missingCaptureDate = 0;

        while (inspected < summary.ItemCount)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = await _mediaIndexService.SearchAsync(
                new MediaIndexSearchQuery(
                    Limit: PageSize,
                    Offset: offset,
                    SortOrder: MediaIndexSortOrder.LastSeenDescending),
                cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            foreach (var result in page)
            {
                if (result.PresentLocationCount > 0)
                {
                    present++;
                }
                else
                {
                    orphaned++;
                }

                if (result.Item.MediaType == MediaFileType.Image
                    && result.CaptureDate is null)
                {
                    missingCaptureDate++;
                }
            }

            inspected += page.Count;
            offset += page.Count;
        }

        return new LibraryHealthIndexSummary(
            summary.ItemCount,
            present,
            orphaned,
            missingCaptureDate,
            summary.LastIndexedUtc);
    }
}
