using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.MediaAnalyzer.ViewModels;
using Elbwald.DesktopTools.MediaAnalyzer.Views;

namespace Elbwald.DesktopTools.MediaAnalyzer;

public sealed class MediaAnalyzerModule
    : IToolModule
{
    public string Id =>
        "elbwald.mediaanalyzer";

    public ToolModuleDescriptor GetDescriptor()
    {
        return new ToolModuleDescriptor(
            Id: Id,
            Name: "Analysieren",
            Description:
                "Ordner und Medien prüfen, bevor sie importiert oder bearbeitet werden.",
            Icon: "Search",
            ViewModelType:
                typeof(MediaAnalyzerViewModel),
            ViewType:
                typeof(MediaAnalyzerView));
    }
}
