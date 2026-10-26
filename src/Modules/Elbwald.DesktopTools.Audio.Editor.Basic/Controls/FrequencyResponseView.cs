using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Elbwald.DesktopTools.Audio.Editor.Basic.Dsp;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Controls;

public sealed class FrequencyResponseView : Control
{
    public static readonly StyledProperty<int> SampleRateProperty =
        AvaloniaProperty.Register<FrequencyResponseView, int>(nameof(SampleRate), 48_000);

    public static readonly StyledProperty<bool> EqualizerEnabledProperty =
        AvaloniaProperty.Register<FrequencyResponseView, bool>(nameof(EqualizerEnabled));

    public static readonly StyledProperty<double> BassDbProperty =
        AvaloniaProperty.Register<FrequencyResponseView, double>(nameof(BassDb));

    public static readonly StyledProperty<double> MidDbProperty =
        AvaloniaProperty.Register<FrequencyResponseView, double>(nameof(MidDb));

    public static readonly StyledProperty<double> MidFrequencyHzProperty =
        AvaloniaProperty.Register<FrequencyResponseView, double>(nameof(MidFrequencyHz), 1_000);

    public static readonly StyledProperty<double> TrebleDbProperty =
        AvaloniaProperty.Register<FrequencyResponseView, double>(nameof(TrebleDb));

    public static readonly StyledProperty<bool> HighPassEnabledProperty =
        AvaloniaProperty.Register<FrequencyResponseView, bool>(nameof(HighPassEnabled));

    public static readonly StyledProperty<double> HighPassFrequencyHzProperty =
        AvaloniaProperty.Register<FrequencyResponseView, double>(nameof(HighPassFrequencyHz), 80);

    public static readonly StyledProperty<bool> LowPassEnabledProperty =
        AvaloniaProperty.Register<FrequencyResponseView, bool>(nameof(LowPassEnabled));

    public static readonly StyledProperty<double> LowPassFrequencyHzProperty =
        AvaloniaProperty.Register<FrequencyResponseView, double>(nameof(LowPassFrequencyHz), 18_000);

    public static readonly StyledProperty<bool> NotchEnabledProperty =
        AvaloniaProperty.Register<FrequencyResponseView, bool>(nameof(NotchEnabled));

    public static readonly StyledProperty<double> NotchFrequencyHzProperty =
        AvaloniaProperty.Register<FrequencyResponseView, double>(nameof(NotchFrequencyHz), 6_500);

    public static readonly StyledProperty<double> NotchWidthPercentProperty =
        AvaloniaProperty.Register<FrequencyResponseView, double>(nameof(NotchWidthPercent), 30);

    public static readonly StyledProperty<bool> HumRemovalEnabledProperty =
        AvaloniaProperty.Register<FrequencyResponseView, bool>(nameof(HumRemovalEnabled));

    public static readonly StyledProperty<double> HumFrequencyHzProperty =
        AvaloniaProperty.Register<FrequencyResponseView, double>(nameof(HumFrequencyHz), 50);

    public static readonly StyledProperty<int> HumHarmonicsProperty =
        AvaloniaProperty.Register<FrequencyResponseView, int>(nameof(HumHarmonics), 4);

    public static readonly StyledProperty<double> HumReductionDbProperty =
        AvaloniaProperty.Register<FrequencyResponseView, double>(nameof(HumReductionDb), 18);

    public static readonly StyledProperty<bool> DeEsserEnabledProperty =
        AvaloniaProperty.Register<FrequencyResponseView, bool>(nameof(DeEsserEnabled));

    public static readonly StyledProperty<double> DeEsserFrequencyHzProperty =
        AvaloniaProperty.Register<FrequencyResponseView, double>(nameof(DeEsserFrequencyHz), 6_500);

    public static readonly StyledProperty<double> DeEsserStrengthProperty =
        AvaloniaProperty.Register<FrequencyResponseView, double>(nameof(DeEsserStrength), 35);

    public static readonly StyledProperty<IBrush?> CurveBrushProperty =
        AvaloniaProperty.Register<FrequencyResponseView, IBrush?>(nameof(CurveBrush));

    public static readonly StyledProperty<IBrush?> GuideBrushProperty =
        AvaloniaProperty.Register<FrequencyResponseView, IBrush?>(nameof(GuideBrush));

    public static readonly StyledProperty<IReadOnlyList<double>?> SpectrumDbProperty =
        AvaloniaProperty.Register<FrequencyResponseView, IReadOnlyList<double>?>(nameof(SpectrumDb));

    public static readonly StyledProperty<int> SpectrumFftSizeProperty =
        AvaloniaProperty.Register<FrequencyResponseView, int>(nameof(SpectrumFftSize));

