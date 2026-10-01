using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Support;
using Pga2K25.GreensBook;

namespace Pga2K25.Tests;

/// <summary>
/// Builds a TGC-Tool-style ("LiDAR import") course from a hand-built sample: a <c>type 72</c> absolute
/// lattice sampled from our calibrated terrain replay over a few holes, plus an optional pond (a carved bowl
/// with a closed water spline). The reference Finisher's terrain passes need such a lattice; none of the
/// repo's Designer-built samples has one.
/// </summary>
static class FinisherFixture
{
    public static readonly Vec2 PondCenter = new(470, 130);
    /// <summary>The pond sits on a flat 30 m plateau (blending back to the real terrain by 40 m) with a 15 cm deep bed.</summary>
    public const double PondRadius = 12, PlateauRadius = 30, PlateauBlend = 10, BedDepth = 0.15;

    static readonly Lazy<string> CassimboHoles1To3 = new(() => Create(Path.Combine(TestPaths.SamplesDir, "cassimbo.course"), [1, 2, 3], 2.0, addPond: true));

    /// <summary>Cached fixture: Cassimbo holes 1–3 on a 2 m lattice, with a pond.</summary>
    public static string Cassimbo => CassimboHoles1To3.Value;

    public static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pga2k25-finisher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string Create(string sourcePath, int[] holes, double spacing, bool addPond)
    {
        var file = CourseFile.Load(sourcePath);
        var model = file.ReadModel();
        var desc = file.Description;

        var pts = model.Holes.Where(h => holes.Contains(h.Number)).SelectMany(h => h.Route.Concat(h.Tees)).ToList();
        var min = new Vec2(pts.Min(p => p.X) - 45, pts.Min(p => p.Z) - 45);
        var max = new Vec2(pts.Max(p => p.X) + 45, pts.Max(p => p.Z) + 45);
        var field = TerrainReplay.Build(model, min, max, new TerrainModelOptions { CellMetres = 0.5 });

        var height = Json.EnsureArray(desc, "height");
        double plateau = field.Sample(PondCenter);
        for (double z = Math.Ceiling(min.Z / spacing) * spacing; z <= max.Z; z += spacing)
            for (double x = Math.Ceiling(min.X / spacing) * spacing; x <= max.X; x += spacing)
            {
                double v = field.Sample(new Vec2(x, z));
                if (addPond)
                {
                    double r = Vec2.Distance(new Vec2(x, z), PondCenter);
                    double t = Math.Clamp((r - PlateauRadius) / PlateauBlend, 0, 1);
                    v = plateau + (v - plateau) * t;
                    if (r <= PondRadius - 0.5) v -= BedDepth;
                }
                height.Add(new JsonObject
                {
                    ["tool"] = 0,
                    ["position"] = Json.XGroundZ(x, z),
                    ["rotation"] = Json.Xyz(0, 0, 0),
                    ["scale"] = Json.Xyz(spacing, 1.0, spacing),
                    ["type"] = 72,
                    ["value"] = Json.F(Math.Round(v, 4)),
                    ["holeId"] = -1,
                });
            }
        if (addPond) Json.EnsureArray(desc, "surfaceSplines2").Add(CircleSpline(PondCenter, PondRadius, 10, 0.1));

        string dir = TempDir();
        string path = Path.Combine(dir, Path.GetFileNameWithoutExtension(sourcePath) + " lidar.course");
        file.Save(path);
        return path;
    }

    /// <summary>A closed 8-point circular spline (Bézier handles 0.265 r along the tangent).</summary>
    public static JsonObject CircleSpline(Vec2 c, double r, int surface, double width)
    {
        const int n = 8;
        double k = 4.0 / 3.0 * Math.Tan(Math.PI / (2 * n)) * r;
        var wps = new JsonArray();
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            var p = new Vec2(c.X + r * Math.Cos(a), c.Z + r * Math.Sin(a));
            var t = new Vec2(-Math.Sin(a), Math.Cos(a));
            wps.Add(new JsonObject
            {
                ["pointOne"] = Json.Xyz(p.X - t.X * k, 0, p.Z - t.Z * k),
                ["pointTwo"] = Json.Xyz(p.X + t.X * k, 0, p.Z + t.Z * k),
                ["waypoint"] = Json.Xyz(p.X, 0, p.Z),
                ["width"] = Json.F(width),
            });
        }
        return new JsonObject
        {
            ["surface"] = surface,
            ["secondarySurface"] = -1,
            ["secondaryWidth"] = Json.F(-1.0),
            ["opacity"] = Json.F(1.0),
            ["waypoints"] = wps,
            ["width"] = Json.F(width),
            ["state"] = 3,
            ["absoluteY"] = false,
        };
    }
}
