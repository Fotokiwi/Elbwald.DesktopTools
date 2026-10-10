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
    public string DetailText
    {
        get
        {
            var parts = new List<string> { Project.Kind };
            if (Project.StartDate.HasValue || Project.EndDate.HasValue)
            {
                var start = Project.StartDate?.ToString("dd.MM.yyyy") ?? "…";
                var end = Project.EndDate?.ToString("dd.MM.yyyy") ?? "…";
                parts.Add($"{start} – {end}");
            }
            if (!string.IsNullOrWhiteSpace(Project.Location)) parts.Add(Project.Location);
            return string.Join(" · ", parts);
        }
    }
}
