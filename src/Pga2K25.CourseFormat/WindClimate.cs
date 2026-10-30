namespace Pga2K25.CourseFormat;

/// <summary>
/// A modelled wind rose for an invented place. Directions are a mix of bell curves round the compass (von Mises
/// lobes); speeds in each lobe follow a Weibull curve, the standard model for wind. A seed adds the small
/// sector-to-sector unevenness real records have. The same settings always give the same rose.
/// </summary>
public static class WindClimate
{
    public static readonly string[] Presets = ["trade", "west", "sea", "monsoon", "plains", "valley"];

    public static string Label(string preset) => preset switch
    {
        "trade" => "Trade winds: steady, one direction",
        "west" => "Maritime westerlies: windy, broad",
        "sea" => "Sea-breeze coast: onshore by day",
        "monsoon" => "Monsoon: two opposite seasons",
        "plains" => "Inland plains: variable, some calm",
        "valley" => "Mountain valley: channelled, often calm",
        _ => preset,
    };

    public static string ShortLabel(string preset) => Label(preset).Split(':')[0];

    /// <summary>The climates that need no sea.</summary>
    public static bool IsInland(string preset) => preset is "plains" or "valley";

    readonly record struct Lobe(double From, double Kappa, double Weight, double Scale, double Shape);

    /// <param name="preset">One of <see cref="Presets"/>.</param>
    /// <param name="prevailingFrom">The main wind's FROM bearing.</param>
    /// <param name="seaBearing">Bearing of the plot's sea, for the sea-breeze lobe (null: the prevailing bearing).</param>
    /// <param name="strength">Speed scale: 0.7 light, 1 moderate, 1.35 windy.</param>
    /// <param name="steadiness">Lobe tightness: below 1 wanders more, above 1 holds its direction.</param>
    public static WindRose Generate(string preset, int prevailingFrom, int? seaBearing, double strength, double steadiness, int seed)
    {
        double P = prevailingFrom, sea = seaBearing ?? prevailingFrom;
        var (calm, lobes) = preset switch
        {
            "trade" => (.04, new Lobe[] { new(P, 8, .9, 15, 3), new(P + 40, 3, .1, 9, 2.2) }),
            "west" => (.06, new Lobe[] { new(P, 2.2, .65, 16, 2), new(P + 180, 1.5, .35, 10, 1.8) }),
            "monsoon" => (.08, new Lobe[] { new(P, 4, .52, 12, 2.3), new(P + 180, 4, .48, 10, 2.3) }),
            "plains" => (.18, new Lobe[] { new(P, 1.2, .6, 10, 1.8), new(P + 90, .8, .4, 8, 1.7) }),
            "valley" => (.25, new Lobe[] { new(P, 6, .55, 9, 1.7), new(P + 180, 6, .45, 7, 1.6) }),
            _ => (.10, new Lobe[] { new(sea, 4, .45, 11, 2.4), new(sea + 180, 3, .2, 6, 2), new(P, 2, .35, 12, 2) }),
        };
        lobes = lobes.Select(l => l with { From = (l.From % 360 + 360) % 360, Kappa = l.Kappa * steadiness, Scale = l.Scale * strength }).ToArray();

        // Each lobe's share of each 22.5° sector, integrated in 2.5° steps.
        var share = Enumerable.Range(0, 16).Select(i => lobes.Select(l =>
        {
            double d = 0;
            for (int j = -4; j <= 4; j++) d += VonMises(i * 22.5 + j * 2.5, l.From, l.Kappa) * 2.5 * Math.PI / 180;
            return d * l.Weight;
        }).ToArray()).ToArray();

        var rng = Rng(seed);
        var jitter = Enumerable.Range(0, 16).Select(_ => 0.88 + rng() * 0.24).ToArray();
        double total = 0;
        for (int i = 0; i < 16; i++) total += share[i].Sum() * jitter[i];

        double[] edges = [.. WindRose.Edges, double.PositiveInfinity];
        var sectors = new List<List<double>>();
        double mean = 0;
        for (int i = 0; i < 16; i++)
        {
            var bands = new double[5];
            for (int k = 0; k < lobes.Length; k++)
            {
                var l = lobes[k];
                double w = share[i][k] * jitter[i] / total * (1 - calm), above = 1 - Weibull(edges[0], l);
                for (int b = 0; b < 5; b++) bands[b] += w * (Weibull(edges[b + 1], l) - Weibull(edges[b], l)) / above;
                mean += w * WeibullMeanAbove(edges[0], l);
            }
            sectors.Add([.. bands]);
        }
        return new WindRose
        {
            Kind = "model",
            Source = $"Modelled: {ShortLabel(preset)}, seed {seed}",
            Calm = calm,
            Sectors = sectors,
            MeanMph = mean + calm * 1.5,
        };
    }

    static double VonMises(double deg, double mu, double kappa) =>
        Math.Exp(kappa * Math.Cos((deg - mu) * Math.PI / 180)) / (2 * Math.PI * BesselI0(kappa));

    static double BesselI0(double x)
    {
        double s = 1, t = 1;
        for (int k = 1; k < 30; k++) { t *= x / 2 / k; s += t * t; }
        return s;
    }

    static double Weibull(double v, Lobe l) => double.IsPositiveInfinity(v) ? 1 : 1 - Math.Exp(-Math.Pow(v / l.Scale, l.Shape));

    /// <summary>Mean speed of the hours at or above <paramref name="v0"/>, by a simple sum in 0.5 mph steps.</summary>
    static double WeibullMeanAbove(double v0, Lobe l)
    {
        double above = 1 - Weibull(v0, l), sum = 0;
        for (double v = v0; v < 80; v += 0.5) sum += (v + 0.25) * (Weibull(v + 0.5, l) - Weibull(v, l));
        return above > 0 ? sum / above : v0;
    }

    /// <summary>mulberry32, so a seed gives the same rose on every machine and version.</summary>
    static Func<double> Rng(int seed)
    {
        uint s = unchecked((uint)seed);
        return () =>
        {
            s = unchecked(s + 0x6D2B79F5);
            uint t = s;
            t = unchecked((t ^ (t >> 15)) * (1 | t));
            t ^= unchecked(t + (t ^ (t >> 7)) * (61 | t));
            return (t ^ (t >> 14)) / 4294967296.0;
        };
    }
}
