using Elbwald.DesktopTools.Contracts.Media.Editing;
using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.Image.Editor.Basic.Rendering;

/// <summary>
/// Resolves edit processors from active extension capabilities. This keeps the
/// Basic editor open for later operation packages without hard references.
/// </summary>
public sealed class ImageEditOperationRegistry : IImageEditOperationRegistry, IDisposable
{
    private readonly IExtensionRegistry _extensionRegistry;
    private IImageEditOperationProcessor[] _processors;
    private bool _disposed;

    public ImageEditOperationRegistry(IExtensionRegistry extensionRegistry)
    {
        ArgumentNullException.ThrowIfNull(extensionRegistry);

        _extensionRegistry = extensionRegistry;
        _processors = BuildSnapshot();
        _extensionRegistry.ActivationChanged += OnActivationChanged;
    }

    public event EventHandler? ProcessorsChanged;

    public IReadOnlyList<IImageEditOperationProcessor> Processors => _processors;

    public IImageEditOperationProcessor? FindProcessor(ImageEditOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return _processors.FirstOrDefault(processor => processor.CanProcess(operation));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _extensionRegistry.ActivationChanged -= OnActivationChanged;
        _disposed = true;
    }

    private void OnActivationChanged(
        object? sender,
        ExtensionActivationChangedEventArgs args)
    {
        _processors = BuildSnapshot();
        ProcessorsChanged?.Invoke(this, EventArgs.Empty);
    }

    private IImageEditOperationProcessor[] BuildSnapshot()
    {
        return _extensionRegistry
            .GetProviders<IImageEditOperationProcessor>(ImageEditCapabilityIds.OperationProcessor)
            .Select(ValidateProcessor)
            .OrderByDescending(processor => processor.Priority)
            .ThenBy(processor => processor.ProcessorId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(processor => processor.GetType().FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IImageEditOperationProcessor ValidateProcessor(
        IImageEditOperationProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);

        if (string.IsNullOrWhiteSpace(processor.ProcessorId))
        {
            throw new InvalidOperationException(
                $"Bildbearbeitungsprozessor '{processor.GetType().FullName}' hat keine Processor-ID.");
        }

        return processor;
    }
}
