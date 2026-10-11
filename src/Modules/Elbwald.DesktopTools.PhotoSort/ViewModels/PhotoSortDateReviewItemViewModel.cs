using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.Media.Dates;
using Elbwald.DesktopTools.Contracts.Media.Sorting;

namespace Elbwald.DesktopTools.PhotoSort.ViewModels;

public sealed class PhotoSortDateReviewItemViewModel : ObservableObject
{
    private readonly Func<string, MediaSortDateDecision, Task> _applyDecisionAsync;
    private readonly Action<string> _openFile;

    private PhotoSortDateCandidateOptionViewModel? _selectedCandidate;
    private string _manualDateText = string.Empty;
    private string _validationMessage = string.Empty;

    public PhotoSortDateReviewItemViewModel(
        string path,
        MediaDateResolution resolution,
        Func<string, MediaSortDateDecision, Task> applyDecisionAsync,
        Action<string> openFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(applyDecisionAsync);
        ArgumentNullException.ThrowIfNull(openFile);

        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        Resolution = resolution;
        _applyDecisionAsync = applyDecisionAsync;
        _openFile = openFile;

        CandidateOptions = resolution.Candidates
            .GroupBy(candidate => candidate.Value)
            .Select(group => group.First())
            .OrderBy(candidate => candidate.Value)
            .Select(candidate => new PhotoSortDateCandidateOptionViewModel(candidate))
            .ToArray();

        SelectedCandidate = CandidateOptions.FirstOrDefault(option =>
            resolution.Value.HasValue
            && option.Value == resolution.Value.Value)
            ?? CandidateOptions.FirstOrDefault();

        if (resolution.Value.HasValue)
        {
            ManualDateText = resolution.Value.Value.ToString(
                "dd.MM.yyyy HH:mm:ss",
                CultureInfo.CurrentCulture);
        }

        UseSuggestedCommand = new AsyncRelayCommand(
            UseSuggestedAsync,
            () => resolution.Value.HasValue);

        UseSelectedCandidateCommand = new AsyncRelayCommand(
            UseSelectedCandidateAsync,
            () => SelectedCandidate is not null);

        UseManualDateCommand = new AsyncRelayCommand(UseManualDateAsync);

        SortAsUnclearCommand = new AsyncRelayCommand(
            () => _applyDecisionAsync(
                Path,
                MediaSortDateDecision.SortAsUnclear()));

        SkipCommand = new AsyncRelayCommand(
            () => _applyDecisionAsync(
                Path,
                MediaSortDateDecision.Skip()));

        OpenFileCommand = new RelayCommand(
            () => _openFile(Path));
    }

    public string Path { get; }

    public string FileName { get; }

    public MediaDateResolution Resolution { get; }

    public IReadOnlyList<PhotoSortDateCandidateOptionViewModel> CandidateOptions { get; }

    public bool HasSuggestedDate => Resolution.Value.HasValue;

    public bool HasMultipleCandidates => CandidateOptions.Count > 1;

    public string SuggestedDateText =>
        Resolution.Value.HasValue
            ? Resolution.Value.Value.ToString("dd.MM.yyyy, HH:mm:ss")
            : "Kein eindeutiges Datum gefunden";

    public string ExplanationText =>
        Resolution.Value.HasValue
            ? "Elbwald Digital hat ein Datum ausgewählt, ist sich bei dieser Datei aber nicht sicher genug. Du entscheidest, welches Datum für den Zielordner verwendet wird."
            : "Für diese Datei wurde kein ausreichend verlässliches Aufnahmedatum gefunden.";

    public PhotoSortDateCandidateOptionViewModel? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            if (SetProperty(ref _selectedCandidate, value))
            {
                UseSelectedCandidateCommand?.NotifyCanExecuteChanged();
            }
        }
    }

    public string ManualDateText
    {
        get => _manualDateText;
        set => SetProperty(ref _manualDateText, value);
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public IAsyncRelayCommand UseSuggestedCommand { get; }

    public IAsyncRelayCommand UseSelectedCandidateCommand { get; }

    public IAsyncRelayCommand UseManualDateCommand { get; }

    public IAsyncRelayCommand SortAsUnclearCommand { get; }

    public IAsyncRelayCommand SkipCommand { get; }

    public IRelayCommand OpenFileCommand { get; }

    private Task UseSuggestedAsync()
    {
        if (!Resolution.Value.HasValue)
        {
            return Task.CompletedTask;
        }

        return _applyDecisionAsync(
            Path,
            MediaSortDateDecision.UseDate(
                Resolution.Value.Value,
                "Vorschlag für die Sortierung vom Nutzer bestätigt."));
    }

    private Task UseSelectedCandidateAsync()
    {
        if (SelectedCandidate is null)
        {
            return Task.CompletedTask;
        }

        return _applyDecisionAsync(
            Path,
            MediaSortDateDecision.UseDate(
                SelectedCandidate.Value,
                $"{SelectedCandidate.SourceLabel} wurde vom Nutzer für die Sortierung gewählt."));
    }

    private async Task UseManualDateAsync()
    {
        if (!DateTime.TryParse(
                ManualDateText,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsed))
        {
            ValidationMessage =
                "Bitte ein Datum wie 16.05.2021 09:27 eingeben.";
            return;
        }

        ValidationMessage = string.Empty;

        await _applyDecisionAsync(
            Path,
            MediaSortDateDecision.UseDate(
                parsed,
                "Manuell eingegebenes Datum wurde für die Sortierung gewählt."));
    }
}
