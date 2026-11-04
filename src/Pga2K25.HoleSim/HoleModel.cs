using System.Globalization;
using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using Pga2K25.YardageBook;

namespace Pga2K25.HoleSim;

public sealed record TeeInfo(int Index, string Name, int Yards, Vec2 World, double ElevationFt);

/// <param name="BackYards">+ toward the back of the green from its centre; <paramref name="RightYards"/> + right, both seen from the chosen tee.</param>
public sealed record PinInfo(int Number, Vec2 World, FramePoint Frame, double BackYards, double RightYards)
{
    public string Where
    {
        get
        {
            string depth = Math.Abs(BackYards) < 3 ? "" : BackYards > 0 ? $"{BackYards:0} back" : $"{-BackYards:0} short";
            string side = Math.Abs(RightYards) < 3 ? "" : RightYards > 0 ? $"{RightYards:0} right" : $"{-RightYards:0} left";
            return depth == "" && side == "" ? "centre" : string.Join(", ", new[] { depth, side }.Where(s => s != ""));
        }
    }
}

/// <summary>A hazard along the line of play: <see cref="FromYards"/>–<see cref="ToYards"/> from the chosen tee, measured along the tee-to-green line.</summary>
public sealed record HazardInfo(Ground Ground, string Side, int FromYards, int ToYards, int AreaSqYd)
{
    public string Text => $"{Grounds.Name(Ground)} {Side.ToLowerInvariant()}, {FromYards}–{ToYards}";
}

/// <param name="Station">Yards from the back tee, as the yardage book measures them.</param>
public sealed record WidthInfo(int Station, int Yards, Vec2 Left, Vec2 Right);

/// <summary>A surface id the model has no rule for, painted near the hole.</summary>
public sealed record UnknownSurface(int SurfaceId, string Name, int Brushes, int Splines)
{
    public string Text => $"{Name} (id {SurfaceId}): " + string.Join(", ", new[]
    {
        Brushes > 0 ? $"{Brushes} brush{(Brushes == 1 ? "" : "es")}" : "",
        Splines > 0 ? $"{Splines} spline{(Splines == 1 ? "" : "s")}" : "",
    }.Where(s => s != ""));
}

/// <summary>A course-level ground setting as the file has it (normalised; −1 is the game default).</summary>
public sealed record CourseSetting(string Key, double Value)
{
    public string Text => Value < 0 ? "−1 (game default)" : Value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// One hole as the simulator reads it from the course file, in the chosen tee's frame. Reuses the yardage book's
/// <see cref="HoleAnalysis"/> for tees, aim points, widths, bunker and vegetation shapes so the read-back matches
/// the book. Everything here is FILE data except <see cref="TeeElevationFt"/>, which comes from the terrain model.
/// </summary>
public sealed class HoleModel
{
    public required string CourseName { get; init; }
    public required FileStamp? Stamp { get; init; }
    public required int Number { get; init; }
    public required int Par { get; init; }
    public required IReadOnlyList<TeeInfo> Tees { get; init; }
    public required int TeeIndex { get; init; }
    public TeeInfo Tee => Tees[TeeIndex];
    public TeeInfo BackTee => Tees[^1];
    public required HoleFrame Frame { get; init; }
    public required Vec2 Green { get; init; }
    public required FramePoint GreenFrame { get; init; }
    public required IReadOnlyList<PinInfo> Pins { get; init; }

    /// <summary>The game's aim marker for the tee shot (the route's first aim point; the green on a hole without one).</summary>
    public required Vec2 AimMarker { get; init; }
    public required FramePoint AimFrame { get; init; }
    public bool HasAimMarker { get; init; }

