using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.Media.Knowledge;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class QuickKnowledgeItemViewModel
{
    public QuickKnowledgeItemViewModel(
        MediaKnowledgeItem item,
        Func<QuickKnowledgeItemViewModel, Task> applyAsync)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        ArgumentNullException.ThrowIfNull(applyAsync);
        ApplyCommand = new AsyncRelayCommand(() => applyAsync(this));
    }

    public MediaKnowledgeItem Item { get; }
    public string Id => Item.Id;
    public string Name => Item.Name;
    public string UsageText => Item.UsageCount switch
    {
        <= 0 => "neu",
        1 => "1× verwendet",
        _ => $"{Item.UsageCount:N0}× verwendet"
    };
    public bool IsPinned => Item.IsPinned;
    public IAsyncRelayCommand ApplyCommand { get; }
}
