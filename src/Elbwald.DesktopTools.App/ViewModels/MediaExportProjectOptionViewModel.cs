namespace Elbwald.DesktopTools.App.ViewModels;

public sealed record MediaExportProjectOptionViewModel(
    string Id,
    string Name,
    int MediaCount)
{
    public string DisplayName => $"{Name} · {MediaCount:N0} Medien";
}
