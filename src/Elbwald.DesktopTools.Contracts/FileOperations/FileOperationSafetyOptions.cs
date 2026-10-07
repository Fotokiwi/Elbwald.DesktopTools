namespace Elbwald.DesktopTools.Contracts.FileOperations;

public sealed record FileOperationSafetyOptions
{
    public long MinimumFreeSpaceReserveBytes { get; init; } =
        512L * 1024L * 1024L;

    public double FreeSpaceReserveRatio { get; init; } = 0.02;

    public long MaximumRatioReserveBytes { get; init; } =
        5L * 1024L * 1024L * 1024L;

    public static FileOperationSafetyOptions Default => new();

    public void Validate()
    {
        if (MinimumFreeSpaceReserveBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumFreeSpaceReserveBytes));
        }

        if (FreeSpaceReserveRatio < 0
            || double.IsNaN(FreeSpaceReserveRatio)
            || double.IsInfinity(FreeSpaceReserveRatio))
        {
            throw new ArgumentOutOfRangeException(
                nameof(FreeSpaceReserveRatio));
        }

        if (MaximumRatioReserveBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumRatioReserveBytes));
        }
    }

    public long CalculateReserveBytes(long volumeSizeBytes)
    {
        Validate();

        if (volumeSizeBytes <= 0)
        {
            return MinimumFreeSpaceReserveBytes;
        }

        var ratioReserve = Math.Min(
            MaximumRatioReserveBytes,
            (long)Math.Floor(volumeSizeBytes * FreeSpaceReserveRatio));

        return Math.Max(
            MinimumFreeSpaceReserveBytes,
            ratioReserve);
    }
}
