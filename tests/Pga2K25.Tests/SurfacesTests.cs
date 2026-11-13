using Pga2K25.CourseFormat;
using Pga2K25.Studio.Services;

namespace Pga2K25.Tests;

/// <summary>
/// The terrain map's Surfaces card: theme, materials, terrain painting and weathering read from the course file, with
/// the slots matched to the designer's own lists (Create Surfaces, Terrain Painting, Surface Weathering).
/// </summary>
[Trait("Area", "Core")]
public class SurfacesTests
{
    [Theory]
    [InlineData("Fairway43_RenaissanceClub", "Fairway 43", "Renaissance Club")]
    [InlineData("LightRough27_EastLakeGC", "Light Rough 27", "East Lake GC")]
    [InlineData("Splat1_TPC_Sawgrass", "Splat 1", "TPC Sawgrass")]
    [InlineData("HeavyRough38_PaynesValley", "Heavy Rough 38", "Paynes Valley")]
    [InlineData("Gravel05", "Gravel 05", null)]
    [InlineData("SandPebbles01", "Sand Pebbles 01", null)]
    [InlineData("Asphalt01B", "Asphalt 01 B", null)]
    [InlineData("Fringe58_Nordic", "Fringe 58", "Nordic")]
    public void MaterialNames_SplitIntoMaterialAndSource(string name, string material, string? source)
    {
        Assert.Equal((material, source), CourseSurfaces.Split(name));
    }

    [Fact]
    public void Cassimbo_ReadsEverySlotWeatheringAndPaint()
    {
        var look = Samples.Cassimbo.Look;
        Assert.Equal(2, look.Theme);
        Assert.Equal("Backdrop 6", look.BackdropLabel);
        Assert.Equal("Sand56_FriscoGC", look.Materials[CourseSurfaces.Bunker].Name);
        Assert.Equal(1.0, look.Materials[CourseSurfaces.Bunker].Blend, 3);
        Assert.Equal("Fairway43_RenaissanceClub", look.Materials[CourseSurfaces.Fairway].Name);
        Assert.Equal("Splat3_Desert", look.Materials[CourseSurfaces.Bedrock].Name);
        Assert.True(look.Materials[CourseSurfaces.Path3].IsThemeDefault);
        Assert.Equal("Fairway62_RoyalBirkdale", look.Weathered[CourseSurfaces.Fairway].Name);
        Assert.True(look.Weathered[CourseSurfaces.Bunker].IsThemeDefault);
        Assert.Equal(1.0, look.WeatheringEdgeBlend);

        var (heading, summary, groups) = SurfacesSummary.Build(Samples.Cassimbo);

        Assert.Equal("Theme 2 · Backdrop 6", heading);
        Assert.Equal("Theme 2 · 16 of 19 chosen · weathered", summary);   // 12 of the 15 slots (③ ④ ⑤ are the theme's) + 4 weathered grasses
        Assert.Equal(["Create surfaces", "Terrain painting", "Surface weathering"], groups.Select(g => g.Title));
        var create = groups[0].Rows;
        Assert.Contains(create, r => r.Label == "Fairway" && r.Value == "Fairway 43" && r.Source == "Renaissance Club");
        Assert.Contains(create, r => r.Label == "Path ③ ④ ⑤" && r.IsDefault);   // the theme's own, folded into one line
        var painting = groups[1].Rows;
        Assert.Equal(["Grass / topsoil", "Subsoil A", "Subsoil B", "Bedrock"], painting.Select(r => r.Label));
        Assert.Equal(["36 strokes", "457 strokes", "20 strokes", "31 strokes"], painting.Select(r => r.Detail));
        var weathering = groups[2].Rows;
        Assert.Equal(["Green", "Fairway", "Light rough", "Heavy rough", "Painted"], weathering.Select(r => r.Label));
        Assert.Equal("fringe Fringe 46", weathering[0].Detail);
        Assert.Equal(("431 strokes", "edge blending 100"), (weathering[^1].Value, weathering[^1].Detail));
    }

