using Elbwald.DesktopTools.Contracts.Recovery;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class RecoveryResultViewModel
{
    public RecoveryResultViewModel(
        FileOperationRecoveryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        TransactionShortId =
            result.Candidate.TransactionId.ToString("N")[..8];

        Message = result.Message;
        RecoveryRetained = result.RecoveryRetained;

        OutcomeLabel = result.Outcome switch
        {
            FileOperationRecoveryOutcome.Recovered =>
                "Wiederhergestellt",

            FileOperationRecoveryOutcome.Finalized =>
                "Sicher abgeschlossen",

            FileOperationRecoveryOutcome.ManualActionRequired =>
                "Manuelle Prüfung erforderlich",

            FileOperationRecoveryOutcome.Failed =>
                "Recovery fehlgeschlagen",

            _ => result.Outcome.ToString()
        };

        IsSuccessful =
            result.Outcome is
                FileOperationRecoveryOutcome.Recovered
                or FileOperationRecoveryOutcome.Finalized;

        RequiresManualAction =
            result.Outcome == FileOperationRecoveryOutcome.ManualActionRequired;

        IsFailed =
            result.Outcome == FileOperationRecoveryOutcome.Failed;
    }

    public string TransactionShortId { get; }

    public string OutcomeLabel { get; }

    public string Message { get; }

    public bool RecoveryRetained { get; }

    public bool IsSuccessful { get; }

    public bool RequiresManualAction { get; }

    public bool IsFailed { get; }
}
