using Pga2K25.CourseFormat;

namespace Pga2K25.GreensBook;

public sealed record GreensBookOptions
{
    public TerrainModelOptions Terrain { get; init; } = new();

    /// <summary>In-game grid colours arrows red above this slope (degrees).</summary>
    public double SteepThresholdDegrees { get; init; } = 3.3;

    public double ArrowSpacingMetres { get; init; } = 3.5;
    public double MinArrowSlopePct { get; init; } = 0.3;
    public double ContourIntervalInches { get; init; } = 2;
    public int HeavyContourEvery { get; init; } = 3;

    /// <summary>Semi-axes of the estimated outline used when a green has no stored spline.</summary>
    public double EstimatedSemiMajorMetres { get; init; } = 15;
    public double EstimatedSemiMinorMetres { get; init; } = 12;

    public int OutlineHarmonics { get; init; } = 8;
    public string Credit { get; init; } = "ALEX C. FLEMING";

    /// <summary>Statistics ignore the band this close to the green edge (collar roll-off).</summary>
    public double EdgeExclusionMetres { get; init; } = 1.0;

    /// <summary>Colour the putting surface by slope (reference-app style heat map).</summary>
    public bool ColourSlopes { get; init; } = true;
}

public sealed record SlopeArrow(Vec2 Position, Vec2 Downhill, double SlopePct, bool Steep);
public sealed record ContourLine(double LevelInches, bool Heavy, IReadOnlyList<Vec2> Points);
public sealed record PinSpot(int Number, Vec2 Position, double Inches);

public sealed record GreenAnalysis
{
    public required Hole Hole { get; init; }
    public required IReadOnlyList<Vec2> Outline { get; init; }
    public required bool OutlineEstimated { get; init; }
    /// <summary>Unit vector of the approach (second-last waypoint → green anchor).</summary>
    public required Vec2 Forward { get; init; }
    public required IReadOnlyList<SlopeArrow> Arrows { get; init; }
    public required IReadOnlyList<ContourLine> Contours { get; init; }
    public required IReadOnlyList<PinSpot> Pins { get; init; }
    public required double AreaSquareMetres { get; init; }
    /// <summary>Length of the outline as drawn (smoothed spline).</summary>
    public required double PerimeterMetres { get; init; }
    public required double FallInches { get; init; }
    public required double HighInches { get; init; }
    public required double LowInches { get; init; }
    public required double MedianSlopePct { get; init; }
    public required double P95SlopePct { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>Reconstructed terrain around the green (for slope colouring).</summary>
    public required Heightfield Field { get; init; }

    /// <summary>Elevation (m) of the green anchor; all inch values are relative to it.</summary>
    public required double ReferenceMetres { get; init; }
}

public static class GreenAnalyzer
{
    const double MetresToInches = 39.3700787;

    public static GreenAnalysis Analyze(CourseModel course, Hole hole, GreensBookOptions options)
    {
        var warnings = new List<string>();
        var anchor = hole.GreenAnchor;
        var forward = GreenOutline.Approach(hole);

        var resolved = GreenOutline.Resolve(course, hole, options.OutlineHarmonics, options.EstimatedSemiMajorMetres, options.EstimatedSemiMinorMetres);
        var (outline, estimated) = (resolved.Points, resolved.Estimated);
        if (estimated) warnings.Add("Green outline estimated (game-generated green, no spline in file).");

        double minX = outline.Min(p => p.X), maxX = outline.Max(p => p.X);
        double minZ = outline.Min(p => p.Z), maxZ = outline.Max(p => p.Z);
        const double pad = 3;
        var field = TerrainReplay.Build(course, new Vec2(minX - pad, minZ - pad), new Vec2(maxX + pad, maxZ + pad), options.Terrain);

        double reference = field.Sample(anchor);
        double Inches(Vec2 p) => (field.Sample(p) - reference) * MetresToInches;

        // Surface statistics on a 0.5 m lattice over the putting surface, excluding the edge band.
        var elevations = new List<double>();
        var slopes = new List<double>();
        for (double x = minX; x <= maxX; x += 0.5)
            for (double z = minZ; z <= maxZ; z += 0.5)
            {
                var p = new Vec2(x, z);
                if (!Geometry.Contains(outline, p) || Geometry.DistanceToEdge(outline, p) < options.EdgeExclusionMetres) continue;
                elevations.Add(Inches(p));
                slopes.Add(field.Gradient(p).Length * 100);
            }
        if (elevations.Count == 0) { elevations.Add(0); slopes.Add(0); }
        slopes.Sort();
        double median = slopes[slopes.Count / 2], p95 = slopes[(int)(slopes.Count * 0.95)];
        if (p95 > 12) warnings.Add($"Implausibly steep for a green (p95 {p95:0.#}%): terrain model likely off here.");

        return new GreenAnalysis
        {
            Hole = hole,
            Outline = outline,
            OutlineEstimated = estimated,
            Forward = forward,
            Arrows = BuildArrows(field, outline, forward, options.ArrowSpacingMetres, options),
            Contours = BuildContours(field, outline, reference, options),
            Pins = hole.Pins.Select((p, i) => new PinSpot(i + 1, p, Inches(p))).ToList(),
            AreaSquareMetres = Geometry.Area(outline),
            PerimeterMetres = Geometry.Perimeter(outline),
            HighInches = elevations.Max(),
            LowInches = elevations.Min(),
            FallInches = elevations.Max() - elevations.Min(),
            MedianSlopePct = median,
            P95SlopePct = p95,
            Warnings = warnings,
            Field = field,
            ReferenceMetres = reference,
        };
    }

