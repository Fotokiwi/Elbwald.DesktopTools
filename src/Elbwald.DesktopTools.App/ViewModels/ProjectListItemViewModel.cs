using CommunityToolkit.Mvvm.ComponentModel;
using Elbwald.DesktopTools.Contracts.Projects;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class ProjectListItemViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MediaCountText))]
    private int mediaCount;

    public ProjectListItemViewModel(ProjectRecord project, int mediaCount)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        this.mediaCount = mediaCount;
    }

    public ProjectRecord Project { get; }
    public string Id => Project.Id;
    public string Name => Project.Name;
    public string Kind => Project.Kind;
    public string MediaCountText => MediaCount == 1 ? "1 Medium" : $"{MediaCount:N0} Medien";
    public string DetailText => string.IsNullOrWhiteSpace(Project.Location) ? Project.Kind : $"{Project.Kind} · {Project.Location}";
}
