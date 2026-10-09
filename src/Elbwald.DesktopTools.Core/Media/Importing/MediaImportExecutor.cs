using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Media.Importing;

namespace Elbwald.DesktopTools.Core.Media.Importing;

public sealed class MediaImportExecutor : IMediaImportExecutor
{
    private readonly IFileOperationPlanner _fileOperationPlanner;
    private readonly IFileOperationExecutor _fileOperationExecutor;

    public MediaImportExecutor(
        IFileOperationPlanner fileOperationPlanner,
        IFileOperationExecutor fileOperationExecutor)
    {
        ArgumentNullException.ThrowIfNull(fileOperationPlanner);
        ArgumentNullException.ThrowIfNull(fileOperationExecutor);
        _fileOperationPlanner = fileOperationPlanner;
        _fileOperationExecutor = fileOperationExecutor;
    }

    public async Task<MediaImportExecutionResult> ExecuteAsync(
        MediaImportPlan plan,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.IsComplete)
        {
            return new MediaImportExecutionResult(
                MediaImportExecutionState.Blocked,
                plan.CopyCount,
                0,
                plan.AlreadyImportedCount,
                plan.ConflictCount,
                plan.CopyCount,
                "Die Importvorschau ist unvollständig. Aus Sicherheitsgründen wird nichts kopiert.");
        }

        var requests = plan.Items
            .Where(item => item.State == MediaImportPlanItemState.ReadyToCopy)
            .Select(item => FileOperationRequest.Copy(item.SourcePath, item.DestinationPath))
            .ToArray();

        if (requests.Length == 0)
        {
            return new MediaImportExecutionResult(
                MediaImportExecutionState.Completed,
                0,
                0,
                plan.AlreadyImportedCount,
                plan.ConflictCount,
                0,
                "Es gibt keine neuen Dateien zu importieren.");
        }

        var filePlan = _fileOperationPlanner.CreatePlan(requests);
        if (!filePlan.CanExecute)
        {
            return new MediaImportExecutionResult(
                MediaImportExecutionState.Blocked,
                requests.Length,
                0,
                plan.AlreadyImportedCount,
                plan.ConflictCount,
                requests.Length,
                "Der Importplan ist seit der Vorschau nicht mehr konfliktfrei. Bitte die Vorschau neu erstellen.");
        }

        var fileProgress = new Progress<FileOperationProgress>(entry =>
            progress?.Report(entry.CompletedCount));

        var result = await _fileOperationExecutor.ExecuteAsync(
            filePlan,
            fileProgress,
            cancellationToken);

        var state = result.State switch
        {
            FileOperationExecutionState.Completed => MediaImportExecutionState.Completed,
            FileOperationExecutionState.Cancelled => MediaImportExecutionState.Cancelled,
            FileOperationExecutionState.BlockedByProcessLock or
            FileOperationExecutionState.BlockedByRecovery => MediaImportExecutionState.Blocked,
            _ => MediaImportExecutionState.Failed
        };

        var message = state switch
        {
            MediaImportExecutionState.Completed => "Import abgeschlossen. Alle kopierten Dateien wurden durch die File-Safety-Engine verifiziert.",
            MediaImportExecutionState.Cancelled => "Import abgebrochen. Bereits vollständig abgeschlossene Kopien bleiben erhalten; Quellen wurden nicht verändert.",
            MediaImportExecutionState.Blocked => result.ErrorMessage ?? "Import wurde durch eine Sicherheitsprüfung blockiert.",
            _ => result.ErrorMessage ?? "Import konnte nicht vollständig abgeschlossen werden. Quellen wurden nicht gelöscht."
        };

        return new MediaImportExecutionResult(
            state,
            requests.Length,
            result.CompletedCount,
            plan.AlreadyImportedCount,
            plan.ConflictCount,
            result.RemainingCount,
            message,
            result.TransactionId);
    }
}
