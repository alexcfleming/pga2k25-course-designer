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

    /// <summary>The workspace menu folded to its icon rail (the terrain map gets the width).</summary>
    public bool MenuCollapsed { get; set; }

    /// <summary>The workspace menu's groups that are folded shut, by title. Course setup and Recent start folded.</summary>
    public List<string> ClosedMenuGroups { get; set; } = ["Course setup", "Recent"];

    /// <summary>Course files added by hand from outside the courses folder (e.g. a published course's saved copy).</summary>
    public List<string> ExtraCourses { get; set; } = [];

    /// <summary>
    /// Courses hidden from the library by Archive. Nothing on disk changes: the course file stays in its folder and
    /// its products stay where they are. Kept by the course's id (from the file), so a course renamed or moved in the
    /// designer stays archived; a copy saved as a new file has its own id and shows as usual.
    /// </summary>
    public List<ArchivedCourse> Archived { get; set; } = [];

    /// <summary>The archive entry for a course, or null when it isn't archived: by id, or by path for a file with no id.</summary>
    public ArchivedCourse? ArchiveOf(CourseEntry entry) =>
        Archived.FirstOrDefault(a => entry.Id.Length > 0 ? a.Id == entry.Id : string.Equals(a.Path, entry.Path, StringComparison.OrdinalIgnoreCase));

    public bool IsArchived(CourseEntry entry) => ArchiveOf(entry) is not null;

    /// <summary>Hides <paramref name="entry"/> from the library (and the Recent list); returns the entry made.</summary>
    public ArchivedCourse Archive(CourseEntry entry, DateTime? when = null)
    {
        Restore(entry);
        var a = new ArchivedCourse { Id = entry.Id, Path = entry.Path, Name = entry.Name, Archived = when ?? DateTime.Now };
        Archived.Add(a);
        return a;
    }

    /// <summary>Puts <paramref name="entry"/> back in the library.</summary>
    public void Restore(CourseEntry entry) =>
        Archived.RemoveAll(a => entry.Id.Length > 0 ? a.Id == entry.Id : string.Equals(a.Path, entry.Path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Per course file: course-card settings and terrain map options.</summary>
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
    public string? CardConfig { get; set; }
    public TerrainSettings Terrain { get; set; } = new();

    /// <summary>
    /// The scorecard maps' bunker merge distance (m). 10 suits bunkers painted as scattered dabs (Cassimbo's sand);
    /// courses built from tight pot complexes read better at 4 (Sangoma).
    /// </summary>
    public double BunkerMergeMetres { get; set; } = DefaultBunkerMerge;

    public const double DefaultBunkerMerge = 10;
    public static readonly IReadOnlyList<double> BunkerMergeChoices = [10, 6, 4, 2];

    /// <summary>
    /// The scorecard map styles to build, by name (<see cref="Pga2K25.Maps.MapThemeKind"/>). Null or empty (the
    /// default since 3.6.0) builds none: a course builds only the styles ticked for it. Names the app no longer knows
    /// are ignored.
    /// </summary>
    public List<string>? ScorecardThemes { get; set; }

    /// <summary>The styles "Build" makes for this course, in menu order; empty until one is ticked.</summary>
    public IReadOnlyList<Pga2K25.Maps.MapThemeKind> ScorecardThemeKinds() =>
        ScorecardThemes is null ? []
        : Enum.GetValues<Pga2K25.Maps.MapThemeKind>().Where(k => ScorecardThemes.Contains(k.ToString(), StringComparer.OrdinalIgnoreCase)).ToList();

    /// <summary>Ticks or unticks one style. Unticking the last leaves none, and Build then skips scorecard maps.</summary>
    public void SetScorecardTheme(Pga2K25.Maps.MapThemeKind kind, bool on)
    {
        var picked = ScorecardThemeKinds().ToList();
        if (on == picked.Contains(kind)) return;
        if (on) picked.Add(kind); else picked.Remove(kind);
        ScorecardThemes = picked.Count == 0 ? null : Enum.GetValues<Pga2K25.Maps.MapThemeKind>().Where(picked.Contains).Select(k => k.ToString()).ToList();
    }
}

/// <summary>A course hidden from the library: its id and the file and name it had when archived, and when.</summary>
public sealed class ArchivedCourse
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime Archived { get; set; }
}
