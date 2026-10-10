using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Elbwald.DesktopTools.App.ViewModels;

public enum LibraryNavigationKind
{
    All,
    Endpoint,
    Folder,
    Project,
    MediaType
}

public partial class LibraryNavigationItemViewModel : ObservableObject
{
    public LibraryNavigationItemViewModel(
        LibraryNavigationKind kind,
        string title,
        string key,
        int depth = 0,
        int? count = null,
        string? parentKey = null,
        Func<LibraryNavigationItemViewModel, Task>? select = null)
    {
        Kind = kind;
        Title = title;
        Key = key;
        Depth = Math.Max(0, depth);
        Count = count;
        ParentKey = parentKey;
        SelectCommand = new AsyncRelayCommand(() => select is null ? Task.CompletedTask : select(this));
    }

    public LibraryNavigationKind Kind { get; }
    public string Title { get; }
    public string Key { get; }
    public string? ParentKey { get; }
    public int Depth { get; }
    public int? Count { get; }
    public double Indent => Depth * 14d;
    public string CountText => Count.HasValue ? Count.Value.ToString("N0") : string.Empty;
    public IAsyncRelayCommand SelectCommand { get; }

    [ObservableProperty]
    private bool isSelected;
}
