using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.Diagnostics;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class DiagnosticsLogViewModel
    : ObservableObject
{
    private readonly IDiagnosticEventStore _diagnosticEventStore;

    [ObservableProperty]
    private IReadOnlyList<DiagnosticEventItemViewModel> events =
        Array.Empty<DiagnosticEventItemViewModel>();

    [ObservableProperty]
    private string selectedSeverity = "Alle";

    [ObservableProperty]
    private string selectedCategory = "Alle";

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string statusMessage =
        "Protokoll wird geladen …";

    [ObservableProperty]
    private bool isBusy;

    public DiagnosticsLogViewModel(
        IDiagnosticEventStore diagnosticEventStore)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEventStore);
        _diagnosticEventStore = diagnosticEventStore;

        RefreshCommand =
            new AsyncRelayCommand(
                RefreshAsync,
                () => !IsBusy);

        _ = RefreshAsync();
    }

    public IReadOnlyList<string> SeverityOptions { get; } =
        new[]
        {
            "Alle",
            "Warnung+",
            "Fehler+",
            "Kritisch"
        };

    public IReadOnlyList<string> CategoryOptions { get; } =
        new[]
        {
            "Alle",
            "Datenträger",
            "Dateioperation",
            "Recovery",
            "Anwendung"
        };

    public IAsyncRelayCommand RefreshCommand { get; }

    public bool HasEvents =>
        Events.Count > 0;

    public bool HasNoEvents =>
        !IsBusy
        && Events.Count == 0;

    partial void OnIsBusyChanged(
        bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasNoEvents));
    }

    partial void OnEventsChanged(
        IReadOnlyList<DiagnosticEventItemViewModel> value)
    {
        OnPropertyChanged(nameof(HasEvents));
        OnPropertyChanged(nameof(HasNoEvents));
    }

    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage =
            "Protokoll wird geladen …";

        try
        {
            var query =
                new DiagnosticEventQuery(
                    ParseMinimumSeverity(
                        SelectedSeverity),
                    ParseCategory(
                        SelectedCategory),
                    string.IsNullOrWhiteSpace(SearchText)
                        ? null
                        : SearchText.Trim(),
                    Limit: 250);

            var result =
                await _diagnosticEventStore.QueryAsync(
                    query);

            Events =
                result
                    .Select(entry =>
                        new DiagnosticEventItemViewModel(entry))
                    .ToArray();

            StatusMessage =
                Events.Count == 0
                    ? "Keine passenden Ereignisse gefunden."
                    : $"{Events.Count:N0} Ereignis(se) · neueste zuerst · maximal 250 angezeigt.";
        }
        catch (Exception exception)
        {
            Events =
                Array.Empty<DiagnosticEventItemViewModel>();

            StatusMessage =
                "Das Diagnoseprotokoll konnte nicht gelesen werden: "
                + exception.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static DiagnosticEventSeverity? ParseMinimumSeverity(
        string selection)
    {
        return selection switch
        {
            "Warnung+" => DiagnosticEventSeverity.Warning,
            "Fehler+" => DiagnosticEventSeverity.Error,
            "Kritisch" => DiagnosticEventSeverity.Critical,
            _ => null
        };
    }

    private static DiagnosticEventCategory? ParseCategory(
        string selection)
    {
        return selection switch
        {
            "Datenträger" => DiagnosticEventCategory.Storage,
            "Dateioperation" => DiagnosticEventCategory.FileOperation,
            "Recovery" => DiagnosticEventCategory.Recovery,
            "Anwendung" => DiagnosticEventCategory.Application,
            _ => null
        };
    }
}
