using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.TerrainMap;

namespace Pga2K25.View3D;

public sealed record View3DOptions
{
    /// <summary>The terrain replay and window, as the terrain map takes them; a wider margin, so the model has ground round the holes.</summary>
    public TerrainMapOptions Map { get; init; } = new() { MarginMetres = 200, ShowSummits = false };

    /// <summary>Most ground points along the model's longer side; the grid coarsens to fit (never finer than the replay).</summary>
    public int MaxGrid { get; init; } = 1024;

    /// <summary>The draped ground picture's longer side, pixels.</summary>
    public int MaxTexture { get; init; } = 4096;

    /// <summary>Most trees and bushes sent to the page; scatter groups and planted areas are thinned to fit.</summary>
    public int MaxPlants { get; init; } = 60_000;
}

/// <summary>What a plant looks like in the model: the file names the species, not its shape.</summary>
public enum PlantShape { Broadleaf = 0, Conifer = 1, Bush = 2 }

public sealed record ScenePlant(Vec2 At, double Scale, int Species);
public sealed record SceneSpecies(string Name, PlantShape Shape);
public sealed record SceneWater(double Level, IReadOnlyList<List<Vec2>> Shores);

/// <summary>
/// A course as a 3D model, engine-neutral: the replayed ground on a regular grid, the water surfaces, every planted
/// tree and bush, and the holes. Read from a <see cref="TerrainSurvey"/>, so it is the same ground, sea and water the
/// terrain map draws.
/// </summary>
public sealed class Scene3D
{
    public required TerrainSurvey Survey { get; init; }

    /// <summary>World position of grid point (0, 0) and the spacing (m).</summary>
    public required Vec2 Origin { get; init; }
    public required double Cell { get; init; }
    public required int Width { get; init; }
    public required int Depth { get; init; }

    /// <summary>Ground heights (world m), row by row from <see cref="Origin"/>: index = iz × Width + ix.</summary>
    public required float[] Heights { get; init; }
    public required double Low { get; init; }
    public required double High { get; init; }

    /// <summary>Ponds and lakes with depth: flat surfaces at their level. Painted streams are on the ground picture instead.</summary>
    public required IReadOnlyList<SceneWater> Water { get; init; }
    public required IReadOnlyList<SceneSpecies> Species { get; init; }
    public required IReadOnlyList<ScenePlant> Plants { get; init; }

    /// <summary>How many plants the file holds or implies, before thinning to <see cref="View3DOptions.MaxPlants"/>.</summary>
    public required int PlantsInFile { get; init; }

    public Vec2 Max => new(Origin.X + (Width - 1) * Cell, Origin.Z + (Depth - 1) * Cell);

    public static Scene3D Build(CourseModel course, View3DOptions options) => From(TerrainSurvey.Build(course, options.Map), options);

    /// <summary>
    /// The model of a survey already made (Studio's live terrain map): the same window, ground, sea and water, so
    /// the 3D view and the map never disagree. <see cref="View3DOptions.Map"/> is not used.
    /// </summary>
    public static Scene3D From(TerrainSurvey survey, View3DOptions options)
    {
        double spanX = survey.Max.X - survey.Min.X, spanZ = survey.Max.Z - survey.Min.Z;
        double cell = Math.Max(survey.Field.Cell, Math.Max(spanX, spanZ) / (Math.Max(2, options.MaxGrid) - 1));
        int width = (int)Math.Floor(spanX / cell) + 1, depth = (int)Math.Floor(spanZ / cell) + 1;

        var heights = new float[width * depth];
        Parallel.For(0, depth, iz =>
        {
            for (int ix = 0; ix < width; ix++)
                heights[iz * width + ix] = (float)survey.Field.Sample(new Vec2(survey.Min.X + ix * cell, survey.Min.Z + iz * cell));
        });
        double lo = heights.Min(), hi = heights.Max();

        var max = new Vec2(survey.Min.X + (width - 1) * cell, survey.Min.Z + (depth - 1) * cell);
        var (species, plants, inFile) = Plant(survey, survey.Min, max, options.MaxPlants);
        return new Scene3D
        {
            Survey = survey, Origin = survey.Min, Cell = cell, Width = width, Depth = depth, Heights = heights, Low = lo, High = hi,
            Water = survey.InlandWater?.Bodies.Where(b => b.MaxDepthMetres > MinWaterDepthMetres && b.Shores.Count > 0)
                .Select(b => new SceneWater(b.Level, b.Shores)).ToList() ?? [],
            Species = species, Plants = plants, PlantsInFile = inFile,
        };
    }

