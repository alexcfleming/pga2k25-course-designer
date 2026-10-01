using System.Globalization;
using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Pins;
using Pga2K25.Finisher.Support;
using Pga2K25.Finisher.Terrain;
using SkiaSharp;

namespace Pga2K25.Finisher.Greens;

public sealed record FinisherGreenPin(int Index, double SlopePct, double X, double Z);

public sealed record FinisherGreenPage(int Hole, int Par, int GreenSpline, double ElevationMinM, double ElevationMaxM, double ElevationRangeM,
    double MaximumSlopePct, IReadOnlyList<FinisherGreenPin> Pins);

public sealed record FinisherGreensBookResult(string Folder, string Pdf, IReadOnlyList<string> Pngs, IReadOnlyList<FinisherGreenPage> Pages);

/// <summary>
/// Pass 11, <c>export_greens_book</c>: one 1200 × 1500 px page per hole — approach-oriented slope heat map
/// (0–8 %), 5 cm contours (20 cm bold), downhill arrows and numbered pins — written as PNGs plus a PDF into
/// "&lt;output&gt; - Greens Book". This is the Finisher's quick book; the repo's calibrated greens book lives in
/// Pga2K25.GreensBook.
/// </summary>
public static class FinisherGreensBook
{
    public const string MetaKey = "greensBook";
    public const double ContourM = 0.05, DisplayMaxSlopePct = 8.0;
    public const int PageW = 1200, PageH = 1500;
    public const string CalibrationNote = "Color scale is visual; calibrated approx. to Green Speed 10";
    static readonly double[] LegendTicks = [0, 1, 2, 3, 4, 5, 6, 7, 8.0];

    static readonly (double Slope, byte R, byte G, byte B)[] SlopeStops =
    [
        (0.0, 35, 112, 64), (1.25, 48, 139, 67), (2.25, 77, 160, 68), (3.0, 116, 177, 66), (3.3, 160, 187, 63), (3.5, 207, 194, 58),
        (4.25, 235, 171, 50), (5.25, 238, 127, 45), (6.25, 224, 81, 42), (7.25, 199, 51, 43), (8.0, 161, 35, 43),
    ];

    /// <summary><c>_greens_book_slope_rgb</c>: palette colour, shaded ±6 % by relative elevation.</summary>
    static SKColor SlopeColor(double slope, double elevationNorm, byte alpha = 255)
    {
        double s = Math.Clamp(double.IsFinite(slope) ? slope : 0.0, SlopeStops[0].Slope, SlopeStops[^1].Slope);
        double factor = 0.94 + 0.12 * Math.Clamp(elevationNorm, 0.0, 1.0);
        byte Channel(Func<(double, byte, byte, byte), byte> pick)
        {
            var xs = SlopeStops.Select(st => st.Slope).ToArray();
            var ys = SlopeStops.Select(st => (double)pick(st)).ToArray();
            double v = NumPy.Interp(s, xs, ys) * factor;
            return (byte)Math.Clamp(Math.Truncate(v), 0, 255);
        }
        return new SKColor(Channel(st => st.Item2), Channel(st => st.Item3), Channel(st => st.Item4), alpha);
    }

    static SKTypeface Typeface(bool bold)
    {
        string windir = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
        foreach (var file in bold ? new[] { "seguisb.ttf", "arialbd.ttf" } : new[] { "segoeui.ttf", "arial.ttf" })
        {
            string path = Path.Combine(windir, "Fonts", file);
            if (File.Exists(path) && SKTypeface.FromFile(path) is SKTypeface face) return face;
        }
        return SKTypeface.FromFamilyName("Arial", bold ? SKFontStyle.Bold : SKFontStyle.Normal) ?? SKTypeface.Default;
    }

