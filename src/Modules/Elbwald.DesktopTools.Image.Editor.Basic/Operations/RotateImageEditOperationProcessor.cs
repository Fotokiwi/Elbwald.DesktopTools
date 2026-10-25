using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Media.Decoding;
using Elbwald.DesktopTools.Contracts.Media.Editing;

namespace Elbwald.DesktopTools.Image.Editor.Basic.Operations;

public sealed class RotateImageEditOperationProcessor : IImageEditOperationProcessor
{
    public const int SupportedVersion = 1;

    public string ProcessorId => "elbwald.image.editor.basic.rotate.v1";

    public int Priority => 100;

    public bool CanProcess(ImageEditOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return operation.Version == SupportedVersion
            && string.Equals(
                operation.TypeId,
                BasicImageEditOperationTypeIds.Rotate,
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
        var result = Rotate(source, parameters.QuarterTurnsClockwise, cancellationToken);

        return ValueTask.FromResult(result);
    }

    private static RotateParameters DeserializeParameters(string parametersJson)
    {
        RotateParameters? parameters;

        try
        {
            parameters = JsonSerializer.Deserialize<RotateParameters>(
                parametersJson,
                JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Die Parameter der Drehoperation sind ungültig.",
                exception);
        }

        if (parameters is null || parameters.QuarterTurnsClockwise is < 1 or > 3)
        {
            throw new InvalidOperationException(
                "quarterTurnsClockwise muss 1, 2 oder 3 sein.");
        }

        return parameters;
    }

    private static DecodedImageFrame Rotate(
        DecodedImageFrame source,
        int quarterTurnsClockwise,
        CancellationToken cancellationToken)
    {
        var bytesPerPixel = GetBytesPerPixel(source.PixelFormat);
        var outputWidth = quarterTurnsClockwise is 1 or 3
            ? source.PixelHeight
            : source.PixelWidth;
        var outputHeight = quarterTurnsClockwise is 1 or 3
            ? source.PixelWidth
            : source.PixelHeight;
        var outputStride = checked(outputWidth * bytesPerPixel);
        var outputPixels = new byte[checked(outputStride * outputHeight)];

        for (var sourceY = 0; sourceY < source.PixelHeight; sourceY++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var sourceX = 0; sourceX < source.PixelWidth; sourceX++)
            {
                var (targetX, targetY) = quarterTurnsClockwise switch
                {
                    1 => (source.PixelHeight - 1 - sourceY, sourceX),
                    2 => (source.PixelWidth - 1 - sourceX, source.PixelHeight - 1 - sourceY),
                    3 => (sourceY, source.PixelWidth - 1 - sourceX),
                    _ => throw new ArgumentOutOfRangeException(nameof(quarterTurnsClockwise))
                };

                CopyPixel(
                    source.Pixels,
                    checked(sourceY * source.Stride + sourceX * bytesPerPixel),
                    outputPixels,
                    checked(targetY * outputStride + targetX * bytesPerPixel),
                    bytesPerPixel);
            }
        }

        return CreateFrame(source, outputWidth, outputHeight, outputStride, outputPixels);
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

    private static void CopyPixel(
        byte[] source,
        int sourceOffset,
        byte[] target,
        int targetOffset,
        int bytesPerPixel)
    {
        Buffer.BlockCopy(source, sourceOffset, target, targetOffset, bytesPerPixel);
    }

    private static DecodedImageFrame CreateFrame(
        DecodedImageFrame source,
        int width,
        int height,
        int stride,
        byte[] pixels)
    {
        return new DecodedImageFrame(
            width,
            height,
            stride,
            source.PixelFormat,
            source.AlphaMode,
            source.ColorSpace,
            pixels,
            source.IccProfile?.ToArray());
    }

    private sealed record RotateParameters(int QuarterTurnsClockwise);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
