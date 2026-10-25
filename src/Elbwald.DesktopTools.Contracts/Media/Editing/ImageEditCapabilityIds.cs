namespace Elbwald.DesktopTools.Contracts.Media.Editing;

/// <summary>
/// Capability identifiers for non-destructive image editing services.
/// Concrete editor modules can contribute processors without the host knowing
/// their implementation types.
/// </summary>
public static class ImageEditCapabilityIds
{
    public const string OperationProcessor = "media.image.edit.operation-processor";

    public const string Renderer = "media.image.edit.renderer";

    public const string PreviewService = "media.image.edit.preview-service";
}
