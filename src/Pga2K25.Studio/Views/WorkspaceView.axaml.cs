using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Pga2K25.Studio.ViewModels;

namespace Pga2K25.Studio.Views;

/// <summary>Viewer interaction: zoom (buttons, Ctrl + wheel), drag to pan, hover readout, centring on a hole.</summary>
public partial class WorkspaceView : UserControl
{
    readonly ScrollViewer _scroller;
    readonly LayoutTransformControl _zoomer;
    readonly Image _map;
    readonly TextBlock _zoomText;
    double _zoom = 0.4;
    Point? _dragFrom;
    Vector _dragOffset;
    WorkspaceViewModel? _vm;

    public WorkspaceView()
    {
        AvaloniaXamlLoader.Load(this);
        _scroller = this.FindControl<ScrollViewer>("Scroller")!;
        _zoomer = this.FindControl<LayoutTransformControl>("Zoomer")!;
        _map = this.FindControl<Image>("Map")!;
        _zoomText = this.FindControl<TextBlock>("ZoomText")!;
        ApplyZoom(_zoom, null);

        _map.PointerMoved += (_, e) => _vm?.Hover(e.GetPosition(_map));
        _scroller.AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        _scroller.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(_scroller).Properties.IsLeftButtonPressed) return;
            _dragFrom = e.GetPosition(_scroller);
            _dragOffset = _scroller.Offset;
            e.Pointer.Capture(_scroller);
        };
        _scroller.PointerMoved += (_, e) =>
        {
            if (_dragFrom is not { } from) return;
            var d = e.GetPosition(_scroller) - from;
            _scroller.Offset = new Vector(_dragOffset.X - d.X, _dragOffset.Y - d.Y);
        };
        _scroller.PointerReleased += (_, e) => { _dragFrom = null; e.Pointer.Capture(null); };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) { _vm.CentreOn -= CentreOn; _vm.FitRequested -= Fit; }
        _vm = DataContext as WorkspaceViewModel;
        if (_vm is not null) { _vm.CentreOn += CentreOn; _vm.FitRequested += Fit; }
    }

    void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
        ApplyZoom(_zoom * (e.Delta.Y > 0 ? 1.2 : 1 / 1.2), e.GetPosition(_scroller));
        e.Handled = true;
    }

    void ZoomIn_Click(object? sender, RoutedEventArgs e) => ApplyZoom(_zoom * 1.25, null);
    void ZoomOut_Click(object? sender, RoutedEventArgs e) => ApplyZoom(_zoom / 1.25, null);
    void Fit_Click(object? sender, RoutedEventArgs e) => Fit();

    /// <summary>Fit the sheet's width to the viewer.</summary>
    void Fit()
    {
        if (_vm?.MapImage is not { } bmp || _scroller.Viewport.Width <= 0) return;
        ApplyZoom(Math.Max(0.05, (_scroller.Viewport.Width - 4) / bmp.PixelSize.Width), null);
    }

    /// <summary>Zoom keeping the viewer point under <paramref name="anchor"/> (or the centre) fixed.</summary>
    void ApplyZoom(double zoom, Point? anchor)
    {
        zoom = Math.Clamp(zoom, 0.05, 4);
        var a = anchor ?? new Point(_scroller.Viewport.Width / 2, _scroller.Viewport.Height / 2);
        var content = (_scroller.Offset + a) / _zoom;
        _zoom = zoom;
        _zoomer.LayoutTransform = new ScaleTransform(zoom, zoom);
        _zoomText.Text = $"{zoom * 100:0} %";
        Dispatcher.UIThread.Post(() => _scroller.Offset = content * zoom - a, DispatcherPriority.Background);
    }

    void CentreOn(Point px)
    {
        if (_zoom < 0.5) ApplyZoom(0.6, null);
        Dispatcher.UIThread.Post(() =>
            _scroller.Offset = new Vector(px.X * _zoom - _scroller.Viewport.Width / 2, px.Y * _zoom - _scroller.Viewport.Height / 2),
            DispatcherPriority.Background);
    }
}
