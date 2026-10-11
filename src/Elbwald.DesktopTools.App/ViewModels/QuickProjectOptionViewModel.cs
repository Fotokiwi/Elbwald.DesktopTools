using Elbwald.DesktopTools.Contracts.Projects;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class QuickProjectOptionViewModel
{
    public QuickProjectOptionViewModel(ProjectRecord project)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
    }

    public ProjectRecord Project { get; }
    public string Id => Project.Id;
    public string Name => Project.Name;
    public override string ToString() => Name;
}
