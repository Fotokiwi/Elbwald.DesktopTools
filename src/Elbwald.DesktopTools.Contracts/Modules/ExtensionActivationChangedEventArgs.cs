namespace Elbwald.DesktopTools.Contracts.Modules;

public sealed class ExtensionActivationChangedEventArgs : EventArgs
{
    public ExtensionActivationChangedEventArgs(
        string extensionId,
        bool isEnabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);

        ExtensionId = extensionId.Trim();
        IsEnabled = isEnabled;
    }

    public string ExtensionId { get; }

    public bool IsEnabled { get; }
}
