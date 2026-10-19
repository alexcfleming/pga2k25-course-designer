using Pga2K25.GreensBook;

namespace Pga2K25.Estimate;

/// <summary>Baseline settings for one worked zone class, in engine units (metres).</summary>
/// <param name="Fidelity">Share of finished relief treated as given (1 = no earthwork).</param>
/// <param name="WavelengthMetres">Landforms wider than this are natural; ≤ 0 = best-fit plane over the zone.</param>
/// <param name="RockFactor">Multiplier on the cut's earthwork price.</param>
public sealed record ZoneParams(string Name, double Fidelity, double WavelengthMetres, double RockFactor);

/// <summary>
/// The raster the engine runs on: the finished surface plus, per cell, which zone class applies
/// (−1 = natural ground, no earthwork) and whether the cell is counted (e.g. belongs to the hole).
/// </summary>
public sealed class ZoneRaster
{
    public required Heightfield Finished { get; init; }
    public required int[] Zone { get; init; }
    public required bool[] Counted { get; init; }
    public required IReadOnlyList<ZoneParams> Classes { get; init; }

    // Smoothed surfaces and edge distances don't depend on fidelity, so scenarios on one raster share them.
    internal Dictionary<int, double[]?> SmoothCache { get; } = new();
    internal double[]? InsideCache { get; set; }
}

public sealed record ZoneVolumes(string Name, double AreaSqM, double CutCuM, double FillCuM);

/// <summary>Cut and fill against the assumed starting ground. Volumes are bank cubic metres, before shrink/swell.</summary>
public sealed class EarthworkResult
{
    public required double[] Dz { get; init; }          // finished − baseline (m): + fill, − cut
    public required double[] Baseline { get; init; }
    public required double CutCuM { get; init; }
    public required double FillCuM { get; init; }
    /// <summary>Cut volume weighted by each zone's rock factor (for pricing the cut).</summary>
    public required double RockWeightedCutCuM { get; init; }
    public required double WorkedAreaSqM { get; init; }
    public required IReadOnlyList<ZoneVolumes> Zones { get; init; }

    public double CutCuYd => CutCuM * Units.CubicYardsPerCubicMetre;
    public double FillCuYd => FillCuM * Units.CubicYardsPerCubicMetre;
}

/// <summary>Bank volumes balanced on site, with what has to come in or go out.</summary>
/// <param name="BalancedCuM">Cut that is placed as fill on site (bank m³).</param>
/// <param name="ImportCuM">Extra fill needed (bank m³ from a borrow source).</param>
/// <param name="ExportCuM">Surplus cut hauled off (loose m³, after swell).</param>
public sealed record EarthBalance(double BalancedCuM, double ImportCuM, double ExportCuM);

/// <summary>
/// Pure earthwork maths. Per worked zone the starting ground is
/// <c>Base = g·F + (1 − g)·S</c>, where F is the finished surface and S its low-pass at the zone's wavelength
/// (or a best-fit plane), so <c>dz = F − Base = (1 − g)(F − S)</c>. Near natural ground g ramps back to 1 over the
/// feather width, so the starting ground meets the untouched ground without a step.
/// </summary>
public static class Earthwork
{
    /// <summary>
    /// A Gaussian of standard deviation σ halves the amplitude of a wave of length λ ≈ 5.34 σ, so σ = λ / 5.34 puts the
    /// zone's wavelength at the half-way point between "kept as natural" and "built".
    /// </summary>
    public const double WavelengthPerSigma = 5.336;

    public static EarthworkResult Compute(ZoneRaster raster, double featherMetres, double fidelityOffset = 0)
    {
        var f = raster.Finished;
        int n = f.Values.Length;
        if (raster.Zone.Length != n || raster.Counted.Length != n)
            throw new ArgumentException("Zone and Counted must match the heightfield size.");

        var smooth = new double[]?[raster.Classes.Count];
        for (int c = 0; c < raster.Classes.Count; c++)
            smooth[c] = raster.SmoothCache.TryGetValue(c, out var cached) ? cached : raster.SmoothCache[c] = Low(f, raster, c);

        var inside = raster.InsideCache ??= DistanceInside(raster.Zone, f.Width, f.Height, f.Cell);
        var dz = new double[n];
        var baseline = (double[])f.Values.Clone();
        double cellArea = f.Cell * f.Cell, cut = 0, fill = 0, rockCut = 0, worked = 0;
        var zoneCut = new double[raster.Classes.Count];
        var zoneFill = new double[raster.Classes.Count];
        var zoneArea = new double[raster.Classes.Count];

        for (int k = 0; k < n; k++)
        {
            int c = raster.Zone[k];
            if (c < 0) continue;
            var zp = raster.Classes[c];
            double g = Math.Clamp(zp.Fidelity + fidelityOffset, 0, 1);
            double ramp = featherMetres > 0 ? Math.Min(1, inside[k] / featherMetres) : 1;
            double built = (1 - g) * ramp;
            dz[k] = built * (f.Values[k] - smooth[c]![k]);
            baseline[k] = f.Values[k] - dz[k];
            if (!raster.Counted[k]) continue;
            worked += cellArea;
            zoneArea[c] += cellArea;
            if (dz[k] > 0) { fill += dz[k] * cellArea; zoneFill[c] += dz[k] * cellArea; }
            else
            {
                double v = -dz[k] * cellArea;
                cut += v; rockCut += v * zp.RockFactor; zoneCut[c] += v;
            }
        }

        return new EarthworkResult
        {
            Dz = dz, Baseline = baseline, CutCuM = cut, FillCuM = fill, RockWeightedCutCuM = rockCut, WorkedAreaSqM = worked,
            Zones = raster.Classes.Select((z, i) => new ZoneVolumes(z.Name, zoneArea[i], zoneCut[i], zoneFill[i]))
                .Where(z => z.AreaSqM > 0).ToList(),
        };
    }

