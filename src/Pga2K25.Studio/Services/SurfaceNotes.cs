using System.Text.Json;

namespace Pga2K25.Studio.Services;

/// <summary>A note on a surface material, with when it was last edited and the courses it has been seen on.</summary>
public sealed class MaterialNote
{
    public string Text { get; set; } = "";
    public DateTime Edited { get; set; }
    public List<string> Courses { get; set; } = [];
}

/// <summary>
/// Notes on surface materials that follow the material to every course: what Splat 1 · TPC Sawgrass looks like, how
/// it sits under Bedrock. One file for all courses, kept with Studio's settings
/// (<c>%AppData%\CourseDesignerStudio\surface-notes.json</c>), keyed by <see cref="SurfaceRow.Key"/>.
/// </summary>
public sealed class MaterialNotes
{
    public Dictionary<string, MaterialNote> Notes { get; set; } = new(StringComparer.Ordinal);

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CourseDesignerStudio", "surface-notes.json");

    public static MaterialNotes Load(string path)
    {
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<MaterialNotes>(File.ReadAllText(path), Json) ?? new();
        }
        catch (Exception) { /* a damaged file starts empty rather than stopping the card */ }
        return new();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }

    public string? Text(string key) => Notes.TryGetValue(key, out var n) && n.Text.Length > 0 ? n.Text : null;

    /// <summary>Sets or (with blank text) removes the note; the course goes on its list.</summary>
    public void Set(string key, string text, string course, DateTime now)
    {
        text = text.Trim();
        if (text.Length == 0) { Notes.Remove(key); return; }
        var n = Notes.TryGetValue(key, out var old) ? old : Notes[key] = new MaterialNote();
        n.Text = text;
        n.Edited = now;
        SeenOn(key, course);
    }

    /// <summary>Records that a course uses the material, so the note can say where else it's in use. True when that's new.</summary>
    public bool SeenOn(string key, string course)
    {
        if (!Notes.TryGetValue(key, out var n) || n.Courses.Contains(course, StringComparer.OrdinalIgnoreCase)) return false;
        n.Courses.Add(course);
        return true;
    }
}

/// <summary>
/// One course's own surface notes, beside its products in <c>SurfaceNotes.json</c>: a line's note kept for this course
/// only, and each group's note on how its layers work together here ("These four together on Cassimbo").
/// </summary>
public sealed class CourseSurfaceNotes
{
    public const string FileName = "SurfaceNotes.json";

    /// <summary>"This course only" notes, keyed by <see cref="SurfaceRow.Key"/>.</summary>
    public Dictionary<string, string> Lines { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The combination notes, keyed by the group's title.</summary>
    public Dictionary<string, string> Groups { get; set; } = new(StringComparer.Ordinal);

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static CourseSurfaceNotes Load(string outputDir)
    {
        string path = Path.Combine(outputDir, FileName);
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<CourseSurfaceNotes>(File.ReadAllText(path), Json) ?? new();
        }
        catch (Exception) { /* as for the material notes */ }
        return new();
    }

    public void Save(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, FileName), JsonSerializer.Serialize(this, Json));
    }

    public static void Put(Dictionary<string, string> into, string key, string text)
    {
        text = text.Trim();
        if (text.Length == 0) into.Remove(key); else into[key] = text;
    }

    public int Count => Lines.Count + Groups.Count;
}