    /// <summary>Line of play in the frame: tee → aim points → green.</summary>
    public required IReadOnlyList<FramePoint> Route { get; init; }
    public required IReadOnlyList<WidthInfo> Widths { get; init; }
    public required int GreenDepthYards { get; init; }
    public required int GreenFrontYards { get; init; }
    public required int GreenBackYards { get; init; }
    public required IReadOnlyList<HazardInfo> Hazards { get; init; }
    public required IReadOnlyList<UnknownSurface> UnknownSurfaces { get; init; }
    public required IReadOnlyDictionary<int, Ground?> Treatments { get; init; }
    public required SurfaceGrid Grid { get; init; }
    public required IReadOnlyList<CourseSetting> Settings { get; init; }
    public required double? GreenSpeed { get; init; }

    /// <summary>Tee height relative to the green centre (ft, + above), from the terrain replay: confirm in game.</summary>
    public double TeeElevationFt => Tee.ElevationFt;

    /// <summary>Height brushes within reach of the line of play, as signatures (for the change list on a new save).</summary>
    public required IReadOnlyList<(string Signature, FramePoint Where)> HeightStamps { get; init; }
    public required IReadOnlyList<(string Signature, int Surface)> SurfaceSignatures { get; init; }

    public int AimFromTeeYards => (int)Math.Round(AimFrame.Length);
    public int AimToGreenYards => (int)Math.Round(AimFrame.DistanceTo(GreenFrame));
    public string TeeLabel => $"{Tee.Name} · {Tee.Yards}";
}

public sealed record HoleModelOptions
{
    /// <summary>Tee index (0 = front); null = the back tee.</summary>
    public int? TeeIndex { get; init; }

    /// <summary>How unknown surfaces play, by id: a ground, or null to ignore. Ids not listed play as native.</summary>
    public IReadOnlyDictionary<int, Ground?> Treatments { get; init; } = new Dictionary<int, Ground?>();

    /// <summary>A painted surface brush counts where its strength is at least this (PLACEHOLDER rule from the proof of concept).</summary>
    public double BrushStrength { get; init; } = 0.5;

    /// <summary>Reach (yards) from the line of play for "near the hole": unknown surfaces and height brushes.</summary>
    public double NearYards { get; init; } = 60;

    /// <summary>Reach (yards) from the line of play for listing hazards.</summary>
    public double HazardReachYards { get; init; } = 40;

    /// <summary>Hazards also count within this many yards of the hole's own fairway or green.</summary>
    public int HazardFairwayReachYards { get; init; } = 12;

    /// <summary>Hazard pieces smaller than this (square yards) aren't listed.</summary>
    public int MinHazardSqYd { get; init; } = 20;

    /// <summary>Pieces that end closer to the tee than this aren't listed (planting beside the tee box).</summary>
    public int HazardMinAlongYards { get; init; } = 30;

    /// <summary>Yardage book options (tee names, width stations).</summary>
    public YardageOptions Yardage { get; init; } = new();
}

public static class HoleModelBuilder
{
    /// <summary>Surface ids the model has rules for: bunker, green, fairway, rough, heavy rough, cart path, road, water, weathering.</summary>
    public static readonly IReadOnlySet<int> KnownSurfaces = new HashSet<int> { 0, 1, 2, 3, 4, 7, 8, 10, 18 };

    public static readonly string[] SettingKeys =
    [
        "greenSpeed", "greenFirmness", "fairwaySpeed", "fairwayFirmness", "roughSpeed", "roughFirmness",
        "heavyRoughSpeed", "heavyRoughFirmness", "bunkerSpeed", "bunkerFirmness", "terrainFirmness",
    ];

    public static HoleModel Build(string coursePath, int holeNumber, HoleModelOptions? options = null)
    {
        var stamp = FileStamp.Read(coursePath);
        var file = CourseFile.Load(coursePath);
        return Build(file.ReadModel(), file.Description, holeNumber, options, stamp);
    }

