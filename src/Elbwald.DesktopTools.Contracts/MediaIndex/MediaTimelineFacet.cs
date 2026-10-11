namespace Elbwald.DesktopTools.Contracts.MediaIndex;

public sealed record MediaTimelineFacet(
    int Year,
    int? Month,
    int? Day,
    int MediaCount);
