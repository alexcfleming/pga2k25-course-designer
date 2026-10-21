using System.Text.Json;

namespace Pga2K25.CourseFormat;

/// <summary>A spot the designer has seen is dry in game, though the model shows water there. World X/Z, metres.</summary>
public sealed class NotWaterMark
{
    public double X { get; set; }
    public double Z { get; set; }
    public string Note { get; set; } = "";
    public DateTime Marked { get; set; } = DateTime.Now;

    public Vec2 Point => new(X, Z);
}

/// <summary>
/// A course's "not water in game" marks, saved as NotWater.json in its output folder beside Notes.json. The water
/// tracer leaves out any piece of modelled water a mark falls in, so every product drops it. Planes show water only
/// where the replayed ground dips below their level, and the replay can be a metre or two out over shallow ground.
/// When the designer can see the game, their eye beats the model.
/// </summary>
public static class NotWaterMarks
{
    public const string FileName = "NotWater.json";

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string PathFor(string outputDir) => Path.Combine(outputDir, FileName);

    public static List<NotWaterMark> Load(string outputDir)
    {
        string path = PathFor(outputDir);
        if (!File.Exists(path)) return [];
        try { return JsonSerializer.Deserialize<List<NotWaterMark>>(File.ReadAllText(path), Json) ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>The marks' points, ready for <see cref="CourseModel.NotWater"/>.</summary>
    public static IReadOnlyList<Vec2> Points(string outputDir) => Load(outputDir).Select(m => m.Point).ToList();

    public static void Save(string outputDir, IEnumerable<NotWaterMark> marks)
    {
        Directory.CreateDirectory(outputDir);
        string path = PathFor(outputDir), tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(marks.ToList(), Json));
        File.Move(tmp, path, overwrite: true);
    }
}
