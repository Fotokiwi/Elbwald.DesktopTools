using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Media.Decoding;
using Elbwald.DesktopTools.Contracts.Media.Editing;

namespace Elbwald.DesktopTools.Image.Editor.Basic.Operations;

/// <summary>
/// Applies the basic photographic light/color adjustment step without modifying
/// the source frame. Version 2 extends the original exposure/contrast/saturation
/// step with white balance, shadows and highlights while remaining compatible
/// with persisted version-1 operations.
/// </summary>
public sealed class ToneImageEditOperationProcessor : IImageEditOperationProcessor
{
    public const int LegacyVersion = 1;
    public const int SupportedVersion = 2;

    private const int LinearToSrgbLutMaximumIndex = 65535;

    private static readonly double[] SrgbByteToLinearLut = BuildSrgbByteToLinearLut();
    private static readonly byte[] LinearToSrgbByteLut = BuildLinearToSrgbByteLut();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string ProcessorId => "elbwald.image.editor.basic.tone.v2";

    public int Priority => 100;

    public bool CanProcess(ImageEditOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return (operation.Version is LegacyVersion or SupportedVersion)
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

        var parameters = DeserializeParameters(operation);
        var result = Apply(source, parameters, cancellationToken);
        return ValueTask.FromResult(result);
    }

    internal static void ValidateParameters(
        double exposureEv,
        double contrast,
        double saturation,
        double temperature,
        double tint,
        double shadows,
        double highlights)
    {
        if (!double.IsFinite(exposureEv)
            || !double.IsFinite(contrast)
            || !double.IsFinite(saturation)
            || !double.IsFinite(temperature)
            || !double.IsFinite(tint)
            || !double.IsFinite(shadows)
            || !double.IsFinite(highlights))
        {
            throw new InvalidOperationException("Tonwertparameter müssen endliche Zahlen sein.");
        }

        if (exposureEv is < -5d or > 5d)
        {
            throw new InvalidOperationException("Belichtung muss zwischen -5 und +5 EV liegen.");
        }

        ValidatePercentParameter(contrast, "Kontrast");
        ValidatePercentParameter(saturation, "Sättigung");
        ValidatePercentParameter(temperature, "Temperatur");
        ValidatePercentParameter(tint, "Farbton");
        ValidatePercentParameter(shadows, "Schatten");
        ValidatePercentParameter(highlights, "Lichter");
    }

    internal static bool TryReadParameters(
        ImageEditOperation operation,
        out double exposureEv,
        out double contrast,
        out double saturation,
        out double temperature,
        out double tint,
        out double shadows,
        out double highlights)
    {
        exposureEv = 0d;
        contrast = 0d;
        saturation = 0d;
        temperature = 0d;
        tint = 0d;
        shadows = 0d;
        highlights = 0d;

        if (!string.Equals(
                operation.TypeId,
                BasicImageEditOperationTypeIds.Tone,
                StringComparison.OrdinalIgnoreCase)
            || operation.Version is not (LegacyVersion or SupportedVersion))
        {
            return false;
        }

        var parameters = DeserializeParameters(operation);
        exposureEv = parameters.ExposureEv;
        contrast = parameters.Contrast;
        saturation = parameters.Saturation;
        temperature = parameters.Temperature;
        tint = parameters.Tint;
        shadows = parameters.Shadows;
        highlights = parameters.Highlights;
        return true;
    }

