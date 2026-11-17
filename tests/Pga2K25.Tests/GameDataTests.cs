using Pga2K25.CourseCard;
using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using Pga2K25.Readiness;
using Pga2K25.TerrainMap;
using Severity = Pga2K25.Readiness.Severity;

namespace Pga2K25.Tests;

/// <summary>
/// Course file data read since 3.7: out of bounds, cleared ground, playing conditions, tee markers, pars set by hand and
/// the saved scorecard's tee colours, pin sets and playoff holes; and the checks, planting layer and card that use it.
/// </summary>
[Trait("Area", "Core")]
public class GameDataTests
{
    static CourseModel Cassimbo => Samples.Course("cassimbo.course");

    static ReadinessCheck Check(CourseModel course, string id, GameMeterReadings? meters = null) =>
        ReadinessChecker.DesignerChecks(course, meters).SingleOrDefault(c => c.Id == id) ?? throw new Xunit.Sdk.XunitException($"no check {id}");

    static SplinePoint At(double x, double z) => new(new Vec2(x, z), new Vec2(x, z), new Vec2(x, z), 0);

    // A 300 m par 4 straight up +Z, and out of bounds as given.
    static CourseModel WithOob(params SurfaceSpline[] oob) => new()
    {
        Name = "Synthetic", Version = GameVersion.PGA2K25, SchemaVersion = 49, SurfaceSplines = [], HeightStamps = [], SurfaceBrushes = [], PlacedObjects = [],
        Holes = [new Hole(1, 4, [new(0, 0), new(0, 300)], [new(0, 0)], [new(0, 300)])],
        OutOfBounds = new MapEdits([], oob),
    };

    static SurfaceSpline Box(double x0, double z0, double x1, double z1) =>
        new(0, -1, true, 2.5, [At(x0, z0), At(x1, z0), At(x1, z1), At(x0, z1)]);

    [Fact]
    public void CourseFile_NewFieldsAreRead()
    {
        var c = Cassimbo;
        Assert.Equal(3, c.OutOfBounds.Splines.Count);
        Assert.All(c.OutOfBounds.Splines, s => Assert.True(s.Closed));
        Assert.Empty(c.OutOfBounds.Brushes);
        Assert.Equal(495, c.ClearDetail.Brushes.Count);
        Assert.Single(c.ClearDetail.Splines);
        Assert.True(c.ClearTrees.IsEmpty);
        Assert.Equal(0, c.TreeDensityEntries);
        Assert.Equal(0, c.TerrainHeightEntries);
        Assert.Equal("Narrow", c.TeeMarkerWidthName);
        Assert.Equal([3, 7], c.Holes.Where(h => h.CreatorPar is not null).Select(h => h.Number));
        Assert.All(c.Holes.Where(h => h.CreatorPar is not null), h => Assert.Equal(h.Par, h.CreatorPar));
        Assert.All(c.Holes, h => Assert.Equal(default, h.FlagOffset));
        Assert.All(c.Holes.SelectMany(h => h.PinDescriptions), d => Assert.Equal(0, d));
        Assert.InRange(c.Conditions.GreenFirmness, 0.85, 0.95);
        Assert.Equal(-1, c.Conditions.TerrainFirmness);

        var s = c.Stored!;
        Assert.Equal([4, 0, 3, 2], s.TeeColours);
        Assert.Equal(["Red", "Green", "White", "Blue"], s.TeeColours.Select(StoredScorecard.TeeColourName));
        Assert.Equal(4, s.PinSets);
        Assert.Equal([18, 10], s.SuddenDeathHoles);
        Assert.True(s.PlayoffsAreDefault);
        Assert.Null(s.DifficultyName);
    }

    [Fact]
    public void OutOfBounds_IsInTheMapLayers()
    {
        var layers = CourseLayers.From(Cassimbo);
        Assert.Equal(3, layers.OutOfBounds.Count);
        Assert.Equal(3, layers.RoutedOnly().OutOfBounds.Count);
        Assert.Empty(CourseLayers.From(Samples.Course("sagan-khushun.course")).OutOfBounds);
    }

    [Fact]
    public void OutOfBoundsCheck_Cassimbo_ListsTheNearHoles()
    {
        var c = Check(Cassimbo, "out-of-bounds");
        Assert.Equal(Severity.Pass, c.Severity);
        Assert.Equal([1, 11], c.Holes);
        Assert.Contains("3 out-of-bounds areas", c.Detail);
    }

    [Fact]
    public void OutOfBoundsCheck_TeeInside_Flags_LineAcross_Warns_None_Informs()
    {
        var tee = Check(WithOob(Box(-20, -20, 20, 20)), "out-of-bounds");
        Assert.Equal(Severity.Flag, tee.Severity);
        Assert.Contains("A tee inside out of bounds on 1", tee.Detail);

        var across = Check(WithOob(Box(-30, 140, 30, 160)), "out-of-bounds");
        Assert.Equal(Severity.Warning, across.Severity);
        Assert.Contains("crosses out of bounds on 1", across.Detail);

        var near = Check(WithOob(Box(20, 100, 60, 200)), "out-of-bounds");   // 20 m (22 yds) to the side
        Assert.Equal(Severity.Pass, near.Severity);
        Assert.Contains("1 (22 yds)", near.Detail);

        Assert.Equal(Severity.Info, Check(WithOob(), "out-of-bounds").Severity);
    }