    /// <summary>The slope arrows again on a coarser lattice (the one-scale sheet thins them so they keep a readable spacing).</summary>
    public static IReadOnlyList<SlopeArrow> ArrowsAt(GreenAnalysis a, double spacingMetres, GreensBookOptions options) =>
        spacingMetres <= options.ArrowSpacingMetres ? a.Arrows : BuildArrows(a.Field, a.Outline, a.Forward, spacingMetres, options);

    static List<SlopeArrow> BuildArrows(Heightfield field, IReadOnlyList<Vec2> outline, Vec2 forward, double step, GreensBookOptions options)
    {
        double steepPct = Math.Tan(options.SteepThresholdDegrees * Math.PI / 180) * 100;
        var side = new Vec2(-forward.Z, forward.X);
        var centre = new Vec2(outline.Average(p => p.X), outline.Average(p => p.Z));
        double reach = outline.Max(p => Vec2.Distance(p, centre));
        var arrows = new List<SlopeArrow>();

        // Lattice aligned with the approach so rows read straight on the rotated page.
        for (double u = -reach; u <= reach; u += step)
            for (double v = -reach; v <= reach; v += step)
            {
                var p = centre + forward * u + side * v;
                if (!Geometry.Contains(outline, p) || Geometry.DistanceToEdge(outline, p) < step * 0.35) continue;
                var g = field.Gradient(p);
                double pct = g.Length * 100;
                if (pct < options.MinArrowSlopePct) continue;
                arrows.Add(new SlopeArrow(p, (g * -1).Normalized(), pct, pct > steepPct));
            }
        return arrows;
    }

    static List<ContourLine> BuildContours(Heightfield field, IReadOnlyList<Vec2> outline, double reference, GreensBookOptions options)
    {
        int w = field.Width, h = field.Height;
        var inch = new double[field.Values.Length];
        var mask = new bool[field.Values.Length];
        double lo = double.MaxValue, hi = double.MinValue;
        for (int iz = 0; iz < h; iz++)
            for (int ix = 0; ix < w; ix++)
            {
                int k = iz * w + ix;
                inch[k] = (field.Values[k] - reference) * MetresToInches;
                var c = field.CellCentre(ix, iz);
                mask[k] = Geometry.Contains(outline, c) || Geometry.DistanceToEdge(outline, c) < field.Cell * 2;
                if (mask[k]) { lo = Math.Min(lo, inch[k]); hi = Math.Max(hi, inch[k]); }
            }

        var lines = new List<ContourLine>();
        if (lo > hi) return lines;
        double interval = options.ContourIntervalInches;
        for (int level = (int)Math.Ceiling(lo / interval); level * interval <= hi; level++)
        {
            double value = level * interval;
            bool heavy = level % options.HeavyContourEvery == 0;
            foreach (var poly in MarchingSquares.Trace(field, inch, mask, value))
                if (poly.Count >= 2) lines.Add(new ContourLine(value, heavy, poly));
        }
        return lines;
    }

    /// <summary>Formats signed inches the way 2K25 labels them: 0, +6", -1'4", +1'.</summary>
    public static string FormatFeetInches(double inches)
    {
        int total = (int)Math.Round(inches);
        if (total == 0) return "0";
        string sign = total < 0 ? "-" : "+";
        int abs = Math.Abs(total), feet = abs / 12, rest = abs % 12;
        if (feet == 0) return $"{sign}{rest}\"";
        return rest == 0 ? $"{sign}{feet}'" : $"{sign}{feet}'{rest}\"";
    }
}
