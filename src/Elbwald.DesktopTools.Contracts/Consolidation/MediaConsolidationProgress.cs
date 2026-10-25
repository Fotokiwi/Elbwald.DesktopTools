namespace Elbwald.DesktopTools.Contracts.Consolidation;

public sealed record MediaConsolidationProgress(
    string Phase,
    int Processed,
    int Total,
    string? CurrentItem = null)
{
    public int Percentage => Total <= 0
        ? 0
        : Math.Clamp((int)Math.Round(Processed * 100d / Total), 0, 100);
}
