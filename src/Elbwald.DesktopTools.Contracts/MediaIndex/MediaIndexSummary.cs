namespace Elbwald.DesktopTools.Contracts.MediaIndex;

public sealed record MediaIndexSummary(
    int ItemCount,
    int PresentLocationCount,
    int HistoricalLocationCount,
    DateTimeOffset? LastIndexedUtc);
