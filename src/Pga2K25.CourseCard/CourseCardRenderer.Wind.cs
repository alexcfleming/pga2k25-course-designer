using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using SkiaSharp;

namespace Pga2K25.CourseCard;

public sealed partial class CourseCardRenderer
{
    /// <summary>The course's wind rose (from Place.json), or null for no wind card.</summary>
    public WindRose? Wind { get; init; }

    /// <summary>Each hole's exposure to <see cref="Wind"/>, in hole order.</summary>
    public IReadOnlyList<HoleWind> HoleWinds { get; init; } = [];

    static readonly SKColor IntoColour = SKColor.Parse("#A8432F"), AcrossColour = SKColor.Parse("#C9A04A"), DownColour = SKColor.Parse("#4F7F8F");

    /// <summary>
    /// Wind card: the rose (how often the wind blows from each direction, by speed) and a table of how each hole
    /// plays in it. The game sets wind per round and stores none in the course file, so this is a design aid.
    /// </summary>
    public void DrawWindPage(SKCanvas canvas)
    {
        if (Wind is null) return;
        var (w, h) = (CardPage.Width, CardPage.Height);
        canvas.Clear(_salt);
        float x0 = (_logo is null ? 0.030f : 0.082f) * w;
        Text(canvas, Title, x0, (1 - 0.955f) * h, 30, _bold, _ink, tracking: 0.32f * 30);
        Text(canvas, "Wind card  ·  how often the wind blows from each direction, and how each hole plays in it", x0, (1 - 0.925f) * h, 12.5f, _italic, Muted);
        string source = Wind.Source + (Wind.Hours is int n ? $"  ·  {n:N0} hours" : "") + (Wind.Fetched is DateTime f ? $"  ·  fetched {f:d MMM yyyy}" : "");
        Text(canvas, $"{source}  ·  a design aid: the game sets the wind each round and the course file stores none", x0, (1 - 0.902f) * h, 9, _serif, Faint);
        Text(canvas, $"PREVAILING  {WindRose.SectorNames[WindRose.SectorOf(Wind.PrevailingFrom)]}", 0.970f * w, (1 - 0.955f) * h, 14, _bold, _ink, SKTextAlign.Right);
        Text(canvas, $"calm {Wind.Calm:P0}  ·  breezy (8 mph +) {Wind.Breezy:P0}" + (Wind.MeanMph is double m ? $"  ·  mean {m:0.0} mph" : ""),
            0.970f * w, (1 - 0.925f) * h, 10.5f, _serif, Muted, SKTextAlign.Right);
        using (var rule = Stroke(_fog, 1.3f)) canvas.DrawLine(0.024f * w, (1 - 0.884f) * h, 0.970f * w, (1 - 0.884f) * h, rule);
        if (_logo is not null) DrawLogo(canvas, FigRect(0.024f, 0.898f, 0.046f, 0.0674f, CardPage));

        var roseBox = FigRect(0.030f, 0.06f, 0.40f, 0.78f, CardPage);
        WindRoseDrawing.Draw(canvas, roseBox, Wind, _serif, _ink, _salt, 1.6f);

        if (HoleWinds.Count > 0) DrawHoleWinds(canvas, FigRect(0.48f, 0.06f, 0.49f, 0.80f, CardPage));
    }

    void DrawHoleWinds(SKCanvas canvas, SKRect r)
    {
        var rows = HoleWinds;
        float[] cols = [0, 0.07f, 0.13f, 0.27f, 0.36f, 0.45f, 0.54f, 0.63f, 0.84f];
        string[] heads = ["HOLE", "PAR", "PLAYS TO", "INTO", "ACROSS L", "ACROSS R", "DOWN", "MIX", "CHARACTER"];
        float X(int c) => r.Left + cols[c] * r.Width;
        float rowH = Math.Min(24, (r.Height - 110) / rows.Count), y = r.Top + 14;
        for (int c = 0; c < heads.Length; c++)
            Text(canvas, heads[c], X(c) + (c is >= 3 and <= 6 ? 0.07f * r.Width : 0), y, 8, _serif, Muted, c is >= 3 and <= 6 ? SKTextAlign.Right : SKTextAlign.Left, tracking: 0.8f);
        y += 8;
        using (var rule = Stroke(_fog, 1)) canvas.DrawLine(r.Left, y, r.Right, y, rule);
        foreach (var hw in rows)
        {
            float base_ = y + rowH * 0.68f;
            if (hw.Hole % 2 == 0) using (var band = Fill(_fog.WithAlpha(45))) canvas.DrawRect(r.Left, y, r.Width, rowH, band);
            Text(canvas, hw.Hole.ToString(), X(0), base_, 10.5f, _bold, _ink);
            Text(canvas, hw.Par.ToString(), X(1), base_, 10.5f, _serif, _ink);
            Text(canvas, $"{WindRose.SectorNames[WindRose.SectorOf(hw.Bearing)]}  {hw.Bearing}°", X(2), base_, 10.5f, _serif, _ink);
            double[] v = [hw.Into, hw.FromLeft, hw.FromRight, hw.Down];
            for (int i = 0; i < 4; i++) Text(canvas, $"{v[i] * 100:0}%", X(3 + i) + 0.07f * r.Width, base_, 10.5f, _serif, _ink, SKTextAlign.Right);
            float bx = X(7), bw = (cols[8] - cols[7] - 0.02f) * r.Width, bh = rowH * 0.42f, by = y + (rowH - bh) / 2;
            foreach (var (share, colour) in new[] { (hw.Into, IntoColour), (hw.Across, AcrossColour), (hw.Down, DownColour) })
            {
                using var f = Fill(colour);
                canvas.DrawRect(bx, by, (float)share * bw, bh, f);
                bx += (float)share * bw;
            }
            var ch = hw.Character;
            Text(canvas, ch, X(8), base_, 10, _italic, ch switch { "Into the wind" => IntoColour, "Downwind" => DownColour, "Crosswind" => AcrossColour, _ => Muted });
            y += rowH;
        }
        using (var rule = Stroke(_fog, 1)) canvas.DrawLine(r.Left, y, r.Right, y, rule);

        // Key and summary.
        y += 22;
        float kx = r.Left;
        foreach (var (label, colour) in new[] { ("into", IntoColour), ("across", AcrossColour), ("downwind", DownColour) })
        {
            using (var f = Fill(colour)) canvas.DrawRect(kx, y - 8, 18, 8, f);
            Text(canvas, label, kx + 24, y, 10, _serif, _ink);
            kx += 24 + Measure(label, 10, _serif) + 22;
        }
        y += 18;
        Text(canvas, "Share of breezy hours (8 mph and up) with the wind against, across or behind the line of play, back tee to green.",
            r.Left, y, 8.5f, _italic, Faint);
        y += 28;
        foreach (var part in WindExposure.Summary(rows).Split("  ·  "))
        {
            int colon = part.IndexOf(':');
            string label = char.ToUpperInvariant(part[0]) + part[1..colon];
            Text(canvas, label, r.Left, y, 11, _serif, Muted);
            Text(canvas, part[(colon + 1)..].Trim(), r.Left + 0.30f * r.Width, y, 11, _bold, _ink);
            y += 18;
        }
    }
}