    public static readonly StyledProperty<int> SpectrumSampleRateProperty =
        AvaloniaProperty.Register<FrequencyResponseView, int>(nameof(SpectrumSampleRate), 48_000);

    public static readonly StyledProperty<IBrush?> SpectrumBrushProperty =
        AvaloniaProperty.Register<FrequencyResponseView, IBrush?>(nameof(SpectrumBrush));

    static FrequencyResponseView()
    {
        AffectsRender<FrequencyResponseView>(
            SampleRateProperty,
            EqualizerEnabledProperty,
            BassDbProperty,
            MidDbProperty,
            MidFrequencyHzProperty,
            TrebleDbProperty,
            HighPassEnabledProperty,
            HighPassFrequencyHzProperty,
            LowPassEnabledProperty,
            LowPassFrequencyHzProperty,
            NotchEnabledProperty,
            NotchFrequencyHzProperty,
            NotchWidthPercentProperty,
            HumRemovalEnabledProperty,
            HumFrequencyHzProperty,
            HumHarmonicsProperty,
            HumReductionDbProperty,
            DeEsserEnabledProperty,
            DeEsserFrequencyHzProperty,
            DeEsserStrengthProperty,
            CurveBrushProperty,
            GuideBrushProperty,
            SpectrumDbProperty,
            SpectrumFftSizeProperty,
            SpectrumSampleRateProperty,
            SpectrumBrushProperty);
    }

    public int SampleRate
    {
        get => GetValue(SampleRateProperty);
        set => SetValue(SampleRateProperty, value);
    }

    public bool EqualizerEnabled
    {
        get => GetValue(EqualizerEnabledProperty);
        set => SetValue(EqualizerEnabledProperty, value);
    }

    public double BassDb
    {
        get => GetValue(BassDbProperty);
        set => SetValue(BassDbProperty, value);
    }

    public double MidDb
    {
        get => GetValue(MidDbProperty);
        set => SetValue(MidDbProperty, value);
    }

    public double MidFrequencyHz
    {
        get => GetValue(MidFrequencyHzProperty);
        set => SetValue(MidFrequencyHzProperty, value);
    }

    public double TrebleDb
    {
        get => GetValue(TrebleDbProperty);
        set => SetValue(TrebleDbProperty, value);
    }

    public bool HighPassEnabled
    {
        get => GetValue(HighPassEnabledProperty);
        set => SetValue(HighPassEnabledProperty, value);
    }

    public double HighPassFrequencyHz
    {
        get => GetValue(HighPassFrequencyHzProperty);
        set => SetValue(HighPassFrequencyHzProperty, value);
    }

    public bool LowPassEnabled
    {
        get => GetValue(LowPassEnabledProperty);
        set => SetValue(LowPassEnabledProperty, value);
    }

    public double LowPassFrequencyHz
    {
        get => GetValue(LowPassFrequencyHzProperty);
        set => SetValue(LowPassFrequencyHzProperty, value);
    }

    public bool NotchEnabled
    {
        get => GetValue(NotchEnabledProperty);
        set => SetValue(NotchEnabledProperty, value);
    }

    public double NotchFrequencyHz
    {
        get => GetValue(NotchFrequencyHzProperty);
        set => SetValue(NotchFrequencyHzProperty, value);
    }

    public double NotchWidthPercent
    {
        get => GetValue(NotchWidthPercentProperty);
        set => SetValue(NotchWidthPercentProperty, value);
    }

    public bool HumRemovalEnabled
    {
        get => GetValue(HumRemovalEnabledProperty);
        set => SetValue(HumRemovalEnabledProperty, value);
    }

    public double HumFrequencyHz
    {
        get => GetValue(HumFrequencyHzProperty);
        set => SetValue(HumFrequencyHzProperty, value);
    }

    public int HumHarmonics
    {
        get => GetValue(HumHarmonicsProperty);
        set => SetValue(HumHarmonicsProperty, value);
    }

    public double HumReductionDb
    {
        get => GetValue(HumReductionDbProperty);
        set => SetValue(HumReductionDbProperty, value);
    }

    public bool DeEsserEnabled
    {
        get => GetValue(DeEsserEnabledProperty);
        set => SetValue(DeEsserEnabledProperty, value);
    }

    public double DeEsserFrequencyHz
    {
        get => GetValue(DeEsserFrequencyHzProperty);
        set => SetValue(DeEsserFrequencyHzProperty, value);
    }

    public double DeEsserStrength
    {
        get => GetValue(DeEsserStrengthProperty);
        set => SetValue(DeEsserStrengthProperty, value);
    }

