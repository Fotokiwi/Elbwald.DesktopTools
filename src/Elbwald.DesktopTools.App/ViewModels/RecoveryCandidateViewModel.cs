using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Contracts.Recovery;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class RecoveryCandidateViewModel
{
    public RecoveryCandidateViewModel(
        FileOperationRecoveryCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        TransactionId = candidate.TransactionId;
        OperationKind = candidate.OperationKind;
        SourcePath = candidate.SourcePath;
        DestinationPath = candidate.DestinationPath;
        LastKnownState = candidate.LastKnownState;
        SourceExists = candidate.SourceExists;
        DestinationExists = candidate.DestinationExists;
        HasPersistentRecovery =
            candidate.Recovery?.StorageKind == RecoveryStorageKind.PersistentFile;

        OperationLabel = candidate.OperationKind switch
        {
            "Move" => "Verschieben",
            "Copy" => "Kopieren",
            _ => candidate.OperationKind
        };

        StateLabel = candidate.LastKnownState switch
        {
            OperationJournalState.Prepared => "Vorbereitet",
            OperationJournalState.Executing => "Unterbrochen während Ausführung",
            OperationJournalState.Committed => "Dateisystem-Änderung committed",
            OperationJournalState.RecoveryRequired => "Recovery erforderlich",
            _ => candidate.LastKnownState.ToString()
        };

        SourceStateLabel = SourceExists
            ? "Quelle vorhanden"
            : "Quelle fehlt";

        DestinationStateLabel = DestinationExists
            ? "Ziel vorhanden"
            : "Ziel fehlt";

        RecoveryStateLabel = HasPersistentRecovery
            ? "Persistente Recovery-Sicherung vorhanden"
            : "Keine persistente Recovery-Sicherung";
    }

    public Guid TransactionId { get; }

    public string TransactionShortId =>
        TransactionId.ToString("N")[..8];

    public string OperationKind { get; }

    public string OperationLabel { get; }

    public string SourcePath { get; }

    public string DestinationPath { get; }

    public OperationJournalState LastKnownState { get; }

    public string StateLabel { get; }

    public bool SourceExists { get; }

    public bool DestinationExists { get; }

    public bool HasPersistentRecovery { get; }

    public string SourceStateLabel { get; }

    public string DestinationStateLabel { get; }

    public string RecoveryStateLabel { get; }
}
