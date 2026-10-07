namespace Elbwald.DesktopTools.Contracts.Recovery;

public sealed record RecoveryOptions
{
    public required string PersistentDirectory { get; init; }

    public long MaxMemoryBytes { get; init; } =
        512L * 1024L * 1024L;

    public long MaxSingleMemoryItemBytes { get; init; } =
        64L * 1024L * 1024L;

    public long MinimumPersistentFreeSpaceReserveBytes { get; init; } =
        1024L * 1024L * 1024L;

    public double PersistentFreeSpaceReserveRatio { get; init; } = 0.02;

    public long MaximumPersistentRatioReserveBytes { get; init; } =
        5L * 1024L * 1024L * 1024L;

    public static RecoveryOptions CreateDefault(
        string persistentDirectory)
    {
        return new RecoveryOptions
        {
            PersistentDirectory = persistentDirectory
        };
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PersistentDirectory))
        {
            throw new ArgumentException(
                "Das persistente Recovery-Verzeichnis darf nicht leer sein.",
                nameof(PersistentDirectory));
        }

        if (MaxMemoryBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxMemoryBytes));
        }

        if (MaxSingleMemoryItemBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxSingleMemoryItemBytes));
        }

        if (MinimumPersistentFreeSpaceReserveBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumPersistentFreeSpaceReserveBytes));
        }

        if (PersistentFreeSpaceReserveRatio < 0
            || double.IsNaN(PersistentFreeSpaceReserveRatio)
            || double.IsInfinity(PersistentFreeSpaceReserveRatio))
        {
            throw new ArgumentOutOfRangeException(
                nameof(PersistentFreeSpaceReserveRatio));
        }

        if (MaximumPersistentRatioReserveBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumPersistentRatioReserveBytes));
        }
    }

    public long CalculatePersistentReserveBytes(
        long volumeSizeBytes)
    {
        Validate();

        if (volumeSizeBytes <= 0)
        {
            return MinimumPersistentFreeSpaceReserveBytes;
        }

        var ratioReserve = Math.Min(
            MaximumPersistentRatioReserveBytes,
            (long)Math.Floor(
                volumeSizeBytes * PersistentFreeSpaceReserveRatio));

        return Math.Max(
            MinimumPersistentFreeSpaceReserveBytes,
            ratioReserve);
    }
}
