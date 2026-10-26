using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Controls;

public sealed class AudioWaveformView : Control, ICustomHitTest
{
    public static readonly StyledProperty<WaveformEnvelope?> EnvelopeProperty =
        AvaloniaProperty.Register<AudioWaveformView, WaveformEnvelope?>(nameof(Envelope));

    public static readonly StyledProperty<double> WindowStartSecondsProperty =
        AvaloniaProperty.Register<AudioWaveformView, double>(nameof(WindowStartSeconds));

    public static readonly StyledProperty<double> WindowDurationSecondsProperty =
        AvaloniaProperty.Register<AudioWaveformView, double>(nameof(WindowDurationSeconds));

    public static readonly StyledProperty<double> CursorSecondsProperty =
        AvaloniaProperty.Register<AudioWaveformView, double>(nameof(CursorSeconds), -1);

    public static readonly StyledProperty<IBrush?> WaveformBrushProperty =
        AvaloniaProperty.Register<AudioWaveformView, IBrush?>(nameof(WaveformBrush));

    public static readonly StyledProperty<IBrush?> GuideBrushProperty =
        AvaloniaProperty.Register<AudioWaveformView, IBrush?>(nameof(GuideBrush));

    public static readonly StyledProperty<IBrush?> CursorBrushProperty =
        AvaloniaProperty.Register<AudioWaveformView, IBrush?>(nameof(CursorBrush));

    public static readonly StyledProperty<double> AmplitudeScaleProperty =
        AvaloniaProperty.Register<AudioWaveformView, double>(nameof(AmplitudeScale), 1d);

    public static readonly StyledProperty<bool> UseDecibelScaleProperty =
        AvaloniaProperty.Register<AudioWaveformView, bool>(nameof(UseDecibelScale));

    public static readonly StyledProperty<bool> AutoNormalizeAmplitudeProperty =
        AvaloniaProperty.Register<AudioWaveformView, bool>(nameof(AutoNormalizeAmplitude));

    public static readonly StyledProperty<double> DecibelFloorProperty =
        AvaloniaProperty.Register<AudioWaveformView, double>(nameof(DecibelFloor), -60d);

    public static readonly StyledProperty<double> DecibelCeilingProperty =
        AvaloniaProperty.Register<AudioWaveformView, double>(nameof(DecibelCeiling), 6d);

    public static readonly StyledProperty<bool> ShowLevelWarningsProperty =
        AvaloniaProperty.Register<AudioWaveformView, bool>(nameof(ShowLevelWarnings));

    public static readonly StyledProperty<double> WarningThresholdDbProperty =
        AvaloniaProperty.Register<AudioWaveformView, double>(nameof(WarningThresholdDb), -3d);

    public static readonly StyledProperty<double> CriticalThresholdDbProperty =
        AvaloniaProperty.Register<AudioWaveformView, double>(nameof(CriticalThresholdDb), 0d);

    public static readonly StyledProperty<IBrush?> WarningBrushProperty =
        AvaloniaProperty.Register<AudioWaveformView, IBrush?>(nameof(WarningBrush));

    public static readonly StyledProperty<IBrush?> CriticalBrushProperty =
        AvaloniaProperty.Register<AudioWaveformView, IBrush?>(nameof(CriticalBrush));

    public static readonly StyledProperty<IReadOnlyList<WaveformWarningMarker>?> WarningMarkersProperty =
        AvaloniaProperty.Register<AudioWaveformView, IReadOnlyList<WaveformWarningMarker>?>(nameof(WarningMarkers));

    public static readonly StyledProperty<bool> IsSelectionEnabledProperty =
        AvaloniaProperty.Register<AudioWaveformView, bool>(nameof(IsSelectionEnabled));

