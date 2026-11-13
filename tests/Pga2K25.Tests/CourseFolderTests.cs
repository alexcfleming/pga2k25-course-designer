using Pga2K25.Studio.Services;

namespace Pga2K25.Tests;

/// <summary>
/// Following a rename made in the designer: the product folder and its products take the new name, a copy with a
/// file of its own doesn't, and a rename that can't happen in full changes nothing. Everything runs in a temp folder
/// with the settings never saved.
/// </summary>
[Trait("Area", "Core")]
public sealed class CourseFolderTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), $"studio-rename-{Guid.NewGuid():N}");
    readonly string _courses;
    readonly StudioSettings _settings;

    public CourseFolderTests()
    {
        _courses = Path.Combine(_root, "Courses");
        Directory.CreateDirectory(_courses);
        _settings = new StudioSettings { OutputRoot = Path.Combine(_root, "Books"), CoursesFolder = _courses };
        Directory.CreateDirectory(_settings.OutputRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>A course file on disk (its contents don't matter here) and the library's entry for it.</summary>
    CourseEntry Course(string file, string name, string? id = null)
    {
        string path = Path.Combine(_courses, file + ".course");
        if (!File.Exists(path)) File.WriteAllBytes(path, [0]);
        return new CourseEntry(path, name, CourseKind.Routed, 18, 72, 6800, DateTime.Now, null, Id: id ?? "offlineSave" + file);
    }

    /// <summary>Some built products, a file of Studio's own and one of the designer's, in the course's folder; stamped.</summary>
    string Build(CourseEntry e)
    {
        string dir = Products.OutputDir(_settings, e.Name);
        Directory.CreateDirectory(Path.Combine(dir, $"{e.Name} - Hole Sheets"));
        foreach (var f in new[] { $"{e.Name} - Terrain Map.png", $"{e.Name} - Terrain Map.pdf", $"{e.Name} - Hole Sheets/Hole_01.png",
                                  "Notes.json", "Doc - Oct 5 2026.pdf", $"{e.Name}_-_Strategic_Concepts.pdf" })
            File.WriteAllText(Path.Combine(dir, f), f);
        CourseFolders.Stamp(_settings, e);
        return dir;
    }

    static ProductDefinition Terrain => Products.All.Single(p => p.Name == "Terrain map");

    [Fact]
    public void CourseModel_ReadsTheGamesId()
    {
        Assert.Equal("offlineSaveaw76akrqqqdi", Samples.Cassimbo.Id);
    }

    [Fact]
    public void Rename_RenamesTheFolderAndTheProductsOnly()
    {
        var before = Course("byvwcvzpqavk", "Halvoy");
        string old = Build(before);
        var after = before with { Name = "Halvoy Links" };

        Assert.Equal("Halvoy", CourseFolders.RenamedFrom(_settings, after));
        // Before the rename is followed the products are still found, under the old name.
        Assert.NotNull(Products.BuiltFile(Terrain, CourseFolders.PlaceOf(_settings, after)));

        var r = CourseFolders.Follow(_settings, after);

        Assert.NotNull(r);
        Assert.True(r.Done);
        Assert.Equal(3, r.Renamed);   // the PNG, the PDF and the Hole Sheets folder
        string dir = Path.Combine(_settings.OutputRoot, "Halvoy Links");
        Assert.False(Directory.Exists(old));
        Assert.True(File.Exists(Path.Combine(dir, "Halvoy Links - Terrain Map.png")));
        Assert.True(File.Exists(Path.Combine(dir, "Halvoy Links - Hole Sheets", "Hole_01.png")));
        Assert.Equal("Halvoy - Terrain Map.png", File.ReadAllText(Path.Combine(dir, "Halvoy Links - Terrain Map.png")));   // contents untouched
        Assert.True(File.Exists(Path.Combine(dir, "Notes.json")));
        Assert.True(File.Exists(Path.Combine(dir, "Doc - Oct 5 2026.pdf")));
        Assert.True(File.Exists(Path.Combine(dir, "Halvoy_-_Strategic_Concepts.pdf")));   // not "Halvoy - …": left alone

        Assert.Equal(new ProductPlace(dir, "Halvoy Links"), CourseFolders.PlaceOf(_settings, after));
        Assert.Equal(ProductPlace.Named(_settings, "Halvoy Links"), CourseFolders.PlaceOf(_settings, after));
        Assert.Null(CourseFolders.RenamedFrom(_settings, after));
        Assert.Null(CourseFolders.Follow(_settings, after));   // nothing more to do
        Assert.Equal("Halvoy Links", CourseFolders.LoadStamp(dir)!.Name);
    }

    [Fact]
    public void ACopyInAFileOfItsOwn_IsANewCourse()
    {
        var original = Course("byvwcvzpqavk", "Halvoy");
        string dir = Build(original);
        // Copied in Explorer: the same id inside, but another file, and renamed in the game.
        var copy = Course("byvwcvzpqavk - Copy", "Halvoy Two", id: original.Id);

        Assert.Null(CourseFolders.Follow(_settings, copy));
        Assert.Equal(ProductPlace.Named(_settings, "Halvoy Two"), CourseFolders.PlaceOf(_settings, copy));
        Assert.True(Directory.Exists(dir));
        Assert.Equal(new ProductPlace(dir, "Halvoy"), CourseFolders.PlaceOf(_settings, original));
    }

    [Fact]
    public void ACopyWithTheSameName_SharesTheFolderWithoutTakingIt()
    {
        var original = Course("aaa", "Cassimbo");
        string dir = Build(original);
        var copy = Course("bbb", "Cassimbo");

        CourseFolders.Stamp(_settings, copy);

        Assert.Equal(original.Path, CourseFolders.LoadStamp(dir)!.Path);
        Assert.Equal(dir, CourseFolders.PlaceOf(_settings, copy).Dir);
    }

    [Fact]
    public void ATakenName_ChangesNothing_AndOffersAFolderOfItsOwn()
    {
        var other = Course("murray", "Murray Estate");
        string theirs = Build(other);
        var mine = Course("reboot", "Murray Estate Reboot Dunes");
        string old = Build(mine);
        var renamed = mine with { Name = "Murray Estate" };

        var r = CourseFolders.Follow(_settings, renamed)!;

        Assert.False(r.Done);
        Assert.Equal(other.Path, r.TakenBy);
        Assert.Equal("Murray Estate (2)", r.SuggestedFolder);
        Assert.True(File.Exists(Path.Combine(old, "Murray Estate Reboot Dunes - Terrain Map.png")));
        Assert.Equal(new ProductPlace(old, "Murray Estate Reboot Dunes"), CourseFolders.PlaceOf(_settings, renamed));

        var done = CourseFolders.Follow(_settings, renamed, r.SuggestedFolder)!;

        Assert.True(done.Done);
        string dir = Path.Combine(_settings.OutputRoot, "Murray Estate (2)");
        Assert.True(File.Exists(Path.Combine(dir, "Murray Estate - Terrain Map.png")));
        Assert.Equal(new ProductPlace(dir, "Murray Estate"), CourseFolders.PlaceOf(_settings, renamed));
        Assert.Equal(new ProductPlace(theirs, "Murray Estate"), CourseFolders.PlaceOf(_settings, other));
        Assert.Null(CourseFolders.Follow(_settings, renamed));
    }

    [Fact]
    public void AnEmptyFolderOfTheNewName_IsReplaced()
    {
        var before = Course("x1", "Halvoy");
        Build(before);
        Directory.CreateDirectory(Path.Combine(_settings.OutputRoot, "Halvoy Links"));

        Assert.True(CourseFolders.Follow(_settings, before with { Name = "Halvoy Links" })!.Done);
        Assert.True(File.Exists(Path.Combine(_settings.OutputRoot, "Halvoy Links", "Halvoy Links - Terrain Map.pdf")));
    }

    [Fact]
    public void AFileOpenElsewhere_ChangesNothing()
    {
        var before = Course("x2", "Halvoy");
        string old = Build(before);
        var after = before with { Name = "Halvoy Links" };

        RenameResult r;
        using (new FileStream(Path.Combine(old, "Halvoy - Terrain Map.pdf"), FileMode.Open, FileAccess.Read, FileShare.Read))
            r = CourseFolders.Follow(_settings, after)!;

        Assert.False(r.Done);
        Assert.Equal(["Halvoy - Terrain Map.pdf"], r.InUse.Select(Path.GetFileName));
        Assert.Null(r.SuggestedFolder);
        Assert.True(File.Exists(Path.Combine(old, "Halvoy - Terrain Map.png")));
        Assert.False(Directory.Exists(Path.Combine(_settings.OutputRoot, "Halvoy Links")));

        Assert.True(CourseFolders.Follow(_settings, after)!.Done);   // closed: Try again works
    }

    [Fact]
    public void ACaseOnlyRename_IsFollowed()
    {
        var before = Course("x3", "halvoy");
        Build(before);

        Assert.True(CourseFolders.Follow(_settings, before with { Name = "Halvoy" })!.Done);
        Assert.Contains("Halvoy", Directory.GetDirectories(_settings.OutputRoot).Select(Path.GetFileName));
        Assert.True(File.Exists(Path.Combine(_settings.OutputRoot, "Halvoy", "Halvoy - Terrain Map.png")));
        Assert.Contains("Halvoy - Terrain Map.png", Directory.GetFiles(Path.Combine(_settings.OutputRoot, "Halvoy")).Select(Path.GetFileName));
    }

    [Fact]
    public void Undo_PutsTheNamesBack_AndTheRenamedCourseStartsAfresh()
    {
        var before = Course("x4", "Halvoy");
        string old = Build(before);
        var after = before with { Name = "Halvoy Links" };
        var done = CourseFolders.Follow(_settings, after)!;

        var back = CourseFolders.Undo(_settings, after, done);

        Assert.True(back.Done);
        Assert.True(File.Exists(Path.Combine(old, "Halvoy - Terrain Map.png")));
        Assert.True(CourseFolders.LoadStamp(old)!.Released);
        Assert.Equal(ProductPlace.Named(_settings, "Halvoy Links"), CourseFolders.PlaceOf(_settings, after));
        Assert.Null(CourseFolders.Follow(_settings, after));   // not renamed again on the next open
        // The released folder is offered back by Find earlier products.
        Assert.Equal(["Halvoy"], CourseFolders.Unclaimed(_settings, [after]).Select(f => f.Name));
    }

    [Fact]
    public void FindEarlierProducts_ListsOnlyFoldersNoCourseClaims_AndAdoptRenamesOne()
    {
        // Renamed before 2.9: an unstamped folder under the old name.
        string earlier = Path.Combine(_settings.OutputRoot, "Wakinyan Dunes");
        Directory.CreateDirectory(earlier);
        File.WriteAllText(Path.Combine(earlier, "Wakinyan Dunes - Greens Book.pdf"), "x");
        File.WriteAllText(Path.Combine(earlier, "Measurements.csv"), "x");
        var other = Course("other", "Cassimbo");
        Build(other);
        Directory.CreateDirectory(Path.Combine(_settings.OutputRoot, "Empty"));
        var mine = Course("wak", "Wakinyan Tournament");

        var found = CourseFolders.Unclaimed(_settings, [other, mine]);

        var f = Assert.Single(found);   // not Cassimbo's, not the empty one
        Assert.Equal("Wakinyan Dunes", f.Name);
        Assert.Equal(2, f.Files);

        var r = CourseFolders.Adopt(_settings, mine, f);

        Assert.True(r.Done);
        string dir = Path.Combine(_settings.OutputRoot, "Wakinyan Tournament");
        Assert.True(File.Exists(Path.Combine(dir, "Wakinyan Tournament - Greens Book.pdf")));
        Assert.True(File.Exists(Path.Combine(dir, "Measurements.csv")));
        Assert.Equal(mine.Path, CourseFolders.LoadStamp(dir)!.Path);
        Assert.Empty(CourseFolders.Unclaimed(_settings, [other, mine]));
    }

    [Fact]
    public void Adopt_IsRefused_WhenTheCourseHasProductsOfItsOwn()
    {
        string earlier = Path.Combine(_settings.OutputRoot, "Old Name");
        Directory.CreateDirectory(earlier);
        File.WriteAllText(Path.Combine(earlier, "Old Name - Greens Book.pdf"), "x");
        var mine = Course("c", "New Name");
        Build(mine);

        var r = CourseFolders.Adopt(_settings, mine, CourseFolders.Unclaimed(_settings, [mine]).Single());

        Assert.False(r.Done);
        Assert.Contains("already has products", r.Error);
        Assert.True(File.Exists(Path.Combine(earlier, "Old Name - Greens Book.pdf")));
    }

    [Fact]
    public void ACardSettingsFileInTheFolder_MovesWithIt()
    {
        var before = Course("x5", "Halvoy");
        string old = Build(before);
        _settings.ExtrasFor(before.Path).CardConfig = Path.Combine(old, "Halvoy - card.json");
        File.WriteAllText(_settings.ExtrasFor(before.Path).CardConfig!, "{}");

        CourseFolders.Follow(_settings, before with { Name = "Halvoy Links" });

        string moved = Path.Combine(_settings.OutputRoot, "Halvoy Links", "Halvoy Links - card.json");
        Assert.Equal(moved, _settings.ExtrasFor(before.Path).CardConfig);
        Assert.True(File.Exists(moved));
    }

    [Fact]
    public void AGameFileThatMoved_StillFindsItsFolderByItsId()
    {
        var before = Course("x6", "Halvoy");
        string old = Build(before);
        File.Delete(before.Path);
        var moved = before with { Path = Path.Combine(_root, "elsewhere", "x6.course"), Name = "Halvoy Links" };

        Assert.True(CourseFolders.Follow(_settings, moved)!.Done);
        Assert.False(Directory.Exists(old));
        Assert.Equal(moved.Path, CourseFolders.LoadStamp(Path.Combine(_settings.OutputRoot, "Halvoy Links"))!.Path);
    }
}
