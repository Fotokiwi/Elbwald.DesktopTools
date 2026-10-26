using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Elbwald.DesktopTools.Audio.Editor.Basic.Services;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Views;

public partial class StemRepairWindow : Window
{
    private const double DefaultWidth = 1080;
    private const double DefaultHeight = 760;

    private readonly StemRepairWindowPlacementStore? _placementStore;
    private double _normalWidth = DefaultWidth;
    private double _normalHeight = DefaultHeight;
    private PixelPoint _normalPosition;
    private bool _hasNormalPosition;
    private bool _isRestoringPlacement;

    public StemRepairWindow()
    {
        AvaloniaXamlLoader.Load(this);
        SizeChanged += (_, _) => CaptureNormalPlacement();
        PositionChanged += (_, _) => CaptureNormalPlacement();
    }

    public StemRepairWindow(StemRepairWindowPlacementStore placementStore)
        : this()
    {
        _placementStore = placementStore ?? throw new ArgumentNullException(nameof(placementStore));
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RestorePlacement();
        CaptureNormalPlacement();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        SavePlacement();

        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        DataContext = null;
        base.OnClosing(e);
    }

    private void RestorePlacement()
    {
        var snapshot = _placementStore?.Load();
        if (snapshot is null)
        {
            return;
        }

        _isRestoringPlacement = true;
        try
        {
            Width = ClampDimension(snapshot.Width, MinWidth, DefaultWidth);
            Height = ClampDimension(snapshot.Height, MinHeight, DefaultHeight);
            _normalWidth = Width;
            _normalHeight = Height;
            Topmost = snapshot.Topmost;

            var savedPosition = new PixelPoint(snapshot.X, snapshot.Y);
            if (Screens.All.Any(screen => screen.WorkingArea.Contains(savedPosition)))
            {
                Position = savedPosition;
                _normalPosition = savedPosition;
                _hasNormalPosition = true;
            }

            WindowState = ParseWindowState(snapshot.State);
        }
        finally
        {
            _isRestoringPlacement = false;
        }
    }

    private void CaptureNormalPlacement()
    {
        if (_isRestoringPlacement || WindowState != WindowState.Normal)
        {
            return;
        }

        if (Bounds.Width > 0)
        {
            _normalWidth = Math.Max(MinWidth, Bounds.Width);
        }

        if (Bounds.Height > 0)
        {
            _normalHeight = Math.Max(MinHeight, Bounds.Height);
        }

        _normalPosition = Position;
        _hasNormalPosition = true;
    }

    private void SavePlacement()
    {
        if (_placementStore is null)
        {
            return;
        }

        CaptureNormalPlacement();
        var state = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
        var position = _hasNormalPosition ? _normalPosition : Position;

        _placementStore.Save(
            new StemRepairWindowPlacementSnapshot(
                _normalWidth,
                _normalHeight,
                position.X,
                position.Y,
                state.ToString(),
                Topmost));
    }

    private static WindowState ParseWindowState(string? value)
    {
        if (!Enum.TryParse<WindowState>(value, true, out var state))
        {
            return WindowState.Normal;
        }

        return state == WindowState.Minimized ? WindowState.Normal : state;
    }

    private static double ClampDimension(double value, double minimum, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            return fallback;
        }

        return Math.Max(minimum, Math.Min(value, 6000));
    }
}
