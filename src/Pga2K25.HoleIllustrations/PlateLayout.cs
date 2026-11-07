using Pga2K25.CourseFormat;
using Pga2K25.YardageBook;
using SkiaSharp;

namespace Pga2K25.HoleIllustrations;

/// <summary>
/// The plate's page: US Letter portrait in page units of 1/100 in (850 × 1100), the frame, the art zone and the
/// yardage ruler. The ruler is fitted to each hole (locked 7 Nov 2026): its top is the back-tee yards plus 30, rounded
/// up to the next 50, and the map is drawn at the ruler's scale with the back tee on the "0 YDS." line.
/// </summary>
public static class PlateLayout
{
    public const float PageWidth = 850, PageHeight = 1100;
    public static readonly SKRect Frame = new(32, 32, 818, 1058);

    /// <summary>Where the art may go; it feathers to paper inside these edges.</summary>
    public static readonly SKRect ArtZone = new(147, 132, 806, 973);

    /// <summary>The ruler's "0 YDS." line (the back tee) and its top tick.</summary>
    public const float ZeroY = 953, TopTickY = 187;

    /// <summary>Horizontal centre of the map: the middle of the art zone.</summary>
    public static float MapCentreX => ArtZone.MidX;

    /// <summary>Raster pixels per page unit (300 dpi).</summary>
    public const float RasterScale = 3;

    public static int RulerTop(int backTeeYards, int marginYards = 30, int stepYards = 50) =>
        (int)Math.Ceiling((backTeeYards + marginYards) / (double)stepYards) * stepYards;

    /// <summary>Page units per yard on a plate whose ruler tops out at <paramref name="rulerTop"/>.</summary>
    public static float UnitsPerYard(int rulerTop) => (ZeroY - TopTickY) / rulerTop;
}

/// <summary>
/// World → plate transform at the ruler's scale: play runs up the page (the yardage book's rotation), the back tee
/// sits on the "0 YDS." line, and the route's lateral spread is centred on the map.
/// </summary>
public sealed class PlateProjection
{
    readonly double _cos, _sin, _uMid, _vBack;
    public float UnitsPerMetre { get; }

    /// <summary>Raster pixels per metre.</summary>
    public float PixelsPerMetre => UnitsPerMetre * PlateLayout.RasterScale;

    public float PixelsPerYard => (float)(PixelsPerMetre * HoleAnalysis.Yard);

    public PlateProjection(double rotationDegrees, IReadOnlyList<Vec2> route, float unitsPerYard)
    {
        double r = rotationDegrees * Math.PI / 180;
        _cos = Math.Cos(r); _sin = Math.Sin(r);
        UnitsPerMetre = (float)(unitsPerYard / HoleAnalysis.Yard);
        var us = route.Select(p => p.X * _cos - p.Z * _sin).ToList();
        _uMid = (us.Min() + us.Max()) / 2;
        _vBack = route[0].X * _sin + route[0].Z * _cos;
    }

    /// <summary>Page position (units) of a world point.</summary>
    public SKPoint Page(Vec2 w)
    {
        double u = w.X * _cos - w.Z * _sin, v = w.X * _sin + w.Z * _cos;
        return new SKPoint(PlateLayout.MapCentreX + (float)((u - _uMid) * UnitsPerMetre), PlateLayout.ZeroY - (float)((v - _vBack) * UnitsPerMetre));
    }

    /// <summary>Art-raster pixel position of a world point (the raster covers the art zone at 300 dpi).</summary>
    public SKPoint Pixel(Vec2 w)
    {
        var p = Page(w);
        return new SKPoint((p.X - PlateLayout.ArtZone.Left) * PlateLayout.RasterScale, (p.Y - PlateLayout.ArtZone.Top) * PlateLayout.RasterScale);
    }

    public List<SKPoint> Pixels(IReadOnlyList<Vec2> pts) => pts.Select(Pixel).ToList();

    public SKPath Path(IReadOnlyList<Vec2> pts, bool close = true) => PixelPath(Pixels(pts), close);

    public static SKPath PixelPath(IReadOnlyList<SKPoint> pts, bool close = true)
    {
        using var b = new SKPathBuilder();
        AddPixels(b, pts, close);
        return b.Detach();
    }

    /// <summary>Appends a polyline in raster pixels, skipping points closer than a quarter pixel.</summary>
    public static void AddPixels(SKPathBuilder b, IReadOnlyList<SKPoint> pts, bool close = true)
    {
        if (pts.Count == 0) return;
        b.MoveTo(pts[0]);
        var last = pts[0];
        for (int i = 1; i < pts.Count; i++)
        {
            if (SKPoint.Distance(pts[i], last) < 0.25f) continue;
            b.LineTo(pts[i]);
            last = pts[i];
        }
        if (close) b.Close();
    }
}
