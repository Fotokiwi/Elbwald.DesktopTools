namespace Elbwald.DesktopTools.Contracts.MediaIndex;

/// <summary>
/// Read-only projection used by planners that need current media identity,
/// location and the best known media date without issuing one query per item.
/// </summary>
public sealed record MediaIndexPlanningEntry(
    MediaIndexItem Item,
    MediaIndexLocation Location,
    DateTime? CaptureDate);
