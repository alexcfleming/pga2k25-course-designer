using System.Text.Json;

namespace Pga2K25.Studio.Services;

/// <summary>Where courses are read from and products written to, plus per-course extras. Stored as JSON in AppData.</summary>
public sealed class StudioSettings
{
    static readonly string Documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public string CoursesFolder { get; set; } = Path.Combine(Documents, "My Games", "PGA TOUR 2K25", "Courses");
    public string OutputRoot { get; set; } = Path.Combine(Documents, "PGA 2K25 Greens Books");
    public string Credit { get; set; } = "";

    /// <summary>
    /// Lets the Place card fetch a real place's wind record from the internet, only when its button is pressed.
    /// Off by default: with it off Studio makes no network calls at all.
    /// </summary>
    public bool AllowOnlineWind { get; set; }

    /// <summary>Course files opened most recently first (at most <see cref="MaxRecent"/>).</summary>
    public List<string> Recent { get; set; } = [];
    public const int MaxRecent = 6;

    public void Touch(string coursePath)
    {
        Recent.RemoveAll(p => string.Equals(p, coursePath, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, coursePath);
        if (Recent.Count > MaxRecent) Recent.RemoveRange(MaxRecent, Recent.Count - MaxRecent);
    }

    /// <summary>Course files added by hand from outside the courses folder (e.g. a published course's saved copy).</summary>
    public List<string> ExtraCourses { get; set; } = [];

    /// <summary>Per course file: hole-data workbook and course-card settings.</summary>
    public Dictionary<string, CourseExtras> Courses { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public CourseExtras ExtrasFor(string coursePath) =>
        Courses.TryGetValue(coursePath, out var e) ? e : Courses[coursePath] = new CourseExtras();

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CourseDesignerStudio", "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static StudioSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<StudioSettings>(File.ReadAllText(FilePath), Json) ?? new StudioSettings();
        }
        catch (Exception) { /* a damaged settings file falls back to defaults */ }
        return new StudioSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}

public sealed class CourseExtras
{
    public string? Workbook { get; set; }
    public string? CardConfig { get; set; }
    public TerrainSettings Terrain { get; set; } = new();
}
