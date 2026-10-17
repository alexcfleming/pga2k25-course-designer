using System.Text.Json;
using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.TerrainMap;

/// <summary>
/// One hole's scanned sketch, tied to the ground: <see cref="Frame"/> holds the world X/Z under the printed map
/// frame's corners (top-left, top-right, bottom-right, bottom-left), so the images stay put if the routing or the
/// map's area changes later. Images are the straightened frame, top-left first.
/// </summary>
public sealed record HoleSketch
{
    public int Hole { get; init; }
    /// <summary>The pencil on a transparent background (PNG).</summary>
    public string Pencil { get; init; } = "";
    /// <summary>The whole straightened scan (JPEG).</summary>
    public string Scan { get; init; } = "";
    public string Source { get; init; } = "";
    public int Page { get; init; }
    /// <summary>How well the page matched the hole's sheet (correlation, 0–1).</summary>
    public double Match { get; init; }
    public double[][] Frame { get; init; } = [];
    public DateTime Imported { get; init; }

    public Vec2[] FrameCorners() => Frame.Select(c => new Vec2(c[0], c[1])).ToArray();
}

public sealed record HoleSketchSet
{
    public int Version { get; init; } = 1;
    public List<HoleSketch> Sheets { get; init; } = [];
}

/// <summary>What happened to one scanned page; <see cref="Saved"/> when it was imported as <see cref="Hole"/>'s sketch.</summary>
public sealed record ScanResult(string Source, int Page, int? Hole, double Match, string Message, bool Saved = false);

/// <summary>
/// Imports scanned hole sheets with hand sketching on them. Each page's map frame is found and straightened,
/// matched against a fresh render of every hole's sheet (so page order doesn't matter and pages that aren't hole
/// sheets are skipped), and the pencil is separated from the print. Writes "&lt;course&gt; - Sketches/" with a PNG of
/// the pencil and a JPEG of the scan per hole, and sketches.json. Re-importing a hole replaces it.
/// </summary>
public static class HoleSketches
{
    public const string FileName = "sketches.json";
    /// <summary>Saved images, px per point (the frame is straightened at <see cref="SheetScan.PixelsPerPoint"/>).</summary>
    public const int SavedPixelsPerPoint = 2;
    /// <summary>Below this a page is not taken for any hole's sheet.</summary>
    public const double MinMatch = 0.3;
    static readonly SKColor Graphite = new(0x26, 0x26, 0x26);
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string FolderFor(string outputDir, string courseStem) => Path.Combine(outputDir, $"{courseStem} - Sketches");

    public static HoleSketchSet Load(string folder)
    {
        string path = Path.Combine(folder, FileName);
        if (!File.Exists(path)) return new HoleSketchSet();
        try { return JsonSerializer.Deserialize<HoleSketchSet>(File.ReadAllText(path), Json) ?? new HoleSketchSet(); }
        catch (JsonException) { return new HoleSketchSet(); }
    }