    public static readonly StyledProperty<double> SelectionStartSecondsProperty =
        AvaloniaProperty.Register<AudioWaveformView, double>(
            nameof(SelectionStartSeconds),
            -1d,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> SelectionEndSecondsProperty =
        AvaloniaProperty.Register<AudioWaveformView, double>(
            nameof(SelectionEndSeconds),
            -1d,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IBrush?> SelectionBrushProperty =
        AvaloniaProperty.Register<AudioWaveformView, IBrush?>(nameof(SelectionBrush));

    private bool _isSelecting;
    private double _selectionAnchorSeconds;

    static AudioWaveformView()
    {
        AffectsRender<AudioWaveformView>(
            EnvelopeProperty,
            WindowStartSecondsProperty,
            WindowDurationSecondsProperty,
            CursorSecondsProperty,
            WaveformBrushProperty,
            GuideBrushProperty,
            CursorBrushProperty,
            AmplitudeScaleProperty,
            UseDecibelScaleProperty,
            AutoNormalizeAmplitudeProperty,
            DecibelFloorProperty,
            DecibelCeilingProperty,
            ShowLevelWarningsProperty,
            WarningThresholdDbProperty,
            CriticalThresholdDbProperty,
            WarningBrushProperty,
            CriticalBrushProperty,
            WarningMarkersProperty,
            IsSelectionEnabledProperty,
            SelectionStartSecondsProperty,
            SelectionEndSecondsProperty,
            SelectionBrushProperty);
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

    public IBrush? WaveformBrush
    {
        get => GetValue(WaveformBrushProperty);
        set => SetValue(WaveformBrushProperty, value);
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

    /// <summary>
    /// Visual-only vertical gain for waveform inspection. This never changes audio
    /// samples or project gain; it only magnifies small amplitudes in the renderer.
    /// </summary>
    public double AmplitudeScale
    {
        get => GetValue(AmplitudeScaleProperty);
        set => SetValue(AmplitudeScaleProperty, value);
    }

    /// <summary>
    /// Maps amplitudes to a fixed dBFS window instead of a linear 0..1 scale.
    /// This is visual only and never changes the underlying audio.
    /// </summary>
    public bool UseDecibelScale
    {
        get => GetValue(UseDecibelScaleProperty);
        set => SetValue(UseDecibelScaleProperty, value);
    }

    /// <summary>
    /// When enabled the loudest visible sample becomes the 0 dB visual reference.
    /// Useful for inspecting quiet tracks, but no longer suitable for absolute
    /// level comparison between different tracks.
    /// </summary>
    public bool AutoNormalizeAmplitude
    {
        get => GetValue(AutoNormalizeAmplitudeProperty);
        set => SetValue(AutoNormalizeAmplitudeProperty, value);
    }

    public double DecibelFloor
    {
        get => GetValue(DecibelFloorProperty);
        set => SetValue(DecibelFloorProperty, value);
    }

    public double DecibelCeiling
    {
        get => GetValue(DecibelCeilingProperty);
        set => SetValue(DecibelCeilingProperty, value);
    }

    /// <summary>
    /// Visually highlights real sample peaks near/above full scale. This never changes audio.
    /// Thresholds are evaluated against the un-normalized waveform amplitude, even when
    /// the view itself is shown in Auto scale.
    /// </summary>
    public bool ShowLevelWarnings
    {
        get => GetValue(ShowLevelWarningsProperty);
        set => SetValue(ShowLevelWarningsProperty, value);
    }

    public double WarningThresholdDb
    {
        get => GetValue(WarningThresholdDbProperty);
        set => SetValue(WarningThresholdDbProperty, value);
    }

    public double CriticalThresholdDb
    {
        get => GetValue(CriticalThresholdDbProperty);
        set => SetValue(CriticalThresholdDbProperty, value);
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

    public bool IsSelectionEnabled
    {
        get => GetValue(IsSelectionEnabledProperty);
        set => SetValue(IsSelectionEnabledProperty, value);
    }

    public double SelectionStartSeconds
    {
        get => GetValue(SelectionStartSecondsProperty);
        set => SetValue(SelectionStartSecondsProperty, value);
    }

    public double SelectionEndSeconds
    {
        get => GetValue(SelectionEndSecondsProperty);
        set => SetValue(SelectionEndSecondsProperty, value);
    }

    public IBrush? SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    bool ICustomHitTest.HitTest(Point point)
    {
        // Custom-drawn controls are otherwise hit-tested against rendered pixels only.
        // For waveform selection we need every empty point inside the viewport to be clickable.
        return new Rect(0, 0, Bounds.Width, Bounds.Height).Contains(point);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var envelope = Envelope;
        if (envelope is null || envelope.PeakCount == 0 || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return;
        }

        var waveformPen = new Pen(WaveformBrush ?? Brushes.SlateGray, 1);
        var guidePen = new Pen(GuideBrush ?? Brushes.Gray, 1);
        var cursorPen = new Pen(CursorBrush ?? Brushes.Orange, 1.5);
        var warningBrush = WarningBrush ?? new SolidColorBrush(Color.Parse("#D89B35"));
        var criticalBrush = CriticalBrush ?? new SolidColorBrush(Color.Parse("#C94A4A"));
        var warningPen = new Pen(warningBrush, 1.2);
        var criticalPen = new Pen(criticalBrush, 1.4);

        DrawSelection(context, envelope);
        var displayReferencePeak = ResolveDisplayReferencePeak(envelope);

        if (envelope.Channels == 1)
        {
            DrawChannel(
                context,
                envelope,
                envelope.Left,
                new Rect(0, 0, Bounds.Width, Bounds.Height),
                waveformPen,
                warningPen,
                criticalPen,
                guidePen,
                displayReferencePeak);
        }
        else
        {
            var laneHeight = Bounds.Height / 2;
            DrawChannel(
                context,
                envelope,
                envelope.Left,
                new Rect(0, 0, Bounds.Width, laneHeight),
                waveformPen,
                warningPen,
                criticalPen,
                guidePen,
                displayReferencePeak);
            DrawChannel(
                context,
                envelope,
                envelope.Right ?? envelope.Left,
                new Rect(0, laneHeight, Bounds.Width, laneHeight),
                waveformPen,
                warningPen,
                criticalPen,
                guidePen,
                displayReferencePeak);
        }

        DrawWarningMarkers(context, envelope, warningBrush, criticalBrush);
        DrawCursor(context, envelope, cursorPen);
    }

    private void DrawChannel(
        DrawingContext context,
        WaveformEnvelope envelope,
        IReadOnlyList<WaveformPeak> peaks,
        Rect lane,
        Pen waveformPen,
        Pen warningPen,
        Pen criticalPen,
        Pen guidePen,
        double displayReferencePeak)
    {
        var centerY = lane.Y + lane.Height / 2;
        context.DrawLine(
            guidePen,
            new Point(lane.Left, centerY),
            new Point(lane.Right, centerY));

        var (startIndex, endIndex) = ResolvePeakWindow(envelope);
        var visiblePeakCount = Math.Max(1, endIndex - startIndex);
        var amplitude = Math.Max(1, lane.Height * 0.46);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(lane.Width));

        for (var x = 0; x < pixelWidth; x++)
        {
            var normalizedX = pixelWidth <= 1 ? 0 : x / (double)(pixelWidth - 1);
            var peakIndex = startIndex + (int)Math.Round(normalizedX * (visiblePeakCount - 1));
            peakIndex = Math.Clamp(peakIndex, startIndex, endIndex - 1);

            var peak = peaks[peakIndex];
            var minimum = TransformAmplitudeForDisplay(peak.Minimum, displayReferencePeak);
            var maximum = TransformAmplitudeForDisplay(peak.Maximum, displayReferencePeak);
            var drawX = lane.X + Math.Min(x, lane.Width - 1);
            var top = centerY - maximum * amplitude;
            var bottom = centerY - minimum * amplitude;

            // Keep the waveform itself readable. Level warnings are drawn only
            // as small edge ticks instead of recolouring the complete vertical
            // sample column (which turned dense masters into a red wall).
            context.DrawLine(
                waveformPen,
                new Point(drawX, top),
                new Point(drawX, bottom));

            if (ShowLevelWarnings && x % 3 == 0)
            {
                var levelPen = ResolveLevelPen(peak, waveformPen, warningPen, criticalPen);
                if (!ReferenceEquals(levelPen, waveformPen))
                {
                    context.DrawLine(levelPen, new Point(drawX, lane.Top + 1), new Point(drawX, lane.Top + 4));
                    context.DrawLine(levelPen, new Point(drawX, lane.Bottom - 4), new Point(drawX, lane.Bottom - 1));
                }
            }
        }
    }

    private Pen ResolveLevelPen(
        WaveformPeak peak,
        Pen normalPen,
        Pen warningPen,
        Pen criticalPen)
    {
        if (!ShowLevelWarnings)
        {
            return normalPen;
        }

        var absolute = Math.Max(Math.Abs(peak.Minimum), Math.Abs(peak.Maximum));
        if (absolute <= 1e-12)
        {
            return normalPen;
        }

        var db = 20d * Math.Log10(absolute);
        if (db >= CriticalThresholdDb)
        {
            return criticalPen;
        }

        return db >= WarningThresholdDb ? warningPen : normalPen;
    }

    private void DrawWarningMarkers(
        DrawingContext context,
        WaveformEnvelope envelope,
        IBrush warningBrush,
        IBrush criticalBrush)
    {
        if (WarningMarkers is not { Count: > 0 } || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var sourceDuration = Math.Max(envelope.DurationSeconds, double.Epsilon);
        var visibleStart = Math.Clamp(WindowStartSeconds, 0, sourceDuration);
        var visibleDuration = WindowDurationSeconds <= 0
            ? sourceDuration - visibleStart
            : Math.Min(WindowDurationSeconds, sourceDuration - visibleStart);
        if (visibleDuration <= 0)
        {
            return;
        }

        foreach (var marker in WarningMarkers)
        {
            var markerEnd = marker.TimeSeconds + Math.Max(0, marker.DurationSeconds);
            if (markerEnd < visibleStart || marker.TimeSeconds > visibleStart + visibleDuration)
            {
                continue;
            }

            var brush = marker.Severity == WaveformWarningSeverity.Critical
                ? criticalBrush
                : warningBrush;
            var pen = new Pen(brush, marker.IsSustained ? 1.6 : 1.25);

            if (marker.IsSustained && marker.DurationSeconds > 0)
            {
                var startNormalized = (Math.Max(marker.TimeSeconds, visibleStart) - visibleStart) / visibleDuration;
                var endNormalized = (Math.Min(markerEnd, visibleStart + visibleDuration) - visibleStart) / visibleDuration;
                var x1 = Math.Clamp(startNormalized, 0, 1) * Bounds.Width;
                var x2 = Math.Clamp(endNormalized, 0, 1) * Bounds.Width;
                var width = Math.Max(2, x2 - x1);

                IBrush fill = brush;
                if (brush is SolidColorBrush solid)
                {
                    fill = new SolidColorBrush(Color.FromArgb(72, solid.Color.R, solid.Color.G, solid.Color.B));
                }

                // Sustained clipping gets a compact ribbon instead of painting
                // the whole waveform red. The waveform remains readable while
                // the duration of the problem is still obvious.
                context.DrawRectangle(fill, null, new Rect(x1, 0, width, 6));
                context.DrawLine(pen, new Point(x1, 0), new Point(x1, Bounds.Height));
                context.DrawLine(pen, new Point(x2, 0), new Point(x2, Bounds.Height));
                continue;
            }

            var normalized = (marker.TimeSeconds - visibleStart) / visibleDuration;
            var x = Math.Clamp(normalized, 0, 1) * Bounds.Width;
            context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
            context.DrawEllipse(brush, null, new Point(x, 4), 3.0, 3.0);
        }
    }

    private double ResolveDisplayReferencePeak(WaveformEnvelope envelope)
    {
        if (!UseDecibelScale || !AutoNormalizeAmplitude)
        {
            return 1d;
        }

        var (startIndex, endIndex) = ResolvePeakWindow(envelope);
        var peak = 0d;
        for (var index = startIndex; index < endIndex; index++)
        {
            var left = envelope.Left[index];
            peak = Math.Max(peak, Math.Max(Math.Abs(left.Minimum), Math.Abs(left.Maximum)));

            if (envelope.Right is { } right)
            {
                var rightPeak = right[index];
                peak = Math.Max(peak, Math.Max(Math.Abs(rightPeak.Minimum), Math.Abs(rightPeak.Maximum)));
            }
        }

        return Math.Max(peak, 1e-9);
    }

    private double TransformAmplitudeForDisplay(double sample, double referencePeak)
    {
        var amplitudeScale = Math.Clamp(AmplitudeScale, 1d, 128d);
        if (!UseDecibelScale)
        {
            return Math.Clamp(sample * amplitudeScale, -1d, 1d);
        }

        var sign = Math.Sign(sample);
        var absolute = Math.Abs(sample);
        if (absolute <= 1e-12)
        {
            return 0d;
        }

        if (AutoNormalizeAmplitude)
        {
            absolute /= Math.Max(referencePeak, 1e-12);
        }

        absolute *= amplitudeScale;
        var floor = Math.Min(DecibelFloor, -1d);
        var ceiling = AutoNormalizeAmplitude ? 0d : Math.Max(floor + 1d, DecibelCeiling);
        var db = 20d * Math.Log10(Math.Max(absolute, 1e-12));
        var normalized = Math.Clamp((db - floor) / (ceiling - floor), 0d, 1d);
        return sign * normalized;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsSelectionEnabled || Envelope is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _selectionAnchorSeconds = PointToSeconds(e.GetPosition(this).X);
        _isSelecting = true;
        SetCurrentValue(SelectionStartSecondsProperty, _selectionAnchorSeconds);
        SetCurrentValue(SelectionEndSecondsProperty, _selectionAnchorSeconds);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isSelecting || !IsSelectionEnabled)
        {
            return;
        }

        var current = PointToSeconds(e.GetPosition(this).X);
        SetCurrentValue(SelectionStartSecondsProperty, Math.Min(_selectionAnchorSeconds, current));
        SetCurrentValue(SelectionEndSecondsProperty, Math.Max(_selectionAnchorSeconds, current));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isSelecting)
        {
            return;
        }

        _isSelecting = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private double PointToSeconds(double x)
    {
        var envelope = Envelope;
        if (envelope is null || Bounds.Width <= 0)
        {
            return 0;
        }

        var sourceDuration = Math.Max(envelope.DurationSeconds, double.Epsilon);
        var startSeconds = Math.Clamp(WindowStartSeconds, 0, sourceDuration);
        var duration = WindowDurationSeconds <= 0
            ? sourceDuration - startSeconds
            : Math.Min(WindowDurationSeconds, sourceDuration - startSeconds);
        var normalized = Math.Clamp(x / Bounds.Width, 0, 1);
        return Math.Clamp(startSeconds + normalized * duration, 0, sourceDuration);
    }

    private void DrawSelection(DrawingContext context, WaveformEnvelope envelope)
    {
        if (!IsSelectionEnabled || SelectionStartSeconds < 0 || SelectionEndSeconds < 0 || Bounds.Width <= 0)
        {
            return;
        }

        var sourceDuration = Math.Max(envelope.DurationSeconds, double.Epsilon);
        var visibleStart = Math.Clamp(WindowStartSeconds, 0, sourceDuration);
        var visibleDuration = WindowDurationSeconds <= 0
            ? sourceDuration - visibleStart
            : Math.Min(WindowDurationSeconds, sourceDuration - visibleStart);
        if (visibleDuration <= 0)
        {
            return;
        }

        var selectionStart = Math.Max(Math.Min(SelectionStartSeconds, SelectionEndSeconds), visibleStart);
        var selectionEnd = Math.Min(Math.Max(SelectionStartSeconds, SelectionEndSeconds), visibleStart + visibleDuration);
        if (selectionEnd <= selectionStart)
        {
            return;
        }

        var left = (selectionStart - visibleStart) / visibleDuration * Bounds.Width;
        var right = (selectionEnd - visibleStart) / visibleDuration * Bounds.Width;
        var baseBrush = SelectionBrush ?? Brushes.CornflowerBlue;
        IBrush fill = baseBrush;
        if (baseBrush is SolidColorBrush solid)
        {
            fill = new SolidColorBrush(Color.FromArgb(46, solid.Color.R, solid.Color.G, solid.Color.B));
        }

        context.DrawRectangle(fill, null, new Rect(left, 0, Math.Max(1, right - left), Bounds.Height));
        var edgePen = new Pen(baseBrush, 1.2);
        context.DrawLine(edgePen, new Point(left, 0), new Point(left, Bounds.Height));
        context.DrawLine(edgePen, new Point(right, 0), new Point(right, Bounds.Height));
    }

    private (int StartIndex, int EndIndex) ResolvePeakWindow(WaveformEnvelope envelope)
    {
        var sourceDuration = Math.Max(envelope.DurationSeconds, double.Epsilon);
        var startSeconds = Math.Clamp(WindowStartSeconds, 0, sourceDuration);
        var requestedDuration = WindowDurationSeconds <= 0
            ? sourceDuration - startSeconds
            : WindowDurationSeconds;
        var endSeconds = Math.Clamp(startSeconds + requestedDuration, startSeconds, sourceDuration);

        var startIndex = (int)Math.Floor(startSeconds / sourceDuration * envelope.PeakCount);
        var endIndex = (int)Math.Ceiling(endSeconds / sourceDuration * envelope.PeakCount);

        startIndex = Math.Clamp(startIndex, 0, envelope.PeakCount - 1);
        endIndex = Math.Clamp(endIndex, startIndex + 1, envelope.PeakCount);
        return (startIndex, endIndex);
    }

    private void DrawCursor(
        DrawingContext context,
        WaveformEnvelope envelope,
        Pen cursorPen)
    {
        if (CursorSeconds < 0)
        {
            return;
        }

        var sourceDuration = Math.Max(envelope.DurationSeconds, double.Epsilon);
        var startSeconds = Math.Clamp(WindowStartSeconds, 0, sourceDuration);
        var duration = WindowDurationSeconds <= 0
            ? sourceDuration - startSeconds
            : Math.Min(WindowDurationSeconds, sourceDuration - startSeconds);

        if (duration <= 0 || CursorSeconds < startSeconds || CursorSeconds > startSeconds + duration)
        {
            return;
        }

        var normalized = (CursorSeconds - startSeconds) / duration;
        var x = Math.Clamp(normalized, 0, 1) * Bounds.Width;
        context.DrawLine(
            cursorPen,
            new Point(x, 0),
            new Point(x, Bounds.Height));
    }
}
