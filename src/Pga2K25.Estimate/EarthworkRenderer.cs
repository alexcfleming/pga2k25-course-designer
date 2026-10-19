using System.Globalization;
using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.Estimate;

/// <summary>
/// Cut/fill heat map of one hole, north up (game north = −Z, east = −X). Natural ground is a grey hillshade,
/// the hole's worked ground is coloured by depth of cut (orange) or fill (teal), other holes' play ground is pale.
/// </summary>
public static class EarthworkRenderer
{
    static readonly SKColor Cream = SKColor.Parse("#F4F1EA"), Ink = SKColor.Parse("#1f1d1a"), Gold = SKColor.Parse("#B09766"),
        Gray = SKColor.Parse("#6b665e"), Fill = SKColor.Parse("#2E8C73"), Cut = SKColor.Parse("#E06A3A");

    public static void RenderPng(HoleEstimate e, string courseName, string path, double pixelsPerMetre = 2)
    {
        var f = e.Raster.Finished;
        // Crop to the hole's own cells plus a little natural ground.
        int minX = f.Width, maxX = -1, minZ = f.Height, maxZ = -1;
        for (int iz = 0; iz < f.Height; iz++)
            for (int ix = 0; ix < f.Width; ix++)
                if (e.Raster.Counted[iz * f.Width + ix])
                { minX = Math.Min(minX, ix); maxX = Math.Max(maxX, ix); minZ = Math.Min(minZ, iz); maxZ = Math.Max(maxZ, iz); }
        if (maxX < 0) throw new InvalidOperationException("Hole has no cells.");

        int ppc = Math.Max(1, (int)Math.Round(pixelsPerMetre * f.Cell));
        int mapW = (maxX - minX + 1) * ppc, mapH = (maxZ - minZ + 1) * ppc;
        const int Header = 96, Footer = 64, Side = 24;
        int w = mapW + 2 * Side, h = mapH + Header + Footer;

        double limitFt = Math.Max(1, Percentile(e, 0.98) * Units.FeetPerMetre);
        using var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(Cream);

        using var cell = new SKPaint { IsAntialias = false };
        for (int iz = minZ; iz <= maxZ; iz++)
            for (int ix = minX; ix <= maxX; ix++)
            {
                int k = iz * f.Width + ix;
                SKColor c;
                if (e.Raster.Counted[k] && e.Raster.Zone[k] >= 0)
                {
                    double t = Math.Clamp(e.Earthwork.Dz[k] * Units.FeetPerMetre / limitFt, -1, 1);
                    c = Mix(SKColors.White, t >= 0 ? Fill : Cut, Math.Sqrt(Math.Abs(t)));
                }
                else
                {
                    double shade = Shade(f, ix, iz);
                    c = e.Manufactured[k] ? Mix(Cream, SKColor.Parse("#d8d2c4"), 0.6) : Mix(SKColor.Parse("#b9b3a6"), Cream, shade);
                }
                cell.Color = c;
                // North up: screen x runs east = −X, screen y runs south = +Z.
                float sx = Side + (maxX - ix) * ppc, sy = Header + (iz - minZ) * ppc;
                canvas.DrawRect(sx, sy, ppc, ppc, cell);
            }

        SKPoint Screen(Vec2 p) => new(
            (float)(Side + (maxX + 0.5 - (p.X - f.Origin.X) / f.Cell) * ppc),
            (float)(Header + ((p.Z - f.Origin.Z) / f.Cell - 0.5 - minZ) * ppc));
        using var route = new SKPaint { Color = Ink, StrokeWidth = 2, IsStroke = true, IsAntialias = true, PathEffect = SKPathEffect.CreateDash([8, 6], 0) };
        var r = e.Hole.Tees.Count > 0 ? new[] { e.Hole.Tees[^1] }.Concat(e.Hole.Route.Skip(1)).ToList() : e.Hole.Route.ToList();
        for (int i = 1; i < r.Count; i++) canvas.DrawLine(Screen(r[i - 1]), Screen(r[i]), route);
        using var dot = new SKPaint { Color = Ink, IsAntialias = true };
        foreach (var t in e.Hole.Tees) canvas.DrawCircle(Screen(t), 4, dot);

        using var title = new SKFont(SKTypeface.FromFamilyName("Lora", SKFontStyle.Bold) ?? SKTypeface.Default, 26);
        using var body = new SKFont(SKTypeface.FromFamilyName("Lora") ?? SKTypeface.Default, 15);
        using var ink = new SKPaint { Color = Ink, IsAntialias = true };
        using var gray = new SKPaint { Color = Gray, IsAntialias = true };
        var inv = CultureInfo.InvariantCulture;
        canvas.DrawText($"{courseName} · Hole {e.Hole.Number} · Par {e.Hole.Par} · Earthwork", Side, 38, SKTextAlign.Left, title, ink);
        canvas.DrawText(string.Format(inv, "Cut {0:N0} cu yd   Fill {1:N0} cu yd   Balanced on site {2:N0} cu yd",
            e.Earthwork.CutCuYd, e.Earthwork.FillCuYd, e.Balance.BalancedCuM * Units.CubicYardsPerCubicMetre), Side, 64, SKTextAlign.Left, body, ink);
        canvas.DrawText("ASSUMED STARTING GROUND · PLACEHOLDER zone values · not a bid", Side, 84, SKTextAlign.Left, body, gray);

        // Legend: cut ← 0 → fill, in feet.
        float ly = Header + mapH + 22, lx = Side, lw = 240;
        for (int i = 0; i < lw; i++)
        {
            double t = i / (lw - 1.0) * 2 - 1;
            cell.Color = Mix(SKColors.White, t >= 0 ? Fill : Cut, Math.Sqrt(Math.Abs(t)));
            canvas.DrawRect(lx + i, ly, 1, 14, cell);
        }
        canvas.DrawText(string.Format(inv, "cut {0:0} ft", limitFt), lx, ly + 34, SKTextAlign.Left, body, gray);
        canvas.DrawText(string.Format(inv, "fill {0:0} ft", limitFt), lx + lw - 60, ly + 34, SKTextAlign.Left, body, gray);
        using var goldPaint = new SKPaint { Color = Gold, IsAntialias = true };
        canvas.DrawText("Heights in feet, distances in yards. Grid 1 m from the course file's brush replay. N ↑", lx + lw + 24, ly + 12, SKTextAlign.Left, body, goldPaint);

        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    static double Percentile(HoleEstimate e, double q)
    {
        var v = new List<double>();
        for (int k = 0; k < e.Earthwork.Dz.Length; k++)
            if (e.Raster.Counted[k] && e.Raster.Zone[k] >= 0) v.Add(Math.Abs(e.Earthwork.Dz[k]));
        if (v.Count == 0) return 0;
        v.Sort();
        return v[(int)Math.Min(v.Count - 1, q * v.Count)];
    }

    /// <summary>0..1 hillshade from the north-west.</summary>
    static double Shade(GreensBook.Heightfield f, int ix, int iz)
    {
        int x0 = Math.Max(0, ix - 1), x1 = Math.Min(f.Width - 1, ix + 1), z0 = Math.Max(0, iz - 1), z1 = Math.Min(f.Height - 1, iz + 1);
        double gx = (f[x1, iz] - f[x0, iz]) / ((x1 - x0) * f.Cell), gz = (f[ix, z1] - f[ix, z0]) / ((z1 - z0) * f.Cell);
        // Light from screen upper-left, i.e. north (−Z) and west (+X): slopes falling that way are lit.
        double lit = 0.5 + 0.35 * (gz - gx) / Math.Sqrt(1 + gx * gx + gz * gz) * 3;
        return Math.Clamp(lit, 0, 1);
    }

    static SKColor Mix(SKColor a, SKColor b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new SKColor((byte)(a.Red + (b.Red - a.Red) * t), (byte)(a.Green + (b.Green - a.Green) * t), (byte)(a.Blue + (b.Blue - a.Blue) * t));
    }
}