    public static void Save(string folder, HoleSketchSet set)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, FileName), JsonSerializer.Serialize(set with { Sheets = set.Sheets.OrderBy(s => s.Hole).ToList() }, Json));
    }

    /// <summary>Removes a hole's sketch and its images.</summary>
    public static void Remove(string folder, int hole)
    {
        var set = Load(folder);
        foreach (var s in set.Sheets.Where(s => s.Hole == hole))
            foreach (var f in new[] { s.Pencil, s.Scan })
                if (f.Length > 0 && File.Exists(Path.Combine(folder, f))) File.Delete(Path.Combine(folder, f));
        Save(folder, set with { Sheets = set.Sheets.Where(s => s.Hole != hole).ToList() });
    }

    /// <summary>Hole sheet frame straightened at <see cref="SheetScan.PixelsPerPoint"/>.</summary>
    static SKSizeI FrameSize(int k) => new((int)Math.Round(HoleSheet.MapRectPoints.Width * k), (int)Math.Round(HoleSheet.MapRectPoints.Height * k));

    public static List<ScanResult> Import(string coursePath, IReadOnlyList<string> scanFiles, string folder, HoleSheetOptions options,
        IProgress<string>? log = null, string? printedSheets = null)
    {
        var course = CourseFile.Load(coursePath).ReadModel();
        if (course.Holes.Count == 0) throw new InvalidOperationException("The course has no holes, so there are no hole sheets to match.");
        var layers = Maps.CourseLayers.From(course);
        int k = SheetScan.PixelsPerPoint;
        var size = FrameSize(k);
        var page = new SKSize(HoleSheet.PageWidth, HoleSheet.PageHeight);

        // 1. Find and straighten each page's frame.
        var results = new List<ScanResult>();
        var pages = new List<(ScanPage Page, SKBitmap Rect, FrameFit Fit)>();
        foreach (var file in scanFiles)
            foreach (var p in ScanPages.Load(file))
            {
                using var scan = p.Bitmap;
                var fit = SheetScan.FindFrame(scan, page, HoleSheet.MapRectPoints);
                if (fit is null)
                {
                    results.Add(new ScanResult(p.File, p.Page, null, 0, "no hole-sheet map frame found"));
                    log?.Report($"{Path.GetFileName(p.File)} p{p.Page}: no hole-sheet map frame found, skipped.");
                    continue;
                }
                pages.Add((p, SheetScan.Rectify(scan, fit.Corners, size), fit));
            }
        if (pages.Count == 0) return results;
        log?.Report($"Found the map frame on {pages.Count} page(s); rendering {course.Holes.Count} hole sheets to match them against…");

        // 2. Each hole's sheet as it would print now, normalised the same way as the scans; and, when the Hole sheets
        //    build's PNGs are given, the sheet as last built, which is usually what was printed. Where the ground has
        //    changed since, only the built sheet still explains the printed contours.
        var (datum, sea) = HoleSheetBuilder.PlotDatum(course, options);
        var renderOptions = options with { Dpi = 72.0 * k };
        var references = new (byte[] Norm, float[] Thumb)[course.Holes.Count];
        Parallel.For(0, course.Holes.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4) }, i =>
        {
            var sheet = HoleSheet.Build(course, layers, course.Holes[i], renderOptions, datum, sea);
            using var renderer = new HoleSheetRenderer(renderOptions);
            var full = renderer.PixelSize;
            using var surface = SKSurface.Create(new SKImageInfo(full.Width, full.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            renderer.Draw(surface.Canvas, course.Name, sheet);
            var r = HoleSheet.MapRectPoints;
            using var image = surface.Snapshot(new SKRectI((int)Math.Round(r.Left * k), (int)Math.Round(r.Top * k),
                (int)Math.Round(r.Left * k) + size.Width, (int)Math.Round(r.Top * k) + size.Height));
            using var bmp = SKBitmap.FromImage(image);
            using var rgba = SheetScan.ToRgba(bmp);
            var norm = SheetScan.PaperNormalised(rgba);
            references[i] = (norm, SheetScan.Thumbnail(norm, size.Width, size.Height));
            log?.Report($"Rendered hole {course.Holes[i].Number}'s sheet.");
        });
        var built = new (byte[] Norm, float[] Thumb)?[course.Holes.Count];
        if (printedSheets is not null)
            for (int i = 0; i < course.Holes.Count; i++)
                built[i] = BuiltSheet(Path.Combine(printedSheets, $"Hole_{course.Holes[i].Number:00}.png"), size);
        // Per hole, the sheets to compare a page against.
        (byte[] Norm, float[] Thumb, bool Built)[] Candidates(int i) => built[i] is { } b
            ? [(references[i].Norm, references[i].Thumb, false), (b.Norm, b.Thumb, true)]
            : [(references[i].Norm, references[i].Thumb, false)];

        // 3. Match each page to a hole (either way up), keeping the best page when two match the same hole.
        var matched = new List<(int Page, int HoleIndex, double Score, bool Flip, byte[] Norm, byte[] Reference, bool Built)>();
        for (int pi = 0; pi < pages.Count; pi++)
        {
            var (p, rect, _) = pages[pi];
            var norm = SheetScan.PaperNormalised(rect);
            var thumb = SheetScan.Thumbnail(norm, size.Width, size.Height);
            var scores = Enumerable.Range(0, references.Length)
                .SelectMany(i => Candidates(i).SelectMany(c => new[]
                {
                    (i, s: SheetScan.Correlation(thumb, c.Thumb), flip: false, c.Norm, c.Built),
                    (i, s: SheetScan.Correlation(thumb, c.Thumb, flip: true), flip: true, c.Norm, c.Built),
                }))
                .OrderByDescending(x => x.s).ToList();
            var best = scores[0];
            double runnerUp = scores.FirstOrDefault(x => x.i != best.i).s;
            int number = course.Holes[best.i].Number;
            if (best.s < MinMatch)
            {
                results.Add(new ScanResult(p.File, p.Page, null, best.s, $"doesn't match any hole's sheet (closest hole {number}, {best.s:0.00})"));
                log?.Report($"{Path.GetFileName(p.File)} p{p.Page}: doesn't match any hole's sheet (closest hole {number}, {best.s:0.00}), skipped.");
                continue;
            }
            log?.Report($"{Path.GetFileName(p.File)} p{p.Page}: hole {number} ({best.s:0.00}, next best {runnerUp:0.00}" +
                        $"{(best.flip ? ", upside down" : "")}{(best.Built ? ", against the built sheet" : "")}).");
            matched.Add((pi, best.i, best.s, best.flip, norm, best.Norm, best.Built));
        }

        // 4. Separate the pencil and save.
        var set = Load(folder);
        Directory.CreateDirectory(folder);
        var saved = new HashSet<int>();
        foreach (var m in matched.OrderByDescending(m => m.Score))
        {
            var (p, rect, _) = pages[m.Page];
            var hole = course.Holes[m.HoleIndex];
            if (!saved.Add(hole.Number))
            {
                results.Add(new ScanResult(p.File, p.Page, hole.Number, m.Score, $"another page matched hole {hole.Number} better; skipped"));
                continue;
            }
            var scanRect = rect;
            var norm = m.Norm;
            if (m.Flip)
            {
                scanRect = Rotate180(rect);
                norm = SheetScan.PaperNormalised(scanRect);
            }
            var alpha = SheetScan.PencilAlpha(scanRect, norm, m.Reference);
            string stem = $"Hole_{hole.Number:00}";
            SavePencil(Path.Combine(folder, stem + " pencil.png"), alpha, size, k);
            SaveScan(Path.Combine(folder, stem + " scan.jpg"), scanRect, k);
            if (m.Flip) scanRect.Dispose();

            var corners = HoleSheet.LayoutFor(layers, hole, options).FrameCorners();
            set.Sheets.RemoveAll(s => s.Hole == hole.Number);
            set.Sheets.Add(new HoleSketch
            {
                Hole = hole.Number, Pencil = stem + " pencil.png", Scan = stem + " scan.jpg",
                Source = Path.GetFileName(p.File), Page = p.Page, Match = Math.Round(m.Score, 3),
                Frame = corners.Select(c => new[] { Math.Round(c.X, 3), Math.Round(c.Z, 3) }).ToArray(),
                Imported = DateTime.Now,
            });
            int inked = alpha.Count(a => a > 0);
            results.Add(new ScanResult(p.File, p.Page, hole.Number, m.Score, $"hole {hole.Number}, {100.0 * inked / alpha.Length:0.0} % of the sheet sketched", Saved: true));
        }
        foreach (var (p, rect, _) in pages) rect.Dispose();
        Save(folder, set);
        log?.Report($"Sketches for {saved.Count} hole(s) saved: {folder}");
        return results.OrderBy(r => r.Source, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Page).ToList();
    }

    /// <summary>A built hole sheet PNG's frame at the straightened size, normalised; null when there isn't one.</summary>
    static (byte[] Norm, float[] Thumb)? BuiltSheet(string path, SKSizeI size)
    {
        if (!File.Exists(path)) return null;
        using var png = SKBitmap.Decode(path);
        if (png is null) return null;
        // The PNG is the whole Letter page at its own dpi.
        float u = png.Width / HoleSheet.PageWidth;
        var r = HoleSheet.MapRectPoints;
        using var frame = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(frame))
        using (var image = SKImage.FromBitmap(png))
            c.DrawImage(image, new SKRect(r.Left * u, r.Top * u, r.Right * u, r.Bottom * u), new SKRect(0, 0, size.Width, size.Height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), null);
        var norm = SheetScan.PaperNormalised(frame);
        return (norm, SheetScan.Thumbnail(norm, size.Width, size.Height));
    }

    static SKBitmap Rotate180(SKBitmap b)
    {
        var r = new SKBitmap(b.Info);
        using var c = new SKCanvas(r);
        c.RotateDegrees(180, b.Width / 2f, b.Height / 2f);
        c.DrawBitmap(b, 0, 0);
        return r;
    }

    static void SavePencil(string path, byte[] alpha, SKSizeI size, int k)
    {
        using var full = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var px = full.GetPixelSpan();
        for (int i = 0; i < alpha.Length; i++)
        {
            px[4 * i] = Graphite.Red; px[4 * i + 1] = Graphite.Green; px[4 * i + 2] = Graphite.Blue; px[4 * i + 3] = alpha[i];
        }
        Encode(path, full, k, SKEncodedImageFormat.Png, 100);
    }

    static void SaveScan(string path, SKBitmap rect, int k) => Encode(path, rect, k, SKEncodedImageFormat.Jpeg, 85);

    static void Encode(string path, SKBitmap full, int k, SKEncodedImageFormat format, int quality)
    {
        var s = SavedPixelsPerPoint;
        var info = new SKImageInfo(full.Width * s / k, full.Height * s / k, full.ColorType, full.AlphaType);
        using var small = full.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var image = SKImage.FromBitmap(small);
        using var data = image.Encode(format, quality);
        using var f = File.Create(path);
        data.SaveTo(f);
    }
}
