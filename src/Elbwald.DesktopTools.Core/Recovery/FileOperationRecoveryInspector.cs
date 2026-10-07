using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Contracts.Recovery;

namespace Elbwald.DesktopTools.Core.Recovery;

public sealed class FileOperationRecoveryInspector
    : IFileOperationRecoveryInspector
{
    private readonly IOperationJournal _journal;

    public FileOperationRecoveryInspector(
        IOperationJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        _journal = journal;
    }

    public async Task<IReadOnlyList<FileOperationRecoveryCandidate>>
        InspectAsync(
            CancellationToken cancellationToken = default)
    {
        var incomplete =
            await _journal.GetIncompleteTransactionsAsync(
                cancellationToken);

        return incomplete
            .Select(entry => new FileOperationRecoveryCandidate(
                entry.TransactionId,
                entry.State,
                entry.OperationKind,
                entry.SourcePath,
                entry.DestinationPath,
                entry.OperationIndex,
                entry.Recovery,
                File.Exists(entry.SourcePath),
                File.Exists(entry.DestinationPath)))
            .ToArray();
    }
}
