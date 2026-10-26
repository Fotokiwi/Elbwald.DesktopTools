using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Controls;

/// <summary>
/// Lightweight renderer for precomputed spectrogram matrices. Analysis happens in
/// RestorationAnalysisService; this control only maps dBFS intensity to opacity.
/// </summary>
public sealed class SpectrogramView : Control
{
    public static readonly StyledProperty<SpectrogramData?> DataProperty =
        AvaloniaProperty.Register<SpectrogramView, SpectrogramData?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<SpectrogramView, IBrush?>(nameof(AccentBrush));

    public static readonly StyledProperty<IBrush?> GuideBrushProperty =
        AvaloniaProperty.Register<SpectrogramView, IBrush?>(nameof(GuideBrush));

    static SpectrogramView()
    {
        AffectsRender<SpectrogramView>(DataProperty, AccentBrushProperty, GuideBrushProperty);
    }

    public SpectrogramData? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public IBrush? GuideBrush
    {
        get => GetValue(GuideBrushProperty);
        set => SetValue(GuideBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var data = Data;
        if (data is null || data.Columns <= 0 || data.Rows <= 0 || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return;
        }

        DrawGuides(context);

        var accentColor = AccentBrush is SolidColorBrush solid
            ? solid.Color
            : Color.FromRgb(44, 133, 112);
        var brushes = BuildBrushRamp(accentColor);
        var cellWidth = Bounds.Width / data.Columns;
        var cellHeight = Bounds.Height / data.Rows;

        for (var column = 0; column < data.Columns; column++)
        {
            var x = column * cellWidth;
            for (var row = 0; row < data.Rows; row++)
            {
                var db = data[column, row];
                // Restoration view intentionally reaches down to -120 dBFS so subtle
                // hiss/noise floors remain visible. -18 dBFS and above is strong.
                var normalized = Math.Clamp((db + 120d) / 102d, 0d, 1d);
                if (normalized <= 0.008)
                {
                    continue;
                }

                normalized = Math.Pow(normalized, 0.72);
                var brushIndex = Math.Clamp(
                    (int)Math.Round(normalized * (brushes.Length - 1)),
                    0,
                    brushes.Length - 1);
                context.DrawRectangle(
                    brushes[brushIndex],
                    null,
                    new Rect(x, row * cellHeight, Math.Max(1, cellWidth + 0.4), Math.Max(1, cellHeight + 0.4)));
            }
        }
    }

    private void DrawGuides(DrawingContext context)
    {
        var guidePen = new Pen(GuideBrush ?? Brushes.Gray, 0.6);
        foreach (var fraction in new[] { 0.25, 0.5, 0.75 })
        {
            var y = Bounds.Height * fraction;
            context.DrawLine(guidePen, new Point(0, y), new Point(Bounds.Width, y));
        }

        foreach (var fraction in new[] { 0.25, 0.5, 0.75 })
        {
            var x = Bounds.Width * fraction;
            context.DrawLine(guidePen, new Point(x, 0), new Point(x, Bounds.Height));
        }
    }

    private static IBrush[] BuildBrushRamp(Color color)
    {
        const int steps = 32;
        var brushes = new IBrush[steps];
        for (var index = 0; index < steps; index++)
        {
            var opacity = 0.04 + 0.92 * index / (steps - 1d);
            brushes[index] = new SolidColorBrush(Color.FromArgb((byte)Math.Clamp((int)Math.Round(opacity * 255), 0, 255), color.R, color.G, color.B));
        }

        return brushes;
    }
}
