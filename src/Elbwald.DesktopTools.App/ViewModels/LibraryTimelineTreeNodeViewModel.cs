using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class LibraryTimelineTreeNodeViewModel : ObservableObject
{
    private readonly Func<LibraryTimelineTreeNodeViewModel, Task> _toggle;
    private readonly Func<LibraryTimelineTreeNodeViewModel, Task> _select;

    public LibraryTimelineTreeNodeViewModel(
        int year,
        int? month,
        int? day,
        string title,
        int depth,
        int count,
        bool hasChildren,
        DateTime rangeStart,
        DateTime rangeEnd,
        Func<LibraryTimelineTreeNodeViewModel, Task> toggle,
        Func<LibraryTimelineTreeNodeViewModel, Task> select)
    {
        Year = year;
        Month = month;
        Day = day;
        Title = title;
        Depth = Math.Max(0, depth);
        Count = Math.Max(0, count);
        HasChildren = hasChildren;
        RangeStart = rangeStart;
        RangeEnd = rangeEnd;
        _toggle = toggle;
        _select = select;
        ToggleCommand = new AsyncRelayCommand(() => _toggle(this), () => HasChildren && !IsLoading);
        SelectCommand = new AsyncRelayCommand(() => _select(this));
    }

    public int Year { get; }
    public int? Month { get; }
    public int? Day { get; }
    public string Title { get; }
    public int Depth { get; }
    public int Count { get; }
    public bool HasChildren { get; }
    public DateTime RangeStart { get; }
    public DateTime RangeEnd { get; }
    public double Indent => Depth * 18d;
    public string CountText => Count.ToString("N0");
    public IReadOnlyList<LibraryTimelineTreeNodeViewModel> Children { get; set; } = Array.Empty<LibraryTimelineTreeNodeViewModel>();
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
