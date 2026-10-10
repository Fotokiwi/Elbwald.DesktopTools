using Elbwald.DesktopTools.Contracts.OperationHistory;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class OperationHistoryEntryViewModel
{
    public OperationHistoryEntryViewModel(OperationHistoryEntry entry)
    {
        Entry = entry;
    }

    public OperationHistoryEntry Entry { get; }
    public string SourcePath => Entry.SourcePath;
    public string DestinationPath => Entry.DestinationPath;
    public string Sha256Short => Entry.Sha256.Length > 14 ? Entry.Sha256[..14] + "…" : Entry.Sha256;
    public string SizeText => FormatBytes(Entry.Length);
    public string MediaItemId => Entry.MediaItemId ?? "–";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes:N0} {units[unit]}" : $"{value:N1} {units[unit]}";
    }
}
