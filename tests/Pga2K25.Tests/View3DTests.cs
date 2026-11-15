using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pga2K25.CourseFormat;
using Pga2K25.TerrainMap;
using Pga2K25.View3D;

namespace Pga2K25.Tests;

[Trait("Area", "Products")]
public class View3DTests
{
    const string Sagan = "sagan-khushun.course";

    // A coarse replay, a small grid and a small picture keep these quick; nothing here depends on resolution.
    static View3DOptions Coarse(int maxPlants = 60_000) => new()
    {
        Map = new View3DOptions().Map with { Terrain = new TerrainMapOptions().Terrain with { CellMetres = 4 } },
        MaxGrid = 160, MaxTexture = 256, MaxPlants = maxPlants,
    };

    static readonly Lazy<Scene3D> Scene = new(() => Scene3D.Build(Samples.Course(Sagan), Coarse()));

    [Fact]
    public void Scene_GridCoversTheSurveyWindowAndHoldsItsGround()
    {
        var s = Scene.Value;
        Assert.Equal(s.Survey.Min, s.Origin);
        Assert.InRange(Math.Max(s.Width, s.Depth), 150, 160);
        Assert.True(s.Cell >= s.Survey.Field.Cell);
        Assert.InRange(s.Survey.Max.X - s.Max.X, 0, s.Cell);
        Assert.InRange(s.Survey.Max.Z - s.Max.Z, 0, s.Cell);
        Assert.Equal(s.Width * s.Depth, s.Heights.Length);
        foreach (var (ix, iz) in new[] { (0, 0), (s.Width - 1, s.Depth - 1), (s.Width / 2, s.Depth / 3) })
        {
            var p = new Vec2(s.Origin.X + ix * s.Cell, s.Origin.Z + iz * s.Cell);
            Assert.Equal(s.Survey.Field.Sample(p), s.Heights[iz * s.Width + ix], 3);
        }
        Assert.Equal(s.Heights.Min(), s.Low, 6);
        Assert.Equal(s.Heights.Max(), s.High, 6);
    }

    [Fact]
    public void Scene_PlantsOnlyTreesAndBushesOnDryGroundInsideTheModel()
    {
        var s = Scene.Value;
        Assert.NotEmpty(s.Plants);
        Assert.True(s.Plants.Count <= s.PlantsInFile);
        Assert.All(s.Plants, p =>
        {
            Assert.InRange(p.At.X, s.Origin.X, s.Max.X);
            Assert.InRange(p.At.Z, s.Origin.Z, s.Max.Z);
            Assert.False(s.Survey.IsWater(s.Survey.Field.Sample(p.At)));
            Assert.InRange(p.Species, 0, s.Species.Count - 1);
        });
        // Every species sent is used, and none is a grass or a flower.
        Assert.Equal(s.Species.Count, s.Plants.Select(p => p.Species).Distinct().Count());
        var woody = s.Survey.Planting.Singles.Where(p => p.Species.Kind is PlantKind.Tree or PlantKind.Bush).Select(p => p.Species.Name).ToHashSet();
        Assert.All(s.Species, sp => Assert.Contains(sp.Name, woody.Concat(s.Survey.Planting.Groups.Select(g => g.Species.Name)).Concat(s.Survey.Planting.Areas.Select(a => a.Species.Name))));
    }

    [Fact]
    public void Scene_ThinsScatterToTheLimitButKeepsEveryTreePlacedByHand()
    {
        var all = Scene.Value;
        var thin = Scene3D.Build(Samples.Course(Sagan), Coarse(maxPlants: 1));
        int singles = all.Survey.Planting.Singles.Count(p => p.Species.Kind is PlantKind.Tree or PlantKind.Bush);
        Assert.Equal(all.PlantsInFile, thin.PlantsInFile);
        Assert.True(thin.Plants.Count <= singles);
        Assert.True(thin.Plants.Count <= all.Plants.Count);
    }

    [Fact]
    public void Build_WritesOneSelfContainedPageWithTheGroundPackedInside()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-view3d-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = View3DBuilder.Build(Path.Combine(TestPaths.SamplesDir, Sagan), dir, Coarse());
            Assert.Equal("Sagan-Khushun - 3D View.html", Path.GetFileName(result.HtmlPath));
            string html = File.ReadAllText(result.HtmlPath);
            Assert.Equal(result.Bytes, new FileInfo(result.HtmlPath).Length);

            // Nothing left to fill in, and nothing fetched: no script, style sheet or image from anywhere else.
            Assert.DoesNotContain("{{", html[..html.IndexOf("<script>", StringComparison.Ordinal)]);
            Assert.DoesNotContain("<script src", html);
            Assert.DoesNotContain("<link", html);
            Assert.Contains("Three.js Authors", html);
            // three.js without its deprecation warning, still one expression (a bare "function(…)" would not parse).
            Assert.DoesNotContain("console.warn('Scripts \"build/three.js\"", html);
            Assert.Contains("<script>0,/**", html);

            string Block(string id) => Regex.Match(html, $"<script id=\"{id}\"[^>]*>(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
            using var scene = JsonDocument.Parse(Block("scene"));
            var grid = scene.RootElement.GetProperty("grid");
            int nx = grid.GetProperty("nx").GetInt32(), nz = grid.GetProperty("nz").GetInt32();
            Assert.Equal((result.Scene.Width, result.Scene.Depth), (nx, nz));
            Assert.Equal(result.Scene.Survey.Holes.Count, scene.RootElement.GetProperty("holes").GetArrayLength());
            Assert.Equal(result.Scene.Plants.Count * 4, scene.RootElement.GetProperty("plants").GetArrayLength());

            // Heights: gzipped 16-bit steps between the lowest and highest ground, one per grid point.
            using var unpacked = new MemoryStream();
            using (var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(Block("heights"))), CompressionMode.Decompress)) gzip.CopyTo(unpacked);
            var raw = unpacked.ToArray();
            Assert.Equal(nx * nz * 2, raw.Length);
            double lo = grid.GetProperty("lo").GetDouble(), hi = grid.GetProperty("hi").GetDouble();
            int k = (nz / 2) * nx + nx / 2;
            double height = lo + (raw[2 * k] | raw[2 * k + 1] << 8) / 65535.0 * (hi - lo);
            Assert.Equal(result.Scene.Heights[k], height, 2);

            var jpeg = Convert.FromBase64String(Block("picture"));
            Assert.Equal((0xFF, 0xD8), (jpeg[0], jpeg[1]));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
