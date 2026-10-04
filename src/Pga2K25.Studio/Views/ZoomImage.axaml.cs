using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Pga2K25.Studio.Views;

/// <summary>
/// A large image with zoom (buttons, Ctrl + wheel), drag to pan, fit and 1:1. Fits itself when an image of a
/// new size arrives; keeps the view when a same-size image replaces it (a re-render with other layers).
/// </summary>
public partial class ZoomImage : UserControl
{
    public static readonly StyledProperty<Bitmap?> SourceProperty = AvaloniaProperty.Register<ZoomImage, Bitmap?>(nameof(Source));

    /// <summary>Fit to the viewer's width (true) or to the whole page (false).</summary>
    public static readonly StyledProperty<bool> FitWidthProperty = AvaloniaProperty.Register<ZoomImage, bool>(nameof(FitWidth), true);

    public Bitmap? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public bool FitWidth { get => GetValue(FitWidthProperty); set => SetValue(FitWidthProperty, value); }

    /// <summary>The pointer over the image, in image pixels.</summary>
    public event Action<Point>? ImagePointerMoved;

    readonly ScrollViewer _scroller;
    readonly LayoutTransformControl _zoomer;
    readonly Image _image;
    readonly TextBlock _zoomText;
    double _zoom = 0.4;
    PixelSize _lastSize;
    Point? _dragFrom;
    Vector _dragOffset;

    public ZoomImage()
    {
        AvaloniaXamlLoader.Load(this);
        _scroller = this.FindControl<ScrollViewer>("Scroller")!;
        _zoomer = this.FindControl<LayoutTransformControl>("Zoomer")!;
        _image = this.FindControl<Image>("Picture")!;
        _zoomText = this.FindControl<TextBlock>("ZoomText")!;
        ApplyZoom(_zoom, null);

        _image.PointerMoved += (_, e) => ImagePointerMoved?.Invoke(e.GetPosition(_image));
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SourceProperty) return;
        _image.Source = Source;
        if (Source is { } bmp && bmp.PixelSize != _lastSize)
        {
            _lastSize = bmp.PixelSize;
            // Wait for layout so the viewport is known.
            Dispatcher.UIThread.Post(Fit, DispatcherPriority.Loaded);
        }
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
    void Actual_Click(object? sender, RoutedEventArgs e) => ApplyZoom(1, null);

    public void Fit()
    {
        if (Source is not { } bmp || _scroller.Viewport.Width <= 0) return;
        double w = (_scroller.Viewport.Width - 4) / bmp.PixelSize.Width;
        double h = (_scroller.Viewport.Height - 4) / bmp.PixelSize.Height;
        ApplyZoom(Math.Max(0.02, FitWidth ? w : Math.Min(w, h)), null);
        Dispatcher.UIThread.Post(() => _scroller.Offset = new Vector(0, 0), DispatcherPriority.Background);
    }

    /// <summary>Zoom keeping the viewer point under <paramref name="anchor"/> (or the centre) fixed.</summary>
    void ApplyZoom(double zoom, Point? anchor)
    {
        zoom = Math.Clamp(zoom, 0.02, 4);
        var a = anchor ?? new Point(_scroller.Viewport.Width / 2, _scroller.Viewport.Height / 2);
        var content = (_scroller.Offset + a) / _zoom;
        _zoom = zoom;
        _zoomer.LayoutTransform = new ScaleTransform(zoom, zoom);
        _zoomText.Text = $"{zoom * 100:0} %";
        Dispatcher.UIThread.Post(() => _scroller.Offset = content * zoom - a, DispatcherPriority.Background);
    }

    /// <summary>Centre the view on an image pixel, zooming in a little if the whole page is showing.</summary>
    public void CentreOn(Point px)
    {
        if (_zoom < 0.5) ApplyZoom(0.6, null);
        Dispatcher.UIThread.Post(() =>
            _scroller.Offset = new Vector(px.X * _zoom - _scroller.Viewport.Width / 2, px.Y * _zoom - _scroller.Viewport.Height / 2),
            DispatcherPriority.Background);
    }
}