    public IBrush? CurveBrush
    {
        get => GetValue(CurveBrushProperty);
        set => SetValue(CurveBrushProperty, value);
    }

    public IBrush? GuideBrush
    {
        get => GetValue(GuideBrushProperty);
        set => SetValue(GuideBrushProperty, value);
    }

    public IReadOnlyList<double>? SpectrumDb
    {
        get => GetValue(SpectrumDbProperty);
        set => SetValue(SpectrumDbProperty, value);
    }

    public int SpectrumFftSize
    {
        get => GetValue(SpectrumFftSizeProperty);
        set => SetValue(SpectrumFftSizeProperty, value);
    }

    public int SpectrumSampleRate
    {
        get => GetValue(SpectrumSampleRateProperty);
        set => SetValue(SpectrumSampleRateProperty, value);
    }

    public IBrush? SpectrumBrush
    {
        get => GetValue(SpectrumBrushProperty);
        set => SetValue(SpectrumBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Bounds.Width <= 2 || Bounds.Height <= 2)
        {
            return;
        }

        var guidePen = new Pen(GuideBrush ?? Brushes.Gray, 1);
        var zeroPen = new Pen(GuideBrush ?? Brushes.Gray, 1.25);
        var spectrumPen = new Pen(SpectrumBrush ?? Brushes.DodgerBlue, 1.4);
        var curvePen = new Pen(CurveBrush ?? Brushes.Orange, 2.2);

        DrawGrid(context, guidePen, zeroPen);
        DrawDeEsserRange(context, spectrumPen);
        DrawSpectrum(context, spectrumPen);
        DrawResponse(context, curvePen);
    }

    private void DrawGrid(DrawingContext context, Pen guidePen, Pen zeroPen)
    {
        // The chart overlays two different vertical scales: the real spectrum
        // level in dBFS and the EQ response in dB gain. Keep the spectrum grid
        // dominant and emphasize the EQ 0 dB line in the centre.
        foreach (var dbfs in new[] { -18d, -36d, -54d, -72d })
        {
            var y = SpectrumDbToY(dbfs);
            context.DrawLine(guidePen, new Point(0, y), new Point(Bounds.Width, y));
        }

        var eqZeroY = DbToY(0);
        context.DrawLine(zeroPen, new Point(0, eqZeroY), new Point(Bounds.Width, eqZeroY));

        foreach (var frequency in new[] { 20d, 50d, 100d, 200d, 500d, 1_000d, 2_000d, 5_000d, 10_000d, 20_000d })
        {
            var x = FrequencyToX(frequency);
            context.DrawLine(guidePen, new Point(x, 0), new Point(x, Bounds.Height));
        }
    }


    private void DrawSpectrum(DrawingContext context, Pen spectrumPen)
    {
        var spectrum = SpectrumDb;
        var fftSize = SpectrumFftSize;
        var sampleRate = SpectrumSampleRate;
        if (spectrum is null || spectrum.Count < 2 || fftSize <= 0 || sampleRate <= 0)
        {
            return;
        }

        var nyquist = sampleRate / 2d;
        var maxFrequency = Math.Min(20_000d, nyquist);
        if (maxFrequency <= 20d)
        {
            return;
        }

        Point? previous = null;
        for (var bin = 1; bin < spectrum.Count; bin++)
        {
            var frequency = bin * sampleRate / (double)fftSize;
            if (frequency < 20d)
            {
                continue;
            }

            if (frequency > maxFrequency)
            {
                break;
            }

            var point = new Point(
                FrequencyToX(frequency),
                SpectrumDbToY(spectrum[bin]));

            if (previous is { } last)
            {
                context.DrawLine(spectrumPen, last, point);
            }

            previous = point;
        }
    }

