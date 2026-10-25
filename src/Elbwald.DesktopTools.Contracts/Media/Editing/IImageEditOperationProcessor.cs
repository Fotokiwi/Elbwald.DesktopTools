using Elbwald.DesktopTools.Contracts.Media.Decoding;

namespace Elbwald.DesktopTools.Contracts.Media.Editing;

/// <summary>
/// Applies one non-destructive edit operation to an already decoded image frame.
/// Implementations must not mutate the supplied source frame or its pixel buffer.
/// </summary>
public interface IImageEditOperationProcessor
{
    /// <summary>
    /// Stable implementation identifier used for deterministic ordering and diagnostics.
    /// </summary>
    string ProcessorId { get; }

    /// <summary>
    /// Higher-priority processors are preferred when more than one implementation
    /// can process the same operation.
    /// </summary>
    int Priority { get; }

    bool CanProcess(ImageEditOperation operation);

    ValueTask<DecodedImageFrame> ProcessAsync(
        DecodedImageFrame source,
        ImageEditOperation operation,
        CancellationToken cancellationToken = default);
}
