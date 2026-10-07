namespace Elbwald.DesktopTools.Contracts.FileOperations;

public sealed record FileOperationSafetyIssue(
    FileOperationSafetyIssueKind Kind,
    string Message,
    int? OperationIndex = null,
    string? VolumeName = null,
    long? RequiredBytes = null,
    long? AvailableBytes = null);
