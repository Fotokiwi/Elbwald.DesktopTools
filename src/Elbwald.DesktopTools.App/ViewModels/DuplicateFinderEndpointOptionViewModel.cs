namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class DuplicateFinderEndpointOptionViewModel
{
    public DuplicateFinderEndpointOptionViewModel(string id, string name)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? id : name;
    }

    public string Id { get; }

    public string Name { get; }

    public override string ToString() => Name;
}
