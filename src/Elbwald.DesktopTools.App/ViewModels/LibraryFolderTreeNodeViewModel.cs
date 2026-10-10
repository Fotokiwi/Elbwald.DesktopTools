using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class LibraryFolderTreeNodeViewModel : ObservableObject
{
    private readonly Func<LibraryFolderTreeNodeViewModel, Task> _toggle;
    private readonly Func<LibraryFolderTreeNodeViewModel, Task> _select;

    public LibraryFolderTreeNodeViewModel(
        string endpointId,
        string title,
        string relativeFolder,
        int depth,
        int? count,
        bool hasChildren,
        Func<LibraryFolderTreeNodeViewModel, Task> toggle,
        Func<LibraryFolderTreeNodeViewModel, Task> select)
    {
        EndpointId = endpointId;
        Title = title;
        RelativeFolder = relativeFolder;
        Depth = Math.Max(0, depth);
        Count = count;
        HasChildren = hasChildren;
        _toggle = toggle;
        _select = select;

        ToggleCommand = new AsyncRelayCommand(() => _toggle(this), () => HasChildren && !IsLoading);
        SelectCommand = new AsyncRelayCommand(() => _select(this));
    }

    public string EndpointId { get; }
    public string Title { get; }
    public string RelativeFolder { get; }
    public int Depth { get; }
    public int? Count { get; }
    public bool HasChildren { get; }
    public double Indent => Depth * 18d;
    public string CountText => Count.HasValue ? Count.Value.ToString("N0") : string.Empty;
    public IReadOnlyList<LibraryFolderTreeNodeViewModel> Children { get; set; } = Array.Empty<LibraryFolderTreeNodeViewModel>();
    public bool ChildrenLoaded { get; set; }
    public IAsyncRelayCommand ToggleCommand { get; }
    public IAsyncRelayCommand SelectCommand { get; }

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isLoading;

    public string ExpandGlyph => IsLoading ? "…" : IsExpanded ? "▾" : "▸";

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandGlyph));

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(ExpandGlyph));
        ToggleCommand.NotifyCanExecuteChanged();
    }
}
