using Pga2K25.CourseFormat;
using Pga2K25.TerrainMap;
using SkiaSharp;

namespace Pga2K25.Tests;

[Trait("Area", "Products")]
public class SketchTests
{
    static readonly string SaganPath = Path.Combine(TestPaths.SamplesDir, "sagan-khushun.course");
    static readonly Lazy<CourseModel> Course = new(() => CourseFile.Load(SaganPath).ReadModel());

    [Fact]
    public void Homography_TakesTheFourCornersHome()
    {
        SKPoint[] src = [new(103, 87), new(1890, 120), new(1872, 2410), new(95, 2388)];
        SKPoint[] dst = [new(0, 0), new(1548, 0), new(1548, 1788), new(0, 1788)];
        var h = SheetScan.Homography(src, dst);
        for (int i = 0; i < 4; i++)
        {
            double w = h[6] * src[i].X + h[7] * src[i].Y + h[8];
            Assert.Equal(dst[i].X, (h[0] * src[i].X + h[1] * src[i].Y + h[2]) / w, 3);
            Assert.Equal(dst[i].Y, (h[3] * src[i].X + h[4] * src[i].Y + h[5]) / w, 3);
        }
    }

    /// <summary>
    /// A scan whose print sits 5 px low in one half (paper that wasn't flat): against the reference as rendered, its
    /// contour lines there read as pencil; aligned tile by tile, none do, and a real pencil stroke still does.
    /// </summary>
    [Fact]
    public void AlignTo_MovesThePrintOntoACurledScan()
    {
        const int w = 600, h = 480;
        // Wavy "contours" every 24 px, in a brown that's dark enough to read as pencil if the print is missed.
        SKBitmap Draw(Func<int, int> shiftY, bool pencil)
        {
            var b = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var c = new SKCanvas(b);
            c.Clear(new SKColor(250, 248, 240));
            using var ink = new SKPaint { Color = new SKColor(150, 140, 130), StrokeWidth = 2, IsAntialias = true, Style = SKPaintStyle.Stroke };
            for (int row = 12; row < h; row += 24)
            {
                using var path = new SKPath();
                for (int x = 0; x <= w; x += 4)
                {
                    float y = row + 6 * MathF.Sin(x / 37f + row);
                    var p = new SKPoint(x, y + shiftY(row));
                    if (x == 0) path.MoveTo(p); else path.LineTo(p);
                }
                c.DrawPath(path, ink);
            }
            if (pencil)
            {
                using var graphite = new SKPaint { Color = new SKColor(70, 70, 70), StrokeWidth = 3, IsAntialias = true };
                c.DrawLine(100, 100, 500, 380, graphite);
            }
            return b;
        }
        using var reference = Draw(_ => 0, false);
        using var scan = Draw(row => row > h / 2 ? 5 : 0, true);
        var refNorm = SheetScan.PaperNormalised(reference);
        var scanNorm = SheetScan.PaperNormalised(scan);

        bool[] Pencil(byte[] r) => SheetScan.PencilScore(scan, scanNorm, r).Select(s => s > 0.25).ToArray();
        int Count(bool[] p, Func<int, int, bool> where) => Enumerable.Range(0, w * h).Count(i => p[i] && where(i % w, i / w));
        bool OffTheStroke(int x, int y) => Math.Abs((y - 100) * 400 - (x - 100) * 280) / Math.Sqrt(400 * 400 + 280 * 280) > 6;
        bool LowerHalf(int x, int y) => y > h / 2 + 30 && x > 20 && x < w - 20 && OffTheStroke(x, y);

        Assert.True(Count(Pencil(refNorm), LowerHalf) > 2000, "the test's shift should leave print as pencil");
        var aligned = Pencil(SheetScan.AlignTo(scanNorm, refNorm, w, h));
        Assert.True(Count(aligned, LowerHalf) < 100, $"{Count(aligned, LowerHalf)} px of print left");
        Assert.True(Count(aligned, (x, y) => !OffTheStroke(x, y)) > 800, "the pencil stroke was lost");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(12)]
    public void Layout_IsTheSheetsOwnWindow(int hole)
    {
        var course = Course.Value;
        var layers = Pga2K25.Maps.CourseLayers.From(course);
        var h = course.Holes.Single(x => x.Number == hole);
        var o = new HoleSheetOptions { CellMetres = 1 };
        var sheet = HoleSheet.Build(course, layers, h, o, datumMetres: 0);
        var layout = HoleSheet.LayoutFor(layers, h, o);
        Assert.Equal(sheet.RotationDegrees, layout.RotationDegrees, 9);
        Assert.Equal(sheet.Along, layout.Along);
        Assert.Equal(sheet.Across, layout.Across);

        // The frame corners land on the printed frame's corners.
        var p = sheet.Projection(HoleSheet.MapRectPoints);
        var c = layout.FrameCorners();
        var r = HoleSheet.MapRectPoints;
        SKPoint[] want = [new(r.Left, r.Top), new(r.Right, r.Top), new(r.Right, r.Bottom), new(r.Left, r.Bottom)];
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(want[i].X, p.Map(c[i]).X, 2);
            Assert.Equal(want[i].Y, p.Map(c[i]).Y, 2);
        }
    }

    /// <summary>
    /// A hole sheet "scanned" (printed sheet on a darker desk, turned 1.5°, with a grey pencil line drawn across
    /// it) is matched to its hole, and the pencil lands on the ground where it was drawn. Sheets printed before 2.5
    /// (full width) still import; on a 2.5 strip sheet, writing in the notes column stays off the ground.
    /// Wakinyan Dunes (2.6): a sheet printed before the plot's lowest point moved has every contour off today's
    /// render; on paper that wasn't flat, the print sits a few pixels off in the middle. Neither comes through as
    /// pencil (Murray Estate, whose heights are from its lowest point; Sagan-Khushun's are from the sea).
    /// </summary>
    [Trait("Speed", "Slow")]
    [Theory]
    [InlineData("sagan-khushun", HoleSheetLayout.Strip, 0.0, false)]
    [InlineData("sagan-khushun", HoleSheetLayout.FullWidth, 0.0, false)]
    [InlineData("murray-estate", HoleSheetLayout.Strip, 0.8, true)]
    public void Import_MatchesTheHoleAndPutsThePencilOnTheGround(string courseName, HoleSheetLayout kind, double printedLowerFeet, bool bowed)
    {
        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-sketch-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            string coursePath = Path.Combine(TestPaths.SamplesDir, courseName + ".course");
            var course = CourseFile.Load(coursePath).ReadModel();
            var options = new HoleSheetOptions { Dpi = 150, CellMetres = 1 };
            // The sheet as printed: today's, or with every height read lower (the datum raised).
            var printOptions = options with { Pdf = false, Layout = kind };
            var (datum, sea) = HoleSheetBuilder.PlotDatum(course, printOptions);
            Assert.Equal(printedLowerFeet == 0, sea);
            var sheet = HoleSheet.Build(course, Pga2K25.Maps.CourseLayers.From(course), course.Holes.Single(h => h.Number == 7), printOptions,
                datum + printedLowerFeet / TerrainSurvey.FeetPerMetre, sea);
            using var renderer = new HoleSheetRenderer(printOptions);
            using var surface = SKSurface.Create(new SKImageInfo(renderer.PixelSize.Width, renderer.PixelSize.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            renderer.Draw(surface.Canvas, course.Name, sheet);
            using var printed = SKBitmap.FromImage(surface.Snapshot());
            using var page = bowed ? Bow(printed, 3.5) : printed.Copy();
            float u = (float)(options.Dpi / 72);

            // A pencil line across the fairway, 40 m either side of the route's midpoint.
            var route = sheet.Routing.Route;
            var mid = (route[0] + route[^1]) * 0.5;
            Vec2 a = mid - sheet.Side * 40, b = mid + sheet.Side * 40;
            var bunkerCentre = mid - sheet.Forward * 50 + sheet.Side * 25;
            var proj = sheet.Projection(HoleSheet.MapRectPoints);
            SKPoint Page(Vec2 w) { var q = proj.Map(w); return new SKPoint(q.X * u, q.Y * u); }

            string scanPath = Path.Combine(dir, "scan.jpg");
            int pad = 40;
            using (var scan = new SKBitmap(page.Width + 2 * pad, page.Height + 2 * pad))
            {
                using var c = new SKCanvas(scan);
                c.Clear(new SKColor(70, 70, 75));
                c.Save();
                c.RotateDegrees(1.5f, scan.Width / 2f, scan.Height / 2f);
                c.DrawBitmap(page, pad, pad);
                using var pencil = new SKPaint { Color = new SKColor(80, 80, 80), StrokeWidth = 3, IsAntialias = true, Style = SKPaintStyle.Stroke };
                var pa = Page(a); var pb = Page(b);
                c.DrawLine(pa.X + pad, pa.Y + pad, pb.X + pad, pb.Y + pad, pencil);
                // A bunker: a closed wobbly loop 12 m across, 50 m short of the midpoint and 25 m right of the route.
                var bunker = Page(bunkerCentre);
                using var path = new SKPath();
                for (int k = 0; k <= 72; k++)
                {
                    double ang = k * Math.PI / 36, rad = (12 + 1.5 * Math.Sin(3 * ang)) * proj.PointsPerMetre * u;
                    var q = new SKPoint(bunker.X + pad + (float)(rad * Math.Cos(ang)), bunker.Y + pad + (float)(rad * Math.Sin(ang)));
                    if (k == 0) path.MoveTo(q); else path.LineTo(q);
                }
                c.DrawPath(path, pencil);
                if (kind == HoleSheetLayout.Strip)
                {
                    // A note scrawled down the notes column.
                    var nr = sheet.NotesPoints;
                    for (int row = 0; row < 6; row++)
                    {
                        float y = (nr.Top + 120 + row * 40) * u + pad;
                        using var scrawl = new SKPath();
                        scrawl.MoveTo(nr.Left * u + 20 + pad, y);
                        for (int s = 1; s <= 24; s++) scrawl.LineTo(nr.Left * u + 20 + pad + s * (nr.Width * u - 40) / 24, y + (s % 2 == 0 ? -8 : 8));
                        c.DrawPath(scrawl, pencil);
                    }
                }
                c.Restore();
                // A phone scanner crops to the paper, give or take.
                using var cropped = new SKBitmap(page.Width + 10, page.Height + 10);
                using (var cc = new SKCanvas(cropped)) cc.DrawBitmap(scan, -pad + 5, -pad + 5);
                using var data = SKImage.FromBitmap(cropped).Encode(SKEncodedImageFormat.Jpeg, 92);
                using var f = File.Create(scanPath);
                data.SaveTo(f);
            }

            string folder = Path.Combine(dir, "Sketches");
            var log = new List<string>();
            var results = HoleSketches.Import(coursePath, [scanPath], folder, options, new Collector(log));
            var r = Assert.Single(results);
            Assert.True(r.Saved, r.Message);
            Assert.Equal(7, r.Hole);
            Assert.True(r.Match > 0.6, $"{r.Match}");
            if (printedLowerFeet != 0)
            {
                // Found to the nearest sixteenth of a contour interval.
                var found = log.Select(l => System.Text.RegularExpressions.Regex.Match(l, @"read ([\d.]+) ft lower than today's")).Single(m => m.Success);
                Assert.InRange(double.Parse(found.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), printedLowerFeet - 0.15, printedLowerFeet + 0.15);
            }

            var saved = Assert.Single(HoleSketches.Load(folder).Sheets);
            // Tied to the ground through the layout it was printed in.
            var layoutCorners = HoleSheet.LayoutFor(Pga2K25.Maps.CourseLayers.From(course), sheet.Routing.Hole, options with { Layout = kind }).FrameCorners();
            for (int i = 0; i < 4; i++) Assert.True(Vec2.Distance(layoutCorners[i], saved.FrameCorners()[i]) < 0.01, $"corner {i}");
            using var png = SKBitmap.Decode(Path.Combine(folder, saved.Pencil));
            using var alpha = png.Copy(SKColorType.Alpha8)!;
            var px = alpha.GetPixelSpan().ToArray();

            // World → straightened-image pixel through the saved frame corners.
            var fc = saved.FrameCorners();
            (int X, int Y) Img(Vec2 w)
            {
                var ex = fc[1] - fc[0]; var ey = fc[3] - fc[0]; var d = w - fc[0];
                double fx = (d.X * ex.X + d.Z * ex.Z) / (ex.X * ex.X + ex.Z * ex.Z);
                double fy = (d.X * ey.X + d.Z * ey.Z) / (ey.X * ey.X + ey.Z * ey.Z);
                return ((int)Math.Round(fx * alpha.Width), (int)Math.Round(fy * alpha.Height));
            }
            int Near(Vec2 w, int radius)
            {
                var (x0, y0) = Img(w);
                int best = 0;
                for (int y = y0 - radius; y <= y0 + radius; y++)
                for (int x = x0 - radius; x <= x0 + radius; x++)
                    if (x >= 0 && y >= 0 && x < alpha.Width && y < alpha.Height) best = Math.Max(best, px[y * alpha.Width + x]);
                return best;
            }
            foreach (double t in new[] { 0.2, 0.35, 0.65, 0.8 })   // clear of the route, which masks the pencil where they cross
                Assert.True(Near(a + (b - a) * t, 3) > 100, $"pencil missing at t = {t}");
            Assert.True(Near(mid + sheet.Forward * 60, 3) == 0, "pencil where none was drawn");
            Assert.True(px.Count(v => v > 0) < 0.01 * px.Length, "print left in the pencil layer");

            // Traced: the bunker is an edge on the ground where it was drawn; the straight line is a shot line.
            var lines = HoleSketches.LoadLines(Path.Combine(folder, saved.Lines))!;
            static IEnumerable<Vec2> Points(SketchLine l) { for (int i = 0; i + 1 < l.Xz.Length; i += 2) yield return new Vec2(l.Xz[i], l.Xz[i + 1]); }
            var ring = lines.Lines.Where(l => !l.Construction && Points(l).Any(q => Math.Abs(Vec2.Distance(q, bunkerCentre) - 12) < 3)).ToList();
            Assert.NotEmpty(ring);
            var onRing = ring.SelectMany(Points).ToList();
            Assert.True(onRing.Count(q => Math.Abs(Vec2.Distance(q, bunkerCentre) - 12) < 3) > 0.9 * onRing.Count, "bunker edge off the ground");
            // Most of the loop is traced (the route's ink may cut it).
            double traced = ring.Sum(l => Points(l).Zip(Points(l).Skip(1)).Sum(s => Vec2.Distance(s.First, s.Second)));
            Assert.True(traced > 0.7 * 2 * Math.PI * 12, $"only {traced:0} m of the bunker traced");
            var shot = lines.Lines.Where(l => Points(l).Any(q => Math.Abs((q - mid).X * sheet.Forward.X + (q - mid).Z * sheet.Forward.Z) < 2
                                                                 && Math.Abs((q - mid).X * sheet.Side.X + (q - mid).Z * sheet.Side.Z) is > 10 and < 35)).ToList();
            Assert.NotEmpty(shot);
            Assert.All(shot, l => Assert.True(l.Construction, "a straight line was taken for an edge"));
            // Nothing traced off the sheet's ground (the notes column's scrawl).
            Assert.All(lines.Lines.SelectMany(Points), q => Assert.True(sheet.Contains(q, pad: 3), $"line off the strip at {q.X:0},{q.Z:0}"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Synchronous progress, so every line is in before the import returns.</summary>
    sealed class Collector(List<string> lines) : IProgress<string>
    {
        public void Report(string value) { lock (lines) lines.Add(value); }
    }

    /// <summary>
    /// The page bowed as a sheet that isn't flat scans: the print moves up to <paramref name="pixels"/> in the middle,
    /// diagonally, and not at all at the edges (so the frame stays straight).
    /// </summary>
    static SKBitmap Bow(SKBitmap src, double pixels)
    {
        using var rgba = src.Copy(SKColorType.Rgba8888)!;
        var bowed = new SKBitmap(new SKImageInfo(src.Width, src.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var from = rgba.GetPixelSpan();
        var to = bowed.GetPixelSpan();
        int w = src.Width, h = src.Height;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            double bump = Math.Pow(Math.Sin(Math.PI * x / w) * Math.Sin(Math.PI * y / h), 2) * pixels;
            int sx = Math.Clamp((int)Math.Round(x - bump), 0, w - 1), sy = Math.Clamp((int)Math.Round(y - 0.6 * bump), 0, h - 1);
            for (int c = 0; c < 4; c++) to[4 * (y * w + x) + c] = from[4 * (sy * w + sx) + c];
        }
        return bowed;
    }
}
