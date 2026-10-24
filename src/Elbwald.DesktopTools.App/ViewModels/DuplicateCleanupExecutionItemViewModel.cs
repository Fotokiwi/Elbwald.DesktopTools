using Elbwald.DesktopTools.Contracts.DuplicateFinding;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class DuplicateCleanupExecutionItemViewModel
{
    public DuplicateCleanupExecutionItemViewModel(DuplicateCleanupExecutionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        LocationId = item.LocationId;
        Path = string.IsNullOrWhiteSpace(item.SourcePath)
            ? item.RelativePath
            : item.SourcePath;
        Message = item.Message;
        Status = item.Status switch
        {
            DuplicateCleanupExecutionStatus.MovedToTrash => "IM PAPIERKORB",
            DuplicateCleanupExecutionStatus.BlockedByPreflight => "BLOCKIERT",
            DuplicateCleanupExecutionStatus.TrashFailed => "FEHLGESCHLAGEN",
            _ => item.Status.ToString()
        };
        SizeText = item.MovedBytes > 0
            ? MediaIndexBrowseItemViewModel.FormatBytes(item.MovedBytes)
            : "–";
        IsSuccess = item.Succeeded;
    }

    public long LocationId { get; }

    public string Path { get; }

    public string Message { get; }

    public string Status { get; }

    public string SizeText { get; }

    public bool IsSuccess { get; }
}