    /// <summary>Shallower water than this (m) is a painted stream or a joining channel: drawn on the ground, not as a surface.</summary>
    public const double MinWaterDepthMetres = 0.05;

    /// <summary>
    /// Every tree and bush: the ones placed singly where the file puts them, and scatter groups and planted areas
    /// filled at random from a fixed seed (the game scatters those from its own seed, which the file doesn't hold, so
    /// the count is right and the exact spots are not). Grass and flowers are left out. Nothing is planted in water.
    /// </summary>
    static (List<SceneSpecies> Species, List<ScenePlant> Plants, int InFile) Plant(TerrainSurvey survey, Vec2 min, Vec2 max, int limit)
    {
        var planting = survey.Planting;
        var species = new List<SceneSpecies>();
        var index = new Dictionary<string, int>();
        int Id(PlantSpecies s)
        {
            string key = $"{s.Kind}/{s.Name}";
            if (!index.TryGetValue(key, out int id))
            {
                index[key] = id = species.Count;
                species.Add(new SceneSpecies(s.Name, s.Kind == PlantKind.Bush ? PlantShape.Bush : s.Form == TreeForm.Conifer ? PlantShape.Conifer : PlantShape.Broadleaf));
            }
            return id;
        }
        static bool Woody(PlantSpecies s) => s.Kind is PlantKind.Tree or PlantKind.Bush;
        bool Dry(Vec2 p) => p.X >= min.X && p.X <= max.X && p.Z >= min.Z && p.Z <= max.Z
                            && !survey.IsWater(survey.Field.Sample(p)) && !survey.IsWet(p);

        var singles = planting.Singles.Where(p => Woody(p.Species)).ToList();
        var groups = planting.Groups.Where(g => Woody(g.Species)).ToList();
        var areas = planting.Areas.Where(a => Woody(a.Species)).ToList();
        int scattered = groups.Sum(g => g.Cluster.Count) + areas.Sum(a => a.Placed.EstimatedCount);
        int inFile = singles.Count + scattered;
        // Singles are kept whole; scatter is thinned evenly when the course holds more than the page should carry.
        double keep = scattered == 0 ? 1 : Math.Clamp((limit - singles.Count) / (double)scattered, 0, 1);

        var plants = new List<ScenePlant>();
        foreach (var p in singles)
            if (Dry(p.At)) plants.Add(new ScenePlant(p.At, Math.Clamp(p.Scale, 0.3, 4), Id(p.Species)));

        var random = new Random(2025);
        foreach (var g in groups)
        {
            int n = (int)Math.Round(g.Cluster.Count * keep);
            for (int i = 0; i < n; i++)
            {
                double r = g.Cluster.Radius * Math.Sqrt(random.NextDouble()), a = random.NextDouble() * Math.Tau;
                var at = g.Cluster.Centre + new Vec2(Math.Cos(a) * r, Math.Sin(a) * r);
                if (Dry(at)) plants.Add(new ScenePlant(at, 0.8 + 0.4 * random.NextDouble(), Id(g.Species)));
            }
        }
        foreach (var area in areas)
        {
            var o = area.Placed.Outline;
            if (o.Count < 3) continue;
            double x0 = o.Min(p => p.X), x1 = o.Max(p => p.X), z0 = o.Min(p => p.Z), z1 = o.Max(p => p.Z);
            int n = (int)Math.Round(area.Placed.EstimatedCount * keep);
            for (int i = 0, tries = 0; i < n && tries < n * 20; tries++)
            {
                var at = new Vec2(x0 + random.NextDouble() * (x1 - x0), z0 + random.NextDouble() * (z1 - z0));
                if (!Inside(o, at)) continue;
                i++;
                if (Dry(at)) plants.Add(new ScenePlant(at, 0.8 + 0.4 * random.NextDouble(), Id(area.Species)));
            }
        }
        return (species, plants, inFile);
    }

    static bool Inside(IReadOnlyList<Vec2> poly, Vec2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].Z > p.Z) != (poly[j].Z > p.Z) && p.X < (poly[j].X - poly[i].X) * (p.Z - poly[i].Z) / (poly[j].Z - poly[i].Z) + poly[i].X)
                inside = !inside;
        return inside;
    }
}
