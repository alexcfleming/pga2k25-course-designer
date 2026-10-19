using Pga2K25.CourseFormat;
using Pga2K25.TerrainMap;
using SkiaSharp;

namespace Pga2K25.Tests;

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
    /// it) is matched to its hole, and the pencil lands on the ground where it was drawn.
    /// </summary>
    [Fact]
    public void Import_MatchesTheHoleAndPutsThePencilOnTheGround()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-sketch-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new HoleSheetOptions { Dpi = 150, CellMetres = 1 };
            var built = HoleSheetBuilder.Build(SaganPath, dir, options with { Pdf = false }, [7]);
            var sheet = built.Sheets.Single();
            using var page = SKBitmap.Decode(built.PngPaths.Single());
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
                c.Restore();
                // A phone scanner crops to the paper, give or take.
                using var cropped = new SKBitmap(page.Width + 10, page.Height + 10);
                using (var cc = new SKCanvas(cropped)) cc.DrawBitmap(scan, -pad + 5, -pad + 5);
                using var data = SKImage.FromBitmap(cropped).Encode(SKEncodedImageFormat.Jpeg, 92);
                using var f = File.Create(scanPath);
                data.SaveTo(f);
            }

            string folder = Path.Combine(dir, "Sketches");
            var results = HoleSketches.Import(SaganPath, [scanPath], folder, options);
            var r = Assert.Single(results);
            Assert.True(r.Saved, r.Message);
            Assert.Equal(7, r.Hole);
            Assert.True(r.Match > 0.6, $"{r.Match}");

            var saved = Assert.Single(HoleSketches.Load(folder).Sheets);
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
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
