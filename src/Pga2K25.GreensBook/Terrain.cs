using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using Pga2K25.CourseFormat;

namespace Pga2K25.GreensBook;

/// <summary>
/// Tunables for the heightfield replay. Brushes are stamped with their real shapes (<see cref="BrushShape"/>);
/// the hardness settings only shape brushes of unknown type. Defaults are checked against in-game readings
/// (docs/greens-book.md, "Calibration"; docs/terrain-map.md, "Terrain model").
/// </summary>
public sealed record TerrainModelOptions
{
    /// <summary>
    /// Fraction of the brush radius at full strength before the smoothstep falloff (0 = cone-like, 1 = hard edge).
    /// Calibrated on Cassimbo holes 1, 7, 10, 13 against in-game spot elevations and grid slope colours.
    /// </summary>
    public double Hardness { get; init; } = 0.85;

    /// <summary>
    /// The brush model measured in game by Plot Starter (2026-11-08, on its PS0 Calibration plot): a raise peaks at
    /// (icon peak / 0.9529)^0.65, so soft brushes are stronger than their icons (the soft round about 60 %, not 45 %);
    /// the soft round, hard round and big square use their measured shapes; and there is no final blur, since the
    /// measured shapes already include the game's smoothing. A flatten still reaches its value at the brush's
    /// strongest point: Plot Starter's evidence for otherwise is one soft-round reading, and reaching the value fits
    /// Cassimbo's greens and Devil's Perch far better. Against the icon model (false) over every measured set
    /// (2026-11-10): calibration plot 11.6 → 1.4 ft, Sangoma's markers 2.7 → 1.5 and 7.6 → 5.3 ft, Devil's Perch
    /// 15.5 → 11.2 ft, Halvoy 8.1 → 4.3 ft, Berwick's saved points 1.6 → 0.8 ft, all four Cassimbo greens better
    /// (hole 7 12.6 → 10.0 % of range); typed route legs a little worse (Berwick 2.21 → 2.32, Murray 9.3 → 9.5 ft).
    /// </summary>
    public bool MeasuredBrushes { get; init; } = true;

    /// <summary>
    /// The measured model applies to soft-round stamps (type 54) up to this scale; bigger ones use the icon model (null = any
    /// scale). The soft round was measured at scales 50 and 400 only (the hard round at 200, the big square at 100 and 1000), and stretched over Sangoma's ten 720 m lowers it sank the ground by
    /// hole 14's pond about 6 m: 4,300 m² of water running to the plane's edges, against a small banked pond in game
    /// (about 2,100 m² with the limit). It also fits Sangoma's in-game heights better (2026-11-13): 5.27 → 4.01 ft,
    /// 1.53 → 1.41 ft and 0.76 → 0.66 ft over its three saved marker sets; Devil's Perch 12.41 → 12.50 ft.
    /// </summary>
    public double? SoftRoundMeasuredMaxScale { get; init; } = 450;

    /// <summary>Gaussian blur sigma (m) applied when the course sets blurHeight; 0 disables.</summary>
    public double BlurSigmaMetres { get; init; } = 1.0;   // calibrated with Hardness

    /// <summary>
    /// Hardness for brushes at least <see cref="LargeBrushMetres"/> across; null uses <see cref="Hardness"/>.
    /// The greens calibration only constrains the flat tops of stamps, not how big sculpting brushes fall
    /// off, so plot-scale maps use a softer value to avoid circular terraces the game doesn't show.
    /// </summary>
    public double? LargeBrushHardness { get; init; }

    public double LargeBrushMetres { get; init; } = 40;

    /// <summary>
    /// Stamp each brush with its real shape (<see cref="BrushShape"/>: the designer's brush image over ±scale,
    /// rotated). False replays every brush as a round disc of diameter scale with the hardness falloff above,
    /// which is what the model did before the brush shapes were known. Unknown types always use the disc.
    /// </summary>
    public bool BrushShapes { get; init; } = true;

