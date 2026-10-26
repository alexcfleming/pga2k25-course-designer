using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Pga2K25.Studio.Views;

/// <summary>An image drawn over the map: <see cref="ToMap"/> takes its pixels to map-bitmap pixels.</summary>
public sealed record SketchLayer(Bitmap Image, Matrix ToMap, double Opacity);

/// <summary>
/// Traced sketch lines in map-bitmap pixels, stroked at a steady width on screen whatever the zoom;
/// <see cref="Construction"/> lines (shot lines, aim points) thinner and dashed.
/// </summary>
public sealed record LineLayer(Geometry Geometry, Color Colour, double Opacity, bool Construction);

/// <summary>A note pin at a map-bitmap pixel. <see cref="Callout"/> shows beside it when selected or hovered.</summary>
public sealed record MapPin(string Id, Point At, string Label, Color Colour, string Callout);

/// <summary>A routing-test landing zone: map-pixel lengths along its arrow, and its label (the rise from the tee).</summary>
public sealed record MapZone(double From, double To, string Label);

/// <summary>
/// A routing-test arrow in map-bitmap pixels: tee, unit direction off the tee, each leg's length and the turn at each
/// bend between them (degrees, clockwise on the bitmap). Its par's usual range and its landing zones are lengths
/// along the line of play; its corridor and zones are drawn <see cref="CorridorWidth"/> and <see cref="ZoneWidth"/>
/// wide. <see cref="Label"/> is the hole number on its badge.
/// </summary>
public sealed record MapArrow(string Id, Point Tee, Vector Direction, IReadOnlyList<double> Legs, IReadOnlyList<double> Turns,
    double RangeMin, double RangeMax, Color Colour, string Label, IReadOnlyList<MapZone> Zones, double CorridorWidth, double ZoneWidth)
{
    public double Length => Legs.Sum();
    public Point Head => Points[^1];

    /// <summary>The unit direction of each leg.</summary>
    public IReadOnlyList<Vector> Headings
    {
        get
        {
            var h = new List<Vector> { Direction };
            for (int k = 1; k < Legs.Count; k++) h.Add(Rotate(h[k - 1], k - 1 < Turns.Count ? Turns[k - 1] : 0));
            return h;
        }
    }

    /// <summary>The tee, each bend and the green.</summary>
    public IReadOnlyList<Point> Points
    {
        get
        {
            var h = Headings;
            var pts = new List<Point> { Tee };
            for (int k = 0; k < Legs.Count; k++) pts.Add(pts[k] + h[k] * Legs[k]);
            return pts;
        }
    }

    /// <summary>The point <paramref name="along"/> the line of play; past the green it carries on along the last leg.</summary>
    public Point At(double along) => AtWithHeading(along).At;

    public (Point At, Vector Heading) AtWithHeading(double along)
    {
        var h = Headings;
        var pts = Points;
        int k = 0;
        double start = 0;
        while (k < Legs.Count - 1 && along > start + Legs[k]) start += Legs[k++];
        return (pts[k] + h[k] * (along - start), h[k]);
    }

    /// <summary>The line of play from <paramref name="from"/> to <paramref name="to"/> along it, bends included.</summary>
    public IReadOnlyList<Point> Span(double from, double to)
    {
        var pts = new List<Point> { At(from) };
        var all = Points;
        double at = 0;
        for (int k = 0; k + 1 < Legs.Count; k++)
        {
            at += Legs[k];
            if (at > from && at < to) pts.Add(all[k + 1]);
        }
        pts.Add(At(to));
        return pts;
    }

    /// <summary>
    /// Leg <paramref name="leg"/> swung toward a map point: the first leg turns the whole arrow, a later one its bend
    /// (snapped as saved), and the legs after it follow.
    /// </summary>
    public MapArrow AimedAt(int leg, Point toward)
    {
        Vector d = toward - Points[leg];
        if (d.Length < 1e-6) return this;
        if (leg == 0) return this with { Direction = d / d.Length };
        var turns = Enumerable.Range(0, Legs.Count - 1).Select(k => k < Turns.Count ? Turns[k] : 0).ToArray();
        turns[leg - 1] = Pga2K25.TerrainMap.RoutingArrow.SnapTurn(TurnBetween(Headings[leg - 1], d));
        return this with { Turns = turns };
    }

    /// <summary>A direction turned clockwise on the bitmap (y down).</summary>
    public static Vector Rotate(Vector v, double degrees)
    {
        double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        return new Vector(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    /// <summary>The clockwise turn (degrees, −180 to 180) from one direction to another on the bitmap.</summary>
    public static double TurnBetween(Vector from, Vector to) =>
        Math.Atan2(from.X * to.Y - from.Y * to.X, from.X * to.X + from.Y * to.Y) * 180 / Math.PI;
}

/// <summary>A routing-test walk from one green to the next tee (map-bitmap pixels), labelled with its yards.</summary>
public sealed record MapWalk(Point From, Point To, string Label);

/// <summary>Two routing-test holes crowding each other: their closest points (map-bitmap pixels) and the gap.</summary>
public sealed record MapConflict(Point A, Point B, bool Overlap, string Label);

/// <summary>
/// The live terrain map with sketch layers and note pins, under one view transform: zoom (wheel, about the
/// pointer), pan (drag), rotate (right-drag or Shift + drag, about the centre of the view). The map bitmap is
/// north-up, so the view's rotation is the compass turn. Pins are drawn at screen size so they stay readable.
/// Routing-test arrows sit on top: with an <see cref="ArrowTemplate"/> set, a press drops a tee and the drag aims it;
/// an arrow's legs or badge drag it, and the selected arrow's handle at the end of each leg swings that leg. Corridors
/// and landing zones are drawn under the arrows, and corridor conflicts over them.
/// </summary>
public sealed class MapSurface : Control
{
    const double MinScale = 0.02, MaxScale = 8, PinRadius = 9, ClickSlop = 4, BadgeRadius = 11, ShaftReach = 9;

    enum ArrowDrag { None, Place, Move, Aim }

    Bitmap? _source;
    IReadOnlyList<SketchLayer> _sketches = [];
    IReadOnlyList<LineLayer> _lines = [];
    IReadOnlyList<MapPin> _pins = [];
    string? _selectedPin, _hoverPin;
    bool _placing;
    IReadOnlyList<MapArrow> _arrows = [];
    IReadOnlyList<MapWalk> _walks = [];
    IReadOnlyList<MapConflict> _conflicts = [];
    string? _selectedArrow;
    bool _showRanges = true, _showZones = true, _showCorridors = true;

    // View: the map pixel at the centre of the control, screen pixels per map pixel, and the turn (radians, clockwise).
    Point _centre;
    double _scale = 0.4, _angle;

    // Pointer.
    Point? _press;
    Point _pressCentre;
    double _pressAngle;
    bool _moved, _rotating;
    StandardCursorType _cursor = StandardCursorType.Cross;
    string? _dragPin;
    Point _dragAt;
    ArrowDrag _arrowDrag;
    MapArrow? _arrowPreview;
    Vector _arrowGrab;
    int _aimLeg;

    DispatcherTimer? _anim;
    bool _fitPending;

    public event Action<Point>? MapPointerMoved;
    /// <summary>A click on the map away from any pin (map pixels).</summary>
    public event Action<Point>? MapClicked;
    public event Action<string>? PinClicked;
    public event Action<string, Point>? PinMoved;
    /// <summary>A routing-test tee dropped (map pixels), with the direction it was dragged, or null for a plain click.</summary>
    public event Action<Point, Vector?>? ArrowPlaced;
    public event Action<string>? ArrowClicked;
    /// <summary>An arrow dragged to a new tee (map pixels).</summary>
    public event Action<string, Point>? ArrowMoved;
    /// <summary>One leg of an arrow swung by the handle at its end: the leg, and its new unit direction (map pixels).</summary>
    public event Action<string, int, Vector>? ArrowAimed;
    /// <summary>Zoom or rotation changed.</summary>
    public event Action? ViewChanged;

    public MapSurface()
    {
        ClipToBounds = true;
        Focusable = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    public double Zoom => _scale;
    /// <summary>Clockwise turn of the view, degrees (0 = north up).</summary>
    public double RotationDegrees => _angle * 180 / Math.PI;

    public Bitmap? Source
    {
        get => _source;
        set
        {
            bool newSize = value is not null && (_source is null || _source.PixelSize != value.PixelSize);
            _source = value;
            if (newSize) { _fitPending = true; Dispatcher.UIThread.Post(() => { if (_fitPending) Fit(animate: false); }, DispatcherPriority.Loaded); }
            InvalidateVisual();
        }
    }

    public IReadOnlyList<SketchLayer> Sketches { get => _sketches; set { _sketches = value; InvalidateVisual(); } }
    public IReadOnlyList<LineLayer> Lines { get => _lines; set { _lines = value; InvalidateVisual(); } }
    public IReadOnlyList<MapPin> Pins { get => _pins; set { _pins = value; InvalidateVisual(); } }
    public string? SelectedPin { get => _selectedPin; set { _selectedPin = value; InvalidateVisual(); } }
    public IReadOnlyList<MapArrow> Arrows { get => _arrows; set { _arrows = value; InvalidateVisual(); } }
    public IReadOnlyList<MapWalk> Walks { get => _walks; set { _walks = value; InvalidateVisual(); } }
    public string? SelectedArrow { get => _selectedArrow; set { _selectedArrow = value; InvalidateVisual(); } }
    public bool ShowRanges { get => _showRanges; set { _showRanges = value; InvalidateVisual(); } }
    public bool ShowZones { get => _showZones; set { _showZones = value; InvalidateVisual(); } }
    public bool ShowCorridors { get => _showCorridors; set { _showCorridors = value; InvalidateVisual(); } }
    public IReadOnlyList<MapConflict> Conflicts { get => _conflicts; set { _conflicts = value; InvalidateVisual(); } }

    /// <summary>The par being placed (its length, range and colour; tee and direction unused), or null when not placing.</summary>
    public MapArrow? ArrowTemplate { get; set; }

    /// <summary>Placing a note: clicks drop a pin, and the cursor says so.</summary>
    public bool Placing
    {
        get => _placing;
        set { _placing = value; _cursor = value ? StandardCursorType.Hand : StandardCursorType.Cross; Cursor = new Cursor(_cursor); }
    }

    Size ImageSize => _source is null ? default : new Size(_source.PixelSize.Width, _source.PixelSize.Height);
    Point ViewCentre => new(Bounds.Width / 2, Bounds.Height / 2);

    Matrix ViewMatrix(Point centre, double scale, double angle) =>
        Matrix.CreateTranslation(-centre.X, -centre.Y) * Matrix.CreateRotation(angle) * Matrix.CreateScale(scale, scale)
        * Matrix.CreateTranslation(Bounds.Width / 2, Bounds.Height / 2);

    Matrix View => ViewMatrix(_centre, _scale, _angle);

    Point ToScreen(Point map) => map.Transform(View);
    Point ToMap(Point screen) => View.TryInvert(out var inv) ? screen.Transform(inv) : screen;

    /// <summary>A screen offset as a map-pixel offset (undoing the turn and the zoom).</summary>
    Vector ScreenToMapVector(Vector d, double scale, double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return new Vector((d.X * c + d.Y * s) / scale, (-d.X * s + d.Y * c) / scale);
    }

    // ───────────────────────────── drawing ─────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        if (_source is null) return;
        var view = View;
        // Each layer pushes its whole matrix from the top level, so it doesn't matter whether pushes compose.
        using (ctx.PushTransform(view))
            ctx.DrawImage(_source, new Rect(_source.Size), new Rect(ImageSize));
        foreach (var layer in _sketches)
        {
            using (ctx.PushTransform(layer.ToMap * view))
            using (ctx.PushOpacity(layer.Opacity))
                ctx.DrawImage(layer.Image, new Rect(layer.Image.Size), new Rect(0, 0, layer.Image.PixelSize.Width, layer.Image.PixelSize.Height));
        }
        if (_lines.Count > 0)
        {
            // Pens are in map pixels under the view transform, so divide by the zoom for a steady width on screen;
            // a little heavier when zoomed in close.
            double screen = Math.Clamp(1.4 + 0.8 * _scale, 1.6, 3.2);
            using (ctx.PushTransform(view))
                foreach (var layer in _lines)
                {
                    var brush = new SolidColorBrush(layer.Colour, layer.Opacity);
                    var pen = layer.Construction
                        ? new Pen(brush, screen * 0.6 / _scale, new DashStyle([4, 3], 0), PenLineCap.Round, PenLineJoin.Round)
                        : new Pen(brush, screen / _scale, null, PenLineCap.Round, PenLineJoin.Round);
                    ctx.DrawGeometry(null, pen, layer.Geometry);
                }
        }
        DrawRouting(ctx);
        DrawPins(ctx);
    }

    /// <summary>The arrows being shown: the saved ones, with the one being dragged or dropped in its dragged place.</summary>
    IEnumerable<MapArrow> ShownArrows()
    {
        foreach (var a in _arrows) yield return _arrowPreview is { } p && p.Id == a.Id && _arrowDrag != ArrowDrag.Place ? p : a;
        if (_arrowDrag == ArrowDrag.Place && _arrowPreview is { } placing) yield return placing;
    }

    static readonly Color OverlapColour = Color.Parse("#B3261E"), TightColour = Color.Parse("#A65F00");

    IReadOnlyList<Point> ToScreen(IReadOnlyList<Point> map) => map.Select(ToScreen).ToList();

    static StreamGeometry Polyline(IReadOnlyList<Point> pts)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(pts[0], false);
        for (int i = 1; i < pts.Count; i++) g.LineTo(pts[i]);
        g.EndFigure(false);
        return geo;
    }

    /// <summary>A small label on a white halo, centred on a screen point.</summary>
    static void Tag(DrawingContext ctx, string s, Point centre, IBrush ink, FontWeight weight)
    {
        var t = Text(s, 10, ink, weight);
        var box = new Rect(centre.X - t.Width / 2 - 4, centre.Y - t.Height / 2 - 1, t.Width + 8, t.Height + 2);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), null, box, 4, 4);
        ctx.DrawText(t, new Point(box.X + 4, box.Y + 1));
    }

    void DrawRouting(DrawingContext ctx)
    {
        if (_arrows.Count == 0 && _arrowDrag != ArrowDrag.Place) return;
        var ink = new SolidColorBrush(Color.Parse("#3F4A43"));
        var shown = ShownArrows().Where(a => a.Length > 0).ToList();

        // Corridors under everything: where two cross, the tint darkens.
        if (_showCorridors)
            foreach (var a in shown)
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(a.Colour, 0.13), a.CorridorWidth * _scale, null, PenLineCap.Round, PenLineJoin.Round),
                    Polyline(ToScreen(a.Points)));

        if (_showZones)
            foreach (var a in shown)
            {
                // A dragged arrow's rise labels are out of date until it's dropped.
                bool dragged = _arrowDrag != ArrowDrag.None && _arrowPreview?.Id == a.Id;
                foreach (var z in a.Zones)
                {
                    ctx.DrawGeometry(null, new Pen(new SolidColorBrush(a.Colour, 0.38), a.ZoneWidth * _scale, null, PenLineCap.Flat, PenLineJoin.Round),
                        Polyline(ToScreen(a.Span(z.From, z.To))));
                    if (dragged || z.Label.Length == 0) continue;
                    var (at, h) = a.AtWithHeading((z.From + z.To) / 2);
                    Point c = ToScreen(at), c1 = ToScreen(at + h);
                    Vector u = c1 - c;
                    if (u.Length < 1e-9) continue;
                    u /= u.Length;
                    var n = new Vector(-u.Y, u.X);
                    if (n.X > 0) n = -n;  // labels on the left of the screen
                    Tag(ctx, z.Label, c + n * (a.ZoneWidth * _scale / 2 + 12), ink, FontWeight.SemiBold);
                }
            }

        // Walks and conflicts follow the saved arrows, so they wait for a drag to finish.
        if (_arrowDrag == ArrowDrag.None)
        {
            var walkPen = new Pen(ink, 1.5, new DashStyle([2, 3], 0), PenLineCap.Round);
            foreach (var w in _walks)
            {
                Point a = ToScreen(w.From), b = ToScreen(w.To);
                ctx.DrawLine(walkPen, a, b);
                Tag(ctx, w.Label, new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2), ink, FontWeight.Normal);
            }
        }

        var white = new Pen(Brushes.White, 2);
        foreach (var a in shown)
        {
            var pts = ToScreen(a.Points);
            Point head = pts[^1], prev = pts[^2];
            Vector last = head - prev;
            double len = last.Length;
            if (len < 1) continue;
            var u = last / len;
            var n = new Vector(-u.Y, u.X);
            var brush = new SolidColorBrush(a.Colour);
            bool selected = a.Id == _selectedArrow || (_arrowPreview is { } p && p.Id == a.Id);
            if (_showRanges)
            {
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(a.Colour, 0.22), 12, null, PenLineCap.Flat, PenLineJoin.Round),
                    Polyline(ToScreen(a.Span(a.RangeMin, a.RangeMax))));
                var tick = new Pen(brush, 1.5);
                foreach (double along in new[] { a.RangeMin, a.RangeMax })
                {
                    var (at, h) = a.AtWithHeading(along);
                    Point r = ToScreen(at);
                    Vector t = ToScreen(at + h) - r;
                    if (t.Length < 1e-9) continue;
                    var tn = new Vector(-t.Y, t.X) / t.Length;
                    ctx.DrawLine(tick, r - tn * 7, r + tn * 7);
                }
            }
            double headLen = Math.Min(16, len * 0.4);
            var shaft = pts.Take(pts.Count - 1).Append(head - u * (headLen * 0.75)).ToList();
            ctx.DrawGeometry(null, new Pen(brush, selected ? 5 : 3.5, null, PenLineCap.Round, PenLineJoin.Round), Polyline(shaft));
            var tip = new StreamGeometry();
            using (var g = tip.Open())
            {
                g.BeginFigure(head, true);
                g.LineTo(head - u * headLen + n * (headLen / 2));
                g.LineTo(head - u * headLen - n * (headLen / 2));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(brush, null, tip);
            ctx.DrawEllipse(Brushes.White, new Pen(brush, 3), pts[0], 6, 6);
            for (int k = 1; k < pts.Count - 1; k++) ctx.DrawEllipse(Brushes.White, new Pen(brush, 2.5), pts[k], 4.5, 4.5);
            // The selected arrow's handles: one at the end of each leg.
            if (a.Id == _selectedArrow && _arrowDrag == ArrowDrag.None)
                for (int k = 1; k < pts.Count; k++)
                    ctx.DrawEllipse(new SolidColorBrush(Colors.White, 0.6), new Pen(ink, 1.5, new DashStyle([3, 2], 0)), pts[k], BadgeRadius, BadgeRadius);
            var mid = ToScreen(a.At(a.Length / 2));
            ctx.DrawEllipse(brush, white, mid, BadgeRadius, BadgeRadius);
            var label = Text(a.Label, 11, Brushes.White, FontWeight.Bold);
            ctx.DrawText(label, new Point(mid.X - label.Width / 2, mid.Y - label.Height / 2));
        }

        if (_showCorridors && _arrowDrag == ArrowDrag.None)
            foreach (var c in _conflicts)
            {
                var colour = new SolidColorBrush(c.Overlap ? OverlapColour : TightColour);
                Point a = ToScreen(c.A), b = ToScreen(c.B), m = new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                ctx.DrawLine(new Pen(colour, 2, new DashStyle([2, 1.5], 0)), a, b);
                ctx.DrawEllipse(colour, white, m, 9, 9);
                var bang = Text("!", 12, Brushes.White, FontWeight.Bold);
                ctx.DrawText(bang, new Point(m.X - bang.Width / 2, m.Y - bang.Height / 2));
                Tag(ctx, c.Label, m + new Vector(0, -20), colour, FontWeight.SemiBold);
            }
    }

    void DrawPins(DrawingContext ctx)
    {
        var white = new Pen(Brushes.White, 2);
        MapPin? callout = null;
        foreach (var pin in _pins)
        {
            var at = ToScreen(pin.Id == _dragPin ? _dragAt : pin.At);
            bool selected = pin.Id == _selectedPin;
            double r = selected ? PinRadius + 3 : PinRadius;
            if (selected) ctx.DrawEllipse(null, new Pen(new SolidColorBrush(pin.Colour), 2), at, r + 4, r + 4);
            ctx.DrawEllipse(new SolidColorBrush(pin.Colour), white, at, r, r);
            var label = Text(pin.Label, 10, Brushes.White, FontWeight.Bold);
            ctx.DrawText(label, new Point(at.X - label.Width / 2, at.Y - label.Height / 2));
            if (selected || (pin.Id == _hoverPin && callout is null)) callout = pin;
        }
        if (callout is null) return;

        var p = ToScreen(callout.Id == _dragPin ? _dragAt : callout.At);
        var text = Text(callout.Callout, 12, new SolidColorBrush(Color.Parse("#1F2622")), FontWeight.Normal, 240);
        var box = new Rect(p.X + PinRadius + 8, p.Y - 12, text.Width + 20, text.Height + 14);
        if (box.Right > Bounds.Width - 4) box = box.WithX(p.X - PinRadius - 8 - box.Width);
        if (box.Bottom > Bounds.Height - 4) box = box.WithY(Bounds.Height - 4 - box.Height);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(245, 255, 255, 255)), new Pen(new SolidColorBrush(callout.Colour), 1.5), box, 6, 6);
        ctx.DrawText(text, new Point(box.X + 10, box.Y + 7));
    }

    static FormattedText Text(string s, double size, IBrush brush, FontWeight weight, double maxWidth = double.PositiveInfinity) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush)
        {
            MaxTextWidth = maxWidth,
        };

    // ───────────────────────────── view moves ─────────────────────────────

    /// <summary>The whole map, north up.</summary>
    public void Fit(bool animate = true)
    {
        _fitPending = false;
        if (_source is null || Bounds.Width <= 0) return;
        var s = ImageSize;
        double scale = Math.Clamp(Math.Min((Bounds.Width - 16) / s.Width, (Bounds.Height - 16) / s.Height), MinScale, MaxScale);
        MoveTo(new Point(s.Width / 2, s.Height / 2), scale, 0, animate);
    }

    /// <summary>Centre on a map pixel, zooming in a little if the whole map is showing; keeps the turn.</summary>
    public void CentreOn(Point map) => MoveTo(map, Math.Max(_scale, 0.6), _angle, animate: true);

    /// <summary>
    /// Turns and zooms so the quadrilateral <paramref name="corners"/> (map pixels) fills the view with
    /// <paramref name="up"/> (a map-pixel direction) pointing up the screen: a hole sheet's own view.
    /// </summary>
    public void ShowFrame(IReadOnlyList<Point> corners, Vector up)
    {
        if (Bounds.Width <= 0 || corners.Count == 0) return;
        double angle = -Math.PI / 2 - Math.Atan2(up.Y, up.X);
        double c = Math.Cos(angle), s = Math.Sin(angle);
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var p in corners)
        {
            double x = p.X * c - p.Y * s, y = p.X * s + p.Y * c;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        double scale = Math.Clamp(Math.Min((Bounds.Width - 48) / (maxX - minX), (Bounds.Height - 48) / (maxY - minY)), MinScale, MaxScale);
        var centre = new Point(corners.Average(p => p.X), corners.Average(p => p.Y));
        MoveTo(centre, scale, angle, animate: true);
    }

    public void ZoomBy(double factor) => ZoomAt(ViewCentre, factor, animate: true);

    public void RotateBy(double degrees) => MoveTo(_centre, _scale, _angle + degrees * Math.PI / 180, animate: true);

    public void NorthUp() => MoveTo(_centre, _scale, 0, animate: true);

    void ZoomAt(Point screen, double factor, bool animate)
    {
        double scale = Math.Clamp(_scale * factor, MinScale, MaxScale);
        var map = ToMap(screen);
        // Keep the map point under the pointer where it is.
        var centre = map - ScreenToMapVector(screen - ViewCentre, scale, _angle);
        MoveTo(centre, scale, _angle, animate);
    }

    void MoveTo(Point centre, double scale, double angle, bool animate)
    {
        _anim?.Stop();
        if (!animate)
        {
            (_centre, _scale, _angle) = (centre, scale, angle);
            InvalidateVisual();
            ViewChanged?.Invoke();
            return;
        }
        var (c0, s0, a0) = (_centre, _scale, _angle);
        // Turn the short way round.
        double da = Math.IEEERemainder(angle - a0, 2 * Math.PI);
        var start = DateTime.UtcNow;
        const double ms = 380;
        _anim = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) =>
        {
            double t = Math.Min(1, (DateTime.UtcNow - start).TotalMilliseconds / ms);
            double e = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            _centre = new Point(c0.X + (centre.X - c0.X) * e, c0.Y + (centre.Y - c0.Y) * e);
            _scale = Math.Exp(Math.Log(s0) + (Math.Log(scale) - Math.Log(s0)) * e);
            _angle = a0 + da * e;
            InvalidateVisual();
            ViewChanged?.Invoke();
            if (t >= 1) { _anim?.Stop(); _angle = Math.IEEERemainder(_angle, 2 * Math.PI); }
        });
        _anim.Start();
    }

    // ───────────────────────────── pointer ─────────────────────────────

    string? HitPin(Point screen)
    {
        for (int i = _pins.Count - 1; i >= 0; i--)
        {
            var d = ToScreen(_pins[i].At) - screen;
            if (Math.Sqrt(d.X * d.X + d.Y * d.Y) <= PinRadius + 3) return _pins[i].Id;
        }
        return null;
    }

    /// <summary>
    /// The arrow under a screen point: the selected arrow's leg-end handles first (aim that leg; the head handle wins),
    /// then badges and legs (move).
    /// </summary>
    (MapArrow Arrow, int? AimLeg)? HitArrow(Point screen)
    {
        if (_arrows.FirstOrDefault(a => a.Id == _selectedArrow) is { } s)
        {
            var pts = ToScreen(s.Points);
            for (int k = pts.Count - 1; k >= 1; k--)
                if (Distance(pts[k], screen) <= BadgeRadius + 3) return (s, k - 1);
        }
        for (int i = _arrows.Count - 1; i >= 0; i--)
        {
            var a = _arrows[i];
            if (Distance(ToScreen(a.At(a.Length / 2)), screen) <= BadgeRadius + 2) return (a, null);
            var pts = ToScreen(a.Points);
            for (int k = 0; k + 1 < pts.Count; k++)
                if (SegmentDistance(screen, pts[k], pts[k + 1]) <= ShaftReach) return (a, null);
        }
        return null;
    }

    static double Distance(Point a, Point b) => ((Vector)(a - b)).Length;

    static double SegmentDistance(Point p, Point a, Point b)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Y * ab.Y;
        double t = len2 < 1e-9 ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2, 0, 1);
        return Distance(p, a + ab * t);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetCurrentPoint(this);
        if (!pt.Properties.IsLeftButtonPressed && !pt.Properties.IsRightButtonPressed) return;
        _anim?.Stop();
        _fitPending = false;
        _press = pt.Position;
        _pressCentre = _centre;
        _pressAngle = _angle;
        _moved = false;
        _rotating = pt.Properties.IsRightButtonPressed || (e.KeyModifiers & KeyModifiers.Shift) != 0;
        _dragPin = _rotating ? null : HitPin(pt.Position);
        _dragAt = _dragPin is null ? default : _pins.First(p => p.Id == _dragPin).At;
        _arrowDrag = ArrowDrag.None;
        _arrowPreview = null;
        if (!_rotating && _dragPin is null)
        {
            var at = ToMap(pt.Position);
            if (HitArrow(pt.Position) is { } hit)
            {
                _arrowDrag = hit.AimLeg is null ? ArrowDrag.Move : ArrowDrag.Aim;
                _aimLeg = hit.AimLeg ?? 0;
                _arrowPreview = hit.Arrow;
                _arrowGrab = hit.Arrow.Tee - at;
            }
            else if (ArrowTemplate is { } template)
            {
                // North up the bitmap until the drag aims it.
                _arrowDrag = ArrowDrag.Place;
                _aimLeg = 0;
                _arrowPreview = template with { Id = "", Tee = at, Direction = new Vector(0, -1) };
                InvalidateVisual();
            }
        }
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        if (_source is not null) MapPointerMoved?.Invoke(ToMap(pos));
        if (_press is not { } press)
        {
            var hover = HitPin(pos);
            if (hover != _hoverPin) { _hoverPin = hover; InvalidateVisual(); }
            var over = hover is null ? HitArrow(pos) : null;
            var cursor = over is { AimLeg: not null } ? StandardCursorType.Hand : over is not null ? StandardCursorType.SizeAll
                : _placing ? StandardCursorType.Hand : StandardCursorType.Cross;
            if (cursor != _cursor) { _cursor = cursor; Cursor = new Cursor(cursor); }
            return;
        }
        var d = pos - press;
        if (!_moved && Math.Sqrt(d.X * d.X + d.Y * d.Y) < ClickSlop) return;
        _moved = true;
        if (_rotating)
        {
            var vc = ViewCentre;
            double a0 = Math.Atan2(press.Y - vc.Y, press.X - vc.X), a1 = Math.Atan2(pos.Y - vc.Y, pos.X - vc.X);
            _angle = _pressAngle + (a1 - a0);
            ViewChanged?.Invoke();
        }
        else if (_dragPin is not null)
        {
            _dragAt = ToMap(pos);
        }
        else if (_arrowDrag != ArrowDrag.None && _arrowPreview is { } arrow)
        {
            var at = ToMap(pos);
            if (_arrowDrag == ArrowDrag.Move) _arrowPreview = arrow with { Tee = at + _arrowGrab };
            else _arrowPreview = arrow.AimedAt(_aimLeg, at);
        }
        else
        {
            _centre = _pressCentre - ScreenToMapVector(d, _scale, _angle);
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_press is null) return;
        var pos = e.GetPosition(this);
        if (_arrowDrag != ArrowDrag.None && _arrowPreview is { } arrow)
        {
            var drag = _arrowDrag;
            _arrowDrag = ArrowDrag.None;
            _arrowPreview = null;
            if (drag == ArrowDrag.Place) ArrowPlaced?.Invoke(arrow.Tee, _moved ? arrow.Direction : null);
            else if (!_moved) ArrowClicked?.Invoke(arrow.Id);
            else if (drag == ArrowDrag.Move) ArrowMoved?.Invoke(arrow.Id, arrow.Tee);
            else ArrowAimed?.Invoke(arrow.Id, _aimLeg, arrow.Headings[_aimLeg]);
        }
        else if (!_moved)
        {
            if (HitPin(pos) is { } id) PinClicked?.Invoke(id);
            else MapClicked?.Invoke(ToMap(pos));
        }
        else if (_dragPin is { } moved)
        {
            PinMoved?.Invoke(moved, _dragAt);
        }
        _press = null;
        _dragPin = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomAt(e.GetPosition(this), Math.Pow(1.2, Math.Clamp(e.Delta.Y, -3, 3)), animate: false);
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverPin is not null) { _hoverPin = null; InvalidateVisual(); }
    }
}
