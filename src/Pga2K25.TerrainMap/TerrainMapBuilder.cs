using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.TerrainMap;

public sealed record TerrainMapResult(string PngPath, SKSizeI Size, TerrainSurvey Survey);

public static class TerrainMapBuilder
{
    /// <summary>Writes "&lt;course&gt; - Terrain Map.png" to <paramref name="outputDir"/>.</summary>
    public static TerrainMapResult Build(string coursePath, string outputDir, TerrainMapOptions options, IProgress<string>? log = null)
    {
        var course = CourseFile.Load(coursePath).ReadModel();
        if (course.Holes.Count == 0) throw new InvalidOperationException("The course has no holes to route.");

        var survey = TerrainSurvey.Build(course, options);
        log?.Report($"Terrain: {course.HeightStamps.Count:N0} stamps replayed over {(survey.Max.X - survey.Min.X):0} × {(survey.Max.Z - survey.Min.Z):0} m " +
                    $"at {options.Terrain.CellMetres:0.##} m; relief {survey.MaxFeet:0} ft.");
        log?.Report($"Contours: every {survey.IntervalFeet:0.##} ft ({survey.Contours.Count(c => c.Index)} index / {survey.Contours.Count:N0} lines), " +
                    $"{survey.Summits.Count} hilltop spot heights.");
        log?.Report(survey.SeaLevelMetres is { } sea
            ? $"Sea level {sea:0.##} m (ocean props); shoreline traced in {survey.Shoreline.Count} piece(s){ShoreCheck(survey)}; elevations above sea level."
            : "No sea on the sheet; elevations above the lowest point.");
        foreach (var h in survey.Holes)
            log?.Report($"Hole {h.Hole.Number,2}: par {h.Hole.Par}, {h.Yards} yds, tee {h.TeeFeet:0} ft → green {h.GreenFeet:0} ft " +
                        $"({h.ChangeFeet:+0;-0;0} ft), green site {h.GreenSiteSlopePct:0.0}%.");

        using var renderer = new TerrainMapRenderer(options);
        var size = renderer.PageSize(survey);
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException($"Could not allocate a {size.Width} × {size.Height} px image; lower the resolution.");
        renderer.Draw(surface.Canvas, survey);

        Directory.CreateDirectory(outputDir);
        string baseName = string.Concat((string.IsNullOrWhiteSpace(course.Name) ? Path.GetFileNameWithoutExtension(coursePath) : course.Name)
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        string path = Path.Combine(outputDir, $"{baseName} - Terrain Map.png");
        using (var image = surface.Snapshot())
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var f = File.Create(path))
            data.SaveTo(f);
        log?.Report($"Terrain map ({size.Width:N0} × {size.Height:N0} px): {path}");
        return new TerrainMapResult(path, size, survey);
    }

    /// <summary>How far the on-sheet surf props sit from the modelled shoreline: a check on the terrain replay.</summary>
    static string ShoreCheck(TerrainSurvey s)
    {
        var pts = s.Shoreline.SelectMany(l => l).Where((_, i) => i % 4 == 0).ToList();
        var props = s.Course.PlacedObjects
            .Where(p => p.AssetPath.Contains("Ocean", StringComparison.OrdinalIgnoreCase)
                        && p.Position.X >= s.Min.X && p.Position.X <= s.Max.X && p.Position.Z >= s.Min.Z && p.Position.Z <= s.Max.Z)
            .Select(p => (Distance: pts.Count == 0 ? 0 : pts.Min(q => Vec2.Distance(p.Position, q)), Wet: s.IsWater(s.Field.Sample(p.Position))))
            .OrderBy(p => p.Distance).ToList();
        return props.Count == 0 || pts.Count == 0 ? ""
            : $" ({props.Count} surf props on the sheet, {props.Count(p => p.Wet)} offshore; median {props[props.Count / 2].Distance:0} m " +
              $"from the line, worst {props[^1].Distance:0} m)";
    }
}