    /// <summary>
    /// Leave out raise and lower stamps whose centre sits on the plot edge (±<see cref="PlotHalfMetres"/>). The designer
    /// clamps the centre of a brush dragged past the edge to the edge, and the game doesn't apply those stamps to the
    /// plot: Sangoma's six 1000 m lower squares centred on z = ±1000 would sink the ground 35–100 m below its 2,290
    /// in-game distance markers (RMS 5.7 m with them, 2.3 m without). Flattens on the edge do apply: Links Lowland's
    /// ocean is 19 large flattens dragged along its two sides, and without them the model shows inland lakes instead.
    /// No in-game marker lies under an edge flatten on any course yet.
    /// </summary>
    public bool SkipEdgeStamps { get; init; } = true;

    /// <summary>Half the plot's width: stamp centres are clamped to ±this.</summary>
    public double PlotHalfMetres { get; init; } = 1000;

    /// <summary>
    /// Read each flatten stamp's value as the game's ground where it was clicked (the designer's flatten picks up the
    /// height under the cursor: 85 % of Sagan-Khushun's sub-4 m flatten dabs sit within 3 in of the modelled ground).
    /// Where the replay disagrees, the ground around the stamp is shifted by the gap before the stamp lands, so the
    /// flatten levels what is really out of level instead of building a table on an error left by the big sculpting
    /// brushes. Sagan-Khushun green 9 showed a 17 in crown at pin 1 without it; the game shows a gentle hollow.
    /// </summary>
    public bool FlattenSamples { get; init; } = true;

    /// <summary>Only flattens smaller than this (scale, m) are read as samples; big drags are often set to a height.</summary>
    public double FlattenSampleMaxScale { get; init; } = 8;

    /// <summary>The shift fades out (smoothstep) at this multiple of the stamp's scale.</summary>
    public double FlattenSampleReach { get; init; } = 2;

    /// <summary>Gaps larger than this (m) are taken as a set height, not a sample, and left alone.</summary>
    public double FlattenSampleMaxMetres { get; init; } = 1;

    /// <summary>Raster cell size in metres.</summary>
    public double CellMetres { get; init; } = 0.25;
}

/// <summary>A regular raster of elevations (m) over an X/Z window.</summary>
public sealed class Heightfield
{
    public Vec2 Origin { get; }
    public double Cell { get; }
    public int Width { get; }
    public int Height { get; }
    public double[] Values { get; }

    public Heightfield(Vec2 origin, double cell, int width, int height)
    {
        Origin = origin; Cell = cell; Width = width; Height = height;
        Values = new double[width * height];
    }

    public double this[int ix, int iz]
    {
        get => Values[iz * Width + ix];
        set => Values[iz * Width + ix] = value;
    }

    public Vec2 CellCentre(int ix, int iz) => new(Origin.X + (ix + 0.5) * Cell, Origin.Z + (iz + 0.5) * Cell);

    /// <summary>Bilinear sample at a world point (clamped to the raster).</summary>
    public double Sample(Vec2 p)
    {
        double fx = Math.Clamp((p.X - Origin.X) / Cell - 0.5, 0, Width - 1.001);
        double fz = Math.Clamp((p.Z - Origin.Z) / Cell - 0.5, 0, Height - 1.001);
        int x0 = (int)fx, z0 = (int)fz;
        double tx = fx - x0, tz = fz - z0;
        double a = this[x0, z0] * (1 - tx) + this[x0 + 1, z0] * tx;
        double b = this[x0, z0 + 1] * (1 - tx) + this[x0 + 1, z0 + 1] * tx;
        return a * (1 - tz) + b * tz;
    }

    /// <summary>Downhill-positive gradient (dh/dx, dh/dz) by central differences at a world point.</summary>
    public Vec2 Gradient(Vec2 p)
    {
        double h = Cell;
        return new Vec2(
            (Sample(p + new Vec2(h, 0)) - Sample(p - new Vec2(h, 0))) / (2 * h),
            (Sample(p + new Vec2(0, h)) - Sample(p - new Vec2(0, h))) / (2 * h));
    }
}

