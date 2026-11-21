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

    /// <summary>
    /// The game's sun runs a circle: at noon it stands at sunOrientation, sunInclination high, and it rises 90° to
    /// the left of that bearing and sets 90° to the right, 15° an hour. Checked in game on 21 Nov 2026: a calibration
    /// plot set to 119°, 43° at 0.6725 (16:08) cast shadows 1–7° east of north, and a cliff sunk in 4 m steps
    /// shortened its shadow 3.2 m a metre, a sun 17.4° high. (Earlier releases read the orientation as the sun's
    /// bearing and the inclination as its height, which put it at 119°, 43° on that plot.)
    /// </summary>
    [Fact]
    public void GameSun_FollowsItsPathThroughTheDay_AsMeasuredInGame()
    {
        var plot = new Atmosphere(0.6725, 119, 43, 0, null, null, 0, 0, 0, false);
        var (bearing, height) = plot.Sun();
        Assert.InRange(bearing, 181, 189);        // shadows fell 1–7° (lines) to 8° east of north
        Assert.InRange(height, 15.9, 19.4);       // 17.4° measured, within 1.5°
        Assert.Equal((119.0, 43.0), (Math.Round(plot.With(0.5).Sun().Bearing, 6), Math.Round(plot.With(0.5).Sun().Height, 6)));   // noon
        Assert.Equal(29, plot.With(0.25).Sun().Bearing, 3);                // rises 90° left of the noon bearing
        Assert.Equal(0, plot.With(0.25).Sun().Height, 3);
        Assert.Equal(209, plot.With(0.75).Sun().Bearing, 3);               // sets 90° right
        Assert.True(plot.With(0.0).Sun().Height < 0, "below the horizon at midnight");

        var cassimbo = TerrainInk.SunDirection(Cassimbo)!.Value;            // 15:44, noon at 187°, 29° high
        Assert.Equal(246.6, cassimbo.Bearing, 1);
        Assert.Equal(15.9, cassimbo.Height, 1);
        Assert.False(new TerrainMapOptions().SunLight);                   // maps stay lit from the upper left unless asked
    }

    [Fact]
    public void Mowing_IsRead()
    {
        Assert.Equal(new Mowing(0, 0, 0, 0), Cassimbo.Mowing);
        Assert.Equal(new Mowing(-1, 0.7, 0, 0.05), Samples.Course("murray-estate.course").Mowing);
    }

    [Fact]
    public void Cameras_AreReadPerHole()
    {
        var cams = Cassimbo.Cameras;
        Assert.Equal(68, cams.Count);
        Assert.Equal(Enumerable.Range(1, 18), cams.Select(c => c.Hole).Distinct().Order());
        var c = Check(Cassimbo, "cameras");
        Assert.Equal(Severity.Pass, c.Severity);
        Assert.Contains("68 cameras in 23 shots over 18 holes", c.Detail);

        var jungle = Check(Samples.Course("Jungleplot2.course"), "cameras");
        Assert.Equal(Severity.Confirm, jungle.Severity);
        Assert.Equal(Enumerable.Range(2, 16), jungle.Holes);              // cameras on 1 and 18 only
        Assert.Equal(Severity.Confirm, Check(Samples.Course("sagan-khushun.course"), "cameras").Severity);
    }

    [Fact]
    public void Inventory_SortsObjectsByKind()
    {
        Assert.Equal("Buildings", ObjectInventory.KindOf("Assets/CourseProps/ModularPacks/Wood01/MP_Wood01_House05Prefab"));
        Assert.Equal("Signs", ObjectInventory.KindOf("Assets/CourseProps/Signs/OBMarkerAPrefab"));
        Assert.Equal("Trees", ObjectInventory.KindOf("Assets/Foliage/SpeedTree8/Trees/Oak_LiveMoss/Prefabs/Oak_LiveMoss_904"));
        Assert.Equal("Cameras", ObjectInventory.KindOf("Assets/CourseGen/Bundle/CameraPlaceholder"));
        Assert.Equal("Water effects", ObjectInventory.KindOf("Assets/Effects/OceanFoam/Prefabs/OceanChurnWave_UserPrefab"));
        Assert.Equal("Pot bunkers", ObjectInventory.KindOf("Assets/CourseProps/Bunkers/PotBunker01Prefab"));

        var counts = ObjectInventory.Count(Cassimbo).ToDictionary(c => c.Kind, c => c.Count);
        Assert.Equal(68, counts["Cameras"]);
        Assert.Equal(92, counts["Buildings"]);
        Assert.Contains("cameras 68", Check(Cassimbo, "objects").Detail);
    }

    [Fact]
    public void PotBunkers_AreDrawnAsBunkers()
    {
        var course = WithOob();
        var withPot = new CourseModel
        {
            Name = "Synthetic", Version = GameVersion.PGA2K25, SchemaVersion = 49, SurfaceSplines = [], HeightStamps = [], SurfaceBrushes = [],
            Holes = course.Holes, PlacedObjects = [new PlacedObject("Assets/CourseProps/Bunkers/PotBunker01Prefab", new Vec2(5, 150), 1.2)],
        };
        var disc = Assert.Single(CourseLayers.From(withPot).BunkerBrushes);
        Assert.Equal(new Vec2(5, 150), disc.Centre);
        Assert.Equal(3.0, disc.Radius, 6);
        Assert.Contains("Pot bunkers are drawn as bunkers", Check(withPot, "objects").Detail);
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
        Assert.Equal("68 on 18 holes", Value(settings, "Cameras"));
        Assert.Contains("buildings 92", Value(settings, "Placed objects"));
    }
}

static class AtmosphereTime
{
    public static Atmosphere With(this Atmosphere a, double timeOfDay) => a with { TimeOfDay = timeOfDay };
}
