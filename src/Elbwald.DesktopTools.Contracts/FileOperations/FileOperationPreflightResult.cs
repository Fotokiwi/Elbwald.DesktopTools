namespace Elbwald.DesktopTools.Contracts.FileOperations;

public sealed class FileOperationPreflightResult
{
    public FileOperationPreflightResult(
        IEnumerable<FileOperationSafetyIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = issues.ToArray();
    }

    public IReadOnlyList<FileOperationSafetyIssue> Issues { get; }

    public bool IsSafe => Issues.Count == 0;

    public static FileOperationPreflightResult Safe { get; } =
        new(Array.Empty<FileOperationSafetyIssue>());
}
