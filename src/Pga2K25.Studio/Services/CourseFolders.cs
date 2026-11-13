using System.Text.Json;

namespace Pga2K25.Studio.Services;

/// <summary>Where a course's products are, and the course name their file names start with.</summary>
public sealed record ProductPlace(string Dir, string Stem)
{
    /// <summary>The plain rule: a folder named after the course, its files starting with the same name.</summary>
    public static ProductPlace Named(StudioSettings settings, string courseName)
    {
        string stem = CourseLibrary.FileStem(courseName);
        return new ProductPlace(Path.Combine(settings.OutputRoot, stem), stem);
    }
}

/// <summary>
/// Course.json in a product folder: which course file the folder belongs to, and the course name its products were
/// built under. <see cref="Released"/> marks a folder handed back by Undo, which no course claims any more.
/// </summary>
public sealed record CourseStamp(string Id, string Path, string Name, bool Released = false);

/// <summary>A product folder and its stamp (null when Studio hasn't stamped it).</summary>
public sealed record StampedFolder(string Dir, CourseStamp? Stamp);

/// <summary>A product folder no course in the library claims: offered by "Find earlier products".</summary>
public sealed record EarlierFolder(string Dir, string Name, int Files, DateTime LastWrite);

public enum RenameOutcome { Renamed, Blocked }

/// <summary>What a rename did, or why it didn't happen (nothing is changed when it is blocked).</summary>
public sealed record RenameResult(RenameOutcome Outcome, string OldName, string NewName, string FromDir, string ToDir, int Renamed,
    IReadOnlyList<string> InUse, string? TakenBy = null, string? SuggestedFolder = null, string? Error = null)
{
    public bool Done => Outcome == RenameOutcome.Renamed;
}

/// <summary>
/// Follows a course when it is renamed in the designer. The product folder is named after the course, so a rename
/// used to leave everything in the old folder. A course is known by its file (path and the game's id, both kept by a
/// rename; a copy saved as a new file has its own), recorded in Course.json in its folder. When the name changes,
/// the folder and every file or folder in it named "&lt;old name&gt; - …" are renamed. Nothing is moved, copied or rebuilt,
/// the files' contents are untouched, and a rename happens in full or not at all.
/// </summary>
public static class CourseFolders
{
    public const string StampFile = "Course.json";
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Every folder under the output root with its stamp.</summary>
    public static List<StampedFolder> Read(string outputRoot)
    {
        if (!Directory.Exists(outputRoot)) return [];
        return Directory.GetDirectories(outputRoot).Select(d => new StampedFolder(d, LoadStamp(d))).ToList();
    }

    public static CourseStamp? LoadStamp(string dir)
    {
        string f = Path.Combine(dir, StampFile);
        if (!File.Exists(f)) return null;
        try { return JsonSerializer.Deserialize<CourseStamp>(File.ReadAllText(f), Json); }
        catch (Exception) { return null; }
    }

    static void SaveStamp(string dir, CourseStamp stamp) =>
        File.WriteAllText(Path.Combine(dir, StampFile), JsonSerializer.Serialize(stamp, Json));

    /// <summary>
    /// The stamp is this course's: the same file, with the same id when both have one. A file the game has moved
    /// (a different path) still matches on its id, as long as nothing is at the old path any more.
    /// </summary>
    public static bool Claims(CourseStamp s, CourseEntry e)
    {
        if (s.Released) return false;
        bool idsAgree = s.Id.Length == 0 || e.Id.Length == 0 || s.Id == e.Id;
        if (string.Equals(s.Path, e.Path, StringComparison.OrdinalIgnoreCase)) return idsAgree;
        return e.Id.Length > 0 && s.Id == e.Id && !File.Exists(s.Path);
    }

