using Elbwald.DesktopTools.Contracts.Media.Importing;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class MediaImportPlanItemViewModel
{
    public MediaImportPlanItemViewModel(MediaImportPlanItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        RelativePath = item.RelativePath;
        Size = FormatBytes(item.Length);
        Message = item.Message;
        Status = item.State switch
        {
            MediaImportPlanItemState.ReadyToCopy => "Importieren",
            MediaImportPlanItemState.AlreadyImported => "Bereits vorhanden",
            MediaImportPlanItemState.Conflict => "Konflikt",
            _ => "Unbekannt"
        };
    }

    public string RelativePath { get; }
    public string Size { get; }
    public string Status { get; }
    public string Message { get; }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}