public static class TerrainReplay
{
    /// <summary>
    /// Rebuilds elevations over a window by replaying <c>height[]</c> in file order:
    /// tool 0 (flatten) blends toward the stamp's absolute value, tool 1 (raise) adds its value, both weighted
    /// by the brush's shape at its size and rotation (<see cref="Weight"/>). Unknown types use a circular
    /// plateau + smoothstep falloff. With <see cref="TerrainModelOptions.FlattenSamples"/>, a small flatten first
    /// shifts the ground around it to its value (<see cref="FlattenShifts"/>).
    /// A cell's height depends only on the stamps over that cell, so the rows are replayed in parallel bands, each
    /// band taking every stamp in file order.
    /// </summary>
    public static Heightfield Build(CourseModel course, Vec2 min, Vec2 max, TerrainModelOptions options)
    {
        double cell = options.CellMetres;
        double sigma = course.BlurHeight && !options.MeasuredBrushes ? options.BlurSigmaMetres : 0;
        double margin = sigma * 3;
        var origin = new Vec2(min.X - margin, min.Z - margin);
        int w = (int)Math.Ceiling((max.X - min.X + 2 * margin) / cell);
        int h = (int)Math.Ceiling((max.Z - min.Z + 2 * margin) / cell);
        var field = new Heightfield(origin, cell, w, h);
        var (hard, soft) = Hardness(options);
        var shifts = options.FlattenSamples ? FlattenShifts(course, options) : null;

        // The brushes that reach this window, in file order, each with the cells it can touch.
        var brushes = new List<Brush>();
        var stamps = course.HeightStamps;
        for (int i = 0; i < stamps.Count; i++)
        {
            var s = stamps[i];
            if (!Applies(s, options)) continue;
            if (shifts is not null && shifts[i] != 0)
            {
                double reach = s.Scale * options.FlattenSampleReach;
                var shift = new Brush { Kind = BrushKind.Shift, X = s.Position.X, Z = s.Position.Z, Value = shifts[i], Radius = reach, Round = reach };
                if (SetBox(shift, reach, field)) brushes.Add(shift);
            }
            double widest = s.Scale * Math.Sqrt(2);   // a square brush turned 45°: no brush reaches further
            if (Misses(field, s.Position, widest)) continue;
            var b = Prepare(s, options, hard, soft);
            // Two cells of slack on the brush's own extent, never past the widest box.
            if (SetBox(b, b.Kind == BrushKind.Disc ? b.Extent : Math.Min(widest, b.Extent + 2 * cell), field)) brushes.Add(b);
        }

        var cx = new double[w];
        for (int ix = 0; ix < w; ix++) cx[ix] = origin.X + (ix + 0.5) * cell;
        var values = field.Values;
        int band = Math.Clamp(h / (Environment.ProcessorCount * 4), 1, 32);
        Parallel.For(0, (h + band - 1) / band, n =>
        {
            int first = n * band, last = Math.Min(h, first + band) - 1;
            foreach (var b in brushes)
            {
                if (b.Z1 < first || b.Z0 > last) continue;
                for (int iz = Math.Max(b.Z0, first); iz <= Math.Min(b.Z1, last); iz++)
                    StampRow(b, values.AsSpan(iz * w, w), cx, origin.Z + (iz + 0.5) * cell - b.Z, origin.X, cell);
            }
        });

        if (sigma > 0) GaussianBlur(field, sigma / cell);
        return field;
    }

    static bool Applies(HeightStamp s, TerrainModelOptions options) =>
        s.Scale > 0 && double.IsFinite(s.Value) && !(options.SkipEdgeStamps && s.Tool != 0 && IsOnPlotEdge(s, options.PlotHalfMetres));

    static (double Hard, double Soft) Hardness(TerrainModelOptions options) =>
        (Math.Clamp(options.Hardness, 0, 0.98), Math.Clamp(options.LargeBrushHardness ?? options.Hardness, 0, 0.98));

    enum BrushKind : byte { Shift, Disc, Icon, SoftRound, HardRound, BigSquare }

