using Elbwald.DesktopTools.Contracts.Media.Decoding;
using Elbwald.DesktopTools.Contracts.Media.Editing;

namespace Elbwald.DesktopTools.Image.Editor.Basic.Rendering;

/// <summary>
/// Sequentially renders enabled edit operations. Unknown enabled operations are
/// deliberately not ignored: this prevents silently exporting a visually different
/// result when an operation module is missing.
/// </summary>
public sealed class ImageEditRenderer : IImageEditRenderer
{
    private readonly IImageEditOperationRegistry _operationRegistry;

    public ImageEditRenderer(IImageEditOperationRegistry operationRegistry)
    {
        ArgumentNullException.ThrowIfNull(operationRegistry);
        _operationRegistry = operationRegistry;
    }

    public async ValueTask<DecodedImageFrame> RenderAsync(
        DecodedImageFrame source,
        IReadOnlyList<ImageEditOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(operations);

        var current = source;

        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(operation);
            operation.Validate();

            if (!operation.IsEnabled)
            {
                continue;
            }

            var processor = _operationRegistry.FindProcessor(operation)
                ?? throw new ImageEditOperationUnavailableException(operation);

            current = await processor
                .ProcessAsync(current, operation, cancellationToken)
                .ConfigureAwait(false);
        }

        return current;
    }
}
