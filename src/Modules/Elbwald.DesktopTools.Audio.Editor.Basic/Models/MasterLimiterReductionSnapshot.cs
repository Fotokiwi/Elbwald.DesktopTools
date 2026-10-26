namespace Elbwald.DesktopTools.Audio.Editor.Basic.Models;

public sealed record MasterLimiterReductionSnapshot(
    bool HasData,
    double CurrentReductionDb,
    double MaxReductionDb,
    double AverageReductionDb,
    double PercentAbove3Db,
    double PercentAbove5Db)
{
    public static MasterLimiterReductionSnapshot Empty { get; } =
        new(false, 0, 0, 0, 0, 0);
}
