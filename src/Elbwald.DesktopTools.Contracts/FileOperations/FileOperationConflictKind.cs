namespace Elbwald.DesktopTools.Contracts.FileOperations;

public enum FileOperationConflictKind
{
    InvalidSourcePath,
    InvalidDestinationPath,
    SourceFileMissing,
    SameSourceAndDestination,
    DestinationAlreadyExists,
    DuplicateDestination,
    SourceUsedMultipleTimes,
    DestinationIsAnotherSource
}
