using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Media.Editing;

namespace Elbwald.DesktopTools.Image.Editor.Basic.Operations;

/// <summary>
/// Creates stable persisted operation records for the Basic editor UI.
/// </summary>
public static class BasicImageEditOperations
{
    public static ImageEditOperation RotateClockwise(
        int quarterTurns = 1,
        string? operationId = null)
    {
        if (quarterTurns is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quarterTurns),
                "quarterTurns muss 1, 2 oder 3 sein.");
        }

        return new ImageEditOperation(
            operationId ?? Guid.NewGuid().ToString("N"),
            BasicImageEditOperationTypeIds.Rotate,
            RotateImageEditOperationProcessor.SupportedVersion,
            true,
            JsonSerializer.Serialize(
                new
                {
                    quarterTurnsClockwise = quarterTurns
                }));
    }

    public static ImageEditOperation FlipHorizontal(string? operationId = null)
    {
        return CreateFlip("horizontal", operationId);
    }

    public static ImageEditOperation FlipVertical(string? operationId = null)
    {
        return CreateFlip("vertical", operationId);
    }

    /// <summary>
    /// Creates a crop relative to the frame at this position in the edit stack.
    /// Coordinates are normalized to 0..1 so the same operation can be used for
    /// preview-size and full-resolution rendering.
    /// </summary>
    public static ImageEditOperation Crop(
        double left,
        double top,
        double width,
        double height,
        string? operationId = null)
    {
        CropImageEditOperationProcessor.ValidateNormalizedRectangle(
            left,
            top,
            width,
            height);

        return new ImageEditOperation(
            operationId ?? Guid.NewGuid().ToString("N"),
            BasicImageEditOperationTypeIds.Crop,
            CropImageEditOperationProcessor.SupportedVersion,
            true,
            JsonSerializer.Serialize(
                new
                {
                    left,
                    top,
                    width,
                    height
                }));
    }


    /// <summary>
    /// Creates one basic photographic light/color adjustment step. Exposure is measured
    /// in EV; all remaining controls use the range -100..+100.
    /// </summary>
    public static ImageEditOperation Tone(
        double exposureEv,
        double contrast,
        double saturation,
        double temperature,
        double tint,
        double shadows,
        double highlights,
        string? operationId = null)
    {
        ToneImageEditOperationProcessor.ValidateParameters(
            exposureEv,
            contrast,
            saturation,
            temperature,
            tint,
            shadows,
            highlights);

        return new ImageEditOperation(
            operationId ?? Guid.NewGuid().ToString("N"),
            BasicImageEditOperationTypeIds.Tone,
            ToneImageEditOperationProcessor.SupportedVersion,
            true,
            JsonSerializer.Serialize(
                new
                {
                    exposureEv,
                    contrast,
                    saturation,
                    temperature,
                    tint,
                    shadows,
                    highlights
                }));
    }

    private static ImageEditOperation CreateFlip(string axis, string? operationId)
    {
        return new ImageEditOperation(
            operationId ?? Guid.NewGuid().ToString("N"),
            BasicImageEditOperationTypeIds.Flip,
            FlipImageEditOperationProcessor.SupportedVersion,
            true,
            JsonSerializer.Serialize(new { axis }));
    }
}
