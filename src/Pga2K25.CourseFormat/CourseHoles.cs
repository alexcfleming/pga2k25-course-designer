using System.Text.Json;

namespace Pga2K25.CourseFormat;

/// <summary>One hole's entries: its name and, where the designer writes their own, hazards and green notes.</summary>
public sealed class HoleEntry
{
    public int Hole { get; set; }

    /// <summary>The hole's name, e.g. "Shaman Rock". Blank prints nothing.</summary>
    public string Name { get; set; } = "";

    /// <summary>Hazard tags written by hand; empty uses those read from the course file.</summary>
    public List<string> Hazards { get; set; } = [];

    /// <summary>Green contours and hole positions written by hand; blank uses the course file's.</summary>
    public string GreenNotes { get; set; } = "";
}

/// <summary>
/// What the course file can't say about its holes: tee set names and, per hole, a name and any hazards or green notes
/// written by hand. Saved as Holes.json in the course's output folder beside Place.json, written by the Studio's
/// Holes card and read by every product with a scorecard table. Blank fields print nothing (or fall back to what the
/// course file gives). Pars, yardages and bearings always come from the course file.
/// </summary>
public sealed class CourseHoles
{
    /// <summary>Tee set names, shortest → longest (the course file's tee order). Empty names the sets "Tee 1"… .</summary>
    public List<string> TeeNames { get; set; } = [];

    public List<HoleEntry> Holes { get; set; } = [];

    /// <summary>The hole's entry, or null when it has none.</summary>
    public HoleEntry? For(int hole) => Holes.FirstOrDefault(h => h.Hole == hole);

    /// <summary>The hole's name, trimmed, or null when blank.</summary>
    public string? NameOf(int hole) => For(hole)?.Name.Trim() is { Length: > 0 } n ? n : null;

    /// <summary>True when at least one hole has a name: scorecard tables add their Name column only then.</summary>
    public bool HasNames => Holes.Any(h => h.Name.Trim().Length > 0);

    /// <summary>The tee names that have text, trimmed.</summary>
    public IReadOnlyList<string> Tees => TeeNames.Select(t => t.Trim()).Where(t => t.Length > 0).ToList();

    /// <summary>The entry for <paramref name="hole"/>, added when missing.</summary>
    public HoleEntry Entry(int hole)
    {
        if (For(hole) is { } e) return e;
        e = new HoleEntry { Hole = hole };
        Holes.Add(e);
        Holes.Sort((a, b) => a.Hole.CompareTo(b.Hole));
        return e;
    }
}

public static class CourseHolesFile
{
    public const string FileName = "Holes.json";

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string PathFor(string outputDir) => Path.Combine(outputDir, FileName);

    /// <summary>The course's hole entries, or empty ones when there's no file (or it can't be read).</summary>
    public static CourseHoles Load(string outputDir)
    {
        string path = PathFor(outputDir);
        if (!File.Exists(path)) return new CourseHoles();
        try { return JsonSerializer.Deserialize<CourseHoles>(File.ReadAllText(path), Json) ?? new CourseHoles(); }
        catch (JsonException) { return new CourseHoles(); }
    }

    public static void Save(string outputDir, CourseHoles holes)
    {
        Directory.CreateDirectory(outputDir);
        // Write beside and swap, so a crash mid-write can't lose the entries.
        string path = PathFor(outputDir), tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(holes, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
