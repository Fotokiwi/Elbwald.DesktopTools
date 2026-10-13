using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Elbwald.DesktopTools.Contracts.Projects;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class ProjectListItemViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MediaCountText))]
    private int mediaCount;

    [ObservableProperty]
    private Bitmap? coverThumbnail;

    [ObservableProperty]
    private bool coverLoading;

    [ObservableProperty]
    private string coverStatus = string.Empty;

    public ProjectListItemViewModel(ProjectRecord project, int mediaCount)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        this.mediaCount = mediaCount;
        CoverStatus = HasConfiguredCover
            ? "Projektcover wird geladen …"
            : "Noch kein Projektcover";
    }

    public ProjectRecord Project { get; }
    public string Id => Project.Id;
    public string Name => Project.Name;
    public string Kind => Project.Kind;
    public string? CoverMediaItemId => Project.CoverMediaItemId;
    public bool HasConfiguredCover => !string.IsNullOrWhiteSpace(CoverMediaItemId);
    public bool HasCoverThumbnail => CoverThumbnail is not null;
    public bool HasNoCoverThumbnail => CoverThumbnail is null;
    public string MediaCountText => MediaCount == 1 ? "1 Medium" : $"{MediaCount:N0} Medien";

    public string DetailText
    {
        get
        {
            var parts = new List<string> { Project.Kind };
            if (Project.StartDate.HasValue || Project.EndDate.HasValue)
            {
                var start = Project.StartDate?.ToString("dd.MM.yyyy") ?? "…";
                var end = Project.EndDate?.ToString("dd.MM.yyyy") ?? "…";
                parts.Add($"{start} – {end}");
            }
            if (!string.IsNullOrWhiteSpace(Project.Location)) parts.Add(Project.Location);
            return string.Join(" · ", parts);
        }
    }

    partial void OnCoverThumbnailChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasCoverThumbnail));
        OnPropertyChanged(nameof(HasNoCoverThumbnail));
    }

    public void SetCoverThumbnail(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        CoverThumbnail?.Dispose();
        CoverThumbnail = bitmap;
        CoverLoading = false;
        CoverStatus = string.Empty;
    }

    public void SetCoverUnavailable(string message)
    {
        CoverLoading = false;
        CoverStatus = message;
    }

    public void Dispose()
    {
        CoverThumbnail?.Dispose();
        CoverThumbnail = null;
    }
}