    [Fact]
    public void PlayoffCheck_ConfirmsWhileTheDefaultsStand()
    {
        var c = Check(Cassimbo, "playoffs");
        Assert.Equal(Severity.Confirm, c.Severity);
        Assert.Contains("still the game's default, 18, 10", c.Detail);
        Assert.Contains("4 pin sets", c.Detail);
        Assert.Contains("Red, Green, White, Blue", c.Detail);
        Assert.Contains("Par set by hand on 3, 7", c.Detail);

        var chosen = Cassimbo;
        chosen.Stored = chosen.Stored! with { SuddenDeathHoles = [9, 18] };
        Assert.Equal(Severity.Pass, Check(chosen, "playoffs").Severity);
        chosen.Stored = Samples.Fresh("cassimbo.course").Stored;
    }

    [Fact]
    public void MetersCheck_UsesReadingsOverEstimates_AndAsksForTerrain()
    {
        var sagan = Samples.Course("sagan-khushun.course");
        var none = Check(sagan, "game-meters");
        Assert.Equal(Severity.Confirm, none.Severity);
        Assert.Contains("terrain not read yet", none.Detail);
        Assert.StartsWith("Objects ≈", none.Detail);

        var read = Check(sagan, "game-meters", new GameMeterReadings { Objects = 72.5, Memory = 62.7, Terrain = 40, ReadOn = "16 Nov" });
        Assert.Equal(Severity.Pass, read.Severity);
        Assert.StartsWith("Objects 72.5 %", read.Detail);
        Assert.Contains("terrain 40 %", read.Detail);
        Assert.Contains("Readings from 16 Nov", read.Detail);

        Assert.Equal(Severity.Warning, Check(sagan, "game-meters", new GameMeterReadings { Terrain = 92 }).Severity);
        Assert.Equal(Severity.Flag, Check(sagan, "game-meters", new GameMeterReadings { Terrain = 30, Objects = 101 }).Severity);
    }

    [Fact]
    public void BunkerCheck_UsesTheMemoryReading()
    {
        // Cassimbo's estimate is about 98 %; a reading under 85 % clears the warning.
        Assert.Equal(Severity.Warning, Check(Cassimbo, "bunker-stamping").Severity);
        var read = Check(Cassimbo, "bunker-stamping", new GameMeterReadings { Memory = 70 });
        Assert.Equal(Severity.Pass, read.Severity);
        Assert.Contains("as read in the designer", read.Detail);
    }

    [Fact]
    public void Planting_ShowsClearedGround()
    {
        var p = Planting.From(Cassimbo);
        Assert.True(p.HasCleared);
        Assert.Equal(496, p.ClearedDetail.Count);
        Assert.Empty(p.ClearedTrees);
        Assert.True(p.Has(PlantingLayers.Cleared));
        Assert.False(Planting.From(Samples.Course("brush-test-plot.course")).Has(PlantingLayers.Cleared));
        Assert.True((PlantingLayers.All & PlantingLayers.Cleared) != 0);
        var (trees, detail) = p.ClearedArea();
        Assert.Equal(0, trees);
        Assert.True(detail > 1000, $"{detail:N0} m²");
    }

    [Fact]
    public void CourseCards_NameTeesByTheGamesColours()
    {
        var data = CourseCardData.From(Cassimbo, null, null, []);
        Assert.Equal(["Red", "Green", "White", "Blue"], data.TeeNames);
        // Names given in Studio still win.
        var named = CourseCardData.From(Cassimbo, null, ["A", "B", "C", "D"], []);
        Assert.Equal(["A", "B", "C", "D"], named.TeeNames);
    }

    [Fact]
    public void CourseDetailsCard_Rows()
    {
        var data = CourseCardData.From(Cassimbo, null, null, []);
        var (players, settings) = CourseDetails.For(Cassimbo, data, 270);
        string Value(IEnumerable<DetailRow> rows, string label) => rows.Single(r => r.Label == label).Value;
        Assert.Equal("72", Value(players, "Par"));
        Assert.Equal("18", Value(players, "Holes"));
        Assert.Equal($"{data.Total.Yards[^1]:N0} yds", Value(players, "Length"));
        Assert.StartsWith("Blue ", players.Single(r => r.Label == "Length").Note);
        Assert.Equal("Not rated", Value(players, "Difficulty"));
        Assert.Equal("Stimp 10.9", Value(players, "Green speed"));
        Assert.Equal("4 pin sets", Value(players, "Pins"));
        Assert.Equal("From the W", Value(players, "Wind"));
        Assert.Equal("sudden death 18, 10", Value(settings, "Playoff holes"));
        Assert.Equal("Red, Green, White, Blue", Value(settings, "Tee colours"));
        Assert.Equal("Narrow", Value(settings, "Tee markers"));
        Assert.Equal("3, 7", Value(settings, "Par set by hand"));
        Assert.StartsWith("greens 90", Value(settings, "Firmness"));
    }
}
