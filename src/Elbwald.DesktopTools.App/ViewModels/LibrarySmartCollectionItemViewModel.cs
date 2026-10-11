using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.MediaIndex.Collections;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class LibrarySmartCollectionItemViewModel : ObservableObject
{
    private readonly Func<LibrarySmartCollectionItemViewModel, Task> _open;
    private readonly Func<LibrarySmartCollectionItemViewModel, Task> _edit;
    private readonly Func<LibrarySmartCollectionItemViewModel, Task> _delete;

    public LibrarySmartCollectionItemViewModel(
        MediaSmartCollection collection,
        Func<LibrarySmartCollectionItemViewModel, Task> open,
        Func<LibrarySmartCollectionItemViewModel, Task> edit,
        Func<LibrarySmartCollectionItemViewModel, Task> delete)
    {
        Collection = collection;
        _open = open;
        _edit = edit;
        _delete = delete;
        OpenCommand = new AsyncRelayCommand(() => _open(this));
        EditCommand = new AsyncRelayCommand(() => _edit(this));
        DeleteCommand = new AsyncRelayCommand(() => _delete(this));
    }

    public MediaSmartCollection Collection { get; }
    public string Id => Collection.Id;
    public string Name => Collection.Name;
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Collection.EndpointId))
                parts.Add(string.IsNullOrWhiteSpace(Collection.RelativeFolder) ? "Speicherort" : "Ordner");
            if (Collection.CaptureDateFrom.HasValue || Collection.CaptureDateTo.HasValue)
                parts.Add("Zeitraum");
            if (Collection.MinimumRating.HasValue)
                parts.Add($"ab {Collection.MinimumRating} ★");
            if (Collection.KnowledgeItemIds.Count > 0)
                parts.Add($"{Collection.KnowledgeItemIds.Count} Metadatenfilter");
            if (Collection.MediaType.HasValue)
                parts.Add(Collection.MediaType.Value switch
                {
                    Elbwald.DesktopTools.Contracts.Media.MediaFileType.Image => "Bilder",
                    Elbwald.DesktopTools.Contracts.Media.MediaFileType.Video => "Videos",
                    Elbwald.DesktopTools.Contracts.Media.MediaFileType.Audio => "Audio",
                    _ => "Medien"
                });
            if (!string.IsNullOrWhiteSpace(Collection.SearchText))
                parts.Add("Suche");
            return parts.Count == 0 ? "Alle Medien" : string.Join(" · ", parts);
        }
    }

    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand EditCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }

    [ObservableProperty]
    private bool isSelected;
}
