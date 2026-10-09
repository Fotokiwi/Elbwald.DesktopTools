using Elbwald.DesktopTools.Contracts.FileOperations;

namespace Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;

public sealed record MediaSortExecutionOperation(
    int Index,
    string GroupId,
    FileOperationKind Kind,
    string SourcePath,
    string DestinationPath,
    bool IsSidecar,
    MediaSortSourceSnapshot SourceSnapshot);
