using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Controls;

/// <summary>
/// Draws a time based sample-peak history in dBFS from a waveform envelope.
/// This is a visual analysis control only; it never changes audio samples.
/// </summary>
public sealed class LevelHistoryView : Control
{
    public static readonly StyledProperty<WaveformEnvelope?> EnvelopeProperty =
        AvaloniaProperty.Register<LevelHistoryView, WaveformEnvelope?>(nameof(Envelope));

    public static readonly StyledProperty<double> WindowStartSecondsProperty =
        AvaloniaProperty.Register<LevelHistoryView, double>(nameof(WindowStartSeconds));

    public static readonly StyledProperty<double> WindowDurationSecondsProperty =
        AvaloniaProperty.Register<LevelHistoryView, double>(nameof(WindowDurationSeconds));

    public static readonly StyledProperty<double> CursorSecondsProperty =
        AvaloniaProperty.Register<LevelHistoryView, double>(nameof(CursorSeconds), -1d);

    public static readonly StyledProperty<double> FloorDbProperty =
        AvaloniaProperty.Register<LevelHistoryView, double>(nameof(FloorDb), -60d);

    public static readonly StyledProperty<double> CeilingDbProperty =
        AvaloniaProperty.Register<LevelHistoryView, double>(nameof(CeilingDb), 6d);

    public static readonly StyledProperty<IBrush?> LeftBrushProperty =
        AvaloniaProperty.Register<LevelHistoryView, IBrush?>(nameof(LeftBrush));

    public static readonly StyledProperty<IBrush?> RightBrushProperty =
        AvaloniaProperty.Register<LevelHistoryView, IBrush?>(nameof(RightBrush));

    public static readonly StyledProperty<IBrush?> GuideBrushProperty =
        AvaloniaProperty.Register<LevelHistoryView, IBrush?>(nameof(GuideBrush));

    public static readonly StyledProperty<IBrush?> CursorBrushProperty =
        AvaloniaProperty.Register<LevelHistoryView, IBrush?>(nameof(CursorBrush));

    public static readonly StyledProperty<IBrush?> WarningBrushProperty =
        AvaloniaProperty.Register<LevelHistoryView, IBrush?>(nameof(WarningBrush));

    public static readonly StyledProperty<IBrush?> CriticalBrushProperty =
        AvaloniaProperty.Register<LevelHistoryView, IBrush?>(nameof(CriticalBrush));

    public static readonly StyledProperty<IReadOnlyList<WaveformWarningMarker>?> WarningMarkersProperty =
        AvaloniaProperty.Register<LevelHistoryView, IReadOnlyList<WaveformWarningMarker>?>(nameof(WarningMarkers));

    public static readonly StyledProperty<IReadOnlyList<MasteringLevelHistoryPoint>?> LevelHistoryProperty =
        AvaloniaProperty.Register<LevelHistoryView, IReadOnlyList<MasteringLevelHistoryPoint>?>(nameof(LevelHistory));

    public static readonly StyledProperty<IReadOnlyList<MasteringLevelHistoryPoint>?> ComparisonLevelHistoryProperty =
        AvaloniaProperty.Register<LevelHistoryView, IReadOnlyList<MasteringLevelHistoryPoint>?>(nameof(ComparisonLevelHistory));

    static LevelHistoryView()
    {
        AffectsRender<LevelHistoryView>(
            EnvelopeProperty,
            WindowStartSecondsProperty,
            WindowDurationSecondsProperty,
            CursorSecondsProperty,
            FloorDbProperty,
            CeilingDbProperty,
            LeftBrushProperty,
            RightBrushProperty,
            GuideBrushProperty,
            CursorBrushProperty,
            WarningBrushProperty,
            CriticalBrushProperty,
            WarningMarkersProperty,
            LevelHistoryProperty,
            ComparisonLevelHistoryProperty);
    }

    public WaveformEnvelope? Envelope
    {
        get => GetValue(EnvelopeProperty);
        set => SetValue(EnvelopeProperty, value);
    }

    public double WindowStartSeconds
    {
        get => GetValue(WindowStartSecondsProperty);
        set => SetValue(WindowStartSecondsProperty, value);
    }

    public double WindowDurationSeconds
    {
        get => GetValue(WindowDurationSecondsProperty);
        set => SetValue(WindowDurationSecondsProperty, value);
    }

    public double CursorSeconds
    {
        get => GetValue(CursorSecondsProperty);
        set => SetValue(CursorSecondsProperty, value);
    }