    public static HoleModel Build(CourseModel course, JsonObject? description, int holeNumber, HoleModelOptions? options = null,
        FileStamp? stamp = null, CourseLayers? layers = null)
    {
        options ??= new HoleModelOptions();
        layers ??= CourseLayers.From(course);
        var hole = course.Holes.FirstOrDefault(h => h.Number == holeNumber)
            ?? throw new ArgumentException($"The course has no hole {holeNumber}.");
        var a = HoleAnalysis.Analyze(course, layers, hole, options.Yardage, 0.4);

        var tees = a.TeeSets.Select((t, i) => new TeeInfo(i, t.Name, t.Yards, t.Tee, t.ElevationFt)).ToList();
        if (tees.Count == 0) tees.Add(new TeeInfo(0, "Tee", a.LengthYards, a.BackTee, 0));
        int teeIndex = Math.Clamp(options.TeeIndex ?? tees.Count - 1, 0, tees.Count - 1);
        var tee = tees[teeIndex];
        var green = a.Green;
        var frame = new HoleFrame(tee.World, green);
        var greenF = frame.ToFrame(green);

        bool hasAim = hole.Route.Count > 2;
        var aim = hasAim ? hole.Route[1] : green;
        var route = new List<FramePoint> { new(0, 0) };
        route.AddRange(hole.Route.Skip(1).Take(hole.Route.Count - 2).Select(frame.ToFrame));
        route.Add(greenF);

        var pins = hole.Pins.Select((p, i) =>
        {
            var f = frame.ToFrame(p);
            return new PinInfo(i + 1, p, f, f.Along - greenF.Along, f.Lat - greenF.Lat);
        }).ToList();

        // Grid: the line of play with room either side and behind the green.
        double latLo = Math.Min(0, route.Min(r => r.Lat)), latHi = Math.Max(0, route.Max(r => r.Lat));
        int latMin = (int)Math.Floor(latLo) - 120, latMax = (int)Math.Ceiling(latHi) + 120;
        int alongMin = -40, alongMax = (int)Math.Ceiling(greenF.Along) + 70;
        bool InGrid(Vec2 w, double pad = 0) { var f = frame.ToFrame(w); return f.Lat >= latMin - pad && f.Lat <= latMax + pad && f.Along >= alongMin - pad && f.Along <= alongMax + pad; }
        bool AnyIn(IReadOnlyList<Vec2> poly) => poly.Any(p => InGrid(p, 30));
        double NearLine(Vec2 w) => Polyline.Nearest(route, frame.ToFrame(w)).Distance;

        var shapes = new List<PaintShape>();
        void Splines(int surface, Ground g)
        {
            foreach (var s in course.SurfaceSplines.Where(s => s.Surface == surface && s.Closed && s.Points.Count >= 3))
            {
                var pts = Geometry.SampleSpline(s, 12);
                if (AnyIn(pts)) shapes.Add(new PaintPolygon(g, pts));
            }
        }
        void Brushes(int surface, Ground g)
        {
            foreach (var b in course.SurfaceBrushes.Where(b => b.Category == surface && b.Scale > 0 && b.Value >= options.BrushStrength && InGrid(b.Position, 30)))
                shapes.Add(new PaintDisc(g, b.Position, b.Scale / 2));
        }
        Splines(Surfaces.HeavyRough, Ground.HeavyRough); Brushes(Surfaces.HeavyRough, Ground.HeavyRough);
        Splines(Surfaces.Rough, Ground.Rough); Brushes(Surfaces.Rough, Ground.Rough);
        foreach (var (id, g) in options.Treatments)
        {
            if (g is not { } ground || KnownSurfaces.Contains(id)) continue;
            Splines(id, ground); Brushes(id, ground);
        }
        foreach (var v in a.Vegetation.Concat(a.Outcrops)) if (AnyIn(v)) shapes.Add(new PaintPolygon(Ground.Scrub, v));
        foreach (var w in a.Water) if (AnyIn(w)) shapes.Add(new PaintPolygon(Ground.Water, w));
        foreach (var body in a.WaterBodies.SelectMany(b => b)) if (AnyIn(body)) shapes.Add(new PaintPolygon(Ground.Water, body));
        Splines(Surfaces.Fairway, Ground.Fairway); Brushes(Surfaces.Fairway, Ground.Fairway);
        foreach (var b in a.Bunkers) if (AnyIn(b)) shapes.Add(new PaintPolygon(Ground.Bunker, b));
        Splines(Surfaces.Green, Ground.Green); Brushes(Surfaces.Green, Ground.Green);
        var grid = SurfaceGrid.Build(frame, latMin, latMax, alongMin, alongMax, shapes);

        // Unknown surfaces near the line of play.
        var unknown = new Dictionary<int, (int Brushes, int Splines)>();
        foreach (var b in course.SurfaceBrushes.Where(b => !KnownSurfaces.Contains(b.Category) && b.Category >= 0 && b.Value >= options.BrushStrength && NearLine(b.Position) <= options.NearYards))
            unknown[b.Category] = unknown.TryGetValue(b.Category, out var u) ? (u.Brushes + 1, u.Splines) : (1, 0);
        foreach (var s in course.SurfaceSplines.Where(s => !KnownSurfaces.Contains(s.Surface) && s.Points.Any(p => NearLine(p.Anchor) <= options.NearYards)))
            unknown[s.Surface] = unknown.TryGetValue(s.Surface, out var u) ? (u.Brushes, u.Splines + 1) : (0, 1);
        var unknownList = unknown.OrderBy(kv => kv.Key)
            .Select(kv => new UnknownSurface(kv.Key, kv.Key == Surfaces.Waste ? "Waste" : "Unknown surface", kv.Value.Brushes, kv.Value.Splines)).ToList();

        var heights = course.HeightStamps
            .Where(s => NearLine(s.Position) <= options.NearYards)
            .Select(s => (FormattableString.Invariant($"{s.Tool}|{s.Type}|{s.Position.X:F2}|{s.Position.Z:F2}|{s.Scale:F2}|{s.RotationY:F1}|{s.Value:F3}"), frame.ToFrame(s.Position)))
            .ToList();
        var surfaceSigs = course.SurfaceSplines
            .Where(s => s.Points.Any(p => NearLine(p.Anchor) <= options.NearYards))
            .Select(s => (string.Join(";", s.Points.Select(p => FormattableString.Invariant($"{p.Anchor.X:F2},{p.Anchor.Z:F2}"))) + FormattableString.Invariant($"|{s.Width:F2}|{s.Closed}"), s.Surface))
            .Concat(course.SurfaceBrushes.Where(b => NearLine(b.Position) <= options.NearYards)
                .Select(b => (FormattableString.Invariant($"b|{b.Position.X:F2}|{b.Position.Z:F2}|{b.Scale:F2}|{b.Value:F2}"), b.Category)))
            .ToList();

        // Green front and back: the yardage book measures them from the back tee; shift by the chosen tee's distance.
        double shift = (Vec2.Distance(tees[^1].World, green) - Vec2.Distance(tee.World, green)) / HoleFrame.Yard;

        var settings = description is null ? new List<CourseSetting>()
            : SettingKeys.Select(k => new CourseSetting(k, ReadNum(description[k]))).ToList();

        return new HoleModel
        {
            CourseName = course.Name,
            Stamp = stamp,
            Number = hole.Number,
            Par = hole.Par,
            Tees = tees,
            TeeIndex = teeIndex,
            Frame = frame,
            Green = green,
            GreenFrame = greenF,
            Pins = pins,
            AimMarker = aim,
            AimFrame = frame.ToFrame(aim),
            HasAimMarker = hasAim,
            Route = route,
            Widths = a.Widths.Select(w => new WidthInfo(w.StationYards, w.Yards, w.Left, w.Right)).ToList(),
            GreenDepthYards = a.GreenDepthYards,
            GreenFrontYards = (int)Math.Round(a.GreenFrontYards - shift),
            GreenBackYards = (int)Math.Round(a.GreenBackYards - shift),
            Hazards = FindHazards(grid, route, greenF, options),
            UnknownSurfaces = unknownList,
            Treatments = options.Treatments,
            Grid = grid,
            Settings = settings,
            GreenSpeed = course.GreenSpeed < 0 ? null : course.GreenSpeed,
            HeightStamps = heights,
            SurfaceSignatures = surfaceSigs,
        };
    }

