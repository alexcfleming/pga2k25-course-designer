using Pga2K25.CourseFormat;

namespace Pga2K25.Studio.Services;

public enum CourseKind { Routed, InProgress, Blank }

/// <summary>What the library shows about one course file, read without touching it.</summary>
public sealed record CourseEntry(
    string Path, string Name, CourseKind Kind, int Holes, int Par, int Yards, DateTime Saved, byte[]? Thumbnail, string? Error = null);

public static class CourseLibrary
{
    public const double YardsPerMetre = 1.0936133;

    /// <summary>Every .course in the courses folder plus any added by hand, newest first. Unreadable files are listed with their error.</summary>
    public static List<CourseEntry> Scan(StudioSettings settings)
    {
        var paths = new List<string>();
        if (Directory.Exists(settings.CoursesFolder)) paths.AddRange(Directory.GetFiles(settings.CoursesFolder, "*.course"));
        paths.AddRange(settings.ExtraCourses.Where(File.Exists));
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).Select(Read).OrderByDescending(c => c.Saved).ToList();
    }

    public static CourseEntry Read(string path)
    {
        var saved = File.GetLastWriteTime(path);
        try
        {
            var file = CourseFile.Load(path);
            var model = file.ReadModel();
            byte[]? thumb = null;
            if (file.GetBlob(CourseFile.ThumbnailKey)?["image"]?.GetValue<string>() is { Length: > 0 } b64)
                thumb = Convert.FromBase64String(b64);
            // Per hole, then summed: the same total as the scorecard and the maps.
            int yards = model.Holes.Sum(h => (int)Math.Round(h.TeeSetLength(Math.Max(0, h.Tees.Count - 1)) * YardsPerMetre));
            bool drawn = model.SurfaceSplines.Any(s => s.Surface is Surfaces.Green or Surfaces.Fairway);
            var kind = model.Holes.Count == 0 ? CourseKind.Blank : drawn ? CourseKind.InProgress : CourseKind.Routed;
            string name = string.IsNullOrWhiteSpace(model.Name) ? System.IO.Path.GetFileNameWithoutExtension(path) : model.Name;
            return new CourseEntry(path, name, kind, model.Holes.Count, model.Holes.Sum(h => h.Par), yards, saved, thumb);
        }
        catch (Exception ex)
        {
            return new CourseEntry(path, System.IO.Path.GetFileNameWithoutExtension(path), CourseKind.Blank, 0, 0, 0, saved, null, ex.Message);
        }
    }

    /// <summary>The name products use in their file names (the builders' rule).</summary>
    public static string FileStem(string courseName) =>
        string.Concat(courseName.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
}
