using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>How a style draws its rocks.</summary>
public enum RockLook
{
    /// <summary>Stone lit from the north-west (a light-to-dark gradient), a soft shadow to the south-east, a dark rim, and cracks on craggy shapes.</summary>
    Painted,
    /// <summary>One flat stone colour with a darker rim.</summary>
    Flat,
    /// <summary>An ink outline over the paper, hatched on the shaded south-east half, as a pen sketch shades a boulder.</summary>
    Ink,
}

/// <param name="Light">Lit face (Painted), or the fill (Flat, Ink).</param>
/// <param name="Mid">Middle of the gradient (Painted).</param>
/// <param name="Dark">Shaded face (Painted), or the hatching (Ink).</param>
/// <param name="Rim">The outline.</param>
/// <param name="RimWidth">Outline width in canvas units; the shadow and hatching scale with it.</param>
public sealed record RockStyle(RockLook Look, SKColor Light, SKColor Mid, SKColor Dark, SKColor Rim, float RimWidth)
{
    public static RockStyle Painted(float rim = 0.55f) =>
        new(RockLook.Painted, SKColor.Parse("#e4dfd2"), SKColor.Parse("#b9b2a2"), SKColor.Parse("#8c8576"), SKColor.Parse("#4d483d"), rim);
    public static RockStyle Flat(SKColor fill, SKColor rim, float width = 0.5f) => new(RockLook.Flat, fill, fill, fill, rim, width);
    public static RockStyle Ink(SKColor paper, SKColor ink, float width = 0.6f) => new(RockLook.Ink, paper, paper, ink.WithAlpha(150), ink, width);
}

/// <summary>
/// Draws rocks as they show above the ground (<see cref="RockOutlines"/>): each outline in a style's look, the deepest-set first
/// so a rock standing higher overlaps its neighbours, as in game. Rocks go on after the turf and walled water and before the
/// lines of play, so a rock half of a pond's barrier sits on the water's edge.
/// </summary>
public static class RockArt
{
    public static void Draw(SKCanvas canvas, IEnumerable<RockOutline> rocks, Func<IReadOnlyList<Vec2>, SKPath> project, RockStyle style)
    {
        foreach (var r in rocks.OrderBy(r => (r.Rock.Height ?? 0) + r.VisibleWidth))
        {
            using var path = project(r.Outline);
            if (path.IsEmpty) continue;
            var bb = path.Bounds;
            if (bb.Width < 0.05f && bb.Height < 0.05f) continue;
            switch (style.Look)
            {
                case RockLook.Painted: Painted(canvas, path, bb, r, style); break;
                case RockLook.Flat:
                    using (var fill = new SKPaint { Color = style.Light, IsAntialias = true }) canvas.DrawPath(path, fill);
                    using (var rim = Stroke(style.Rim, style.RimWidth)) canvas.DrawPath(path, rim);
                    break;
                default: Ink(canvas, path, bb, style); break;
            }
        }
    }

    /// <summary>The rocks of <paramref name="course"/> through a map projection.</summary>
    public static void Draw(SKCanvas canvas, CourseModel course, MapProjection p, RockStyle style) =>
        Draw(canvas, RockOutlines.For(course), o => p.Path(o, true), style);

    static void Painted(SKCanvas canvas, SKPath path, SKRect bb, RockOutline r, RockStyle s)
    {
        float k = s.RimWidth / 0.55f;
        canvas.Save();
        canvas.Translate(1.2f * k, 1.6f * k);
        using (var shadow = new SKPaint { Color = SKColor.Parse("#2d2a22").WithAlpha(90), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.2f * k) })
            canvas.DrawPath(path, shadow);
        canvas.Restore();
        using (var stone = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(new SKPoint(bb.Left, bb.Top), new SKPoint(bb.Right, bb.Bottom), [s.Light, s.Mid, s.Dark], [0, 0.55f, 1], SKShaderTileMode.Clamp),
        })
            canvas.DrawPath(path, stone);
        using (var rim = Stroke(s.Rim, s.RimWidth)) canvas.DrawPath(path, rim);
        if (!r.Rough || Math.Max(bb.Width, bb.Height) < 6 * k) return;
        // A few cracks across the craggy shapes, seeded by the rock so they stay put.
        var rnd = new Random((int)(Math.Abs(r.Rock.Position.X * 31 + r.Rock.Position.Z * 17) % 100000));
        using var crack = Stroke(s.Rim.WithAlpha(140), s.RimWidth * 0.65f);
        canvas.Save();
        canvas.ClipPath(path, antialias: true);
        for (int i = 0; i < 3; i++)
        {
            var a = new SKPoint(bb.Left + (float)rnd.NextDouble() * bb.Width, bb.Top + (float)rnd.NextDouble() * bb.Height);
            canvas.DrawLine(a, new SKPoint(a.X + (float)(rnd.NextDouble() - 0.3) * bb.Width * 0.5f, a.Y + (float)(rnd.NextDouble() - 0.3) * bb.Height * 0.5f), crack);
        }
        canvas.Restore();
    }

    static void Ink(SKCanvas canvas, SKPath path, SKRect bb, RockStyle s)
    {
        using (var paper = new SKPaint { Color = s.Light, IsAntialias = true }) canvas.DrawPath(path, paper);
        float gap = Math.Max(1.2f, s.RimWidth * 2.6f);
        if (Math.Max(bb.Width, bb.Height) > gap * 2.5f)
        {
            canvas.Save();
            canvas.ClipPath(path, antialias: true);
            using var hatch = Stroke(s.Dark, s.RimWidth * 0.55f);
            for (float x = bb.MidX; x < bb.Right + bb.Height; x += gap) canvas.DrawLine(x, bb.Bottom, x - bb.Height * 0.5f, bb.MidY, hatch);
            canvas.Restore();
        }
        using var pen = Stroke(s.Rim, s.RimWidth);
        canvas.DrawPath(path, pen);
    }

    static SKPaint Stroke(SKColor c, float w) => new() { Color = c, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = w, StrokeJoin = SKStrokeJoin.Round };
}