    /// <summary>
    /// Balances on site: compacted fill needs <c>fill / (1 − shrink)</c> bank m³ of cut. Any shortfall is imported;
    /// any surplus is hauled off, bulked by swell.
    /// </summary>
    public static EarthBalance Balance(double cutCuM, double fillCuM, double shrink, double swell)
    {
        double needed = fillCuM / (1 - Math.Clamp(shrink, 0, 0.9));
        double balanced = Math.Min(cutCuM, needed);
        return new EarthBalance(balanced, Math.Max(0, needed - cutCuM), Math.Max(0, cutCuM - needed) * (1 + swell));
    }

    /// <summary>The zone's smooth surface S: a Gaussian low-pass of the whole window, or a plane fitted to the zone's cells.</summary>
    static double[]? Low(Heightfield f, ZoneRaster raster, int zone)
    {
        if (!raster.Zone.Contains(zone)) return null;
        var zp = raster.Classes[zone];
        if (zp.WavelengthMetres > 0)
        {
            var copy = new Heightfield(f.Origin, f.Cell, f.Width, f.Height);
            Array.Copy(f.Values, copy.Values, f.Values.Length);
            TerrainReplay.Smooth(copy, zp.WavelengthMetres / WavelengthPerSigma);
            return copy.Values;
        }
        var (a, b, c) = FitPlane(f, raster.Zone, zone);
        var plane = new double[f.Values.Length];
        for (int iz = 0; iz < f.Height; iz++)
            for (int ix = 0; ix < f.Width; ix++)
                plane[iz * f.Width + ix] = a + b * ix + c * iz;
        return plane;
    }

    /// <summary>Least-squares plane h = a + b·ix + c·iz over the cells of one zone.</summary>
    static (double A, double B, double C) FitPlane(Heightfield f, int[] zones, int zone)
    {
        double n = 0, sx = 0, sz = 0, sxx = 0, szz = 0, sxz = 0, sh = 0, sxh = 0, szh = 0;
        for (int iz = 0; iz < f.Height; iz++)
            for (int ix = 0; ix < f.Width; ix++)
            {
                int k = iz * f.Width + ix;
                if (zones[k] != zone) continue;
                double h = f.Values[k];
                n++; sx += ix; sz += iz; sxx += ix * ix; szz += iz * iz; sxz += ix * iz; sh += h; sxh += ix * h; szh += iz * h;
            }
        // Solve [[n sx sz][sx sxx sxz][sz sxz szz]] · [a b c] = [sh sxh szh] by Cramer's rule.
        double Det(double a1, double a2, double a3, double b1, double b2, double b3, double c1, double c2, double c3) =>
            a1 * (b2 * c3 - b3 * c2) - a2 * (b1 * c3 - b3 * c1) + a3 * (b1 * c2 - b2 * c1);
        double d = Det(n, sx, sz, sx, sxx, sxz, sz, sxz, szz);
        if (Math.Abs(d) < 1e-9) return (n > 0 ? sh / n : 0, 0, 0);   // too few or collinear cells: flat at the mean
        return (Det(sh, sx, sz, sxh, sxx, sxz, szh, sxz, szz) / d,
                Det(n, sh, sz, sx, sxh, sxz, sz, szh, szz) / d,
                Det(n, sx, sh, sx, sxx, sxh, sz, sxz, szh) / d);
    }

    /// <summary>
    /// Approximate distance (m) from each worked cell to the nearest natural cell (two-pass 3-4 chamfer).
    /// Natural cells are 0; the window edge is not treated as natural.
    /// </summary>
    public static double[] DistanceInside(int[] zones, int w, int h, double cell)
    {
        const int Ortho = 3, Diag = 4, Far = int.MaxValue / 2;
        var d = new int[w * h];
        for (int k = 0; k < d.Length; k++) d[k] = zones[k] < 0 ? 0 : Far;
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                int k = z * w + x;
                if (d[k] == 0) continue;
                int best = d[k];
                if (x > 0) best = Math.Min(best, d[k - 1] + Ortho);
                if (z > 0)
                {
                    best = Math.Min(best, d[k - w] + Ortho);
                    if (x > 0) best = Math.Min(best, d[k - w - 1] + Diag);
                    if (x < w - 1) best = Math.Min(best, d[k - w + 1] + Diag);
                }
                d[k] = best;
            }
        for (int z = h - 1; z >= 0; z--)
            for (int x = w - 1; x >= 0; x--)
            {
                int k = z * w + x;
                if (d[k] == 0) continue;
                int best = d[k];
                if (x < w - 1) best = Math.Min(best, d[k + 1] + Ortho);
                if (z < h - 1)
                {
                    best = Math.Min(best, d[k + w] + Ortho);
                    if (x < w - 1) best = Math.Min(best, d[k + w + 1] + Diag);
                    if (x > 0) best = Math.Min(best, d[k + w - 1] + Diag);
                }
                d[k] = best;
            }
        var result = new double[d.Length];
        for (int k = 0; k < d.Length; k++) result[k] = d[k] >= Far ? double.MaxValue : d[k] * cell / Ortho;
        return result;
    }
}