    /// <summary>
    /// One stamp ready to replay: everything about its brush that doesn't change from cell to cell, worked out once.
    /// A shaped brush is its image over ±scale around the stamp, top towards +Z, turned by rotation.y as a Unity Y
    /// rotation (clockwise seen from above). A flatten-sample shift (<see cref="BrushKind.Shift"/>) is replayed as a
    /// raise of the gap, fading out at its reach.
    /// </summary>
    sealed class Brush
    {
        public BrushKind Kind;
        /// <summary>Flatten (tool 0) moves the ground towards <see cref="Value"/>; otherwise the value is added.</summary>
        public bool Flatten;
        /// <summary>A measured brush used to flatten: its weight is divided by <see cref="Peak"/>, so it reaches the value.</summary>
        public bool ToPeak;
        public double X, Z, Value, Scale;
        /// <summary>Shaped brushes: the image's half-width (m), its rotation, the icon's gain and the measured shape's peak.</summary>
        public double Half, Cos, Sin, Gain, Peak;
        public BrushShape? Shape;
        /// <summary>Disc and shift: the radius (m) and the disc's hard inner fraction.</summary>
        public double Radius, Inner;
        /// <summary>How far the brush reaches along X or Z (m), and the radius it stays inside when it is round.</summary>
        public double Extent, Round = double.PositiveInfinity;
        /// <summary>The cells it can touch in the window being built.</summary>
        public int X0, X1, Z0, Z1;
    }

    /// <summary>
    /// Sorts out which brush a stamp uses. Icon model: the icon's brightness, and a flatten is scaled by 1 ÷ the
    /// icon's peak so it reaches its value at the brush's strongest point (a raise adds value × brightness, so dim,
    /// soft brushes add less; that split fits both Cassimbo's flattened greens and Berwick Coast's measured heights).
    /// Measured model: the measured shape where there is one, else the icon at its in-game strength; a flatten still
    /// reaches its value. Types without an image are a disc of diameter scale.
    /// </summary>
    static Brush Prepare(HeightStamp s, TerrainModelOptions options, double hard, double soft)
    {
        var b = new Brush { X = s.Position.X, Z = s.Position.Z, Value = s.Value, Scale = s.Scale, Flatten = s.Tool == 0 };
        if (!(options.BrushShapes && BrushShape.ForType(s.Type) is { } shape))
        {
            b.Kind = BrushKind.Disc;
            b.Radius = b.Extent = b.Round = s.Scale / 2;
            b.Inner = s.Scale >= options.LargeBrushMetres ? soft : hard;
            return b;
        }
        b.Shape = shape;
        b.Half = s.Scale;
        double th = s.RotationY * Math.PI / 180;
        b.Cos = Math.Cos(th);
        b.Sin = Math.Sin(th);
        double square = b.Half * (Math.Abs(b.Cos) + Math.Abs(b.Sin));   // the turned image's reach along X or Z
        bool measured = options.MeasuredBrushes && !(s.Type == BrushShape.SoftRound && s.Scale > options.SoftRoundMeasuredMaxScale);
        if (measured && BrushShape.MeasuredWeight(s.Type, 0, 0) is { } peak)
        {
            b.Peak = peak;
            b.ToPeak = b.Flatten && peak > 0;
            switch (s.Type)
            {
                case BrushShape.SoftRound: b.Kind = BrushKind.SoftRound; b.Extent = b.Round = BrushShape.SoftReach * s.Scale; break;
                case BrushShape.HardRound: b.Kind = BrushKind.HardRound; b.Extent = b.Round = BrushShape.HardReach * s.Scale; break;
                case BrushShape.BigSquare: b.Kind = BrushKind.BigSquare; b.Extent = square * (BrushShape.SquareEdge + BrushShape.SquareBlend); break;
                default: throw new NotSupportedException($"Brush type {s.Type} has a measured shape the replay doesn't know.");
            }
            return b;
        }
        b.Kind = BrushKind.Icon;
        b.Extent = square;
        b.Gain = b.Flatten && shape.Peak > 0 ? 1 / shape.Peak : measured ? shape.ResponseGain : 1;
        return b;
    }

