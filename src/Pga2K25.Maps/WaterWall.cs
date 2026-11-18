using SkiaSharp;

namespace Pga2K25.Maps;

/// <summary>
/// Walled water (<see cref="CourseLayers.WaterWalls"/>): the water, then its wall, a dark line along the edge with pale
/// stones picked out along it, so a bunker-walled stream or pond reads as a built channel on every product (Alex's
/// pick, option B, 17 Nov 2026). Drawn after the turf, so the water covers whatever was drawn under the pond, except the
/// playing surfaces drawn after the wall (<see cref="CourseLayers.WaterWallOver"/>), which stay on top (18 Nov 2026).
/// </summary>
public static class WaterWall
{
    /// <summary>One wall: its outline and the surfaces that stay on top of it, already projected.</summary>
    public sealed record Piece(SKPath Outline, IReadOnlyList<SKPath> Over);

    /// <param name="fill">The water colour; null draws only the wall (when the water is already on top).</param>
    /// <param name="ink">The wall line's colour (the theme's ink, or a dark one on painted themes).</param>
    /// <param name="stone">The stones' colour (usually the paper).</param>
    /// <param name="width">The wall line's width in canvas units; stones are 0.4 of it.</param>
    /// <param name="texture">Drawn over the water inside each outline (a theme's ripples), within the same clip.</param>
    public static void Draw(SKCanvas canvas, IEnumerable<Piece> walls, SKColor? fill, SKColor ink, SKColor stone, float width,
        Action<SKCanvas, SKPath>? texture = null)
    {
        using var water = new SKPaint { Color = fill ?? SKColors.Transparent, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var line = new SKPaint { Color = ink, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeJoin = SKStrokeJoin.Round };
        using var dash = SKPathEffect.CreateDash([width * 0.9f, width * 1.8f], 0);
        using var stones = new SKPaint
        {
            Color = stone, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width * 0.4f, StrokeCap = SKStrokeCap.Round, PathEffect = dash,
        };
        foreach (var w in walls)
        {
            canvas.Save();
            // Surfaces drawn after the wall stay on top: the water and the wall stop at them.
            foreach (var over in w.Over) canvas.ClipPath(over, SKClipOperation.Difference, antialias: true);
            if (fill is not null) canvas.DrawPath(w.Outline, water);
            if (texture is not null)
            {
                canvas.Save();
                canvas.ClipPath(w.Outline, antialias: true);
                texture(canvas, w.Outline);
                canvas.Restore();
            }
            canvas.DrawPath(w.Outline, line);
            canvas.DrawPath(w.Outline, stones);
            canvas.Restore();
        }
    }

    /// <summary>Walls without anything on top (paths in canvas units).</summary>
    public static void Draw(SKCanvas canvas, IEnumerable<SKPath> walls, SKColor? fill, SKColor ink, SKColor stone, float width) =>
        Draw(canvas, walls.Select(w => new Piece(w, [])), fill, ink, stone, width);

    /// <summary>The walls of <paramref name="layers"/> through a map projection.</summary>
    public static void Draw(SKCanvas canvas, CourseLayers layers, MapProjection p, SKColor? fill, SKColor ink, SKColor stone, float width,
        Action<SKCanvas, SKPath>? texture = null) =>
        Draw(canvas, layers.WaterWalls, layers.WaterWallOver, w => p.Path(w, true), fill, ink, stone, width, texture);

    /// <summary>Walls given in world space with the surfaces on top of each, projected by <paramref name="project"/>.</summary>
    public static void Draw(SKCanvas canvas, IReadOnlyList<List<CourseFormat.Vec2>> walls, IReadOnlyList<List<List<CourseFormat.Vec2>>> over,
        Func<List<CourseFormat.Vec2>, SKPath> project, SKColor? fill, SKColor ink, SKColor stone, float width,
        Action<SKCanvas, SKPath>? texture = null)
    {
        if (walls.Count == 0) return;
        var pieces = walls.Select((w, i) => new Piece(project(w), i < over.Count ? over[i].Select(project).ToList() : [])).ToList();
        try { Draw(canvas, pieces, fill, ink, stone, width, texture); }
        finally
        {
            foreach (var piece in pieces) { piece.Outline.Dispose(); foreach (var o in piece.Over) o.Dispose(); }
        }
    }
}