    public double FloorDb
    {
        get => GetValue(FloorDbProperty);
        set => SetValue(FloorDbProperty, value);
    }

    public double CeilingDb
    {
        get => GetValue(CeilingDbProperty);
        set => SetValue(CeilingDbProperty, value);
    }

    public IBrush? LeftBrush
    {
        get => GetValue(LeftBrushProperty);
        set => SetValue(LeftBrushProperty, value);
    }

    public IBrush? RightBrush
    {
        get => GetValue(RightBrushProperty);
        set => SetValue(RightBrushProperty, value);
    }

    public IBrush? GuideBrush
    {
        get => GetValue(GuideBrushProperty);
        set => SetValue(GuideBrushProperty, value);
    }

    public IBrush? CursorBrush
    {
        get => GetValue(CursorBrushProperty);
        set => SetValue(CursorBrushProperty, value);
    }

    public IBrush? WarningBrush
    {
        get => GetValue(WarningBrushProperty);
        set => SetValue(WarningBrushProperty, value);
    }

    public IBrush? CriticalBrush
    {
        get => GetValue(CriticalBrushProperty);
        set => SetValue(CriticalBrushProperty, value);
    }

    public IReadOnlyList<WaveformWarningMarker>? WarningMarkers
    {
        get => GetValue(WarningMarkersProperty);
        set => SetValue(WarningMarkersProperty, value);
    }

    public IReadOnlyList<MasteringLevelHistoryPoint>? LevelHistory
    {
        get => GetValue(LevelHistoryProperty);
        set => SetValue(LevelHistoryProperty, value);
    }