    /// <summary>
    /// A brush's weight at an offset (m) from its centre: the one place the replay's weights come from, for the raster
    /// (<see cref="StampRow"/>) and for single points (<see cref="FlattenShifts"/>) alike.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double Weight(Brush b, double dx, double dz)
    {
        if (b.Kind == BrushKind.Shift) return ShiftWeight(Math.Sqrt(dx * dx + dz * dz) / b.Radius);
        if (b.Kind == BrushKind.Disc)
        {
            double d = Math.Sqrt(dx * dx + dz * dz) / b.Radius;
            if (d >= 1) return 0;
            double t = Math.Clamp((1 - d) / (1 - b.Inner), 0, 1);
            return t * t * (3 - 2 * t);
        }
        // World offset into brush axes: local +X = (cos, −sin), local +Z = (sin, cos).
        return ShapedWeight(b, (dx * b.Cos - dz * b.Sin) / b.Half, (dx * b.Sin + dz * b.Cos) / b.Half);
    }

    /// <summary>A shaped brush's weight at brush-local (u, v), in fractions of its scale.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double ShapedWeight(Brush b, double u, double v)
    {
        if (b.Kind == BrushKind.Icon) return Math.Min(1, b.Shape!.Weight(u, v) * b.Gain);
        double m = b.Kind switch
        {
            BrushKind.SoftRound => BrushShape.SoftRoundWeight(Math.Sqrt(u * u + v * v)),
            BrushKind.HardRound => BrushShape.HardRoundWeight(Math.Sqrt(u * u + v * v)),
            _ => BrushShape.BigSquareWeight(u, v),
        };
        return Math.Min(1, b.ToPeak ? m / b.Peak : m);
    }

    static double ShiftWeight(double d)
    {
        if (d >= 1) return 0;
        double t = 1 - d;
        return t * t * (3 - 2 * t);
    }

    /// <summary>The ground after a brush of this weight: a flatten moves it towards the value, anything else adds to it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double Apply(Brush b, double ground, double weight) =>
        b.Flatten ? ground + (b.Value - ground) * weight : ground + b.Value * weight;

    /// <summary>One brush over one row of cells, <paramref name="dz"/> metres from its centre.</summary>
    static void StampRow(Brush b, Span<double> row, double[] cx, double dz, double originX, double cell)
    {
        int x0 = b.X0, x1 = b.X1;
        if (b.Round < double.PositiveInfinity)
        {
            // A round brush only crosses part of the row: its chord here, and a cell either side.
            double rest = b.Round * b.Round - dz * dz;
            if (rest < 0)
            {
                if (Math.Abs(dz) > b.Round + cell) return;
                rest = 0;
            }
            double chord = Math.Sqrt(rest);
            x0 = Math.Max(x0, (int)Math.Floor((b.X - chord - originX) / cell) - 1);
            x1 = Math.Min(x1, (int)Math.Ceiling((b.X + chord - originX) / cell) + 1);
        }
        double x = b.X;
        if (b.Kind is BrushKind.Shift or BrushKind.Disc)
        {
            for (int ix = x0; ix <= x1; ix++)
            {
                double weight = Weight(b, cx[ix] - x, dz);
                if (weight > 0) row[ix] = Apply(b, row[ix], weight);
            }
            return;
        }
        // A shaped brush: the same (u, v) as Weight works out, with the row's share of the turn taken out of the loop.
        double cos = b.Cos, sin = b.Sin, half = b.Half, dzSin = dz * sin, dzCos = dz * cos;
        for (int ix = x0; ix <= x1; ix++)
        {
            double dx = cx[ix] - x;
            double weight = ShapedWeight(b, (dx * cos - dzSin) / half, (dx * sin + dzCos) / half);
            if (weight > 0) row[ix] = Apply(b, row[ix], weight);
        }
    }

    /// <summary>Sets the cells within <paramref name="extent"/> (m) of the brush along X and Z; false when none are in the raster.</summary>
    static bool SetBox(Brush b, double extent, Heightfield f)
    {
        b.X0 = Math.Max(0, (int)Math.Floor((b.X - extent - f.Origin.X) / f.Cell));
        b.X1 = Math.Min(f.Width - 1, (int)Math.Ceiling((b.X + extent - f.Origin.X) / f.Cell));
        b.Z0 = Math.Max(0, (int)Math.Floor((b.Z - extent - f.Origin.Z) / f.Cell));
        b.Z1 = Math.Min(f.Height - 1, (int)Math.Ceiling((b.Z + extent - f.Origin.Z) / f.Cell));
        return b.X0 <= b.X1 && b.Z0 <= b.Z1;
    }

