namespace Elbwald.DesktopTools.Contracts.MediaIndex;

public sealed record MediaIndexSearchQuery(
    string? SearchText = null,
    bool PresentOnly = false,
    bool HistoricalOnly = false,
    int Limit = 200);
