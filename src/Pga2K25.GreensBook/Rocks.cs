using System.Runtime.CompilerServices;
using System.Text.Json;
using Pga2K25.CourseFormat;

namespace Pga2K25.GreensBook;

/// <summary>
/// One rock shape at scale 1 (metres): its width where it meets the ground, its height above its base point, and how wide it
/// still shows when sunk <see cref="SunkDepth"/> (<see cref="SunkWidth"/>). <see cref="Rough"/> marks the craggy shapes
/// (sinkhole pillars, cliffs, rock formations) as against smooth stones and boulders.
/// </summary>
public sealed record RockShape(double Width, double Height, double SunkDepth, double SunkWidth, bool Rough, bool Measured)
{
    /// <summary>
    /// The width still showing when the base point is <paramref name="depth"/> below the ground: a straight line from the
    /// ground width to the sunk width, then on to nothing at the top.
    /// </summary>
    public double WidthAt(double depth)
    {
        if (depth <= 0) return Width;
        if (depth >= Height) return 0;
        if (SunkDepth > 0 && depth <= SunkDepth) return Width + (SunkWidth - Width) * depth / SunkDepth;
        double d0 = SunkDepth > 0 && SunkDepth < Height ? SunkDepth : 0, w0 = d0 > 0 ? SunkWidth : Width;
        return Math.Max(0, w0 * (1 - (depth - d0) / Math.Max(0.01, Height - d0)));
    }
}

/// <summary>
/// The rock shapes, from <c>assets/rocks/catalogue.json</c>: widths and heights measured in game on calibration plots, and how
/// deep designers sink each shape. Height is the measured height or 10% over the depth 90% of real uses stay within, whichever
/// is more (nobody places a rock they can't see, and shadows measured a little short). A shape that wasn't measured takes its
/// family's figures (the same name up to its last part, then the part before), else a typical stone.
/// </summary>
public static class RockCatalogue
{
    /// <summary>A typical stone (Stone01 measured 2.7 m wide, 0.7 m tall), for shapes with no family.</summary>
    public static readonly RockShape Stone = new(2.4, 0.7, 0, 2.4, false, false);

    sealed record Entry(double? Width, double? Height, double? SunkDepth, double? SunkWidth, double? UsedTo);

    static readonly Lazy<Dictionary<string, Entry>> Entries = new(Load);
    static readonly Dictionary<string, RockShape> Cache = new();

    static Dictionary<string, Entry> Load()
    {
        using var s = typeof(RockCatalogue).Assembly.GetManifestResourceStream("rocks.catalogue.json")
            ?? throw new InvalidOperationException("rocks.catalogue.json isn't embedded");
        using var doc = JsonDocument.Parse(s);
        double? Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        return doc.RootElement.GetProperty("shapes").EnumerateObject().ToDictionary(p => p.Name,
            p => new Entry(Num(p.Value, "width"), Num(p.Value, "height"), Num(p.Value, "sunkDepth"), Num(p.Value, "sunkWidth"), Num(p.Value, "usedTo")));
    }

    /// <summary>The shape's name: the prefab's file name without "Prefab".</summary>
    public static string Name(string assetPath)
    {
        string n = assetPath.Replace('\\', '/').Split('/')[^1];
        return n.EndsWith("Prefab", StringComparison.Ordinal) ? n[..^6] : n;
    }

    public static bool IsRock(PlacedObject o) =>
        o.AssetPath.Contains("/Rocks/", StringComparison.OrdinalIgnoreCase) || o.AssetPath.Contains("/Stones/", StringComparison.OrdinalIgnoreCase);

    static bool RoughName(string n) =>
        n.Contains("SinkHole", StringComparison.OrdinalIgnoreCase) || n.Contains("Cliff", StringComparison.OrdinalIgnoreCase)
        || n.Contains("Formation", StringComparison.OrdinalIgnoreCase) || n.Contains("Mountain", StringComparison.OrdinalIgnoreCase);

    public static RockShape For(string assetPath)
    {
        string name = Name(assetPath);
        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var cached)) return cached;
            return Cache[name] = Resolve(name);
        }
    }

    static RockShape Resolve(string name)
    {
        var all = Entries.Value;
        all.TryGetValue(name, out var own);
        RockShape shape;
        if (own?.Width is double w && own.Height is double h)
            shape = new RockShape(w, h, own.SunkDepth ?? 0, own.SunkWidth ?? w, RoughName(name), true);
        else
        {
            shape = Stone with { Rough = RoughName(name) };
            var parts = name.Split('_');
            for (int cut = 1; cut < parts.Length; cut++)
            {
                string prefix = string.Join('_', parts[..^cut]) + "_";
                var family = all.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal) && kv.Value.Width is not null && kv.Value.Height is not null)
                    .Select(kv => kv.Value).ToList();
                if (family.Count == 0) continue;
                double fw = family.Average(f => f.Width!.Value), fh = family.Average(f => f.Height!.Value);
                // The family's sunk point as a share of its width and height, so the profile keeps its shape.
                var sunk = family.Where(f => f.SunkDepth is > 0).ToList();
                double sd = sunk.Count > 0 ? sunk.Average(f => f.SunkDepth!.Value / f.Height!.Value) * fh : 0;
                double sw = sunk.Count > 0 ? sunk.Average(f => f.SunkWidth!.Value / f.Width!.Value) * fw : fw;
                shape = new RockShape(fw, fh, sd, sw, RoughName(name), false);
                break;
            }
        }
        if (own?.UsedTo is double used && used * 1.1 > shape.Height) shape = shape with { Height = used * 1.1 };
        if (shape.SunkDepth >= shape.Height) shape = shape with { SunkDepth = 0, SunkWidth = shape.Width };
        return shape;
    }
}