    /// <summary>True when nothing within <paramref name="extent"/> (m) of a point can lie in the raster (a cell of slack).</summary>
    static bool Misses(Heightfield f, Vec2 p, double extent)
    {
        double far = extent + f.Cell;
        return p.X + far < f.Origin.X || p.X - far > f.Origin.X + f.Width * f.Cell
            || p.Z + far < f.Origin.Z || p.Z - far > f.Origin.Z + f.Height * f.Cell;
    }

    static readonly ConditionalWeakTable<CourseModel, ConcurrentDictionary<TerrainModelOptions, Lazy<double[]>>> ShiftCache = new();

    /// <summary>
    /// For each stamp (by its place in <c>height[]</c>), how far to shift the ground around it before it lands:
    /// the gap between a small flatten's value and the replayed ground at its centre, or 0. Worked out once per
    /// course along the whole replay at the stamp centres alone, so every window agrees, whatever it covers.
    /// The gaps ignore the final blur, which only softens.
    /// </summary>
    public static double[] FlattenShifts(CourseModel course, TerrainModelOptions options) =>
        // Only the settings that change the replay matter; the window and cell don't.
        ShiftCache.GetOrCreateValue(course)
            .GetOrAdd(options with { CellMetres = 0, BlurSigmaMetres = 0 }, key => new Lazy<double[]>(() => ComputeShifts(course, key))).Value;

    /// <summary>
    /// The ground at each small flatten's centre is the replay of the earlier stamps that reach it. Those are looked
    /// up, not searched for: stamps go on a 32 m grid as they are replayed (the few big ones on a list of their own),
    /// so a flatten only meets its neighbours. Checking every earlier stamp took over a minute on Sangoma's 140,000.
    /// </summary>
    static double[] ComputeShifts(CourseModel course, TerrainModelOptions options)
    {
        const double gridMetres = 32, span = 1024, bigMetres = 96;
        const int side = 64;
        static int Square(double x) => Math.Clamp((int)Math.Floor((x + span) / gridMetres), 0, side - 1);

        var stamps = course.HeightStamps;
        var shifts = new double[stamps.Count];
        var brushes = new Brush?[stamps.Count];
        var grid = new List<int>?[side * side];
        var big = new List<int>();
        var (hard, soft) = Hardness(options);
        // How far a stamp can matter: any brush stays within 1.5 × scale, a shift within its reach.
        double reachPerScale = Math.Max(1.5, options.FlattenSampleReach);

        for (int i = 0; i < stamps.Count; i++)
        {
            var s = stamps[i];
            if (!Applies(s, options)) continue;
            if (s.Tool == 0 && s.Scale < options.FlattenSampleMaxScale)
            {
                // The earlier stamps near this one, in file order: its grid square's and the big ones, merged.
                var near = grid[Square(s.Position.Z) * side + Square(s.Position.X)];
                int a = 0, na = near?.Count ?? 0, c = 0, nc = big.Count;
                double ground = 0;
                while (a < na || c < nc)
                {
                    int j = c >= nc || (a < na && near![a] < big[c]) ? near![a++] : big[c++];
                    var t = brushes[j]!;
                    double dx = s.Position.X - t.X, dz = s.Position.Z - t.Z;
                    if (shifts[j] != 0) ground += shifts[j] * ShiftWeight(Math.Sqrt(dx * dx + dz * dz) / (t.Scale * options.FlattenSampleReach));
                    if (Math.Abs(dx) > t.Scale * 1.5 || Math.Abs(dz) > t.Scale * 1.5) continue;   // beyond any brush's reach
                    double weight = Weight(t, dx, dz);
                    if (weight > 0) ground = Apply(t, ground, weight);
                }
                double gap = s.Value - ground;
                if (Math.Abs(gap) <= options.FlattenSampleMaxMetres) shifts[i] = gap;
            }

            var b = brushes[i] = Prepare(s, options, hard, soft);
            double reach = s.Scale * reachPerScale + 0.001;
            if (reach > bigMetres) { big.Add(i); continue; }
            for (int gz = Square(b.Z - reach); gz <= Square(b.Z + reach); gz++)
                for (int gx = Square(b.X - reach); gx <= Square(b.X + reach); gx++)
                    (grid[gz * side + gx] ??= []).Add(i);
        }
        return shifts;
    }

