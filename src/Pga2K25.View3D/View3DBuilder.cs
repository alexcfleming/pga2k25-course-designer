using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Pga2K25.CourseFormat;
using Pga2K25.TerrainMap;

namespace Pga2K25.View3D;

public sealed record View3DResult(string HtmlPath, long Bytes, Scene3D Scene);

/// <summary>
/// Writes "&lt;course&gt; - 3D View.html": one self-contained page (three.js, the ground, its picture and the scene all
/// inside it) that opens in any browser with no network, and in a web view.
/// </summary>
public static class View3DBuilder
{
    public static View3DResult Build(string coursePath, string outputDir, View3DOptions options, IProgress<string>? log = null)
    {
        var course = CourseFile.Load(coursePath).ReadModel();
        course.NotWater = NotWaterMarks.Points(outputDir);
        var scene = Scene3D.Build(course, options);
        var survey = scene.Survey;
        log?.Report($"Ground: {scene.Width:N0} × {scene.Depth:N0} points at {scene.Cell:0.##} m over " +
                    $"{(scene.Width - 1) * scene.Cell:0} × {(scene.Depth - 1) * scene.Cell:0} m; {(scene.High - scene.Low) * TerrainSurvey.FeetPerMetre:0} ft of relief.");
        log?.Report(survey.SeaLevelMetres is { } sea ? $"Sea at {sea:0.##} m; {scene.Water.Count} inland water surface(s)." : $"No sea; {scene.Water.Count} inland water surface(s).");
        log?.Report($"Planting: {scene.Plants.Count:N0} trees and bushes of {scene.Species.Count} kind(s)" +
                    (scene.Plants.Count < scene.PlantsInFile ? $" (of about {scene.PlantsInFile:N0} in the file; thinned, or outside the model or in water)." : "."));

        string name = string.IsNullOrWhiteSpace(course.Name) ? Path.GetFileNameWithoutExtension(coursePath) : course.Name;
        string html = Html(scene, name, options, log);

        Directory.CreateDirectory(outputDir);
        string baseName = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        string path = Path.Combine(outputDir, $"{baseName} - 3D View.html");
        File.WriteAllText(path, html, new UTF8Encoding(false));
        long bytes = new FileInfo(path).Length;
        log?.Report($"3D view ({bytes / 1024.0 / 1024:0.0} MB): {path}");
        return new View3DResult(path, bytes, scene);
    }

    /// <summary>The page for a scene already built, as text: Studio writes it where its web view can load it.</summary>
    public static string Html(Scene3D scene, string name, View3DOptions options, IProgress<string>? log = null)
    {
        var picture = GroundPicture.Build(scene, options);
        log?.Report($"Ground picture: {picture.Width:N0} × {picture.Height:N0} px ({picture.PixelsPerMetre:0.##} px/m), {picture.Jpeg.Length / 1024.0 / 1024:0.0} MB.");
        return Resource("view3d.html")
            .Replace("{{TITLE}}", System.Net.WebUtility.HtmlEncode(name))
            .Replace("{{SCENE}}", SceneJson(scene, name))
            .Replace("{{HEIGHTS}}", HeightsBase64(scene))
            .Replace("{{PICTURE}}", Convert.ToBase64String(picture.Jpeg))
            .Replace("/*{{THREE}}*/", ThreeJs());
    }

    /// <summary>Heights as 16-bit steps between the lowest and highest ground, little-endian, gzipped, base64.</summary>
    static string HeightsBase64(Scene3D scene)
    {
        double range = Math.Max(1e-6, scene.High - scene.Low);
        var raw = new byte[scene.Heights.Length * 2];
        for (int i = 0; i < scene.Heights.Length; i++)
        {
            ushort q = (ushort)Math.Clamp(Math.Round((scene.Heights[i] - scene.Low) / range * 65535), 0, 65535);
            raw[2 * i] = (byte)q; raw[2 * i + 1] = (byte)(q >> 8);
        }
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.SmallestSize, leaveOpen: true)) gzip.Write(raw);
        return Convert.ToBase64String(packed.GetBuffer(), 0, (int)packed.Length);
    }

    static string SceneJson(Scene3D scene, string name)
    {
        var survey = scene.Survey;
        static double R(double v) => Math.Round(v, 2);
        static double[] P(Vec2 p) => [Math.Round(p.X, 1), Math.Round(p.Z, 1)];
        var data = new
        {
            name,
            grid = new { nx = scene.Width, nz = scene.Depth, cell = scene.Cell, minX = scene.Origin.X, minZ = scene.Origin.Z, lo = scene.Low, hi = scene.High },
            sea = survey.SeaLevelMetres,
            datum = survey.DatumMetres,
            datumIsSea = survey.SeaLevelMetres is not null,
            contourFeet = survey.IntervalFeet,
            indexEvery = survey.IndexEvery,
            timeOfDay = survey.Course.Atmosphere?.TimeOfDay,
            // The game's sun: compass bearing and height (degrees); the page opens lit by it.
            sun = TerrainMap.TerrainInk.SunDirection(survey.Course) is var (bearing, height) ? new[] { R(bearing), R(height) } : null,
            holes = survey.Holes.Select(h => new
            {
                n = h.Hole.Number, par = h.Hole.Par, yards = h.Yards,
                route = h.Route.Select(P), tees = h.Hole.Tees.Select(P), pins = h.Hole.Pins.Select(P), green = P(h.GreenSiteCentre),
                teeFeet = R(h.TeeFeet), greenFeet = R(h.GreenFeet),
            }),
            water = scene.Water.Select(w => new { level = R(w.Level), shores = w.Shores.Select(s => s.Select(P)) }),
            species = scene.Species.Select(s => new { name = s.Name, shape = (int)s.Shape }),
            // x, z, scale, species: four numbers a plant.
            plants = scene.Plants.SelectMany(p => new[] { Math.Round(p.At.X, 1), Math.Round(p.At.Z, 1), R(p.Scale), p.Species }),
            plantsInFile = scene.PlantsInFile,
            landmarks = survey.Landmarks.Where(l => !l.Minor).Select(l => new { name = l.Name, at = P(l.Position) }),
        };
        // The default encoder escapes < and >, so the JSON is safe inside a script element.
        return JsonSerializer.Serialize(data);
    }

    /// <summary>
    /// three.js r159 as shipped, licence comment included, less the opening console warning that this classic
    /// (non-module) build is deprecated: the page needs that build, since a module can't be inlined in one file.
    /// </summary>
    static string ThreeJs()
    {
        string js = Resource("three.min.js");
        int licence = js.IndexOf("/**", StringComparison.Ordinal);
        // The warning is the first operand of a comma expression; "0," keeps what follows an expression.
        if (js.StartsWith("console.warn(", StringComparison.Ordinal) && licence > 0) js = "0," + js[licence..];
        return js.Replace("</script", "<\\/script");
    }

    static string Resource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource {name}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