    [Fact]
    public void ACourseOnItsThemesDefaults_SaysSoInAFewLines()
    {
        var course = Samples.Course("ps0-calibration.course");
        var (heading, summary, groups) = SurfacesSummary.Build(course);

        Assert.Equal("Theme 5 · Backdrop 1", heading);
        Assert.Equal("Theme 5 · all theme defaults", summary);
        Assert.Equal(2, groups[0].Rows.Count);   // the grasses and bunker in one line, the paths in another
        Assert.All(groups[0].Rows, r => Assert.Equal(SurfacesSummary.ThemeDefault, r.Value));
        var layers = Assert.Single(groups[1].Rows);
        Assert.Equal(("All four layers", "none painted"), (layers.Label, layers.Detail));
    }

    [Theory]
    [InlineData("murray-estate.course", "Highlands")]
    [InlineData("deltatest.course", "Delta")]
    [InlineData("Jungleplot2.course", "Tropical")]
    [InlineData("sagan-khushun.course", "Fjordlands")]
    [InlineData("cassimbo.course", "Theme 2")]   // not matched yet
    public void Themes_ShowTheDesignersNames(string file, string label)
    {
        Assert.Equal(label, Samples.Course(file).Look.ThemeLabel);
    }

    [Fact]
    public void WeatheringEdgeBlending_ReadsAsTheDesignerShowsIt()
    {
        // Murray Estate: edge blending 85 in the designer, nothing weathered or painted.
        var groups = SurfacesSummary.Build(Samples.Course("murray-estate.course")).Groups;
        var painted = groups[2].Rows[^1];
        Assert.Equal(("none", "edge blending 85"), (painted.Value, painted.Detail));
        Assert.Equal("All", groups[2].Rows[0].Label);
    }

    [Fact]
    public void NoteKeys_AreTheMaterialOrTheThemesLine()
    {
        var groups = SurfacesSummary.Build(Samples.Cassimbo).Groups;
        var rows = groups.SelectMany(g => g.Rows).ToList();
        Assert.Equal("Splat1_TPC_Sawgrass", rows.Single(r => r.Label == "Subsoil A").Key);
        Assert.Equal("theme 2 · Create surfaces · Path ③ ④ ⑤", rows.Single(r => r.Label == "Path ③ ④ ⑤").Key);
        Assert.Equal("", groups[2].Rows[^1].Key);   // "Painted" takes the group's note instead
    }

    [Fact]
    public void MaterialNotes_FollowTheMaterialAndListTheCourses()
    {
        string path = Path.Combine(Path.GetTempPath(), $"surface-notes-{Guid.NewGuid():N}.json");
        try
        {
            var notes = new MaterialNotes();
            notes.Set("Splat1_TPC_Sawgrass", "  Washes out under bedrock.  ", "Cassimbo", new DateTime(2026, 11, 12));
            Assert.True(notes.SeenOn("Splat1_TPC_Sawgrass", "Berwick Coast"));
            Assert.False(notes.SeenOn("Splat1_TPC_Sawgrass", "cassimbo"));   // already listed
            Assert.False(notes.SeenOn("Splat2_Desert", "Cassimbo"));          // no note, nothing to list
            notes.Save(path);

            var back = MaterialNotes.Load(path);
            Assert.Equal("Washes out under bedrock.", back.Text("Splat1_TPC_Sawgrass"));
            Assert.Equal(["Cassimbo", "Berwick Coast"], back.Notes["Splat1_TPC_Sawgrass"].Courses);

            back.Set("Splat1_TPC_Sawgrass", " ", "Cassimbo", DateTime.Now);   // blank removes it
            Assert.Null(back.Text("Splat1_TPC_Sawgrass"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CourseSurfaceNotes_KeepLineAndGroupNotesBesideTheProducts()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"surface-notes-{Guid.NewGuid():N}");
        try
        {
            var notes = new CourseSurfaceNotes();
            CourseSurfaceNotes.Put(notes.Lines, "Fairway43_RenaissanceClub", "Only here: mown in stripes.");
            CourseSurfaceNotes.Put(notes.Groups, "Terrain painting", "Subsoil B at 0.2 softens the topsoil line.");
            notes.Save(dir);

            Assert.True(File.Exists(Path.Combine(dir, CourseSurfaceNotes.FileName)));
            var back = CourseSurfaceNotes.Load(dir);
            Assert.Equal(2, back.Count);
            Assert.Equal("Subsoil B at 0.2 softens the topsoil line.", back.Groups["Terrain painting"]);
            CourseSurfaceNotes.Put(back.Lines, "Fairway43_RenaissanceClub", "");
            Assert.Empty(back.Lines);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
