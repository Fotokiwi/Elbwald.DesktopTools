namespace Elbwald.DesktopTools.Contracts.Media.Knowledge;

public sealed record MediaKnowledgeDraft(
    MediaKnowledgeKind Kind,
    string Name,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    double? Latitude = null,
    double? Longitude = null,
    string? Notes = null,
    bool IsPinned = false);
