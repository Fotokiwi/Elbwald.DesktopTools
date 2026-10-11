using CommunityToolkit.Mvvm.ComponentModel;
using Elbwald.DesktopTools.Contracts.Media.Knowledge;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class SmartCollectionKnowledgeOptionViewModel : ObservableObject
{
    public SmartCollectionKnowledgeOptionViewModel(MediaKnowledgeItem item)
    {
        Item = item;
    }

    public MediaKnowledgeItem Item { get; }
    public string Id => Item.Id;
    public string Name => Item.Name;
    public string KindText => Item.Kind switch
    {
        MediaKnowledgeKind.Person => "Person",
        MediaKnowledgeKind.Place => "Ort",
        MediaKnowledgeKind.Event => "Ereignis",
        _ => "Tag"
    };

    [ObservableProperty]
    private bool isSelected;
}