    static StampedFolder? StampedFor(CourseEntry e, IEnumerable<StampedFolder> folders)
    {
        var mine = folders.Where(f => f.Stamp is { } s && Claims(s, e)).ToList();
        if (mine.Count <= 1) return mine.FirstOrDefault();
        // Two folders claim it (one copied by hand): the one named after the course, else the newest.
        string stem = CourseLibrary.FileStem(e.Name);
        return mine.FirstOrDefault(f => string.Equals(Path.GetFileName(f.Dir), stem, StringComparison.OrdinalIgnoreCase))
            ?? mine.OrderByDescending(f => Directory.GetLastWriteTime(f.Dir)).First();
    }

    /// <summary>
    /// Where the course's products are now: its stamped folder (under the name they were built with, until the rename
    /// is followed), else the folder named after the course.
    /// </summary>
    public static ProductPlace PlaceOf(StudioSettings settings, CourseEntry e, IEnumerable<StampedFolder>? folders = null)
    {
        var mine = StampedFor(e, folders ?? Read(settings.OutputRoot));
        return mine is null ? ProductPlace.Named(settings, e.Name) : new ProductPlace(mine.Dir, CourseLibrary.FileStem(mine.Stamp!.Name));
    }

    /// <summary>The name the products were built under when the course has been renamed since; null when it hasn't.</summary>
    public static string? RenamedFrom(StudioSettings settings, CourseEntry e, IEnumerable<StampedFolder>? folders = null)
    {
        var mine = StampedFor(e, folders ?? Read(settings.OutputRoot));
        return mine?.Stamp is { } s && CourseLibrary.FileStem(s.Name) != CourseLibrary.FileStem(e.Name) ? s.Name : null;
    }

    /// <summary>
    /// Stamps the course's folder when it has one and no other course has claimed it. A folder another course's file
    /// has stamped (two files with one name) is shared as before and left alone.
    /// </summary>
    public static void Stamp(StudioSettings settings, CourseEntry e)
    {
        var place = PlaceOf(settings, e);
        if (!Directory.Exists(place.Dir)) return;
        var current = LoadStamp(place.Dir);
        if (current is not null && !current.Released && !Claims(current, e) && File.Exists(current.Path)) return;
        var stamp = new CourseStamp(e.Id, e.Path, current is { Released: false } c && Claims(c, e) ? c.Name : e.Name);
        if (current != stamp)
            try { SaveStamp(place.Dir, stamp); } catch (Exception) { /* a read-only folder still works, unstamped */ }
    }

    /// <summary>
    /// Renames the course's stamped folder and its products from the name they were built under to the course's
    /// name now (or to <paramref name="folderName"/>, for a "(2)" folder when the name is taken). Null when there is
    /// nothing to follow.
    /// </summary>
    public static RenameResult? Follow(StudioSettings settings, CourseEntry e, string? folderName = null)
    {
        var mine = StampedFor(e, Read(settings.OutputRoot));
        if (mine?.Stamp is not { } stamp) return null;
        string oldStem = CourseLibrary.FileStem(stamp.Name), newStem = CourseLibrary.FileStem(e.Name);
        if (oldStem == newStem && folderName is null) return null;
        string to = Path.Combine(settings.OutputRoot, folderName ?? newStem);
        var result = Move(settings, mine.Dir, oldStem, to, newStem, stamp.Name, e.Name);
        if (result.Done)
        {
            SaveStamp(to, stamp with { Name = e.Name, Id = e.Id.Length > 0 ? e.Id : stamp.Id, Path = e.Path });
            MoveCardConfig(settings, e, mine.Dir, to, oldStem, newStem);
        }
        return result;
    }

    /// <summary>
    /// Puts a followed rename back: the folder and files take the old name again and the folder is released, so the
    /// renamed course starts a fresh folder of its own (listed by "Find earlier products" if wanted back).
    /// </summary>
    public static RenameResult Undo(StudioSettings settings, CourseEntry e, RenameResult done)
    {
        var result = Move(settings, done.ToDir, CourseLibrary.FileStem(done.NewName), done.FromDir, CourseLibrary.FileStem(done.OldName),
            done.NewName, done.OldName);
        if (result.Done)
        {
            SaveStamp(done.FromDir, new CourseStamp(e.Id, e.Path, done.OldName, Released: true));
            MoveCardConfig(settings, e, done.ToDir, done.FromDir, CourseLibrary.FileStem(done.NewName), CourseLibrary.FileStem(done.OldName));
        }
        return result;
    }

