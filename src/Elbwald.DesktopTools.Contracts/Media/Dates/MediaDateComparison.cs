namespace Elbwald.DesktopTools.Contracts.Media.Dates;

public sealed record MediaDateComparison(
    MediaDateSource PrimarySource,
    MediaDateSource ComparedSource,
    TimeSpan Difference,
    MediaDateDeviationLevel Deviation,
    bool IsIndependentEvidence,
    TimeSpan? InferredUtcOffset,
    string Message);
