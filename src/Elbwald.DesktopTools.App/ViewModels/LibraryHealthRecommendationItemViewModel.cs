using System.Windows.Input;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class LibraryHealthRecommendationItemViewModel
{
    public LibraryHealthRecommendationItemViewModel(
        string priority,
        string title,
        string description,
        string actionLabel,
        ICommand actionCommand,
        bool isProblem = false,
        bool isWarning = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(priority);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionLabel);
        ArgumentNullException.ThrowIfNull(actionCommand);

        Priority = priority;
        Title = title;
        Description = description;
        ActionLabel = actionLabel;
        ActionCommand = actionCommand;
        IsProblem = isProblem;
        IsWarning = isWarning;
        IsInfo = !isProblem && !isWarning;
    }

    public string Priority { get; }

    public string Title { get; }

    public string Description { get; }

    public string ActionLabel { get; }

    public ICommand ActionCommand { get; }

    public bool IsProblem { get; }

    public bool IsWarning { get; }

    public bool IsInfo { get; }
}
