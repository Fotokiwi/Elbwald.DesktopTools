namespace Elbwald.DesktopTools.Contracts.FileOperations;

public sealed record FileOperationProgress(
    int CompletedCount,
    int TotalCount,
    FileOperationPlanItem CurrentOperation)
{
    public int Percentage =>
        TotalCount <= 0
            ? 100
            : (int)Math.Round(
                CompletedCount * 100d / TotalCount,
                MidpointRounding.AwayFromZero);
}