/// <summary>A placed rock as it shows: its outline where it meets the ground (world X/Z), and whether it's a craggy shape.</summary>
public sealed record RockOutline(PlacedObject Rock, IReadOnlyList<Vec2> Outline, bool Rough, double VisibleWidth);

/// <summary>
/// Every rock in a course as it shows above the ground: each shape's outline (see <see cref="RockCatalogue"/>) turned by the
/// rock's yaw, scaled, and cut where the ground meets it, from how deep its base point sits below the replayed ground. Rocks
/// sunk out of sight are left out. Each shape's outline is a seeded irregular ring, the same for every copy of that shape (as
/// in game, where one rock is reused turned and scaled); only its width north to south was measured, so its other dimension
/// is a seeded 1.15–1.6 elongation.
/// </summary>
public static class RockOutlines
{
    /// <summary>Narrower than this (m) doesn't print.</summary>
    public const double MinWidth = 0.3;

    static readonly ConditionalWeakTable<CourseModel, IReadOnlyList<RockOutline>> Cache = new();

    public static IReadOnlyList<RockOutline> For(CourseModel course) => Cache.GetValue(course, Build);

    static IReadOnlyList<RockOutline> Build(CourseModel course)
    {
        var rocks = course.PlacedObjects.Where(o => RockCatalogue.IsRock(o) && o.Scale > 0).ToList();
        var result = new List<RockOutline>();
        // The ground under each rock, replayed tile by tile (300 m) at 1 m so a course of scattered rocks stays quick.
        foreach (var tile in rocks.GroupBy(r => ((int)Math.Floor(r.Position.X / 300), (int)Math.Floor(r.Position.Z / 300))))
        {
            Heightfield? ground = null;
            if (tile.Any(r => r.Height is not null))
            {
                var min = new Vec2(tile.Min(r => r.Position.X) - 4, tile.Min(r => r.Position.Z) - 4);
                var max = new Vec2(tile.Max(r => r.Position.X) + 4, tile.Max(r => r.Position.Z) + 4);
                ground = TerrainReplay.Build(course, min, max, new TerrainModelOptions { CellMetres = 1 });
            }
            foreach (var r in tile)
            {
                double sunk = r.Height is double y && ground is not null ? Math.Max(0, ground.Sample(r.Position) - y) : 0;
                if (Of(r, sunk) is { } outline) result.Add(outline);
            }
        }
        return result;
    }

    /// <summary>The outline of <paramref name="rock"/> with its base point <paramref name="sunk"/> m below the ground, or null when none shows.</summary>
    public static RockOutline? Of(PlacedObject rock, double sunk)
    {
        var shape = RockCatalogue.For(rock.AssetPath);
        double width = shape.WidthAt(sunk / rock.Scale) * rock.Scale;
        if (width < MinWidth) return null;
        var rnd = new Random(StableHash(RockCatalogue.Name(rock.AssetPath)));
        int n = shape.Rough ? 11 : 16;
        double aspect = 1.15 + rnd.NextDouble() * 0.45;
        double ru = width / 2 * Math.Sqrt(aspect), rv = width / 2 / Math.Sqrt(aspect);
        double yaw = rock.Yaw * Math.PI / 180, cos = Math.Cos(yaw), sin = Math.Sin(yaw);
        var pts = new List<Vec2>(n);
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            double jitter = shape.Rough ? 0.78 + rnd.NextDouble() * 0.4 : 0.92 + rnd.NextDouble() * 0.14;
            double u = Math.Cos(a) * ru * jitter, v = Math.Sin(a) * rv * jitter;
            // Positive yaw turns clockwise seen from above: local x goes to (cos, -sin) in world X/Z.
            pts.Add(new Vec2(rock.Position.X + u * cos + v * sin, rock.Position.Z - u * sin + v * cos));
        }
        return new RockOutline(rock, pts, shape.Rough, width);
    }

    static int StableHash(string s) { uint h = 2166136261; foreach (char ch in s) { h ^= ch; h *= 16777619; } return (int)(h & 0x7fffffff); }
}
