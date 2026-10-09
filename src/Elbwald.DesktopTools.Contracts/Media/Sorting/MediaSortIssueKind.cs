namespace Elbwald.DesktopTools.Contracts.Media.Sorting;

public enum MediaSortIssueKind
{
    MissingCaptureDate,
    DateReview,
    InsufficientDateConfidence,
    DateConflict,
    MissingCameraInformation,
    CompanionGroup,
    DestinationInsideSource,
    FileOperationConflict
}
