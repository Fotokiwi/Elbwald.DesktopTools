using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Controls;

public sealed class NoiseProfileView : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ProfileDbProperty =
        AvaloniaProperty.Register<NoiseProfileView, IReadOnlyList<double>?>(nameof(ProfileDb));

    public static readonly StyledProperty<int> SampleRateProperty =
        AvaloniaProperty.Register<NoiseProfileView, int>(nameof(SampleRate), 48_000);

    public static readonly StyledProperty<int> FftSizeProperty =
        AvaloniaProperty.Register<NoiseProfileView, int>(nameof(FftSize), 4096);

    public static readonly StyledProperty<IBrush?> CurveBrushProperty =
        AvaloniaProperty.Register<NoiseProfileView, IBrush?>(nameof(CurveBrush));

    public static readonly StyledProperty<IBrush?> GuideBrushProperty =
        AvaloniaProperty.Register<NoiseProfileView, IBrush?>(nameof(GuideBrush));

    static NoiseProfileView()
    {
        AffectsRender<NoiseProfileView>(
            ProfileDbProperty,
            SampleRateProperty,
            FftSizeProperty,
            CurveBrushProperty,
            GuideBrushProperty);
    }

    public IReadOnlyList<double>? ProfileDb
    {
        get => GetValue(ProfileDbProperty);
        set => SetValue(ProfileDbProperty, value);
    }

    public int SampleRate
    {
        get => GetValue(SampleRateProperty);
        set => SetValue(SampleRateProperty, value);
    }

    public int FftSize
    {
        get => GetValue(FftSizeProperty);
        set => SetValue(FftSizeProperty, value);
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

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return;
        }

        var guidePen = new Pen(GuideBrush ?? Brushes.Gray, 0.7);
        foreach (var db in new[] { -30d, -60d, -90d })
        {
            var y = DbToY(db);
            context.DrawLine(guidePen, new Point(0, y), new Point(Bounds.Width, y));
        }

        var profile = ProfileDb;
        if (profile is null || profile.Count < 2 || SampleRate <= 0 || FftSize <= 0)
        {
            return;
        }

        var nyquist = SampleRate / 2d;
        var maximumFrequency = Math.Min(20_000, nyquist);
        if (maximumFrequency <= 20)
        {
            return;
        }

        var pen = new Pen(CurveBrush ?? Brushes.Teal, 1.4);
        Point? previous = null;
        for (var bin = 1; bin < profile.Count; bin++)
        {
            var frequency = bin * SampleRate / (double)FftSize;
            if (frequency < 20 || frequency > maximumFrequency)
            {
                continue;
            }

            var x = FrequencyToX(frequency, maximumFrequency);
            var y = DbToY(profile[bin]);
            var current = new Point(x, y);
            if (previous is { } prior)
            {
                context.DrawLine(pen, prior, current);
            }

            previous = current;
        }
    }

    private double FrequencyToX(double frequency, double maximumFrequency)
    {
        var normalized = Math.Log(frequency / 20d) / Math.Log(maximumFrequency / 20d);
        return Math.Clamp(normalized, 0, 1) * Bounds.Width;
    }

    private double DbToY(double db)
    {
        const double minimumDb = -120;
        var normalized = (Math.Clamp(db, minimumDb, 0) - minimumDb) / -minimumDb;
        return (1d - normalized) * Bounds.Height;
    }
}
