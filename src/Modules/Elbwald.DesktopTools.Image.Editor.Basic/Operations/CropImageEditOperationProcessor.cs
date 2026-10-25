using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Media.Decoding;
using Elbwald.DesktopTools.Contracts.Media.Editing;

namespace Elbwald.DesktopTools.Image.Editor.Basic.Operations;

/// <summary>
/// Applies a rectangular crop whose coordinates are normalized to the image frame
/// that reaches this operation in the edit pipeline.
/// </summary>
public sealed class CropImageEditOperationProcessor : IImageEditOperationProcessor
{
    public const int SupportedVersion = 1;

    public string ProcessorId => "elbwald.image.editor.basic.crop.v1";

    public int Priority => 100;

    public bool CanProcess(ImageEditOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return operation.Version == SupportedVersion
            && string.Equals(
                operation.TypeId,
                BasicImageEditOperationTypeIds.Crop,
                StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask<DecodedImageFrame> ProcessAsync(
        DecodedImageFrame source,
        ImageEditOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanProcess(operation))
        {
            throw new InvalidOperationException(
                $"Prozessor '{ProcessorId}' kann Operation '{operation.TypeId}' "
                + $"Version {operation.Version} nicht verarbeiten.");
        }

        var parameters = DeserializeParameters(operation.ParametersJson);
        var result = Crop(source, parameters, cancellationToken);

        return ValueTask.FromResult(result);
    }

    private static CropParameters DeserializeParameters(string parametersJson)
    {
        CropParameters? parameters;

        try
        {
            parameters = JsonSerializer.Deserialize<CropParameters>(
                parametersJson,
                JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Die Parameter der Zuschneideoperation sind ungültig.",
                exception);
        }

        if (parameters is null)
        {
            throw new InvalidOperationException(
                "Die Parameter der Zuschneideoperation fehlen.");
        }

        ValidateNormalizedRectangle(
            parameters.Left,
            parameters.Top,
            parameters.Width,
            parameters.Height);

        return parameters;
    }

    private static DecodedImageFrame Crop(
        DecodedImageFrame source,
        CropParameters parameters,
        CancellationToken cancellationToken)
    {
        var bytesPerPixel = GetBytesPerPixel(source.PixelFormat);

        var left = Math.Clamp(
            (int)Math.Floor(parameters.Left * source.PixelWidth),
            0,
            source.PixelWidth - 1);
        var top = Math.Clamp(
            (int)Math.Floor(parameters.Top * source.PixelHeight),
            0,
            source.PixelHeight - 1);

        var right = Math.Clamp(
            (int)Math.Ceiling((parameters.Left + parameters.Width) * source.PixelWidth),
            left + 1,
            source.PixelWidth);
        var bottom = Math.Clamp(
            (int)Math.Ceiling((parameters.Top + parameters.Height) * source.PixelHeight),
            top + 1,
            source.PixelHeight);

        var outputWidth = right - left;
        var outputHeight = bottom - top;
        var outputStride = checked(outputWidth * bytesPerPixel);
        var outputPixels = new byte[checked(outputStride * outputHeight)];

        for (var targetY = 0; targetY < outputHeight; targetY++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceOffset = checked((top + targetY) * source.Stride + left * bytesPerPixel);
            var targetOffset = checked(targetY * outputStride);

            Buffer.BlockCopy(
                source.Pixels,
                sourceOffset,
                outputPixels,
                targetOffset,
                outputStride);
        }

        return new DecodedImageFrame(
            outputWidth,
            outputHeight,
            outputStride,
            source.PixelFormat,
            source.AlphaMode,
            source.ColorSpace,
            outputPixels,
            source.IccProfile?.ToArray());
    }

    internal static void ValidateNormalizedRectangle(
        double left,
        double top,
        double width,
        double height)
    {
        if (!double.IsFinite(left)
            || !double.IsFinite(top)
            || !double.IsFinite(width)
            || !double.IsFinite(height))
        {
            throw new InvalidOperationException(
                "Crop-Koordinaten müssen endliche Zahlen sein.");
        }

        if (left < 0d || left >= 1d)
        {
            throw new InvalidOperationException(
                "left muss im Bereich 0 <= left < 1 liegen.");
        }

        if (top < 0d || top >= 1d)
        {
            throw new InvalidOperationException(
                "top muss im Bereich 0 <= top < 1 liegen.");
        }

        if (width <= 0d || width > 1d)
        {
            throw new InvalidOperationException(
                "width muss im Bereich 0 < width <= 1 liegen.");
        }

        if (height <= 0d || height > 1d)
        {
            throw new InvalidOperationException(
                "height muss im Bereich 0 < height <= 1 liegen.");
        }

        const double epsilon = 1e-9;
        if (left + width > 1d + epsilon)
        {
            throw new InvalidOperationException(
                "left + width darf 1 nicht überschreiten.");
        }

        if (top + height > 1d + epsilon)
        {
            throw new InvalidOperationException(
                "top + height darf 1 nicht überschreiten.");
        }
    }

    private static int GetBytesPerPixel(ImagePixelFormat pixelFormat)
    {
        return pixelFormat switch
        {
            ImagePixelFormat.Rgba32 => 4,
            ImagePixelFormat.Bgra32 => 4,
            ImagePixelFormat.Rgba64 => 8,
            _ => throw new NotSupportedException(
                $"Pixelformat '{pixelFormat}' wird von geometrischen Operationen nicht unterstützt.")
        };
    }

    private sealed record CropParameters(
        double Left,
        double Top,
        double Width,
        double Height);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