    /// <summary>PIL-style text: (x, y) is the top-left of the line box.</summary>
    static void Text(SKCanvas canvas, string text, float x, float y, SKFont font, SKColor color)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(text, x, y - font.Metrics.Ascent, SKTextAlign.Left, font, paint);
    }

    static (float W, float H) Measure(string text, SKFont font)
    {
        font.MeasureText(text, out var bounds);
        return (bounds.Width, bounds.Height);
    }

    /// <summary><c>_greens_book_draw_arrow</c>.</summary>
    static void Arrow(SKCanvas canvas, SKPoint start, double vx, double vy, double length, SKColor color, float width)
    {
        double n = double.Hypot(vx, vy);
        if (n < 1e-9) return;
        vx /= n; vy /= n;
        float x1 = (float)(start.X + vx * length), y1 = (float)(start.Y + vy * length);
        using var line = new SKPaint { Color = color, StrokeWidth = width, IsAntialias = true, Style = SKPaintStyle.Stroke };
        canvas.DrawLine(start.X, start.Y, x1, y1, line);
        double head = Math.Max(5.0, Math.Min(10.0, 0.35 * length));
        double px = -vy, py = vx;
        using var builder = new SKPathBuilder();
        builder.MoveTo(x1, y1);
        builder.LineTo((float)(x1 - vx * head + px * 0.45 * head), (float)(y1 - vy * head + py * 0.45 * head));
        builder.LineTo((float)(x1 - vx * head - px * 0.45 * head), (float)(y1 - vy * head - py * 0.45 * head));
        builder.Close();
        using var path = builder.Detach();
        using var fill = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawPath(path, fill);
    }

    /// <summary><c>_greens_book_contour_segments</c>: marching squares over fully valid cells of the smoothed grid.</summary>
    static List<((double U, double V) A, (double U, double V) B)> ContourSegments(GreenGrid grid, double level)
    {
        var table = new Dictionary<int, (int, int)[]>
        {
            [1] = [(3, 0)], [2] = [(0, 1)], [3] = [(3, 1)], [4] = [(1, 2)], [5] = [(3, 2), (0, 1)], [6] = [(0, 2)], [7] = [(3, 2)],
            [8] = [(2, 3)], [9] = [(0, 2)], [10] = [(0, 3), (1, 2)], [11] = [(1, 2)], [12] = [(1, 3)], [13] = [(0, 1)], [14] = [(3, 0)],
        };
        var h = grid.Smooth;
        var segments = new List<((double, double), (double, double))>();
        (double, double) Interp((double U, double V) p1, (double U, double V) p2, double a, double b)
        {
            double t = Math.Abs(b - a) < 1e-12 ? 0.5 : (level - a) / (b - a);
            t = Math.Min(1.0, Math.Max(0.0, t));
            return (p1.U + t * (p2.U - p1.U), p1.V + t * (p2.V - p1.V));
        }
        for (int j = 0; j < grid.Rows - 1; j++)
            for (int i = 0; i < grid.Cols - 1; i++)
            {
                if (!grid.Valid[j, i] || !grid.Valid[j, i + 1] || !grid.Valid[j + 1, i + 1] || !grid.Valid[j + 1, i]) continue;
                double[] vals = [h[j, i], h[j, i + 1], h[j + 1, i + 1], h[j + 1, i]];
                int code = (vals[0] >= level ? 1 : 0) | (vals[1] >= level ? 2 : 0) | (vals[2] >= level ? 4 : 0) | (vals[3] >= level ? 8 : 0);
                if (!table.TryGetValue(code, out var pairs)) continue;
                (double, double)[] p = [(grid.Us[i], grid.Vs[j]), (grid.Us[i + 1], grid.Vs[j]), (grid.Us[i + 1], grid.Vs[j + 1]), (grid.Us[i], grid.Vs[j + 1])];
                var edge = new[]
                {
                    Interp(p[0], p[1], vals[0], vals[1]), Interp(p[1], p[2], vals[1], vals[2]),
                    Interp(p[2], p[3], vals[2], vals[3]), Interp(p[3], p[0], vals[3], vals[0]),
                };
                foreach (var (a, b) in pairs) segments.Add((edge[a], edge[b]));
            }
        return segments;
    }

    /// <summary><c>_greens_book_pin_rows</c>: pins in grid coordinates with the slope of the nearest cell.</summary>
    static List<(int Index, double U, double V, double X, double Z, double SlopePct, double Elevation)> PinRows(JsonObject hole, GreenGrid grid)
    {
        var rows = new List<(int, double, double, double, double, double, double)>();
        var wps = Json.Arr(hole, "waypoints");
        if (wps.Count == 0) return rows;
        var (ax, az) = Json.XZ(wps[^1]);
        var pins = Json.Arr(hole, "pins");
        for (int k = 0; k < pins.Count; k++)
        {
            var pos = pins[k]?["position"];
            double x = ax + Json.Num(pos?["x"], 0.0), z = az + Json.Num(pos?["z"], 0.0);
            double dx = x - grid.Center.X, dz = z - grid.Center.Z;
            double u = dx * grid.Right.X + dz * grid.Right.Z, v = dx * grid.Forward.X + dz * grid.Forward.Z;
            int iu = ArgMinAbs(grid.Us, u), iv = ArgMinAbs(grid.Vs, v);
            double slope = grid.Slope[iv, iu];
            rows.Add((k + 1, u, v, x, z, double.IsFinite(slope) ? slope : double.NaN, grid.Filled[iv, iu]));
        }
        return rows;
    }

    static int ArgMinAbs(double[] values, double target)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
            if (Math.Abs(values[i] - target) < Math.Abs(values[best] - target)) best = i;
        return best;
    }

    /// <summary><c>_greens_book_render_page</c>.</summary>
    static (SKBitmap Page, FinisherGreenPage Detail) RenderPage(JsonObject course, JsonObject hole, int holeIndex, GreenOutline green, MultiScaleTerrain sampler)
    {
        var grid = GreenGrid.Build(hole, green, sampler);
        var validHeights = grid.ValidValues(grid.Heights).ToList();
        double hmin = validHeights.Min(), hmax = validHeights.Max(), hdelta = hmax - hmin;

        var page = new SKBitmap(PageW, PageH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(page);
        canvas.Clear(new SKColor(244, 241, 231));
        using var boldFace = Typeface(true);
        using var regularFace = Typeface(false);
        using var titleFont = new SKFont(boldFace, 48);
        using var subFont = new SKFont(regularFace, 27);
        using var labelFont = new SKFont(boldFace, 22);
        using var smallFont = new SKFont(regularFace, 19);
        using var pinFont = new SKFont(boldFace, 22);

        string courseName = Json.Str(course["name"], "Course") is { Length: > 0 } nm ? nm : "Course";
        // The Python takes creatorDefinedPar as-is; the game stores -1 there for "use the computed par".
        int par = Json.Int(hole["creatorDefinedPar"], 0) is int cdp && cdp > 0 ? cdp : Json.Int(hole["par"], 0);
        Text(canvas, courseName, 60, 36, titleFont, new SKColor(25, 30, 28));
        Text(canvas, $"Hole {holeIndex}{(par != 0 ? $"  -  Par {par}" : "")}", 62, 98, subFont, new SKColor(55, 60, 57));
        Text(canvas, "GREENS BOOK", PageW - 260, 55, labelFont, new SKColor(60, 65, 62));

        // Map transform (_greens_book_map_transform): fit the u/v extent into the map rectangle, back at the top.
        const float mx0 = 90, my0 = 175, mx1 = 1110, my1 = 1160;
        double uw = Math.Max(1e-9, grid.Us[^1] - grid.Us[0]), vh = Math.Max(1e-9, grid.Vs[^1] - grid.Vs[0]);
        double scale = Math.Min((mx1 - mx0) / uw, (my1 - my0) / vh);
        double drawW = uw * scale, drawH = vh * scale;
        double ox = mx0 + 0.5 * ((mx1 - mx0) - drawW), oy = my0 + 0.5 * ((my1 - my0) - drawH);
        SKPoint Pt(double u, double v) => new((float)(ox + (u - grid.Us[0]) * scale), (float)(oy + (grid.Vs[^1] - v) * scale));

        using (var map = new SKBitmap(grid.Cols, grid.Rows, SKColorType.Rgba8888, SKAlphaType.Unpremul))
        {
            for (int r = 0; r < grid.Rows; r++)
                for (int c = 0; c < grid.Cols; c++)
                {
                    double norm = (grid.Filled[r, c] - hmin) / Math.Max(1e-9, hdelta);
                    map.SetPixel(c, grid.Rows - 1 - r, SlopeColor(grid.Slope[r, c], norm, (byte)(grid.Valid[r, c] ? 255 : 0)));
                }
            using var image = SKImage.FromBitmap(map);
            var dest = SKRect.Create((float)Math.Round(ox, MidpointRounding.ToEven), (float)Math.Round(oy, MidpointRounding.ToEven),
                Math.Max(1, (int)Math.Round(drawW, MidpointRounding.ToEven)), Math.Max(1, (int)Math.Round(drawH, MidpointRounding.ToEven)));
            canvas.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Linear));
        }

        using (var builder = new SKPathBuilder())
        {
            for (int k = 0; k < grid.PolyU.Length; k++)
            {
                var p = Pt(grid.PolyU[k], grid.PolyV[k]);
                if (k == 0) builder.MoveTo(p); else builder.LineTo(p);
            }
            builder.Close();
            using var outline = builder.Detach();
            using var stroke = new SKPaint { Color = new SKColor(20, 25, 22), StrokeWidth = 4, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round };
            if (grid.PolyU.Length >= 3) canvas.DrawPath(outline, stroke);
        }

        for (double level = Math.Ceiling(hmin / ContourM - 1e-9) * ContourM; level <= hmax + 1e-9; level += ContourM)
        {
            bool major = Math.Abs(level / 0.2 - Math.Round(level / 0.2, MidpointRounding.ToEven)) < 1e-5;
            using var pen = new SKPaint
            {
                Color = major ? new SKColor(35, 38, 35) : new SKColor(70, 74, 70), StrokeWidth = major ? 2 : 1,
                IsAntialias = true, Style = SKPaintStyle.Stroke,
            };
            foreach (var (a, b) in ContourSegments(grid, level)) canvas.DrawLine(Pt(a.U, a.V), Pt(b.U, b.V), pen);
        }

        int stride = Math.Max(1, (int)Math.Round(3.0 / GreenGrid.Step, MidpointRounding.ToEven));
        for (int j = stride / 2; j < grid.Rows; j += stride)
            for (int i = stride / 2; i < grid.Cols; i += stride)
            {
                if (!grid.Valid[j, i]) continue;
                double s = grid.Slope[j, i];
                if (!double.IsFinite(s) || s < 0.45) continue;
                Arrow(canvas, Pt(grid.Us[i], grid.Vs[j]), -grid.GradU[j, i], grid.GradV[j, i], 12.0 + Math.Min(17.0, 4.5 * s), new SKColor(28, 31, 29), 2);
            }

        var pins = PinRows(hole, grid);
        foreach (var pin in pins)
        {
            var c = Pt(pin.U, pin.V);
            const float r = 16;
            using var fill = new SKPaint { Color = new SKColor(250, 250, 247), IsAntialias = true };
            using var ring = new SKPaint { Color = new SKColor(20, 20, 20), StrokeWidth = 3, IsAntialias = true, Style = SKPaintStyle.Stroke };
            canvas.DrawCircle(c, r, fill);
            canvas.DrawCircle(c, r - 1.5f, ring);
            string label = pin.Index.ToString(CultureInfo.InvariantCulture);
            var (tw, th) = Measure(label, pinFont);
            using var ink = new SKPaint { Color = new SKColor(15, 15, 15), IsAntialias = true };
            canvas.DrawText(label, c.X - tw / 2, c.Y + th / 2, SKTextAlign.Left, pinFont, ink);
        }

        var dark = new SKColor(35, 40, 37);
        Text(canvas, "BACK", PageW / 2f - 36, 145, labelFont, dark);
        float frontX = PageW / 2f - 82;
        Text(canvas, "FRONT", frontX, 1173, labelFont, dark);
        Arrow(canvas, new SKPoint(frontX + 102, 1212), 0, -1, 28, dark, 3);
        Text(canvas, "APPROACH", frontX + 124, 1180, smallFont, new SKColor(55, 60, 57));

        const int legendY = 1256, legendX0 = 62, legendX1 = 1000, legendH = 28;
        Text(canvas, "LOCAL SLOPE", 62, legendY - 34, labelFont, new SKColor(30, 35, 32));
        int legendW = legendX1 - legendX0;
        for (int xx = 0; xx < legendW; xx++)
        {
            using var paint = new SKPaint { Color = SlopeColor(DisplayMaxSlopePct * xx / Math.Max(1, legendW - 1), 0.5) };
            canvas.DrawRect(legendX0 + xx, legendY, 1, legendH, paint);
        }
        var grey = new SKColor(45, 45, 45);
        using (var frame = new SKPaint { Color = grey, StrokeWidth = 1, Style = SKPaintStyle.Stroke })
            canvas.DrawRect(legendX0, legendY, legendW, legendH, frame);
        foreach (double tick in LegendTicks)
        {
            float tx = (float)(legendX0 + Math.Min(1.0, Math.Max(0.0, tick / DisplayMaxSlopePct)) * legendW);
            using var tickPaint = new SKPaint { Color = grey, StrokeWidth = 1 };
            canvas.DrawLine(tx, legendY + legendH, tx, legendY + legendH + 7, tickPaint);
            string label = Py.Fixed(tick, 1).TrimEnd('0').TrimEnd('.') + (Math.Abs(tick - DisplayMaxSlopePct) < 1e-9 ? "%+" : "%");
            var (tw, _) = Measure(label, smallFont);
            Text(canvas, label, tx - tw / 2, legendY + legendH + 8, smallFont, new SKColor(35, 38, 35));
        }

        double maxSlope = grid.ValidValues(grid.Slope).Where(double.IsFinite).DefaultIfEmpty(double.NaN).Max();
        double speed = Json.Num(course["greenSpeed"]);
        string speedText = course["greenSpeed"] is null ? "n/a" : Py.R(speed, 3);
        var body = new SKColor(45, 50, 47);
        const int metricY = 1345;
        Text(canvas, $"Elevation range: {Py.Fixed(hdelta, 2)} m   |   Max sampled slope: {Py.Fixed(maxSlope, 2)}%   |   Green speed: {speedText}", 62, metricY, smallFont, body);
        Text(canvas, "Contours: 5 cm   |   Arrows point downhill   |   " + CalibrationNote, 62, metricY + 34, smallFont, body);
        if (pins.Count > 0)
            Text(canvas, string.Join("   |   ", pins.Select(p => $"Pin {p.Index}: {(double.IsFinite(p.SlopePct) ? Py.Fixed(p.SlopePct, 2) + "%" : "n/a")}")), 62, metricY + 68, smallFont, body);

        var detail = new FinisherGreenPage(holeIndex, par, green.Index, hmin, hmax, hdelta, maxSlope,
            pins.Select(p => new FinisherGreenPin(p.Index, p.SlopePct, p.X, p.Z)).ToList());
        return (page, detail);
    }

    public static FinisherGreensBookResult Export(JsonObject course, string outputCoursePath, IProgress<string>? log = null)
    {
        var holes = Tees.TeeGeometry.Holes(course);
        if (holes.Count == 0) throw new InvalidOperationException("No holes3[] data was found for Greens Book export.");
        log?.Report("=== GREENS BOOK EXPORT: SLOPE HEAT MAPS + CONTOURS ===");
        string stem = Path.GetFileNameWithoutExtension(outputCoursePath);
        string bookDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputCoursePath))!, stem + " - Greens Book");
        Directory.CreateDirectory(bookDir);
        foreach (var old in Directory.GetFiles(bookDir, "Hole_*.png"))
            try { File.Delete(old); } catch (IOException) { }
        string pdfPath = Path.Combine(bookDir, stem + " - Greens Book.pdf");
        if (File.Exists(pdfPath))
            try { File.Delete(pdfPath); } catch (IOException) { }

        var sampler = new MultiScaleTerrain(course);
        var (greens, _) = SmartPins.GreenGeometries(course);
        if (greens.Count == 0) throw new InvalidOperationException("No putting-green spline geometry was detected for Greens Book export.");

        var used = new HashSet<int>();
        var pages = new List<SKBitmap>();
        var details = new List<FinisherGreenPage>();
        var pngs = new List<string>();
        try
        {
            for (int h = 0; h < holes.Count; h++)
            {
                if (holes[h] is not JsonObject hole) continue;
                int holeIndex = h + 1;
                var green = SmartPins.HoleGreen(hole, greens, used)
                    ?? throw new InvalidOperationException($"Hole {holeIndex}: no green spline could be assigned for Greens Book export.");
                used.Add(green.Index);
                var (page, detail) = RenderPage(course, hole, holeIndex, green, sampler);
                string png = Path.Combine(bookDir, $"Hole_{holeIndex:00}.png");
                using (var data = page.Encode(SKEncodedImageFormat.Png, 100))
                using (var f = File.Create(png)) data.SaveTo(f);
                pages.Add(page);
                pngs.Add(png);
                details.Add(detail);
                log?.Report($"Greens Book Hole {holeIndex}: elevation {Py.Fixed(detail.ElevationRangeM, 2)} m / max slope {Py.Fixed(detail.MaximumSlopePct, 2)}% / {detail.Pins.Count} pin(s).");
            }
            if (pages.Count == 0) throw new InvalidOperationException("No Greens Book pages were rendered.");

            // Pillow writes the pages at 150 dpi: 8 × 10 in.
            const float pointsPerPixel = 72f / 150f;
            using (var stream = File.Create(pdfPath))
            using (var doc = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
            {
                Title = Json.Str(course["name"], "Course") + " - Greens Book",
                Author = "Mal's TGC 2K25 Course Finisher (C# port)",
                Creator = "PGA 2K25 Course Designer",
                RasterDpi = 150,
            }))
            {
                foreach (var page in pages)
                {
                    var pdfCanvas = doc.BeginPage(PageW * pointsPerPixel, PageH * pointsPerPixel);
                    pdfCanvas.Scale(pointsPerPixel);
                    using (var image = SKImage.FromBitmap(page))
                        pdfCanvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                    doc.EndPage();
                }
                doc.Close();
            }
        }
        finally
        {
            foreach (var page in pages) page.Dispose();
        }

        var meta = Provenance.Meta(course);
        meta[MetaKey] = new JsonObject
        {
            ["holes"] = details.Count,
            ["gridM"] = Json.F(GreenGrid.Step),
            ["contourM"] = Json.F(ContourM),
            ["outputFolder"] = Path.GetFileName(bookDir),
            ["pdf"] = Path.GetFileName(pdfPath),
            ["method"] = "approach-oriented-terrain-slope-heatmap-contours-downhill-arrows",
            ["displayPalette"] = "game-like-continuous-rev9c",
            ["displayCalibration"] = "visual-approx-green-speed-10",
            ["displayMaxSlopePct"] = Json.F(DisplayMaxSlopePct),
            ["details"] = Json.Array(details.Select(d => (JsonNode?)new JsonObject
            {
                ["hole"] = d.Hole,
                ["par"] = d.Par,
                ["greenSpline"] = d.GreenSpline,
                ["elevationMinM"] = Json.F(d.ElevationMinM),
                ["elevationMaxM"] = Json.F(d.ElevationMaxM),
                ["elevationRangeM"] = Json.F(d.ElevationRangeM),
                ["maximumSlopePct"] = Json.F(d.MaximumSlopePct),
                ["pins"] = Json.Array(d.Pins.Select(p => (JsonNode?)new JsonObject
                {
                    ["index"] = p.Index,
                    ["slopePct"] = double.IsFinite(p.SlopePct) ? Json.F(p.SlopePct) : null,
                    ["x"] = Json.F(p.X),
                    ["z"] = Json.F(p.Z),
                })),
            })),
        };
        log?.Report($"Greens Book complete: {bookDir}");
        log?.Report($"Greens Book PDF: {pdfPath}");
        return new FinisherGreensBookResult(bookDir, pdfPath, pngs, details);
    }
}
