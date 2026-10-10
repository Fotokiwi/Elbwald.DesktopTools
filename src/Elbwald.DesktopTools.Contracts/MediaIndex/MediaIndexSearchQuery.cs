using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.Contracts.MediaIndex;

public sealed record MediaIndexSearchQuery(
    string? SearchText = null,
    bool PresentOnly = false,
    bool HistoricalOnly = false,
    int Limit = 200,
    int Offset = 0,
    MediaFileType? MediaType = null,
    MediaIndexSortOrder SortOrder = MediaIndexSortOrder.LastSeenDescending,
    string? EndpointId = null,
    string? RelativeFolder = null,
    DateTime? CaptureDateFrom = null,
    DateTime? CaptureDateTo = null,
    int? MinimumRating = null,
    string? KnowledgeItemId = null);
