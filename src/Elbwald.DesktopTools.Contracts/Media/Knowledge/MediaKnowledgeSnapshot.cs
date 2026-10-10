namespace Elbwald.DesktopTools.Contracts.Media.Knowledge;

public sealed record MediaKnowledgeSnapshot(
    string MediaItemId,
    int Rating,
    IReadOnlyList<MediaKnowledgeItem> Items);
