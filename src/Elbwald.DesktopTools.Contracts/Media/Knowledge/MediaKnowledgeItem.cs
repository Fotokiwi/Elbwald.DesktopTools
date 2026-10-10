namespace Elbwald.DesktopTools.Contracts.Media.Knowledge;

public sealed record MediaKnowledgeItem(
    string Id,
    MediaKnowledgeKind Kind,
    string Name,
    DateOnly? StartDate,
    DateOnly? EndDate,
    double? Latitude,
    double? Longitude,
    string? Notes,
    bool IsPinned,
    int UsageCount,
    DateTimeOffset? LastUsedUtc,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);
