using Pga2K25.CourseFormat;
using Pga2K25.TerrainMap;
using SkiaSharp;

namespace Pga2K25.Tests;

public class PlantingTests
{
    static readonly Lazy<CourseModel> Murray = new(() => CourseFile.Load(Path.Combine(TestPaths.SamplesDir, "murray-estate.course")).ReadModel());
    static readonly Lazy<CourseModel> Cassimbo = new(() => CourseFile.Load(Path.Combine(TestPaths.SamplesDir, "cassimbo.course")).ReadModel());

    [Theory]
    [InlineData("Assets/Foliage/SpeedTree8/Trees/Birch_River/Prefabs/Birch_River_004", "Birch River", PlantKind.Tree, TreeForm.Broadleaf)]
    [InlineData("Assets/Foliage/SpeedTree/Trees/Eastern_White_Pine/Prefabs/Eastern_White_Pine_Desktop02", "Eastern White Pine", PlantKind.Tree, TreeForm.Conifer)]
    [InlineData("Assets/Foliage/SpeedTree8/Trees/Larch_European/Prefabs/Larch_European_002", "Larch European", PlantKind.Tree, TreeForm.Conifer)]
    [InlineData("Assets/Foliage/SpeedTree/Bushes/AfghanBushes/Prefabs/AfghanBush03", "Afghan Bushes", PlantKind.Bush, TreeForm.Broadleaf)]
    [InlineData("Assets/Foliage/SpeedTree8/Grass/Prefabs/Grass_Rough_FA_001", "Grass", PlantKind.Grass, TreeForm.Broadleaf)]
    [InlineData("Assets/CourseGen/Detail/Grass/DetailGrassClusterBPrefab", "Grass", PlantKind.Grass, TreeForm.Broadleaf)]
    [InlineData("Assets/Foliage/SpeedTree/Plants/California_Poppy/Prefabs/Poppy_01", "California Poppy", PlantKind.Flower, TreeForm.Broadleaf)]
    public void Species_ComeFromTheAssetPath(string path, string name, PlantKind kind, TreeForm form) =>
        Assert.Equal(new PlantSpecies(name, kind, form), PlantSpecies.Of(path));

    [Theory]
    [InlineData("Assets/CourseProps/Equipment/RangeDividerPrefab")]
    [InlineData("Assets/CourseGen/Detail/Walls/WoodFencesAPostAPrefab")]
    [InlineData("Assets/CourseGen/Detail/Rocks/RockAPrefab")]
    public void Props_AreNotPlants(string path) => Assert.Null(PlantSpecies.Of(path));

    [Fact]
    public void PlantedArea_EstimatesAreaTimesFill()
    {
        var a = new PlacedArea("x", [new(0, 0), new(100, 0), new(100, 100), new(0, 100)], 0.0075);
        Assert.Equal(10_000, a.AreaSquareMetres, 6);
        Assert.Equal(75, a.EstimatedCount);
    }

    [Fact]
    public void Murray_HasFourPineAreas_AndItsWalls()
    {
        var p = Planting.From(Murray.Value);
        Assert.Equal(4, p.Areas.Count);
        Assert.All(p.Areas, a => Assert.Equal("Eastern White Pine", a.Species.Name));
        var (placed, scattered) = p.Count(PlantKind.Tree);
        Assert.Equal(0, placed);
        Assert.InRange(scattered, 360, 380);   // 49,371 m² at 0.0075 per m²
        Assert.Equal(6, p.Walls.Count);
        Assert.Equal(10, p.Count(PlantKind.Bush).Placed);
    }

    [Fact]
    public void Cassimbo_ScatterGroupsCountTheirInstances()
    {
        var p = Planting.From(Cassimbo.Value);
        Assert.Equal(2102, p.Groups.Count(g => g.Species.Kind == PlantKind.Grass));
        Assert.Equal((3540, 258_553), p.Count(PlantKind.Grass));
        Assert.Equal((36, 317), p.Count(PlantKind.Flower));
        // Cassimbo's walls and fences are all object paths; native water would be one too, and isn't a wall.
        Assert.Equal(35, p.Walls.Count);
    }

    [Fact]
    public void TreeSpecies_AddUpAndMarkEstimates()
    {
        var p = Planting.From(Murray.Value);
        var (species, count, estimated) = Assert.Single(p.TreeSpecies());
        Assert.Equal("Eastern White Pine", species.Name);
        Assert.Equal(p.Count(PlantKind.Tree).Scattered, count);
        Assert.True(estimated);
    }

    [Fact]
    public void Planting_DrawsOnlyWhenALayerIsOn()
    {
        var options = new TerrainMapOptions { Terrain = new TerrainMapOptions().Terrain with { CellMetres = 4 }, PixelsPerMetre = 0.5 };
        var survey = TerrainSurvey.Build(Murray.Value, options);
        byte[] Render(TerrainMapOptions o)
        {
            var r = new TerrainMapRenderer(o);
            var size = r.PageSize(survey);
            using var bitmap = new SKBitmap(size.Width, size.Height);
            using (var canvas = new SKCanvas(bitmap)) r.Draw(canvas, survey);
            return bitmap.Bytes;
        }
        var none = Render(options);
        Assert.Equal(none, Render(options with { TreesByType = false }));
        Assert.NotEqual(none, Render(options with { Planting = PlantingLayers.Trees }));
        // Murray's planting is pine areas, walls and bushes: grass alone draws nothing.
        Assert.Equal(none, Render(options with { Planting = PlantingLayers.Grass }));
    }
}
