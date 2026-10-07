namespace Elbwald.DesktopTools.Contracts.Recovery;

public interface IFileOperationRecoveryInspector
{
    Task<IReadOnlyList<FileOperationRecoveryCandidate>> InspectAsync(
        CancellationToken cancellationToken = default);
}