    private static ToneParameters DeserializeParameters(ImageEditOperation operation)
    {
        try
        {
            if (operation.Version == LegacyVersion)
            {
                var legacy = JsonSerializer.Deserialize<LegacyToneParameters>(
                    operation.ParametersJson,
                    JsonOptions)
                    ?? throw new InvalidOperationException("Die Parameter der Tonwertoperation fehlen.");

                var migrated = new ToneParameters(
                    legacy.ExposureEv,
                    legacy.Contrast,
                    legacy.Saturation,
                    0d,
                    0d,
                    0d,
                    0d);

                Validate(migrated);
                return migrated;
            }

            var parameters = JsonSerializer.Deserialize<ToneParameters>(
                operation.ParametersJson,
                JsonOptions)
                ?? throw new InvalidOperationException("Die Parameter der Tonwertoperation fehlen.");

            Validate(parameters);
            return parameters;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Die Parameter der Tonwertoperation sind ungültig.",
                exception);
        }
    }

    private static void Validate(ToneParameters parameters)
    {
        ValidateParameters(
            parameters.ExposureEv,
            parameters.Contrast,
            parameters.Saturation,
            parameters.Temperature,
            parameters.Tint,
            parameters.Shadows,
            parameters.Highlights);
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

        var sourceIsLinear = source.ColorSpace == ImageColorSpace.LinearSrgb;
        var isPremultiplied = source.AlphaMode == ImageAlphaMode.Premultiplied;

        if (!sourceIsLinear && !isPremultiplied)
        {
            return ApplySrgbStraightAlphaFast(source, parameters, cancellationToken);
        }

        var pixels = source.Pixels.ToArray();
        var transforms = ToneTransforms.Create(parameters);
        var isBgra = source.PixelFormat == ImagePixelFormat.Bgra32;

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

                ApplyLinearTone(ref red, ref green, ref blue, transforms);

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

        return CloneFrameWithPixels(source, pixels);
    }

    private static DecodedImageFrame ApplySrgbStraightAlphaFast(
        DecodedImageFrame source,
        ToneParameters parameters,
        CancellationToken cancellationToken)
    {
        var pixels = source.Pixels.ToArray();
        var transforms = ToneTransforms.Create(parameters);
        var isBgra = source.PixelFormat == ImagePixelFormat.Bgra32;

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

                var red = SrgbByteToLinearLut[pixels[redIndex]];
                var green = SrgbByteToLinearLut[pixels[greenIndex]];
                var blue = SrgbByteToLinearLut[pixels[blueIndex]];

                ApplyLinearTone(ref red, ref green, ref blue, transforms);

                pixels[redIndex] = LinearToSrgbByte(red);
                pixels[greenIndex] = LinearToSrgbByte(green);
                pixels[blueIndex] = LinearToSrgbByte(blue);
            }
        }

        return CloneFrameWithPixels(source, pixels);
    }

    private static void ApplyLinearTone(
        ref double red,
        ref double green,
        ref double blue,
        ToneTransforms transforms)
    {
        red *= transforms.ExposureFactor * transforms.RedWhiteBalanceGain;
        green *= transforms.ExposureFactor * transforms.GreenWhiteBalanceGain;
        blue *= transforms.ExposureFactor * transforms.BlueWhiteBalanceGain;

        var luminance = Luminance(red, green, blue);
        var shadowWeight = 1d - SmoothStep(0.06d, 0.58d, luminance);
        var highlightWeight = SmoothStep(0.42d, 0.96d, luminance);

        ApplyRangeAdjustment(ref red, transforms.ShadowAmount * shadowWeight);
        ApplyRangeAdjustment(ref green, transforms.ShadowAmount * shadowWeight);
        ApplyRangeAdjustment(ref blue, transforms.ShadowAmount * shadowWeight);

        ApplyRangeAdjustment(ref red, transforms.HighlightAmount * highlightWeight);
        ApplyRangeAdjustment(ref green, transforms.HighlightAmount * highlightWeight);
        ApplyRangeAdjustment(ref blue, transforms.HighlightAmount * highlightWeight);

        const double contrastPivot = 0.18d;
        red = (red - contrastPivot) * transforms.ContrastFactor + contrastPivot;
        green = (green - contrastPivot) * transforms.ContrastFactor + contrastPivot;
        blue = (blue - contrastPivot) * transforms.ContrastFactor + contrastPivot;

        luminance = Luminance(red, green, blue);
        red = luminance + (red - luminance) * transforms.SaturationFactor;
        green = luminance + (green - luminance) * transforms.SaturationFactor;
        blue = luminance + (blue - luminance) * transforms.SaturationFactor;

        red = Math.Clamp(red, 0d, 1d);
        green = Math.Clamp(green, 0d, 1d);
        blue = Math.Clamp(blue, 0d, 1d);
    }

    private static void ApplyRangeAdjustment(ref double channel, double amount)
    {
        if (amount >= 0d)
        {
            if (channel < 1d)
            {
                channel += (1d - channel) * amount;
            }
        }
        else
        {
            channel *= 1d + amount;
        }
    }

    private static double Luminance(double red, double green, double blue)
    {
        return 0.2126d * red + 0.7152d * green + 0.0722d * blue;
    }

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0d, 1d);
        return t * t * (3d - 2d * t);
    }

    private static DecodedImageFrame CloneFrameWithPixels(DecodedImageFrame source, byte[] pixels)
    {
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

    private static byte LinearToSrgbByte(double linearValue)
    {
        var index = Math.Clamp(
            (int)Math.Round(Math.Clamp(linearValue, 0d, 1d) * LinearToSrgbLutMaximumIndex),
            0,
            LinearToSrgbLutMaximumIndex);
        return LinearToSrgbByteLut[index];
    }

    private static double[] BuildSrgbByteToLinearLut()
    {
        var lut = new double[256];
        for (var index = 0; index < lut.Length; index++)
        {
            lut[index] = SrgbToLinear(index / 255d);
        }

        return lut;
    }

    private static byte[] BuildLinearToSrgbByteLut()
    {
        var lut = new byte[LinearToSrgbLutMaximumIndex + 1];
        for (var index = 0; index < lut.Length; index++)
        {
            var linear = index / (double)LinearToSrgbLutMaximumIndex;
            lut[index] = ToByte(LinearToSrgb(linear));
        }

        return lut;
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
        var clamped = Math.Clamp(value, 0d, 1d);
        return clamped <= 0.0031308d
            ? clamped * 12.92d
            : 1.055d * Math.Pow(clamped, 1d / 2.4d) - 0.055d;
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(Math.Clamp(value, 0d, 1d) * 255d), 0, 255);
    }

    private static void ValidatePercentParameter(double value, string name)
    {
        if (value is < -100d or > 100d)
        {
            throw new InvalidOperationException($"{name} muss zwischen -100 und +100 liegen.");
        }
    }

    private sealed record LegacyToneParameters(
        double ExposureEv,
        double Contrast,
        double Saturation);

    private sealed record ToneParameters(
        double ExposureEv,
        double Contrast,
        double Saturation,
        double Temperature,
        double Tint,
        double Shadows,
        double Highlights);

    private readonly record struct ToneTransforms(
        double ExposureFactor,
        double ContrastFactor,
        double SaturationFactor,
        double RedWhiteBalanceGain,
        double GreenWhiteBalanceGain,
        double BlueWhiteBalanceGain,
        double ShadowAmount,
        double HighlightAmount)
    {
        public static ToneTransforms Create(ToneParameters parameters)
        {
            var temperature = parameters.Temperature / 100d;
            var tint = parameters.Tint / 100d;

            // Deliberately moderate gains: the UI range remains useful without
            // turning normal photos into extreme channel clipping too quickly.
            var redGain = 1d + 0.24d * temperature + 0.06d * tint;
            var greenGain = 1d - 0.12d * tint;
            var blueGain = 1d - 0.24d * temperature + 0.06d * tint;

            return new ToneTransforms(
                Math.Pow(2d, parameters.ExposureEv),
                GetContrastFactor(parameters.Contrast),
                1d + parameters.Saturation / 100d,
                Math.Clamp(redGain, 0.55d, 1.45d),
                Math.Clamp(greenGain, 0.70d, 1.30d),
                Math.Clamp(blueGain, 0.55d, 1.45d),
                parameters.Shadows / 100d * 0.42d,
                parameters.Highlights / 100d * 0.42d);
        }
    }
}
