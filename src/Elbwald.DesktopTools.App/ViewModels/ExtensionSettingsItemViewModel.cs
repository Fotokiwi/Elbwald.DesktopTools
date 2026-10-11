using CommunityToolkit.Mvvm.ComponentModel;
using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class ExtensionSettingsItemViewModel : ObservableObject
{
    private readonly IExtensionActivationService _activationService;
    private bool _isEnabled;
    private string _statusText = string.Empty;

    public ExtensionSettingsItemViewModel(
        ExtensionDescriptor descriptor,
        IExtensionActivationService activationService)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(activationService);

        _activationService = activationService;

        Id = descriptor.Id;
        Name = descriptor.Name;
        Description = descriptor.Description;
        Version = descriptor.Version;
        Capabilities = descriptor.Capabilities;
        Permissions = descriptor.Permissions;
        _isEnabled = activationService.IsEnabled(descriptor.Id);
        _statusText = BuildStatusText(_isEnabled);
    }

    public string Id { get; }

    public string Name { get; }

    public string Description { get; }

    public string Version { get; }

    public IReadOnlyList<string> Capabilities { get; }

    public IReadOnlyList<string> Permissions { get; }

    public string CapabilitiesText =>
        Capabilities.Count == 0
            ? "Keine deklarierten Capabilities"
            : string.Join(" · ", Capabilities);

    public string PermissionsText =>
        Permissions.Count == 0
            ? "Keine zusätzlichen Berechtigungen"
            : "Berechtigungen: " + string.Join(", ", Permissions);

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            try
            {
                _activationService.SetEnabled(Id, value);
                SetProperty(ref _isEnabled, value);
                StatusText = BuildStatusText(value);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidOperationException)
            {
                StatusText = "Änderung konnte nicht gespeichert werden: " + exception.Message;
                OnPropertyChanged(nameof(IsEnabled));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private static string BuildStatusText(bool enabled) =>
        enabled
            ? "Aktiv · Änderung wirkt sofort"
            : "Inaktiv · Fähigkeiten werden nicht angeboten";
}
