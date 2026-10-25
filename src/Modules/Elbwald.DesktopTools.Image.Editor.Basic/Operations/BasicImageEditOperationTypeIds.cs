namespace Elbwald.DesktopTools.Image.Editor.Basic.Operations;

/// <summary>
/// Stable operation type identifiers persisted in edit documents.
/// Never rename an existing ID once released.
/// </summary>
public static class BasicImageEditOperationTypeIds
{
    public const string Rotate = "elbwald.image.edit.rotate";

    public const string Flip = "elbwald.image.edit.flip";

    public const string Crop = "elbwald.image.edit.crop";

    public const string Tone = "elbwald.image.edit.tone";
}