    private void DrawResponse(DrawingContext context, Pen curvePen)
    {
        var sampleRate = Math.Max(8_000, SampleRate);
        var highPass = BiquadDesign.HighPass(sampleRate, HighPassFrequencyHz);
        var lowShelf = BiquadDesign.LowShelf(sampleRate, 120, BassDb);
        var midPeak = BiquadDesign.Peaking(sampleRate, MidFrequencyHz, MidDb, 0.9);
        var highShelf = BiquadDesign.HighShelf(sampleRate, 8_000, TrebleDb);
        var lowPass = BiquadDesign.LowPass(sampleRate, LowPassFrequencyHz);

        var notchWidth = Math.Clamp(NotchWidthPercent, 0d, 100d) / 100d;
        var notchQ = 30d - 28d * notchWidth;
        var notch = BiquadDesign.Notch(sampleRate, NotchFrequencyHz, notchQ);

        var humFundamental = HumFrequencyHz < 55d ? 50d : 60d;
        var humHarmonics = Math.Clamp(HumHarmonics, 1, 8);
        var humFilters = new List<BiquadCoefficients>(humHarmonics);
        for (var harmonic = 1; harmonic <= humHarmonics; harmonic++)
        {
            var humFrequency = humFundamental * harmonic;
            if (humFrequency >= sampleRate * 0.47d)
            {
                break;
            }

            var taperedReduction = Math.Clamp(HumReductionDb, 0d, 30d) /
                                   (1d + 0.10d * (harmonic - 1));
            humFilters.Add(BiquadDesign.Peaking(sampleRate, humFrequency, -taperedReduction, q: 18d));
        }

        Point? previous = null;
        var width = Math.Max(2, (int)Math.Ceiling(Bounds.Width));
        for (var pixel = 0; pixel < width; pixel++)
        {
            var x = width <= 1 ? 0d : pixel / (double)(width - 1);
            var frequency = XToFrequency(x);
            var magnitude = 1d;

            if (HighPassEnabled)
            {
                magnitude *= highPass.MagnitudeAt(frequency, sampleRate);
            }

            if (EqualizerEnabled)
            {
                magnitude *= lowShelf.MagnitudeAt(frequency, sampleRate);
                magnitude *= midPeak.MagnitudeAt(frequency, sampleRate);
                magnitude *= highShelf.MagnitudeAt(frequency, sampleRate);
            }

            if (NotchEnabled)
            {
                magnitude *= notch.MagnitudeAt(frequency, sampleRate);
            }

            if (HumRemovalEnabled)
            {
                foreach (var humFilter in humFilters)
                {
                    magnitude *= humFilter.MagnitudeAt(frequency, sampleRate);
                }
            }

            if (LowPassEnabled)
            {
                magnitude *= lowPass.MagnitudeAt(frequency, sampleRate);
            }

            var db = 20d * Math.Log10(Math.Max(0.000_01d, magnitude));
            var point = new Point(pixel, DbToY(Math.Clamp(db, -24, 24)));
            if (previous is { } last)
            {
                context.DrawLine(curvePen, last, point);
            }

            previous = point;
        }
    }

    private void DrawDeEsserRange(DrawingContext context, Pen spectrumPen)
    {
        if (!DeEsserEnabled)
        {
            return;
        }

        var center = Math.Clamp(DeEsserFrequencyHz, 3_000d, 12_000d);
        var strength = Math.Clamp(DeEsserStrength, 0d, 100d) / 100d;

        // The De-Esser is dynamic, so this is deliberately only a working range,
        // not a fake fixed response curve. A stronger setting makes the visual
        // band slightly more prominent, but does not imply permanent attenuation.
        var lower = Math.Max(20d, center / Math.Sqrt(2d));
        var upper = Math.Min(20_000d, center * Math.Sqrt(2d));
        var x1 = FrequencyToX(lower);
        var x2 = FrequencyToX(upper);
        var alpha = (byte)Math.Clamp(20d + 38d * strength, 20d, 58d);

        if (spectrumPen.Brush is SolidColorBrush solid)
        {
            var fill = new SolidColorBrush(
                Color.FromArgb(alpha, solid.Color.R, solid.Color.G, solid.Color.B));
            context.DrawRectangle(fill, null, new Rect(x1, 0, Math.Max(2d, x2 - x1), Bounds.Height));

            var marker = new Pen(
                new SolidColorBrush(Color.FromArgb(150, solid.Color.R, solid.Color.G, solid.Color.B)),
                1.2);
            var centerX = FrequencyToX(center);
            context.DrawLine(marker, new Point(centerX, 0), new Point(centerX, Bounds.Height));
        }
    }

    private double DbToY(double db)
    {
        const double minDb = -24;
        const double maxDb = 24;
        var normalized = (Math.Clamp(db, minDb, maxDb) - minDb) / (maxDb - minDb);
        return Bounds.Height * (1d - normalized);
    }

    private double SpectrumDbToY(double db)
    {
        const double minDb = -90;
        const double maxDb = 0;
        var normalized = (Math.Clamp(db, minDb, maxDb) - minDb) / (maxDb - minDb);
        return Bounds.Height * (1d - normalized);
    }

    private double FrequencyToX(double frequencyHz)
    {
        var normalized = Math.Log10(Math.Clamp(frequencyHz, 20d, 20_000d) / 20d) / Math.Log10(1_000d);
        return Math.Clamp(normalized, 0, 1) * Bounds.Width;
    }

    private static double XToFrequency(double normalizedX) =>
        20d * Math.Pow(1_000d, Math.Clamp(normalizedX, 0, 1));
}
