using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.Contracts.MediaIndex.Collections;

public sealed record MediaSmartCollection(
    string Id,
    string Name,
    string? SearchText,
    MediaFileType? MediaType,
    string? EndpointId,
    string? RelativeFolder,
    DateTime? CaptureDateFrom,
    DateTime? CaptureDateTo,
    int? MinimumRating,
    IReadOnlyList<string> KnowledgeItemIds,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);
