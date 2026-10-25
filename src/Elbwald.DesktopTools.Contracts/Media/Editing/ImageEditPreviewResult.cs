using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Decoding;

namespace Elbwald.DesktopTools.Contracts.Media.Editing;

/// <summary>
/// Immutable preview snapshot. SourceFrame is the decoded, correctly oriented preview
/// before edits and can be used for before/after comparison. RenderedFrame contains the
/// edit stack result. Both frames are in-memory copies and never reference source bytes.
/// </summary>
public sealed record ImageEditPreviewResult(
    string MediaItemId,
    MediaFile SourceFile,
    string DecoderProviderId,
    ImageEditDocument Document,
    DecodedImageFrame SourceFrame,
    DecodedImageFrame RenderedFrame)
{
    public bool HasEnabledEdits =>
        Document.Operations.Any(operation => operation.IsEnabled);
}
