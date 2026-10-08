namespace Elbwald.DesktopTools.Contracts.Media.Dates;

public sealed record MediaDateCandidate(
    MediaDateSource Source,
    DateTime Value,
    MediaDateTimeBasis Basis,
    MediaDatePrecision Precision,
    string Label,
    bool IsCaptureEvidence);
