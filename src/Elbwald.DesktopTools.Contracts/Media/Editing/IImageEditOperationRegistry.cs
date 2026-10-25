namespace Elbwald.DesktopTools.Contracts.Media.Editing;

/// <summary>
/// Dynamic view over all currently active image-edit operation processors.
/// </summary>
public interface IImageEditOperationRegistry
{
    event EventHandler? ProcessorsChanged;

    IReadOnlyList<IImageEditOperationProcessor> Processors { get; }

    IImageEditOperationProcessor? FindProcessor(ImageEditOperation operation);
}