    /// <summary>
    /// Optional second history used for A/B overlays. It is intentionally drawn
    /// first and with lower opacity, while <see cref="LevelHistory"/> stays the
    /// visually dominant signal.
    /// </summary>
    public IReadOnlyList<MasteringLevelHistoryPoint>? ComparisonLevelHistory
    {
        get => GetValue(ComparisonLevelHistoryProperty);
        set => SetValue(ComparisonLevelHistoryProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var envelope = Envelope;
        var history = LevelHistory;
        var comparisonHistory = ComparisonLevelHistory;
        if ((history is not { Count: > 0 }) &&
            (comparisonHistory is not { Count: > 0 }) &&
            (envelope is null || envelope.PeakCount == 0))
        {
            return;
        }

        if (Bounds.Width <= 2 || Bounds.Height <= 2)
        {
            return;
        }

        var floor = Math.Min(FloorDb, CeilingDb - 1d);
        var ceiling = Math.Max(CeilingDb, floor + 1d);
        var warningBrush = WarningBrush ?? new SolidColorBrush(Color.Parse("#D89B35"));
        var criticalBrush = CriticalBrush ?? new SolidColorBrush(Color.Parse("#C94A4A"));
        var guideBrush = GuideBrush ?? Brushes.Gray;
        var leftBrush = LeftBrush ?? Brushes.SeaGreen;
        var rightBrush = RightBrush ?? Brushes.SlateGray;
        var cursorBrush = CursorBrush ?? Brushes.Black;

        DrawLevelZones(context, floor, ceiling, warningBrush, criticalBrush);
        DrawGuides(context, floor, ceiling, guideBrush);

        if (history is { Count: > 0 })
        {
            if (comparisonHistory is { Count: > 0 })
            {
                DrawHistorySeries(
                    context,
                    comparisonHistory,
                    floor,
                    ceiling,
                    new Pen(WithOpacity(leftBrush, 82), 1.0),
                    new Pen(WithOpacity(rightBrush, 70), 0.9));
            }

            DrawHistorySeries(context, history, floor, ceiling, new Pen(leftBrush, 1.5), new Pen(rightBrush, 1.2));
            DrawWarningMarkers(context, ResolveHistoryDuration(history), warningBrush, criticalBrush);
            DrawCursor(context, ResolveHistoryDuration(history), new Pen(cursorBrush, 1.2));
        }
        else if (envelope is not null)
        {
            var (startIndex, endIndex) = ResolvePeakWindow(envelope);
            if (endIndex <= startIndex)
            {
                return;
            }

            DrawSeries(context, envelope.Left, startIndex, endIndex, floor, ceiling, new Pen(leftBrush, 1.4));
            if (envelope.Channels == 2 && envelope.Right is { } right)
            {
                DrawSeries(context, right, startIndex, endIndex, floor, ceiling, new Pen(rightBrush, 1.15));
            }

            DrawWarningMarkers(context, envelope.DurationSeconds, warningBrush, criticalBrush);
            DrawCursor(context, envelope.DurationSeconds, new Pen(cursorBrush, 1.2));
        }
    }

    private void DrawLevelZones(
        DrawingContext context,
        double floor,
        double ceiling,
        IBrush warningBrush,
        IBrush criticalBrush)
    {
        var zeroY = MapDbToY(0d, floor, ceiling);
        var warningY = MapDbToY(-3d, floor, ceiling);

        if (ceiling > 0)
        {
            context.DrawRectangle(WithOpacity(criticalBrush, 34), null, new Rect(0, 0, Bounds.Width, Math.Max(0, zeroY)));
        }

        if (ceiling > -3 && floor < 0)
        {
            var top = Math.Min(zeroY, warningY);
            var bottom = Math.Max(zeroY, warningY);
            context.DrawRectangle(WithOpacity(warningBrush, 28), null, new Rect(0, top, Bounds.Width, Math.Max(0, bottom - top)));
        }
    }

    private void DrawGuides(DrawingContext context, double floor, double ceiling, IBrush guideBrush)
    {
        var guidePen = new Pen(WithOpacity(guideBrush, 110), 1);
        var importantPen = new Pen(WithOpacity(guideBrush, 165), 1.1);
        var values = new[] { 0d, -3d, -6d, -12d, -24d, -36d, -48d, -60d };
        foreach (var value in values)
        {
            if (value < floor || value > ceiling)
            {
                continue;
            }

            var y = MapDbToY(value, floor, ceiling);
            context.DrawLine(value is 0d or -3d ? importantPen : guidePen, new Point(0, y), new Point(Bounds.Width, y));
        }
    }

    private void DrawSeries(
        DrawingContext context,
        IReadOnlyList<WaveformPeak> peaks,
        int startIndex,
        int endIndex,
        double floor,
        double ceiling,
        Pen pen)
    {
        var visibleCount = Math.Max(1, endIndex - startIndex);
        var pixelWidth = Math.Max(2, (int)Math.Ceiling(Bounds.Width));
        Point? previous = null;

        for (var x = 0; x < pixelWidth; x++)
        {
            var normalizedX = x / (double)(pixelWidth - 1);
            var index = startIndex + (int)Math.Round(normalizedX * (visibleCount - 1));
            index = Math.Clamp(index, startIndex, endIndex - 1);
            var peak = peaks[index];
            var amplitude = Math.Max(Math.Abs(peak.Minimum), Math.Abs(peak.Maximum));
            var db = amplitude <= 1e-12 ? floor : 20d * Math.Log10(amplitude);
            db = Math.Clamp(db, floor, ceiling);
            var point = new Point(Math.Min(x, Bounds.Width - 1), MapDbToY(db, floor, ceiling));

            if (previous is { } previousPoint)
            {
                context.DrawLine(pen, previousPoint, point);
            }

            previous = point;
        }
    }

    private void DrawHistorySeries(
        DrawingContext context,
        IReadOnlyList<MasteringLevelHistoryPoint> history,
        double floor,
        double ceiling,
        Pen leftPen,
        Pen rightPen)
    {
        var totalDuration = ResolveHistoryDuration(history);
        var visibleStart = Math.Clamp(WindowStartSeconds, 0d, totalDuration);
        var visibleDuration = WindowDurationSeconds <= 0
            ? Math.Max(0d, totalDuration - visibleStart)
            : Math.Min(WindowDurationSeconds, Math.Max(0d, totalDuration - visibleStart));
        if (visibleDuration <= 0)
        {
            return;
        }

        Point? previousLeft = null;
        Point? previousRight = null;
        foreach (var point in history)
        {
            if (point.TimeSeconds < visibleStart || point.TimeSeconds > visibleStart + visibleDuration)
            {
                continue;
            }

            var x = (point.TimeSeconds - visibleStart) / visibleDuration * Bounds.Width;
            var leftPoint = new Point(x, MapDbToY(point.LeftPeakDbfs, floor, ceiling));
            var rightPoint = new Point(x, MapDbToY(point.RightPeakDbfs, floor, ceiling));

            if (previousLeft is { } previousLeftPoint)
            {
                context.DrawLine(leftPen, previousLeftPoint, leftPoint);
            }

            if (previousRight is { } previousRightPoint)
            {
                context.DrawLine(rightPen, previousRightPoint, rightPoint);
            }

            previousLeft = leftPoint;
            previousRight = rightPoint;
        }
    }

    private static double ResolveHistoryDuration(IReadOnlyList<MasteringLevelHistoryPoint> history)
    {
        return history.Count == 0 ? 0d : Math.Max(0d, history[^1].TimeSeconds);
    }

    private void DrawWarningMarkers(
        DrawingContext context,
        double sourceDuration,
        IBrush warningBrush,
        IBrush criticalBrush)
    {
        if (WarningMarkers is not { Count: > 0 })
        {
            return;
        }

        var (visibleStart, visibleDuration) = ResolveVisibleTime(sourceDuration);
        if (visibleDuration <= 0)
        {
            return;
        }

        foreach (var marker in WarningMarkers)
        {
            var markerEnd = marker.TimeSeconds + Math.Max(0d, marker.DurationSeconds);
            if (markerEnd < visibleStart || marker.TimeSeconds > visibleStart + visibleDuration)
            {
                continue;
            }

            var brush = marker.Severity == WaveformWarningSeverity.Critical ? criticalBrush : warningBrush;
            if (marker.IsSustained && marker.DurationSeconds > 0)
            {
                var start = Math.Max(marker.TimeSeconds, visibleStart);
                var end = Math.Min(markerEnd, visibleStart + visibleDuration);
                var x1 = (start - visibleStart) / visibleDuration * Bounds.Width;
                var x2 = (end - visibleStart) / visibleDuration * Bounds.Width;
                context.DrawRectangle(WithOpacity(brush, 42), null, new Rect(x1, 0, Math.Max(2, x2 - x1), Bounds.Height));
            }
            else
            {
                var x = (marker.TimeSeconds - visibleStart) / visibleDuration * Bounds.Width;
                context.DrawLine(new Pen(brush, 1.4), new Point(x, 0), new Point(x, Bounds.Height));
            }
        }
    }

    private void DrawCursor(DrawingContext context, double sourceDuration, Pen cursorPen)
    {
        var cursor = CursorSeconds;
        if (cursor < 0)
        {
            return;
        }

        var (visibleStart, visibleDuration) = ResolveVisibleTime(sourceDuration);
        if (visibleDuration <= 0 || cursor < visibleStart || cursor > visibleStart + visibleDuration)
        {
            return;
        }

        var x = (cursor - visibleStart) / visibleDuration * Bounds.Width;
        context.DrawLine(cursorPen, new Point(x, 0), new Point(x, Bounds.Height));
    }

    private (int StartIndex, int EndIndex) ResolvePeakWindow(WaveformEnvelope envelope)
    {
        var count = envelope.PeakCount;
        if (count <= 0)
        {
            return (0, 0);
        }

        var sourceDuration = Math.Max(envelope.DurationSeconds, double.Epsilon);
        var (visibleStart, visibleDuration) = ResolveVisibleTime(sourceDuration);
        var startNormalized = Math.Clamp(visibleStart / sourceDuration, 0d, 1d);
        var endNormalized = Math.Clamp((visibleStart + visibleDuration) / sourceDuration, 0d, 1d);
        var startIndex = Math.Clamp((int)Math.Floor(startNormalized * count), 0, count - 1);
        var endIndex = Math.Clamp((int)Math.Ceiling(endNormalized * count), startIndex + 1, count);
        return (startIndex, endIndex);
    }

    private (double Start, double Duration) ResolveVisibleTime(double sourceDuration)
    {
        sourceDuration = Math.Max(sourceDuration, double.Epsilon);
        var start = Math.Clamp(WindowStartSeconds, 0d, sourceDuration);
        var duration = WindowDurationSeconds <= 0
            ? sourceDuration - start
            : Math.Min(WindowDurationSeconds, sourceDuration - start);
        return (start, Math.Max(0d, duration));
    }

    private double MapDbToY(double db, double floor, double ceiling)
    {
        var normalized = (ceiling - Math.Clamp(db, floor, ceiling)) / Math.Max(1e-9, ceiling - floor);
        return normalized * Bounds.Height;
    }

    private static IBrush WithOpacity(IBrush brush, byte alpha)
    {
        if (brush is SolidColorBrush solid)
        {
            return new SolidColorBrush(Color.FromArgb(alpha, solid.Color.R, solid.Color.G, solid.Color.B));
        }

        return brush;
    }
}
