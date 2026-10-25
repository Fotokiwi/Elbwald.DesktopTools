using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Elbwald.DesktopTools.Image.Editor.Basic.Controls;

public sealed class CropOverlayControl : Control
{
    private const double HandleSize = 12d;
    private const double HitPadding = 8d;
    private const double MinimumCropScreenSize = 32d;

    private static readonly IBrush ShadeBrush = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
    private static readonly IBrush HandleFillBrush = Brushes.White;
    private static readonly Pen BorderPen = new(Brushes.White, 2d);
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1d);
    private static readonly Pen HandlePen = new(new SolidColorBrush(Color.FromArgb(220, 30, 45, 38)), 1d);

    private DragMode _dragMode;
    private Point _dragStart;
    private Rect _dragStartCropRect;

    public static readonly StyledProperty<int> ImagePixelWidthProperty =
        AvaloniaProperty.Register<CropOverlayControl, int>(nameof(ImagePixelWidth));

    public static readonly StyledProperty<int> ImagePixelHeightProperty =
        AvaloniaProperty.Register<CropOverlayControl, int>(nameof(ImagePixelHeight));

    public static readonly StyledProperty<double> CropLeftProperty =
        AvaloniaProperty.Register<CropOverlayControl, double>(nameof(CropLeft));

    public static readonly StyledProperty<double> CropTopProperty =
        AvaloniaProperty.Register<CropOverlayControl, double>(nameof(CropTop));

    public static readonly StyledProperty<double> CropWidthProperty =
        AvaloniaProperty.Register<CropOverlayControl, double>(nameof(CropWidth), 1d);

    public static readonly StyledProperty<double> CropHeightProperty =
        AvaloniaProperty.Register<CropOverlayControl, double>(nameof(CropHeight), 1d);

    static CropOverlayControl()
    {
        AffectsRender<CropOverlayControl>(
            ImagePixelWidthProperty,
            ImagePixelHeightProperty,
            CropLeftProperty,
            CropTopProperty,
            CropWidthProperty,
            CropHeightProperty);
    }

    public int ImagePixelWidth
    {
        get => GetValue(ImagePixelWidthProperty);
        set => SetValue(ImagePixelWidthProperty, value);
    }

    public int ImagePixelHeight
    {
        get => GetValue(ImagePixelHeightProperty);
        set => SetValue(ImagePixelHeightProperty, value);
    }

    public double CropLeft
    {
        get => GetValue(CropLeftProperty);
        set => SetValue(CropLeftProperty, value);
    }

    public double CropTop
    {
        get => GetValue(CropTopProperty);
        set => SetValue(CropTopProperty, value);
    }

    public double CropWidth
    {
        get => GetValue(CropWidthProperty);
        set => SetValue(CropWidthProperty, value);
    }

    public double CropHeight
    {
        get => GetValue(CropHeightProperty);
        set => SetValue(CropHeightProperty, value);
    }

    public event EventHandler<CropRectangleChangedEventArgs>? CropRectangleChanged;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));

        if (!TryGetImageRect(out var imageRect) || !TryGetCropRect(imageRect, out var cropRect))
        {
            return;
        }

        DrawOutsideShade(context, imageRect, cropRect);
        context.DrawRectangle(null, BorderPen, cropRect);
        DrawRuleOfThirds(context, cropRect);
        DrawHandles(context, cropRect);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || !TryGetImageRect(out var imageRect)
            || !TryGetCropRect(imageRect, out var cropRect))
        {
            return;
        }

        var point = e.GetPosition(this);
        _dragMode = HitTestDragMode(point, cropRect);
        if (_dragMode == DragMode.None)
        {
            return;
        }

        _dragStart = point;
        _dragStartCropRect = cropRect;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_dragMode == DragMode.None || !TryGetImageRect(out var imageRect))
        {
            return;
        }

        var point = e.GetPosition(this);
        var delta = point - _dragStart;
        var cropRect = ApplyDrag(_dragStartCropRect, delta, imageRect, _dragMode);

        var left = (cropRect.Left - imageRect.Left) / imageRect.Width;
        var top = (cropRect.Top - imageRect.Top) / imageRect.Height;
        var width = cropRect.Width / imageRect.Width;
        var height = cropRect.Height / imageRect.Height;

        CropRectangleChanged?.Invoke(
            this,
            new CropRectangleChangedEventArgs(
                Math.Clamp(left, 0d, 1d),
                Math.Clamp(top, 0d, 1d),
                Math.Clamp(width, 0d, 1d),
                Math.Clamp(height, 0d, 1d)));

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        EndDrag(e.Pointer);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragMode = DragMode.None;
    }

    private void EndDrag(IPointer pointer)
    {
        if (_dragMode == DragMode.None)
        {
            return;
        }

        _dragMode = DragMode.None;
        pointer.Capture(null);
    }

    private bool TryGetImageRect(out Rect imageRect)
    {
        imageRect = default;

        if (ImagePixelWidth <= 0
            || ImagePixelHeight <= 0
            || Bounds.Width <= 0
            || Bounds.Height <= 0)
        {
            return false;
        }

        var scale = Math.Min(Bounds.Width / ImagePixelWidth, Bounds.Height / ImagePixelHeight);
        var width = ImagePixelWidth * scale;
        var height = ImagePixelHeight * scale;
        var left = (Bounds.Width - width) / 2d;
        var top = (Bounds.Height - height) / 2d;

        imageRect = new Rect(left, top, width, height);
        return width > 0d && height > 0d;
    }

    private bool TryGetCropRect(Rect imageRect, out Rect cropRect)
    {
        cropRect = default;

        var left = Math.Clamp(CropLeft, 0d, 1d);
        var top = Math.Clamp(CropTop, 0d, 1d);
        var width = Math.Clamp(CropWidth, 0d, 1d - left);
        var height = Math.Clamp(CropHeight, 0d, 1d - top);

        if (width <= 0d || height <= 0d)
        {
            return false;
        }

        cropRect = new Rect(
            imageRect.Left + left * imageRect.Width,
            imageRect.Top + top * imageRect.Height,
            width * imageRect.Width,
            height * imageRect.Height);

        return true;
    }

    private static void DrawOutsideShade(DrawingContext context, Rect imageRect, Rect cropRect)
    {
        DrawFilledRect(context, new Rect(imageRect.Left, imageRect.Top, imageRect.Width, cropRect.Top - imageRect.Top));
        DrawFilledRect(context, new Rect(imageRect.Left, cropRect.Bottom, imageRect.Width, imageRect.Bottom - cropRect.Bottom));
        DrawFilledRect(context, new Rect(imageRect.Left, cropRect.Top, cropRect.Left - imageRect.Left, cropRect.Height));
        DrawFilledRect(context, new Rect(cropRect.Right, cropRect.Top, imageRect.Right - cropRect.Right, cropRect.Height));
    }

    private static void DrawFilledRect(DrawingContext context, Rect rect)
    {
        if (rect.Width > 0d && rect.Height > 0d)
        {
            context.DrawRectangle(ShadeBrush, null, rect);
        }
    }

    private static void DrawRuleOfThirds(DrawingContext context, Rect cropRect)
    {
        var thirdWidth = cropRect.Width / 3d;
        var thirdHeight = cropRect.Height / 3d;

        context.DrawLine(
            GridPen,
            new Point(cropRect.Left + thirdWidth, cropRect.Top),
            new Point(cropRect.Left + thirdWidth, cropRect.Bottom));
        context.DrawLine(
            GridPen,
            new Point(cropRect.Left + 2d * thirdWidth, cropRect.Top),
            new Point(cropRect.Left + 2d * thirdWidth, cropRect.Bottom));
        context.DrawLine(
            GridPen,
            new Point(cropRect.Left, cropRect.Top + thirdHeight),
            new Point(cropRect.Right, cropRect.Top + thirdHeight));
        context.DrawLine(
            GridPen,
            new Point(cropRect.Left, cropRect.Top + 2d * thirdHeight),
            new Point(cropRect.Right, cropRect.Top + 2d * thirdHeight));
    }

    private static void DrawHandles(DrawingContext context, Rect cropRect)
    {
        foreach (var center in GetHandleCenters(cropRect))
        {
            var handleRect = new Rect(
                center.X - HandleSize / 2d,
                center.Y - HandleSize / 2d,
                HandleSize,
                HandleSize);
            context.DrawRectangle(HandleFillBrush, HandlePen, handleRect);
        }
    }

    private static DragMode HitTestDragMode(Point point, Rect cropRect)
    {
        var centers = GetHandleCenters(cropRect);
        var modes = new[]
        {
            DragMode.TopLeft,
            DragMode.Top,
            DragMode.TopRight,
            DragMode.Right,
            DragMode.BottomRight,
            DragMode.Bottom,
            DragMode.BottomLeft,
            DragMode.Left
        };

        for (var index = 0; index < centers.Length; index++)
        {
            var center = centers[index];
            var hitRect = new Rect(
                center.X - HandleSize / 2d - HitPadding,
                center.Y - HandleSize / 2d - HitPadding,
                HandleSize + 2d * HitPadding,
                HandleSize + 2d * HitPadding);

            if (hitRect.Contains(point))
            {
                return modes[index];
            }
        }

        return cropRect.Contains(point)
            ? DragMode.Move
            : DragMode.None;
    }

    private static Point[] GetHandleCenters(Rect cropRect)
    {
        var centerX = cropRect.Left + cropRect.Width / 2d;
        var centerY = cropRect.Top + cropRect.Height / 2d;

        return
        [
            new Point(cropRect.Left, cropRect.Top),
            new Point(centerX, cropRect.Top),
            new Point(cropRect.Right, cropRect.Top),
            new Point(cropRect.Right, centerY),
            new Point(cropRect.Right, cropRect.Bottom),
            new Point(centerX, cropRect.Bottom),
            new Point(cropRect.Left, cropRect.Bottom),
            new Point(cropRect.Left, centerY)
        ];
    }

    private static Rect ApplyDrag(Rect start, Vector delta, Rect imageRect, DragMode dragMode)
    {
        var left = start.Left;
        var top = start.Top;
        var right = start.Right;
        var bottom = start.Bottom;

        if (dragMode == DragMode.Move)
        {
            var x = Math.Clamp(start.Left + delta.X, imageRect.Left, imageRect.Right - start.Width);
            var y = Math.Clamp(start.Top + delta.Y, imageRect.Top, imageRect.Bottom - start.Height);
            return new Rect(x, y, start.Width, start.Height);
        }

        var minimumWidth = Math.Min(MinimumCropScreenSize, Math.Max(1d, start.Width));
        var minimumHeight = Math.Min(MinimumCropScreenSize, Math.Max(1d, start.Height));

        if (dragMode is DragMode.Left or DragMode.TopLeft or DragMode.BottomLeft)
        {
            left = Math.Clamp(start.Left + delta.X, imageRect.Left, right - minimumWidth);
        }

        if (dragMode is DragMode.Right or DragMode.TopRight or DragMode.BottomRight)
        {
            right = Math.Clamp(start.Right + delta.X, left + minimumWidth, imageRect.Right);
        }

        if (dragMode is DragMode.Top or DragMode.TopLeft or DragMode.TopRight)
        {
            top = Math.Clamp(start.Top + delta.Y, imageRect.Top, bottom - minimumHeight);
        }

        if (dragMode is DragMode.Bottom or DragMode.BottomLeft or DragMode.BottomRight)
        {
            bottom = Math.Clamp(start.Bottom + delta.Y, top + minimumHeight, imageRect.Bottom);
        }

        return new Rect(left, top, Math.Max(1d, right - left), Math.Max(1d, bottom - top));
    }

    private enum DragMode
    {
        None,
        Move,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }
}

public sealed class CropRectangleChangedEventArgs(
    double left,
    double top,
    double width,
    double height) : EventArgs
{
    public double Left { get; } = left;

    public double Top { get; } = top;

    public double Width { get; } = width;

    public double Height { get; } = height;
}
