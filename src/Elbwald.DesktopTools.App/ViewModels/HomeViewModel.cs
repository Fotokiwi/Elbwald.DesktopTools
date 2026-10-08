using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.App.Services;
using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Contracts.Recovery;
using Elbwald.DesktopTools.Core.Modules;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class HomeViewModel : ObservableObject
{
    private readonly IStartupRecoveryService _startupRecoveryService;
    private readonly IFileOperationRecoveryCoordinator _recoveryCoordinator;
    private readonly IOperationJournalMaintenance _journalMaintenance;

    private string _recoveryStatusTitle = string.Empty;
    private string _recoveryStatusMessage = string.Empty;
    private string _recoveryActionMessage = string.Empty;

    private bool _isRecoveryClean;
    private bool _isRecoveryAttentionRequired;
    private bool _isRecoveryScanFailed;
    private bool _isProcessLockUnavailable;
    private bool _isRecoveryNotScanned;
    private bool _canStartFileOperations;
    private bool _hasRecoveryCandidates;
    private bool _hasJournalCorruption;
    private bool _isRecoveryBusy;
    private bool _isRecoveryPanelVisible;
    private bool _isRecoveryActionMessageVisible;
    private bool _canRunAutomaticRecovery;

    private IReadOnlyList<RecoveryCandidateViewModel> _recoveryCandidates =
        Array.Empty<RecoveryCandidateViewModel>();

    private IReadOnlyList<RecoveryResultViewModel> _recoveryResults =
        Array.Empty<RecoveryResultViewModel>();

    public HomeViewModel(
        IModuleRegistry moduleRegistry,
        IModuleLoadReport moduleLoadReport,
        INavigationService navigationService,
        IStartupRecoveryService startupRecoveryService,
        IFileOperationRecoveryCoordinator recoveryCoordinator,
        IOperationJournalMaintenance journalMaintenance)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(moduleLoadReport);
        ArgumentNullException.ThrowIfNull(navigationService);
        ArgumentNullException.ThrowIfNull(startupRecoveryService);
        ArgumentNullException.ThrowIfNull(recoveryCoordinator);
        ArgumentNullException.ThrowIfNull(journalMaintenance);

        _startupRecoveryService = startupRecoveryService;
        _recoveryCoordinator = recoveryCoordinator;
        _journalMaintenance = journalMaintenance;

        Modules = moduleRegistry.Modules
            .Select(module => new HomeModuleItemViewModel(
                module.GetDescriptor(),
                navigationService))
            .OrderBy(module => module.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        ModuleSummary = Modules.Count switch
        {
            0 => "Noch keine Werkzeuge geladen.",
            1 => "1 Werkzeug ist bereit.",
            _ => $"{Modules.Count} Werkzeuge sind bereit."
        };

        FailedModules = moduleLoadReport.Failures
            .Select(result => new ModuleLoadFailureViewModel(result))
            .ToArray();

        OpenAnalyzeCommand = new RelayCommand(
            () => navigationService.NavigateTo("analyze"));

        OpenOrganizeCommand = new RelayCommand(
            () => navigationService.NavigateTo("organize"));

        OpenEditCommand = new RelayCommand(
            () => navigationService.NavigateTo("edit"));

        OpenBackupCommand = new RelayCommand(
            () => navigationService.NavigateTo("backup"));

        RunSafeRecoveryCommand = new AsyncRelayCommand(
            RunSafeRecoveryAsync,
            CanRunSafeRecovery);

        RefreshRecoveryCommand = new AsyncRelayCommand(
            RefreshRecoveryAsync,
            CanRefreshRecovery);

        RepairJournalCommand = new AsyncRelayCommand(
            RepairJournalAsync,
            CanRepairJournal);

        ApplyRecoverySnapshot(
            startupRecoveryService.Current);
    }

    public string Title => "Willkommen";

    public string ModuleSummary { get; }

    public IReadOnlyList<HomeModuleItemViewModel> Modules { get; }

    public IReadOnlyList<ModuleLoadFailureViewModel> FailedModules { get; }

    public bool HasModuleLoadFailures => FailedModules.Count > 0;

    public IRelayCommand OpenAnalyzeCommand { get; }

    public IRelayCommand OpenOrganizeCommand { get; }

    public IRelayCommand OpenEditCommand { get; }

    public IRelayCommand OpenBackupCommand { get; }

    public IAsyncRelayCommand RunSafeRecoveryCommand { get; }

    public IAsyncRelayCommand RefreshRecoveryCommand { get; }

    public IAsyncRelayCommand RepairJournalCommand { get; }

    public string RecoveryStatusTitle
    {
        get => _recoveryStatusTitle;
        private set => SetProperty(
            ref _recoveryStatusTitle,
            value);
    }

    public string RecoveryStatusMessage
    {
        get => _recoveryStatusMessage;
        private set => SetProperty(
            ref _recoveryStatusMessage,
            value);
    }

    public string RecoveryActionMessage
    {
        get => _recoveryActionMessage;
        private set
        {
            if (SetProperty(
                    ref _recoveryActionMessage,
                    value))
            {
                IsRecoveryActionMessageVisible =
                    !string.IsNullOrWhiteSpace(value);
            }
        }
    }

    public bool IsRecoveryClean
    {
        get => _isRecoveryClean;
        private set => SetProperty(
            ref _isRecoveryClean,
            value);
    }

    public bool IsRecoveryAttentionRequired
    {
        get => _isRecoveryAttentionRequired;
        private set => SetProperty(
            ref _isRecoveryAttentionRequired,
            value);
    }

    public bool IsRecoveryScanFailed
    {
        get => _isRecoveryScanFailed;
        private set => SetProperty(
            ref _isRecoveryScanFailed,
            value);
    }

    public bool IsProcessLockUnavailable
    {
        get => _isProcessLockUnavailable;
        private set => SetProperty(
            ref _isProcessLockUnavailable,
            value);
    }

    public bool IsRecoveryNotScanned
    {
        get => _isRecoveryNotScanned;
        private set => SetProperty(
            ref _isRecoveryNotScanned,
            value);
    }

    public bool CanStartFileOperations
    {
        get => _canStartFileOperations;
        private set => SetProperty(
            ref _canStartFileOperations,
            value);
    }

    public bool HasRecoveryCandidates
    {
        get => _hasRecoveryCandidates;
        private set => SetProperty(
            ref _hasRecoveryCandidates,
            value);
    }

    public bool HasJournalCorruption
    {
        get => _hasJournalCorruption;
        private set => SetProperty(
            ref _hasJournalCorruption,
            value);
    }

    public bool IsRecoveryBusy
    {
        get => _isRecoveryBusy;
        private set
        {
            if (!SetProperty(
                    ref _isRecoveryBusy,
                    value))
            {
                return;
            }

            RunSafeRecoveryCommand.NotifyCanExecuteChanged();
            RefreshRecoveryCommand.NotifyCanExecuteChanged();
            RepairJournalCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsRecoveryPanelVisible
    {
        get => _isRecoveryPanelVisible;
        private set => SetProperty(
            ref _isRecoveryPanelVisible,
            value);
    }

    public bool IsRecoveryActionMessageVisible
    {
        get => _isRecoveryActionMessageVisible;
        private set => SetProperty(
            ref _isRecoveryActionMessageVisible,
            value);
    }

    public bool CanRunAutomaticRecovery
    {
        get => _canRunAutomaticRecovery;
        private set
        {
            if (SetProperty(
                    ref _canRunAutomaticRecovery,
                    value))
            {
                RunSafeRecoveryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<RecoveryCandidateViewModel> RecoveryCandidates
    {
        get => _recoveryCandidates;
        private set => SetProperty(
            ref _recoveryCandidates,
            value);
    }

    public IReadOnlyList<RecoveryResultViewModel> RecoveryResults
    {
        get => _recoveryResults;
        private set
        {
            if (SetProperty(
                    ref _recoveryResults,
                    value))
            {
                OnPropertyChanged(
                    nameof(HasRecoveryResults));
            }
        }
    }

    public bool HasRecoveryResults =>
        RecoveryResults.Count > 0;

    private bool CanRunSafeRecovery()
    {
        return CanRunAutomaticRecovery
            && !IsRecoveryBusy;
    }

    private bool CanRefreshRecovery()
    {
        return !IsRecoveryBusy;
    }

    private bool CanRepairJournal()
    {
        return HasJournalCorruption
            && !IsRecoveryBusy;
    }

    private async Task RepairJournalAsync()
    {
        if (!HasJournalCorruption)
        {
            return;
        }

        IsRecoveryBusy = true;
        RecoveryActionMessage =
            "Journal-Reparatur wird sicher geprüft. Nur ein eindeutig "
            + "unvollständiger letzter Datensatz darf automatisch entfernt werden.";

        try
        {
            var result =
                await _journalMaintenance.RepairTrailingRecordAsync();

            RecoveryActionMessage =
                result.Outcome switch
                {
                    OperationJournalRepairOutcome.Repaired =>
                        result.Message
                        + (string.IsNullOrWhiteSpace(result.BackupPath)
                            ? string.Empty
                            : $" Backup: {result.BackupPath}"),

                    OperationJournalRepairOutcome.NotNeeded =>
                        result.Message,

                    OperationJournalRepairOutcome.UnsafeCorruption =>
                        result.Message
                        + " Die Dateioperationen bleiben gesperrt.",

                    _ =>
                        result.Message
                        + " Die Dateioperationen bleiben gesperrt."
                };

            var refreshed =
                await _startupRecoveryService.ScanAsync();

            ApplyRecoverySnapshot(refreshed);
        }
        catch (OperationCanceledException)
        {
            RecoveryActionMessage =
                "Journal-Reparatur wurde abgebrochen.";
        }
        catch (Exception exception)
        {
            RecoveryActionMessage =
                "Journal-Reparatur konnte nicht sicher ausgeführt werden. "
                + $"Fehler: {exception.GetBaseException().Message}";

            await RefreshRecoveryAfterFailureAsync();
        }
        finally
        {
            IsRecoveryBusy = false;
        }
    }

    private async Task RunSafeRecoveryAsync()
    {
        if (!CanRunAutomaticRecovery)
        {
            return;
        }

        IsRecoveryBusy = true;
        RecoveryActionMessage =
            "Sichere Recovery wird geprüft. Es werden keine vorhandenen "
            + "Dateien überschrieben oder automatisch gelöscht.";

        try
        {
            var results =
                await _recoveryCoordinator.RecoverPendingAsync();

            RecoveryResults = results
                .Select(result =>
                    new RecoveryResultViewModel(result))
                .ToArray();

            var refreshed =
                await _startupRecoveryService.ScanAsync();

            ApplyRecoverySnapshot(refreshed);

            var successCount = results.Count(result =>
                result.Outcome is
                    FileOperationRecoveryOutcome.Recovered
                    or FileOperationRecoveryOutcome.Finalized);

            var manualCount = results.Count(result =>
                result.Outcome ==
                    FileOperationRecoveryOutcome.ManualActionRequired);

            var failedCount = results.Count(result =>
                result.Outcome ==
                    FileOperationRecoveryOutcome.Failed);

            RecoveryActionMessage =
                $"Recovery-Prüfung abgeschlossen: {successCount} sicher "
                + $"abgeschlossen, {manualCount} manuell zu prüfen, "
                + $"{failedCount} fehlgeschlagen.";
        }
        catch (OperationCanceledException)
        {
            RecoveryActionMessage =
                "Recovery-Prüfung wurde abgebrochen.";
        }
        catch (Exception exception)
        {
            RecoveryActionMessage =
                "Recovery konnte nicht sicher ausgeführt werden. "
                + $"Es wurden keine unsicheren Folgeschritte gestartet. "
                + $"Fehler: {exception.GetBaseException().Message}";

            await RefreshRecoveryAfterFailureAsync();
        }
        finally
        {
            IsRecoveryBusy = false;
        }
    }

    private async Task RefreshRecoveryAsync()
    {
        IsRecoveryBusy = true;

        try
        {
            var snapshot =
                await _startupRecoveryService.ScanAsync();

            ApplyRecoverySnapshot(snapshot);

            RecoveryActionMessage =
                snapshot.State == StartupRecoveryState.Clean
                    ? "Recovery-Status erneut geprüft: Dateisicherheit bereit."
                    : "Recovery-Status erneut geprüft. Offene oder unklare "
                      + "Zustände bleiben weiterhin gesperrt.";
        }
        catch (OperationCanceledException)
        {
            RecoveryActionMessage =
                "Recovery-Prüfung wurde abgebrochen.";
        }
        finally
        {
            IsRecoveryBusy = false;
        }
    }

    private async Task RefreshRecoveryAfterFailureAsync()
    {
        try
        {
            var snapshot =
                await _startupRecoveryService.ScanAsync();

            ApplyRecoverySnapshot(snapshot);
        }
        catch
        {
            // The original UI-level recovery error remains the useful message.
            // A failed refresh must never trigger further file operations.
        }
    }

    private void ApplyRecoverySnapshot(
        StartupRecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        RecoveryStatusTitle =
            BuildRecoveryStatusTitle(snapshot);

        RecoveryStatusMessage =
            BuildRecoveryStatusMessage(snapshot);

        IsRecoveryClean =
            snapshot.State == StartupRecoveryState.Clean;

        IsRecoveryAttentionRequired =
            snapshot.State == StartupRecoveryState.AttentionRequired;

        IsRecoveryScanFailed =
            snapshot.State == StartupRecoveryState.ScanFailed;

        IsProcessLockUnavailable =
            snapshot.State == StartupRecoveryState.ProcessLockUnavailable;

        IsRecoveryNotScanned =
            snapshot.State == StartupRecoveryState.NotScanned;

        CanStartFileOperations =
            snapshot.CanStartFileOperations;

        HasJournalCorruption =
            snapshot.HasJournalCorruption;

        RecoveryCandidates = snapshot.Candidates
            .Select(candidate =>
                new RecoveryCandidateViewModel(candidate))
            .ToArray();

        HasRecoveryCandidates =
            RecoveryCandidates.Count > 0;

        IsRecoveryPanelVisible =
            snapshot.RequiresAttention
            || snapshot.State == StartupRecoveryState.NotScanned;

        CanRunAutomaticRecovery =
            snapshot.State == StartupRecoveryState.AttentionRequired
            && HasRecoveryCandidates
            && !snapshot.HasJournalCorruption;

        RunSafeRecoveryCommand.NotifyCanExecuteChanged();
        RepairJournalCommand.NotifyCanExecuteChanged();
    }

    private static string BuildRecoveryStatusTitle(
        StartupRecoverySnapshot snapshot)
    {
        return snapshot.State switch
        {
            StartupRecoveryState.Clean =>
                "Dateisicherheit bereit",

            StartupRecoveryState.AttentionRequired =>
                "Dateioperationen gesperrt",

            StartupRecoveryState.ScanFailed =>
                "Sicherheitsprüfung fehlgeschlagen",

            StartupRecoveryState.ProcessLockUnavailable =>
                "Andere Instanz hat Dateizugriff",

            _ =>
                "Dateisicherheit wird geprüft"
        };
    }

    private static string BuildRecoveryStatusMessage(
        StartupRecoverySnapshot snapshot)
    {
        return snapshot.State switch
        {
            StartupRecoveryState.Clean =>
                "Recovery-Journal geprüft. Es sind keine offenen "
                + "Dateioperationen vorhanden.",

            StartupRecoveryState.AttentionRequired
                when snapshot.Candidates.Count > 0
                     && snapshot.CorruptJournalLineCount > 0 =>
                $"{snapshot.Candidates.Count} unvollständige Transaktion(en) "
                + $"und {snapshot.CorruptJournalLineCount} beschädigte "
                + "Journalzeile(n) müssen geprüft werden.",

            StartupRecoveryState.AttentionRequired
                when snapshot.Candidates.Count > 0 =>
                $"{snapshot.Candidates.Count} unvollständige Transaktion(en) "
                + "müssen geprüft werden, bevor Dateien verändert werden.",

            StartupRecoveryState.AttentionRequired =>
                $"{snapshot.CorruptJournalLineCount} beschädigte "
                + "Journalzeile(n) verhindern sichere Dateioperationen.",

            StartupRecoveryState.ScanFailed =>
                "Das Recovery-Journal konnte nicht zuverlässig geprüft werden. "
                + "Neue Dateioperationen bleiben aus Sicherheitsgründen gesperrt."
                + (string.IsNullOrWhiteSpace(snapshot.ErrorMessage)
                    ? string.Empty
                    : $" Fehler: {snapshot.ErrorMessage}"),

            StartupRecoveryState.ProcessLockUnavailable =>
                "Eine andere Desktop-Tools-Instanz hält den exklusiven "
                + "Dateisicherheits-Lock. Diese Instanz bleibt im Nur-Lesen-"
                + "Sicherheitszustand, bis der Lock übernommen werden kann.",

            _ =>
                "Neue Dateioperationen bleiben gesperrt, bis die "
                + "Recovery-Prüfung abgeschlossen ist."
        };
    }
}
