using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Media.Decoding;
using Elbwald.DesktopTools.Contracts.Media.Editing;

namespace Elbwald.DesktopTools.Image.Editor.Basic.Operations;

public sealed class FlipImageEditOperationProcessor : IImageEditOperationProcessor
{
    public const int SupportedVersion = 1;

    public string ProcessorId => "elbwald.image.editor.basic.flip.v1";

    public int Priority => 100;

    public bool CanProcess(ImageEditOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return operation.Version == SupportedVersion
            && string.Equals(
                operation.TypeId,
                BasicImageEditOperationTypeIds.Flip,
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
        var result = Flip(source, parameters.Axis, cancellationToken);

        return ValueTask.FromResult(result);
    }

    private static FlipParameters DeserializeParameters(string parametersJson)
    {
        FlipParameters? parameters;

        try
        {
            parameters = JsonSerializer.Deserialize<FlipParameters>(
                parametersJson,
                JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Die Parameter der Spiegeloperation sind ungültig.",
                exception);
        }

        if (parameters is null
            || !string.Equals(parameters.Axis, "horizontal", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parameters.Axis, "vertical", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "axis muss 'horizontal' oder 'vertical' sein.");
        }

        return parameters;
    }

    private static DecodedImageFrame Flip(
        DecodedImageFrame source,
        string axis,
        CancellationToken cancellationToken)
    {
        var bytesPerPixel = GetBytesPerPixel(source.PixelFormat);
        var outputStride = checked(source.PixelWidth * bytesPerPixel);
        var outputPixels = new byte[checked(outputStride * source.PixelHeight)];
        var horizontal = string.Equals(axis, "horizontal", StringComparison.OrdinalIgnoreCase);

        for (var sourceY = 0; sourceY < source.PixelHeight; sourceY++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var sourceX = 0; sourceX < source.PixelWidth; sourceX++)
            {
                var targetX = horizontal
                    ? source.PixelWidth - 1 - sourceX
                    : sourceX;
                var targetY = horizontal
                    ? sourceY
                    : source.PixelHeight - 1 - sourceY;

                Buffer.BlockCopy(
                    source.Pixels,
                    checked(sourceY * source.Stride + sourceX * bytesPerPixel),
                    outputPixels,
                    checked(targetY * outputStride + targetX * bytesPerPixel),
                    bytesPerPixel);
            }
        }

        return new DecodedImageFrame(
            source.PixelWidth,
            source.PixelHeight,
            outputStride,
            source.PixelFormat,
            source.AlphaMode,
            source.ColorSpace,
            outputPixels,
            source.IccProfile?.ToArray());
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

    private sealed record FlipParameters(string Axis);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
