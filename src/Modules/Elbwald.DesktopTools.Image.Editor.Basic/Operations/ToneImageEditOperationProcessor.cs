using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Media.Decoding;
using Elbwald.DesktopTools.Contracts.Media.Editing;

namespace Elbwald.DesktopTools.Image.Editor.Basic.Operations;

/// <summary>
/// Applies basic photographic tone adjustments without modifying the source frame.
/// Exposure is evaluated in linear light; contrast uses a linear 18 % gray pivot and
/// saturation interpolates around Rec.709 luminance.
/// </summary>
public sealed class ToneImageEditOperationProcessor : IImageEditOperationProcessor
{
    public const int SupportedVersion = 1;

    public string ProcessorId => "elbwald.image.editor.basic.tone.v1";

    public int Priority => 100;

    public bool CanProcess(ImageEditOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return operation.Version == SupportedVersion
            && string.Equals(
                operation.TypeId,
                BasicImageEditOperationTypeIds.Tone,
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
        var result = Apply(source, parameters, cancellationToken);
        return ValueTask.FromResult(result);
    }

    internal static void ValidateParameters(
        double exposureEv,
        double contrast,
        double saturation)
    {
        if (!double.IsFinite(exposureEv)
            || !double.IsFinite(contrast)
            || !double.IsFinite(saturation))
        {
            throw new InvalidOperationException("Tonwertparameter müssen endliche Zahlen sein.");
        }

        if (exposureEv is < -5d or > 5d)
        {
            throw new InvalidOperationException("Belichtung muss zwischen -5 und +5 EV liegen.");
        }

        if (contrast is < -100d or > 100d)
        {
            throw new InvalidOperationException("Kontrast muss zwischen -100 und +100 liegen.");
        }

        if (saturation is < -100d or > 100d)
        {
            throw new InvalidOperationException("Sättigung muss zwischen -100 und +100 liegen.");
        }
    }

    internal static bool TryReadParameters(
        ImageEditOperation operation,
        out double exposureEv,
        out double contrast,
        out double saturation)
    {
        exposureEv = 0d;
        contrast = 0d;
        saturation = 0d;

        if (operation.Version != SupportedVersion
            || !string.Equals(
                operation.TypeId,
                BasicImageEditOperationTypeIds.Tone,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parameters = DeserializeParameters(operation.ParametersJson);
        exposureEv = parameters.ExposureEv;
        contrast = parameters.Contrast;
        saturation = parameters.Saturation;
        return true;
    }

    private static ToneParameters DeserializeParameters(string parametersJson)
    {
        ToneParameters? parameters;

        try
        {
            parameters = JsonSerializer.Deserialize<ToneParameters>(parametersJson, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Die Parameter der Tonwertoperation sind ungültig.",
                exception);
        }

        if (parameters is null)
        {
            throw new InvalidOperationException("Die Parameter der Tonwertoperation fehlen.");
        }

        ValidateParameters(parameters.ExposureEv, parameters.Contrast, parameters.Saturation);
        return parameters;
    }

    private static DecodedImageFrame Apply(
        DecodedImageFrame source,
        ToneParameters parameters,
        CancellationToken cancellationToken)
    {
        if (source.PixelFormat is not ImagePixelFormat.Rgba32 and not ImagePixelFormat.Bgra32)
        {
            throw new NotSupportedException(
                $"Pixelformat '{source.PixelFormat}' wird von den Basis-Tonwerten noch nicht unterstützt.");
        }

        var pixels = source.Pixels.ToArray();
        var exposureFactor = Math.Pow(2d, parameters.ExposureEv);
        var contrastFactor = GetContrastFactor(parameters.Contrast);
        var saturationFactor = 1d + parameters.Saturation / 100d;
        var sourceIsLinear = source.ColorSpace == ImageColorSpace.LinearSrgb;
        var isBgra = source.PixelFormat == ImagePixelFormat.Bgra32;
        var isPremultiplied = source.AlphaMode == ImageAlphaMode.Premultiplied;

        for (var y = 0; y < source.PixelHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowOffset = checked(y * source.Stride);

            for (var x = 0; x < source.PixelWidth; x++)
            {
                var offset = checked(rowOffset + x * 4);
                var redIndex = isBgra ? offset + 2 : offset;
                var greenIndex = offset + 1;
                var blueIndex = isBgra ? offset : offset + 2;
                var alphaIndex = offset + 3;

                var alpha = pixels[alphaIndex] / 255d;
                var red = pixels[redIndex] / 255d;
                var green = pixels[greenIndex] / 255d;
                var blue = pixels[blueIndex] / 255d;

                if (isPremultiplied)
                {
                    if (alpha <= 0d)
                    {
                        pixels[redIndex] = 0;
                        pixels[greenIndex] = 0;
                        pixels[blueIndex] = 0;
                        continue;
                    }

                    red = Math.Clamp(red / alpha, 0d, 1d);
                    green = Math.Clamp(green / alpha, 0d, 1d);
                    blue = Math.Clamp(blue / alpha, 0d, 1d);
                }

                if (!sourceIsLinear)
                {
                    red = SrgbToLinear(red);
                    green = SrgbToLinear(green);
                    blue = SrgbToLinear(blue);
                }

                red *= exposureFactor;
                green *= exposureFactor;
                blue *= exposureFactor;

                const double contrastPivot = 0.18d;
                red = (red - contrastPivot) * contrastFactor + contrastPivot;
                green = (green - contrastPivot) * contrastFactor + contrastPivot;
                blue = (blue - contrastPivot) * contrastFactor + contrastPivot;

                var luminance = 0.2126d * red + 0.7152d * green + 0.0722d * blue;
                red = luminance + (red - luminance) * saturationFactor;
                green = luminance + (green - luminance) * saturationFactor;
                blue = luminance + (blue - luminance) * saturationFactor;

                red = Math.Clamp(red, 0d, 1d);
                green = Math.Clamp(green, 0d, 1d);
                blue = Math.Clamp(blue, 0d, 1d);

                if (!sourceIsLinear)
                {
                    red = LinearToSrgb(red);
                    green = LinearToSrgb(green);
                    blue = LinearToSrgb(blue);
                }

                if (isPremultiplied)
                {
                    red *= alpha;
                    green *= alpha;
                    blue *= alpha;
                }

                pixels[redIndex] = ToByte(red);
                pixels[greenIndex] = ToByte(green);
                pixels[blueIndex] = ToByte(blue);
            }
        }

        return new DecodedImageFrame(
            source.PixelWidth,
            source.PixelHeight,
            source.Stride,
            source.PixelFormat,
            source.AlphaMode,
            source.ColorSpace,
            pixels,
            source.IccProfile?.ToArray());
    }

    private static double GetContrastFactor(double contrast)
    {
        if (contrast < 0d)
        {
            return 1d + contrast / 100d;
        }

        return 1d + 3d * contrast / 100d;
    }

    private static double SrgbToLinear(double value)
    {
        return value <= 0.04045d
            ? value / 12.92d
            : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
    }

    private static double LinearToSrgb(double value)
    {
        return value <= 0.0031308d
            ? value * 12.92d
            : 1.055d * Math.Pow(value, 1d / 2.4d) - 0.055d;
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(value * 255d), 0, 255);
    }

    private sealed record ToneParameters(
        double ExposureEv,
        double Contrast,
        double Saturation);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
