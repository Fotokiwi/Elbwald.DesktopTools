using Elbwald.DesktopTools.Contracts.DuplicateFinding;
using Elbwald.DesktopTools.Contracts.MediaIndex;

namespace Elbwald.DesktopTools.Core.DuplicateFinding;

public sealed class DuplicateFinderService : IDuplicateFinderService
{
    private readonly IMediaIndexService _mediaIndexService;

    public DuplicateFinderService(IMediaIndexService mediaIndexService)
    {
        ArgumentNullException.ThrowIfNull(mediaIndexService);
        _mediaIndexService = mediaIndexService;
    }

    public async Task<DuplicateFinderReport> FindExactDuplicatesAsync(
        CancellationToken cancellationToken = default)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;

        var indexSummary = await _mediaIndexService.GetSummaryAsync(
            cancellationToken);

        var allLocations = await _mediaIndexService.GetAllLocationsAsync(
            cancellationToken);

        var presentLocations = allLocations
            .Where(location => location.IsPresent)
            .ToArray();

        var duplicateLocationGroups = presentLocations
            .GroupBy(
                location => location.MediaItemId,
                StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();

        var groups = new List<ExactDuplicateGroup>(duplicateLocationGroups.Length);

        foreach (var locationGroup in duplicateLocationGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = await _mediaIndexService.GetItemAsync(
                locationGroup.Key,
                cancellationToken);

            if (item is null)
            {
                continue;
            }

            var locations = locationGroup
                .OrderBy(location => location.EndpointId, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(location => location.RelativePath, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            groups.Add(
                new ExactDuplicateGroup(
                    item.Id,
                    item.Sha256,
                    item.Length,
                    item.MediaType,
                    locations));
        }

        var orderedGroups = groups
            .OrderByDescending(group => group.ReclaimableBytes)
            .ThenByDescending(group => group.Length)
            .ThenBy(group => group.MediaItemId, StringComparer.Ordinal)
            .ToArray();

        return new DuplicateFinderReport(
            startedAtUtc,
            DateTimeOffset.UtcNow,
            indexSummary.LastIndexedUtc,
            presentLocations.Length,
            orderedGroups);
    }
}