    static double ReadNum(JsonNode? n)
    {
        try { return n is null ? -1 : n.GetValue<double>(); }
        catch (Exception) { return double.TryParse(n?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : -1; }
    }

    /// <summary>
    /// Bunker, scrub and water pieces within reach of the line of play, from tee to just past the green: connected cells
    /// of the grid, so a fused vegetation mass is reported only where it actually meets the hole.
    /// </summary>
    static List<HazardInfo> FindHazards(SurfaceGrid grid, IReadOnlyList<FramePoint> route, FramePoint green, HoleModelOptions o)
    {
        int w = grid.Width, h = grid.Height;

        // Cells within reach of this hole's fairway and green, so a bunker on the far edge of a wide fairway counts
        // even when the aim line runs down the other side.
        var nearPlay = new int[w * h];
        Array.Fill(nearPlay, int.MaxValue);
        var frontier = new Queue<int>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (grid[x, y] is not (Ground.Fairway or Ground.Green)) continue;
                var c = grid.CellCentre(x, y);
                if (c.Along < 0 || c.Along > green.Along + 15 || Polyline.Nearest(route, c).Distance > 90) continue;
                nearPlay[y * w + x] = 0; frontier.Enqueue(y * w + x);
            }
        while (frontier.Count > 0)
        {
            int k = frontier.Dequeue(), x = k % w, y = k / w, d = nearPlay[k];
            if (d >= o.HazardFairwayReachYards) continue;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    int kk = yy * w + xx; if (nearPlay[kk] <= d + 1) continue;
                    nearPlay[kk] = d + 1; frontier.Enqueue(kk);
                }
        }