    /// <summary>
    /// Product folders no course in <paramref name="library"/> claims: released by Undo, stamped by a file that has
    /// gone, or never stamped and not named after any course. Newest first.
    /// </summary>
    public static List<EarlierFolder> Unclaimed(StudioSettings settings, IReadOnlyList<CourseEntry> library)
    {
        var folders = Read(settings.OutputRoot);
        var named = library.Select(c => CourseLibrary.FileStem(c.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var placed = library.Select(c => PlaceOf(settings, c, folders).Dir).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = new List<EarlierFolder>();
        foreach (var f in folders)
        {
            string name = Path.GetFileName(f.Dir);
            bool free = f.Stamp switch
            {
                null => !named.Contains(name),
                { Released: true } => true,
                { } s => !File.Exists(s.Path) && !library.Any(c => Claims(s, c)),
            };
            if (!free || placed.Contains(f.Dir)) continue;
            var files = Directory.EnumerateFiles(f.Dir, "*", SearchOption.AllDirectories).Where(x => Path.GetFileName(x) != StampFile).ToList();
            if (files.Count == 0) continue;
            list.Add(new EarlierFolder(f.Dir, f.Stamp?.Name ?? name, files.Count, files.Max(File.GetLastWriteTime)));
        }
        return list.OrderByDescending(x => x.LastWrite).ToList();
    }

    /// <summary>
    /// Takes an earlier folder as this course's: renamed to the course's name, its products with it, and stamped.
    /// Blocked when the course already has products of its own.
    /// </summary>
    public static RenameResult Adopt(StudioSettings settings, CourseEntry e, EarlierFolder earlier)
    {
        var place = PlaceOf(settings, e);
        string newStem = CourseLibrary.FileStem(e.Name);
        string to = Path.Combine(settings.OutputRoot, newStem);
        if (!string.Equals(place.Dir, earlier.Dir, StringComparison.OrdinalIgnoreCase) && HasContent(place.Dir))
            return new RenameResult(RenameOutcome.Blocked, earlier.Name, e.Name, earlier.Dir, place.Dir, 0, [],
                Error: $"This course already has products in “{Path.GetFileName(place.Dir)}”. Move or delete them first.");
        if (Directory.Exists(place.Dir) && !string.Equals(place.Dir, earlier.Dir, StringComparison.OrdinalIgnoreCase))
            Directory.Delete(place.Dir, true); // only a stamp in it
        var result = Move(settings, earlier.Dir, CourseLibrary.FileStem(earlier.Name), to, newStem, earlier.Name, e.Name);
        if (result.Done) SaveStamp(to, new CourseStamp(e.Id, e.Path, e.Name));
        return result;
    }

    /// <summary>A folder holding anything besides its stamp.</summary>
    static bool HasContent(string dir) =>
        Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any(x => Path.GetFileName(x) != StampFile);

    /// <summary>"Murray Estate (2)", "(3)"… the first folder name not in use.</summary>
    public static string FreeFolderName(string outputRoot, string stem)
    {
        for (int n = 2; ; n++)
            if (!Directory.Exists(Path.Combine(outputRoot, $"{stem} ({n})"))) return $"{stem} ({n})";
    }

    /// <summary>The products a rename would touch: top-level files and folders named "&lt;stem&gt; - …".</summary>
    public static List<string> NamedEntries(string dir, string stem) =>
        !Directory.Exists(dir) ? [] : Directory.EnumerateFileSystemEntries(dir)
            .Where(x => Path.GetFileName(x).StartsWith(stem + " - ", StringComparison.Ordinal))
            .Order(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Files under <paramref name="dir"/> another program has open (a OneDrive file not on this PC is skipped, not downloaded).</summary>
    public static List<string> FilesInUse(string dir)
    {
        const FileAttributes RecallOnOpen = (FileAttributes)0x40000, RecallOnDataAccess = (FileAttributes)0x400000;
        var busy = new List<string>();
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try
            {
                if ((File.GetAttributes(f) & (RecallOnOpen | RecallOnDataAccess | FileAttributes.Offline)) != 0) continue;
                using var _ = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (IOException) { busy.Add(f); }
            catch (UnauthorizedAccessException) { busy.Add(f); }
        }
        return busy;
    }

    static RenameResult Move(StudioSettings settings, string from, string oldStem, string to, string newStem, string oldName, string newName)
    {
        RenameResult Blocked(IReadOnlyList<string>? inUse = null, string? takenBy = null, string? suggest = null, string? error = null) =>
            new(RenameOutcome.Blocked, oldName, newName, from, to, 0, inUse ?? [], takenBy, suggest, error);

        if (!Directory.Exists(from)) return Blocked(error: $"The folder “{Path.GetFileName(from)}” isn't there any more.");
        bool sameDir = string.Equals(from, to, StringComparison.OrdinalIgnoreCase);
        if (!sameDir && Directory.Exists(to))
        {
            if (HasContent(to))
            {
                var owner = LoadStamp(to);
                return Blocked(takenBy: owner is { Released: false } && File.Exists(owner.Path) ? owner.Path : "",
                    suggest: FreeFolderName(settings.OutputRoot, newStem));
            }
        }
        var inUse = FilesInUse(from);
        if (inUse.Count > 0) return Blocked(inUse: inUse);

        var undo = new Stack<(string Now, string Was, bool IsDir)>();
        try
        {
            if (!sameDir && Directory.Exists(to)) Directory.Delete(to, true); // empty but for a stamp
            if (!string.Equals(from, to, StringComparison.Ordinal))
            {
                if (sameDir) // a change of case only: Windows needs a step through another name
                {
                    string step = from + ".renaming";
                    Directory.Move(from, step);
                    undo.Push((step, from, true));
                    Directory.Move(step, to);
                    undo.Pop();
                }
                else Directory.Move(from, to);
                undo.Push((to, from, true));
            }
            int count = 0;
            if (oldStem != newStem)
                foreach (var entry in NamedEntries(to, oldStem))
                {
                    string target = Path.Combine(to, newStem + Path.GetFileName(entry)[oldStem.Length..]);
                    if (string.Equals(entry, target, StringComparison.Ordinal)) continue;
                    bool isDir = Directory.Exists(entry);
                    if (string.Equals(entry, target, StringComparison.OrdinalIgnoreCase))
                    {
                        string step = entry + ".renaming";
                        if (isDir) Directory.Move(entry, step); else File.Move(entry, step);
                        if (isDir) Directory.Move(step, target); else File.Move(step, target);
                    }
                    else if (isDir) Directory.Move(entry, target);
                    else File.Move(entry, target);
                    undo.Push((target, entry, isDir));
                    count++;
                }
            return new RenameResult(RenameOutcome.Renamed, oldName, newName, from, to, count, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Put back what was done, newest first, so the folder isn't left half one name and half the other.
            while (undo.Count > 0)
            {
                var (now, was, isDir) = undo.Pop();
                try { if (isDir) Directory.Move(now, was); else File.Move(now, was); } catch (Exception) { /* best effort */ }
            }
            return Blocked(error: ex.Message);
        }
    }

    /// <summary>
    /// A course-card settings file kept inside the product folder moves with it (renamed too when its name started
    /// with the course's). The caller saves the settings.
    /// </summary>
    static void MoveCardConfig(StudioSettings settings, CourseEntry e, string from, string to, string oldStem, string newStem)
    {
        if (!settings.Courses.TryGetValue(e.Path, out var extras) || extras.CardConfig is not { } cfg) return;
        string prefix = from.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!cfg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
        string rest = cfg[prefix.Length..];
        if (!rest.Contains(Path.DirectorySeparatorChar) && rest.StartsWith(oldStem + " - ", StringComparison.Ordinal))
            rest = newStem + rest[oldStem.Length..];
        extras.CardConfig = Path.Combine(to, rest);
    }
}