    /// <summary>The game's sea plane (world m): every plot has it, though it only shows where the ground dips below.</summary>
    public const double SeaPlaneMetres = 0;

    /// <summary>Sea level: the ocean props' height when the plot has any, otherwise the sea plane.</summary>
    public static double DetectSeaLevel(CourseModel course) => OceanPropLevel(course) ?? SeaPlaneMetres;

    /// <summary>Median explicit height of the ocean props (wave, churn, impact), or null if there are none.</summary>
    public static double? OceanPropLevel(CourseModel course)
    {
        var heights = course.PlacedObjects
            .Where(p => p.Height is not null && p.AssetPath.Contains("Ocean", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Height!.Value).Order().ToList();
        return heights.Count == 0 ? null : heights[heights.Count / 2];
    }

    /// <summary>True when a stamp's centre was clamped to the plot edge (see <see cref="TerrainModelOptions.SkipEdgeStamps"/>).</summary>
    public static bool IsOnPlotEdge(HeightStamp s, double plotHalfMetres = 1000) =>
        Math.Max(Math.Abs(s.Position.X), Math.Abs(s.Position.Z)) >= plotHalfMetres - 0.1;

    /// <summary>Separable Gaussian blur in place; <paramref name="sigmaMetres"/> ≤ 0 does nothing.</summary>
    public static void Smooth(Heightfield field, double sigmaMetres)
    {
        if (sigmaMetres > 0) GaussianBlur(field, sigmaMetres / field.Cell);
    }

    /// <summary>
    /// Across each row, then down each column, edges repeated. Rows run in parallel and each tap is added to a whole
    /// row at once, in the same tap order as summing cell by cell, so the result is the same to the last bit.
    /// </summary>
    static void GaussianBlur(Heightfield f, double sigmaCells)
    {
        int radius = (int)Math.Ceiling(sigmaCells * 3);
        var kernel = new double[2 * radius + 1];
        double sum = 0;
        for (int i = -radius; i <= radius; i++) sum += kernel[i + radius] = Math.Exp(-i * i / (2 * sigmaCells * sigmaCells));
        for (int i = 0; i < kernel.Length; i++) kernel[i] /= sum;

        int w = f.Width, h = f.Height;
        var values = f.Values;
        var across = new double[values.Length];
        Parallel.For(0, h, z =>
        {
            var row = values.AsSpan(z * w, w);
            var acc = across.AsSpan(z * w, w);
            int lo = Math.Min(radius, w), hi = Math.Max(lo, w - radius);   // cells lo..hi-1 have every tap inside the row
            for (int k = -radius; k <= radius; k++)
            {
                double tap = kernel[k + radius];
                for (int x = 0; x < lo; x++) acc[x] += row[Math.Clamp(x + k, 0, w - 1)] * tap;
                if (hi > lo) AddScaled(row.Slice(lo + k, hi - lo), tap, acc[lo..hi]);
                for (int x = hi; x < w; x++) acc[x] += row[Math.Clamp(x + k, 0, w - 1)] * tap;
            }
        });
        Parallel.For(0, h, z =>
        {
            var acc = values.AsSpan(z * w, w);
            acc.Clear();
            for (int k = -radius; k <= radius; k++)
                AddScaled(across.AsSpan(Math.Clamp(z + k, 0, h - 1) * w, w), kernel[k + radius], acc);
        });
    }

    /// <summary>acc += x × scale, cell by cell: a multiply then an add, never a fused one, so it matches the plain loop.</summary>
    static void AddScaled(ReadOnlySpan<double> x, double scale, Span<double> acc)
    {
        int i = 0, lanes = Vector<double>.Count;
        var by = new Vector<double>(scale);
        for (; i <= x.Length - lanes; i += lanes)
            (new Vector<double>(acc.Slice(i, lanes)) + new Vector<double>(x.Slice(i, lanes)) * by).CopyTo(acc.Slice(i, lanes));
        for (; i < x.Length; i++) acc[i] += x[i] * scale;
    }
}
