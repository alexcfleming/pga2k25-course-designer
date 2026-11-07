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
    /// <summary>The traced lines in world X/Z (<see cref="SketchLines"/>, JSON); empty for sketches imported before tracing.</summary>
    public string Lines { get; init; } = "";
    public string Source { get; init; } = "";
    public int Page { get; init; }
    /// <summary>How well the page matched the hole's sheet (correlation, 0–1).</summary>
    public double Match { get; init; }
    public double[][] Frame { get; init; } = [];
    public DateTime Imported { get; init; }

    public Vec2[] FrameCorners() => Frame.Select(c => new Vec2(c[0], c[1])).ToArray();
}

/// <summary>A hole sheet's traced lines on the ground. <see cref="SketchLine.Xz"/> is X, Z, X, Z … in metres.</summary>
public sealed record SketchLines
{
    public int Version { get; init; } = 1;
    /// <summary>The darkness below which strokes counted as light; null when the sheet is drawn in one weight.</summary>
    public double? FaintBelow { get; init; }
    public List<SketchLine> Lines { get; init; } = [];
}

/// <summary>One traced line: X, Z, X, Z … metres. <see cref="Construction"/>: a shot line, aim point, letter or hatching.</summary>
public sealed record SketchLine(bool Construction, double[] Xz);

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
            foreach (var f in new[] { s.Pencil, s.Scan, s.Lines })
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
        //    Sheets printed before 2.5 used the full-width layout, so each hole is rendered both ways.
        var (datum, sea) = HoleSheetBuilder.PlotDatum(course, options);
        HoleSheetLayout[] kinds = [HoleSheetLayout.Strip, HoleSheetLayout.FullWidth];
        int n = course.Holes.Count;
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4) };
        var references = new Reference[n * kinds.Length];
        Parallel.For(0, references.Length, parallel, j =>
        {
            int i = j % n;
            references[j] = Render(course, layers, course.Holes[i], options with { Layout = kinds[j / n] }, datum, sea, size);
            if (j < n) log?.Report($"Rendered hole {course.Holes[i].Number}'s sheet.");
        });
        // Per hole, the sheets to compare a page against. A built sheet is in whichever layout its fresh render looks more like.
        var candidates = new List<Candidate>[n];
        for (int i = 0; i < n; i++)
        {
            candidates[i] = kinds.Select((kind, t) => new Candidate(references[t * n + i].Norm, references[t * n + i].Thumb, false, kind, 0)).ToList();
            if (printedSheets is not null && BuiltSheet(Path.Combine(printedSheets, $"Hole_{course.Holes[i].Number:00}.png"), size) is { } b)
                candidates[i].Add(new Candidate(b.Norm, b.Thumb, true,
                    SheetScan.Correlation(b.Thumb, references[i].Thumb) >= SheetScan.Correlation(b.Thumb, references[n + i].Thumb)
                        ? HoleSheetLayout.Strip : HoleSheetLayout.FullWidth, 0));
        }
        var pageNorms = new byte[pages.Count][];
        var pageThumbs = new float[pages.Count][];
        for (int pi = 0; pi < pages.Count; pi++)
        {
            pageNorms[pi] = SheetScan.PaperNormalised(pages[pi].Rect);
            pageThumbs[pi] = SheetScan.Thumbnail(pageNorms[pi], size.Width, size.Height);
        }
        // A page's closest sheet (either way up), and the closest of any other hole's.
        ((int Hole, double Score, bool Flip, Candidate Sheet) Best, double RunnerUp) Closest(int pi)
        {
            var scores = Enumerable.Range(0, n)
                .SelectMany(i => candidates[i].SelectMany(c => new[]
                {
                    (Hole: i, Score: SheetScan.Correlation(pageThumbs[pi], c.Thumb), Flip: false, Sheet: c),
                    (Hole: i, Score: SheetScan.Correlation(pageThumbs[pi], c.Thumb, flip: true), Flip: true, Sheet: c),
                }))
                .OrderByDescending(x => x.Score).ToList();
            var best = scores[0];
            return (best, scores.FirstOrDefault(x => x.Hole != best.Hole).Score);
        }

        // 3. The heights on a sheet are measured from the plot's lowest point, which moves when the ground there is
        //    reworked (or the terrain model improves), shifting every printed contour off today's. Per scan file, the
        //    best-matched page finds how far its print's heights are off, within one index-contour period, and every
        //    hole is rendered that way too. Sea-level heights don't move.
        if (!sea)
            foreach (var file in pages.Select(p => p.Page.File).Distinct())
            {
                var filePages = Enumerable.Range(0, pages.Count).Where(pi => pages[pi].Page.File == file).ToList();
                var (lead, leadBest) = filePages.Select(pi => (pi, Closest(pi).Best)).MaxBy(x => x.Best.Score);
                using var upright = leadBest.Flip ? Rotate180(pages[lead].Rect) : null;
                var leadScan = upright ?? pages[lead].Rect;
                var fit = FitDatum(course, layers, course.Holes[leadBest.Hole], options with { Layout = leadBest.Sheet.Kind }, datum, size,
                    leadScan, upright is null ? pageNorms[lead] : SheetScan.PaperNormalised(upright), parallel);
                if (Math.Abs(fit.Shift) < 0.05 || fit.Score < fit.AtZero + 0.02) continue;
                if (candidates.Any(cs => cs.Any(c => Math.Abs(c.ShiftFeet - fit.Shift) < 0.05))) continue;   // another file's print
                log?.Report($"{Path.GetFileName(file)}: the printed heights read {Math.Abs(fit.Shift):0.0} ft {(fit.Shift > 0 ? "lower" : "higher")} than " +
                            $"today's (the plot's lowest point has moved since it was printed); matching the contours as printed.");
                var used = filePages.Select(pi => Closest(pi).Best.Sheet.Kind).Distinct().ToArray();
                var shifted = new Reference[n * used.Length];
                Parallel.For(0, shifted.Length, parallel, j =>
                    shifted[j] = Render(course, layers, course.Holes[j % n], options with { Layout = used[j / n] }, datum + fit.Shift / TerrainSurvey.FeetPerMetre, sea, size));
                for (int j = 0; j < shifted.Length; j++)
                    candidates[j % n].Add(new Candidate(shifted[j].Norm, shifted[j].Thumb, false, used[j / n], fit.Shift));
            }

        // 4. Match each page to a hole (either way up), keeping the best page when two match the same hole.
        var matched = new List<(int Page, int HoleIndex, double Score, bool Flip, byte[] Norm, byte[] Reference, bool Built, HoleSheetLayout Kind)>();
        for (int pi = 0; pi < pages.Count; pi++)
        {
            var (p, _, _) = pages[pi];
            var (best, runnerUp) = Closest(pi);
            int number = course.Holes[best.Hole].Number;
            if (best.Score < MinMatch)
            {
                results.Add(new ScanResult(p.File, p.Page, null, best.Score, $"doesn't match any hole's sheet (closest hole {number}, {best.Score:0.00})"));
                log?.Report($"{Path.GetFileName(p.File)} p{p.Page}: doesn't match any hole's sheet (closest hole {number}, {best.Score:0.00}), skipped.");
                continue;
            }
            log?.Report($"{Path.GetFileName(p.File)} p{p.Page}: hole {number} ({best.Score:0.00}, next best {runnerUp:0.00}" +
                        $"{(best.Flip ? ", upside down" : "")}{(best.Sheet.Built ? ", against the built sheet" : "")}" +
                        $"{(best.Sheet.Kind == HoleSheetLayout.FullWidth ? ", a pre-2.5 full-width sheet" : "")}" +
                        $"{(best.Sheet.ShiftFeet != 0 ? $", contours as printed {Math.Abs(best.Sheet.ShiftFeet):0.0} ft {(best.Sheet.ShiftFeet > 0 ? "lower" : "higher")}" : "")}).");
            matched.Add((pi, best.Hole, best.Score, best.Flip, pageNorms[pi], best.Sheet.Norm, best.Sheet.Built, best.Sheet.Kind));
        }

        // 5. Separate the pencil and save.
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
            // The print moved onto the scan's, tile by tile, where the paper wasn't flat.
            var reference = SheetScan.AlignTo(norm, m.Reference, size.Width, size.Height);
            var score = SheetScan.PencilScore(scanRect, norm, reference);
            var layout = HoleSheet.LayoutFor(layers, hole, options with { Layout = m.Kind });
            // Only pencil on the map strip is on the ground; writing in the notes column stays on the paper. The
            // frame line itself is kept out: the alignment can move it.
            var strip = m.Kind == HoleSheetLayout.Strip ? HoleSheet.Within(layout.StripPoints, new SKRect(0, 0, size.Width, size.Height))
                : new SKRect(0, 0, size.Width, size.Height);
            strip.Inflate(-SheetScan.AlignReach, -SheetScan.AlignReach);
            MaskOutside(score, size, strip);
            var alpha = SheetScan.PencilAlpha(score, size.Width, size.Height);
            var traced = SketchTracer.Trace(score, norm, size.Width, size.Height, out double? faintBelow);
            string stem = $"Hole_{hole.Number:00}";
            SavePencil(Path.Combine(folder, stem + " pencil.png"), alpha, size, k);
            SaveScan(Path.Combine(folder, stem + " scan.jpg"), scanRect, k);
            if (m.Flip) scanRect.Dispose();

            var corners = layout.FrameCorners();
            SaveLines(Path.Combine(folder, stem + " lines.json"), traced, faintBelow, corners, size);
            set.Sheets.RemoveAll(s => s.Hole == hole.Number);
            set.Sheets.Add(new HoleSketch
            {
                Hole = hole.Number, Pencil = stem + " pencil.png", Scan = stem + " scan.jpg", Lines = stem + " lines.json",
                Source = Path.GetFileName(p.File), Page = p.Page, Match = Math.Round(m.Score, 3),
                Frame = corners.Select(c => new[] { Math.Round(c.X, 3), Math.Round(c.Z, 3) }).ToArray(),
                Imported = DateTime.Now,
            });
            int firm = traced.Count(t => !t.Construction), faint = traced.Count - firm;
            results.Add(new ScanResult(p.File, p.Page, hole.Number, m.Score,
                $"hole {hole.Number}, {firm} line{(firm == 1 ? "" : "s")}" + (faint > 0 ? $" and {faint} construction mark{(faint == 1 ? "" : "s")} (shot lines, aim points, hatching)" : ""), Saved: true));
        }
        foreach (var (p, rect, _) in pages) rect.Dispose();
        Save(folder, set);
        log?.Report($"Sketches for {saved.Count} hole(s) saved: {folder}");
        return results.OrderBy(r => r.Source, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Page).ToList();
    }

    /// <summary>A sheet rendered to compare pages with: its map frame normalised, and a thumbnail of that.</summary>
    sealed record Reference(byte[] Norm, float[] Thumb);

    /// <summary>
    /// A sheet a page may be: rendered now or the build's PNG (<paramref name="Built"/>), in a layout, with its heights
    /// <paramref name="ShiftFeet"/> lower than today's (the datum raised that much).
    /// </summary>
    sealed record Candidate(byte[] Norm, float[] Thumb, bool Built, HoleSheetLayout Kind, double ShiftFeet);

    static Reference Render(CourseModel course, Maps.CourseLayers layers, Hole hole, HoleSheetOptions options, double datum, bool sea, SKSizeI size) =>
        Render(course, layers, hole, options, datum, sea, size, null).Reference;

    /// <summary>A hole's sheet rendered at <see cref="SheetScan.PixelsPerPoint"/>, its map frame normalised as a scan is.</summary>
    static (Reference Reference, HoleSheet Sheet) Render(CourseModel course, Maps.CourseLayers layers, Hole hole, HoleSheetOptions options,
        double datum, bool sea, SKSizeI size, HoleSheet? sameGround)
    {
        int k = SheetScan.PixelsPerPoint;
        var renderOptions = options with { Dpi = 72.0 * k };
        var sheet = HoleSheet.Build(course, layers, hole, renderOptions, datum, sea, sameGround);
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
        return (new Reference(norm, SheetScan.Thumbnail(norm, size.Width, size.Height)), sheet);
    }

    /// <summary>
    /// How far a printed sheet's heights are off today's: the shift (ft, positive when the print reads lower) that
    /// best explains the straightened page <paramref name="scan"/> (upright, normalised as <paramref name="scanNormalised"/>),
    /// searched within one index-contour period around today's. Contours repeat every period, so only the labels
    /// tell whole periods apart. Quarter-interval steps are compared by thumbnail; then sixteenths round the best
    /// by how much of the print would be left as pencil, at full size and after <see cref="SheetScan.AlignTo"/>.
    /// <see cref="Score"/> and <see cref="AtZero"/> are the thumbnail match at the shift and at today's heights.
    /// </summary>
    internal static (double Shift, double Score, double AtZero) FitDatum(CourseModel course, Maps.CourseLayers layers, Hole hole,
        HoleSheetOptions options, double datum, SKSizeI size, SKBitmap scan, byte[] scanNormalised, ParallelOptions parallel)
    {
        var thumb = SheetScan.Thumbnail(scanNormalised, size.Width, size.Height);
        var (today, ground) = Render(course, layers, hole, options, datum, false, size, null);
        double interval = ground.Survey.IntervalFeet;
        int every = Math.Max(1, ground.Survey.IndexEvery);
        var renders = new Dictionary<double, Reference> { [0] = today };
        void RenderAll(IEnumerable<double> shifts)
        {
            var todo = shifts.Select(s => Math.Round(s, 4)).Where(s => !renders.ContainsKey(s)).Distinct().ToArray();
            var made = new Reference[todo.Length];
            Parallel.For(0, todo.Length, parallel, j =>
                made[j] = Render(course, layers, hole, options, datum + todo[j] / TerrainSurvey.FeetPerMetre, false, size, ground).Reference);
            for (int j = 0; j < todo.Length; j++) renders[todo[j]] = made[j];
        }
        double Match(double s) => SheetScan.Correlation(thumb, renders[s].Thumb);

        RenderAll(Enumerable.Range(0, 4 * every).Select(j => -interval * every / 2 + j * interval / 4));
        double coarse = renders.Keys.MaxBy(Match);
        var fine = Enumerable.Range(-4, 9).Select(j => Math.Round(coarse + j * interval / 16, 4)).ToArray();
        RenderAll(fine);
        var left = new double[fine.Length];
        Parallel.For(0, fine.Length, parallel, j =>
            left[j] = SheetScan.PencilScore(scan, scanNormalised, SheetScan.AlignTo(scanNormalised, renders[fine[j]].Norm, size.Width, size.Height)).Sum());
        double best = fine[Array.IndexOf(left, left.Min())];
        return (best, Match(best), Match(0));
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

    public static SketchLines? LoadLines(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<SketchLines>(File.ReadAllText(path), Json); }
        catch (JsonException) { return null; }
    }

    /// <summary>Traced lines from straightened-frame pixels to world X/Z through the frame's corners.</summary>
    static void SaveLines(string path, List<TracedLine> traced, double? faintBelow, Vec2[] corners, SKSizeI size)
    {
        Vec2 c0 = corners[0], ex = corners[1] - corners[0], ey = corners[3] - corners[0];
        var lines = traced.Select(t => new SketchLine(t.Construction, t.Points.SelectMany(p =>
        {
            var w = c0 + ex * (p.X / size.Width) + ey * (p.Y / size.Height);
            return new[] { Math.Round(w.X, 2), Math.Round(w.Z, 2) };
        }).ToArray())).ToList();
        var compact = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        File.WriteAllText(path, JsonSerializer.Serialize(new SketchLines { FaintBelow = faintBelow, Lines = lines }, compact));
    }

    /// <summary>Clears the pencil score outside <paramref name="keep"/> (straightened-frame pixels), a little inside its edge where the frame line is.</summary>
    static void MaskOutside(float[] score, SKSizeI size, SKRect keep)
    {
        int x0 = (int)Math.Ceiling(keep.Left) + 2, x1 = (int)Math.Floor(keep.Right) - 2;
        int y0 = (int)Math.Ceiling(keep.Top) + 2, y1 = (int)Math.Floor(keep.Bottom) - 2;
        for (int y = 0; y < size.Height; y++)
        for (int x = 0; x < size.Width; x++)
            if (x < x0 || x > x1 || y < y0 || y > y1) score[y * size.Width + x] = 0;
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
