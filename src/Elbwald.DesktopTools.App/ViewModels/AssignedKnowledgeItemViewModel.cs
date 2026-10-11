using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.Media.Knowledge;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class AssignedKnowledgeItemViewModel
{
    public AssignedKnowledgeItemViewModel(
        MediaKnowledgeItem item,
        Func<AssignedKnowledgeItemViewModel, Task> removeAsync)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        ArgumentNullException.ThrowIfNull(removeAsync);
        RemoveCommand = new AsyncRelayCommand(() => removeAsync(this));
    }

    public MediaKnowledgeItem Item { get; }
    public string Id => Item.Id;
    public string Name => Item.Name;
    public string KindLabel => Item.Kind switch
    {
        MediaKnowledgeKind.Person => "Person",
        MediaKnowledgeKind.Place => "Ort",
        MediaKnowledgeKind.Event => "Ereignis",
        _ => "Tag"
    };

    public IAsyncRelayCommand RemoveCommand { get; }
}
