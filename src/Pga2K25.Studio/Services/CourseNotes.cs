using System.Text.Json;

namespace Pga2K25.Studio.Services;

/// <summary>A note pinned to the ground: how to build something, planting, a concept. World X/Z, metres.</summary>
public sealed class CourseNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public double X { get; set; }
    public double Z { get; set; }
    public string Kind { get; set; } = CourseNotes.Build;
    public string Text { get; set; } = "";
    /// <summary>The hole it belongs to; null for the course as a whole.</summary>
    public int? Hole { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Modified { get; set; } = DateTime.Now;
}

/// <summary>
/// A course's pinned notes, saved as Notes.json in its output folder beside Measurements.csv. The course file is
/// never changed.
/// </summary>
public static class CourseNotes
{
    public const string Build = "Build", Planting = "Planting", Concept = "Concept", General = "Note";
    public static readonly IReadOnlyList<string> Kinds = [Build, Planting, Concept, General];

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string PathFor(StudioSettings settings, string courseName) => Path.Combine(Products.OutputDir(settings, courseName), "Notes.json");

    public static List<CourseNote> Load(string path)
    {
        if (!File.Exists(path)) return [];
        try { return JsonSerializer.Deserialize<List<CourseNote>>(File.ReadAllText(path), Json) ?? []; }
        catch (JsonException) { return []; }
    }

    public static void Save(string path, IEnumerable<CourseNote> notes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Write beside and swap, so a crash mid-write can't lose the notes.
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(notes.OrderBy(n => n.Created).ToList(), Json));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Pin colour per kind.</summary>
    public static string ColourOf(string kind) => kind switch
    {
        Build => "#B5542C",
        Planting => "#2E7D32",
        Concept => "#5B45B0",
        _ => "#2B5D8A",
    };
}
