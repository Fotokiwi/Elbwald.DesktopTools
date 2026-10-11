using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Elbwald.DesktopTools.App.Services;

namespace Elbwald.DesktopTools.App.Views;

public partial class MainWindow : Window
{
    private const double DefaultWidth = 1800;
    private const double DefaultHeight = 980;

    private readonly WindowPlacementStore? _placementStore;

    private double _normalWidth = DefaultWidth;
    private double _normalHeight = DefaultHeight;
    private PixelPoint _normalPosition;
    private bool _hasNormalPosition;
    private bool _isRestoringPlacement;
    private WindowState _stateBeforeFullScreen = WindowState.Normal;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        WireShellControls();
        HookPlacementTracking();
    }

    public MainWindow(WindowPlacementStore placementStore)
        : this()
    {
        _placementStore = placementStore;
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
        base.OnClosing(e);
    }

    private void WireShellControls()
    {
        var minimizeButton =
            this.FindControl<Button>("MinimizeButton");

        if (minimizeButton is not null)
        {
            minimizeButton.Click +=
                (_, _) => WindowState = WindowState.Minimized;
        }

        var maximizeButton =
            this.FindControl<Button>("MaximizeButton");

        if (maximizeButton is not null)
        {
            maximizeButton.Click +=
                (_, _) => ToggleMaximized();
        }

        var closeButton =
            this.FindControl<Button>("CloseButton");

        if (closeButton is not null)
        {
            closeButton.Click +=
                (_, _) => Close();
        }

        var fullScreenMenuItem =
            this.FindControl<MenuItem>("FullScreenMenuItem");

        if (fullScreenMenuItem is not null)
        {
            fullScreenMenuItem.Click +=
                (_, _) => ToggleFullScreen();
        }

        KeyDown += OnShellKeyDown;
    }

    private void HookPlacementTracking()
    {
        SizeChanged +=
            (_, _) => CaptureNormalPlacement();

        PositionChanged +=
            (_, _) => CaptureNormalPlacement();
    }

    private void OnTitleBarPointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.FullScreen
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        BeginMoveDrag(e);
        e.Handled = true;
    }

    private void OnTitleBarDoubleTapped(
        object? sender,
        TappedEventArgs e)
    {
        if (WindowState == WindowState.FullScreen)
        {
            return;
        }

        ToggleMaximized();
        e.Handled = true;
    }

    private void OnResizeWestPointerPressed(
        object? sender,
        PointerPressedEventArgs e) =>
        BeginResizeDragIfPossible(
            WindowEdge.West,
            e);

    private void OnResizeEastPointerPressed(
        object? sender,
        PointerPressedEventArgs e) =>
        BeginResizeDragIfPossible(
            WindowEdge.East,
            e);

    private void OnResizeNorthPointerPressed(
        object? sender,
        PointerPressedEventArgs e) =>
        BeginResizeDragIfPossible(
            WindowEdge.North,
            e);

    private void OnResizeSouthPointerPressed(
        object? sender,
        PointerPressedEventArgs e) =>
        BeginResizeDragIfPossible(
            WindowEdge.South,
            e);

    private void OnResizeNorthWestPointerPressed(
        object? sender,
        PointerPressedEventArgs e) =>
        BeginResizeDragIfPossible(
            WindowEdge.NorthWest,
            e);

    private void OnResizeNorthEastPointerPressed(
        object? sender,
        PointerPressedEventArgs e) =>
        BeginResizeDragIfPossible(
            WindowEdge.NorthEast,
            e);

    private void OnResizeSouthWestPointerPressed(
        object? sender,
        PointerPressedEventArgs e) =>
        BeginResizeDragIfPossible(
            WindowEdge.SouthWest,
            e);

    private void OnResizeSouthEastPointerPressed(
        object? sender,
        PointerPressedEventArgs e) =>
        BeginResizeDragIfPossible(
            WindowEdge.SouthEast,
            e);

    private void BeginResizeDragIfPossible(
        WindowEdge edge,
        PointerPressedEventArgs e)
    {
        if (!CanResize
            || WindowState != WindowState.Normal
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        BeginResizeDrag(
            edge,
            e);

        e.Handled = true;
    }

    private void OnShellKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape
            && WindowState == WindowState.FullScreen)
        {
            ExitFullScreen();
            e.Handled = true;
        }
    }

    private void ToggleMaximized()
    {
        if (WindowState == WindowState.FullScreen)
        {
            ExitFullScreen();
            return;
        }

        WindowState =
            WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
    }

    private void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            ExitFullScreen();
            return;
        }

        _stateBeforeFullScreen =
            WindowState == WindowState.Maximized
                ? WindowState.Maximized
                : WindowState.Normal;

        WindowState = WindowState.FullScreen;
    }

    private void ExitFullScreen()
    {
        WindowState =
            _stateBeforeFullScreen == WindowState.Maximized
                ? WindowState.Maximized
                : WindowState.Normal;
    }

    private void RestorePlacement()
    {
        var snapshot =
            _placementStore?.Load();

        if (snapshot is null)
        {
            return;
        }

        _isRestoringPlacement = true;

        try
        {
            var width =
                ClampDimension(
                    snapshot.Width,
                    MinWidth,
                    DefaultWidth);

            var height =
                ClampDimension(
                    snapshot.Height,
                    MinHeight,
                    DefaultHeight);

            Width = width;
            Height = height;
            _normalWidth = width;
            _normalHeight = height;

            var savedPosition =
                new PixelPoint(
                    snapshot.X,
                    snapshot.Y);

            var isOnAvailableScreen =
                Screens.All.Any(
                    screen =>
                        screen.WorkingArea.Contains(
                            savedPosition));

            if (isOnAvailableScreen)
            {
                Position = savedPosition;
                _normalPosition = savedPosition;
                _hasNormalPosition = true;
            }
            else
            {
                CenterOnPrimaryScreen(
                    width,
                    height);
            }

            var restoredState =
                ParseWindowState(
                    snapshot.State);

            if (restoredState == WindowState.FullScreen)
            {
                _stateBeforeFullScreen = WindowState.Normal;
            }

            WindowState = restoredState;
        }
        finally
        {
            _isRestoringPlacement = false;
        }
    }

    private void CenterOnPrimaryScreen(
        double width,
        double height)
    {
        var screen =
            Screens.Primary;

        if (screen is null)
        {
            WindowStartupLocation =
                WindowStartupLocation.CenterScreen;

            return;
        }

        var area =
            screen.WorkingArea;

        var scaling =
            Math.Max(
                screen.Scaling,
                0.1);

        var pixelWidth =
            (int)Math.Round(
                width * scaling);

        var pixelHeight =
            (int)Math.Round(
                height * scaling);

        var position =
            new PixelPoint(
                area.X + Math.Max(0, (area.Width - pixelWidth) / 2),
                area.Y + Math.Max(0, (area.Height - pixelHeight) / 2));

        Position = position;
        _normalPosition = position;
        _hasNormalPosition = true;
    }

    private void CaptureNormalPlacement()
    {
        if (_isRestoringPlacement
            || WindowState != WindowState.Normal)
        {
            return;
        }

        if (Bounds.Width > 0)
        {
            _normalWidth =
                Math.Max(
                    MinWidth,
                    Bounds.Width);
        }

        if (Bounds.Height > 0)
        {
            _normalHeight =
                Math.Max(
                    MinHeight,
                    Bounds.Height);
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

        var state =
            WindowState == WindowState.Minimized
                ? WindowState.Normal
                : WindowState;

        var position =
            _hasNormalPosition
                ? _normalPosition
                : Position;

        _placementStore.Save(
            new WindowPlacementSnapshot(
                _normalWidth,
                _normalHeight,
                position.X,
                position.Y,
                state.ToString()));
    }

    private static WindowState ParseWindowState(
        string? value)
    {
        if (!Enum.TryParse<WindowState>(
                value,
                ignoreCase: true,
                out var state))
        {
            return WindowState.Normal;
        }

        return state == WindowState.Minimized
            ? WindowState.Normal
            : state;
    }

    private static double ClampDimension(
        double value,
        double minimum,
        double fallback)
    {
        if (double.IsNaN(value)
            || double.IsInfinity(value)
            || value <= 0)
        {
            return fallback;
        }

        return Math.Max(
            minimum,
            Math.Min(value, 6000));
    }
}