        var inReach = new bool[w * h];
        var side = new sbyte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = grid.CellCentre(x, y);
                if (c.Along < 0 || c.Along > green.Along + 15 || !Grounds.IsHazard(grid[x, y])) continue;
                var (d, s, _) = Polyline.Nearest(route, c);
                if (d > o.HazardReachYards && nearPlay[y * w + x] > o.HazardFairwayReachYards) continue;
                inReach[y * w + x] = true; side[y * w + x] = (sbyte)s;
            }
        var seen = new bool[w * h];
        var result = new List<HazardInfo>();
        var queue = new Queue<int>();
        for (int i = 0; i < w * h; i++)
        {
            if (!inReach[i] || seen[i]) continue;
            var g = grid[i % w, i / w];
            int n = 0, right = 0, left = 0; double lo = double.MaxValue, hi = double.MinValue;
            seen[i] = true; queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int k = queue.Dequeue(), x = k % w, y = k / w;
                n++; if (side[k] > 0) right++; else if (side[k] < 0) left++;
                double al = grid.CellCentre(x, y).Along; lo = Math.Min(lo, al); hi = Math.Max(hi, al);
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    int kk = yy * w + xx;
                    if (inReach[kk] && !seen[kk] && grid[xx, yy] == g) { seen[kk] = true; queue.Enqueue(kk); }
                }
            }
            if (n < o.MinHazardSqYd || hi < o.HazardMinAlongYards) continue;
            string sideText = right >= 0.85 * n ? "Right" : left >= 0.85 * n ? "Left" : "Across the line";
            result.Add(new HazardInfo(g, sideText, (int)Math.Round(lo), (int)Math.Round(hi), n));
        }
        return result.OrderBy(r => r.FromYards).ToList();
    }
}
