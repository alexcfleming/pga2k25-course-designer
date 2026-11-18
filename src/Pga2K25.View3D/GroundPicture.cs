using System.Runtime.InteropServices;
using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using SkiaSharp;

namespace Pga2K25.View3D;

/// <summary>
/// The picture draped over the model's ground, looking straight down: natural ground tinted by slope and shore,
/// then the drawn surfaces (rough, fairway, waste, roads, paths, greens, bunkers) and the water beds. Pixel (0, 0)
/// is the model's <see cref="Scene3D.Origin"/>; X runs right and Z runs down. Flat colour only: the page lights it.
/// </summary>
public static class GroundPicture
{
    static readonly SKColor Native = SKColor.Parse("#7f8c58"), NativeDry = SKColor.Parse("#9c956a"), Rock = SKColor.Parse("#8d8577");
    static readonly SKColor Beach = SKColor.Parse("#d8caa0"), SeaBedShallow = SKColor.Parse("#b3aa84"), SeaBedDeep = SKColor.Parse("#4a6866");
    static readonly SKColor Rough = SKColor.Parse("#5f8a43"), Fairway = SKColor.Parse("#7ab356"), FairwayStripe = SKColor.Parse("#84bd5f");
    static readonly SKColor Green = SKColor.Parse("#93d06d"), Fringe = SKColor.Parse("#6fa74f");
    static readonly SKColor Sand = SKColor.Parse("#ecdcaa"), Waste = SKColor.Parse("#cdbd94");
    static readonly SKColor Path = SKColor.Parse("#c9bfa8"), Road = SKColor.Parse("#b3a283");
    static readonly SKColor WaterBed = SKColor.Parse("#3c6468"), Stream = SKColor.Parse("#4d8791");

    /// <summary>Width of the fringe drawn round each green, and of one mowing stripe on the fairways (m).</summary>
    const float FringeMetres = 1.6f, StripeMetres = 7f;

    public sealed record Result(byte[] Jpeg, int Width, int Height, double PixelsPerMetre);

    public static Result Build(Scene3D scene, View3DOptions options)
    {
        var survey = scene.Survey;
        double spanX = (scene.Width - 1) * scene.Cell, spanZ = (scene.Depth - 1) * scene.Cell;
        double ppm = Math.Min(4, Math.Min(options.MaxTexture / spanX, options.MaxTexture / spanZ));
        int w = Math.Max(2, (int)Math.Round(spanX * ppm)), h = Math.Max(2, (int)Math.Round(spanZ * ppm));
        double sx = spanX / w, sz = spanZ / h;   // metres per pixel

        var rgba = new byte[w * h * 4];
        double? sea = survey.SeaLevelMetres;
        Parallel.For(0, h, py =>
        {
            for (int px = 0; px < w; px++)
            {
                var p = new Vec2(scene.Origin.X + (px + 0.5) * sx, scene.Origin.Z + (py + 0.5) * sz);
                double ground = survey.Field.Sample(p), slope = survey.Field.Gradient(p).Length;
                var c = Mix(Native, NativeDry, Noise(p.X / 60, p.Z / 60));
                c = Mix(c, Rock, Smooth(0.45, 0.9, slope));
                if (sea is { } level)
                {
                    double above = ground - level;
                    if (above < 0) c = Mix(SeaBedShallow, SeaBedDeep, Math.Clamp(-above / 4, 0, 1));
                    else if (above < 1) c = Mix(Beach, c, above);
                }
                int i = (py * w + px) * 4;
                rgba[i] = c.Red; rgba[i + 1] = c.Green; rgba[i + 2] = c.Blue; rgba[i + 3] = 255;
            }
        });

        using var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque));
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Scale((float)(1 / sx), (float)(1 / sz));
            canvas.Translate((float)-scene.Origin.X, (float)-scene.Origin.Z);
            DrawSurfaces(canvas, survey.Layers);
            if (survey.InlandWater is { } water)
                foreach (var body in water.Bodies)
                    Fill(canvas, body.Shores, body.MaxDepthMetres > Scene3D.MinWaterDepthMetres ? WaterBed : Stream, SKPathFillType.EvenOdd);
        }

        // A little mottle over everything, so mown areas aren't one flat colour: fine grain and a broad drift.
        Marshal.Copy(bitmap.GetPixels(), rgba, 0, rgba.Length);
        Parallel.For(0, h, py =>
        {
            for (int px = 0; px < w; px++)
            {
                double x = scene.Origin.X + px * sx, z = scene.Origin.Z + py * sz;
                double f = 1 + 0.07 * (Noise(x / 2.2, z / 2.2) - 0.5) + 0.08 * (Noise(x / 19 + 40, z / 19 + 40) - 0.5);
                int i = (py * w + px) * 4;
                for (int k = 0; k < 3; k++) rgba[i + k] = (byte)Math.Clamp(rgba[i + k] * f, 0, 255);
            }
        });
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 88);
        return new Result(data.ToArray(), w, h, 1 / sx);
    }

    static void DrawSurfaces(SKCanvas canvas, CourseLayers layers)
    {
        Fill(canvas, layers.Grass, Rough);
        Fill(canvas, layers.Fairways, Fairway);
        // Mowing stripes: a soft repeating band across every fairway.
        using (var stripes = new SKPaint { IsAntialias = true })
        using (var path = Polygons(layers.Fairways, SKPathFillType.Winding))
        {
            stripes.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(StripeMetres * 2, StripeMetres * 2),
                [Fairway.WithAlpha(0), FairwayStripe, FairwayStripe, Fairway.WithAlpha(0)], [0.02f, 0.08f, 0.48f, 0.54f], SKShaderTileMode.Repeat);
            canvas.DrawPath(path, stripes);
        }

        Fill(canvas, layers.WasteAreas, Waste);
        Stroke(canvas, layers.WasteRibbons, Waste);
        Dab(canvas, layers.WasteBrushes, Waste);
        Stroke(canvas, layers.Roads, Road);
        Stroke(canvas, layers.Paths, Path);

        using (var fringe = new SKPaint { IsAntialias = true, Color = Fringe, Style = SKPaintStyle.StrokeAndFill, StrokeWidth = FringeMetres * 2, StrokeJoin = SKStrokeJoin.Round })
        using (var path = Polygons(layers.Greens, SKPathFillType.Winding))
            canvas.DrawPath(path, fringe);
        Fill(canvas, layers.Greens, Green);

        Fill(canvas, layers.MergedBunkers(), Sand);
        Fill(canvas, layers.WaterAreas, Stream);
        Stroke(canvas, layers.WaterRibbons, Stream);
        // Walled water over everything drawn before it, its wall about 0.8 m wide (this canvas is in metres); surfaces
        // drawn after the wall stay on top.
        Maps.WaterWall.Draw(canvas, layers.WaterWalls, layers.WaterWallOver, w => Polygons([w], SKPathFillType.Winding),
            Stream, SKColor.Parse("#5a5548"), SKColor.Parse("#cfc7b2"), 0.8f);
    }

    static SKPath Polygons(IEnumerable<IReadOnlyList<Vec2>> loops, SKPathFillType fill)
    {
        var path = new SKPath { FillType = fill };
        foreach (var loop in loops)
        {
            if (loop.Count < 3) continue;
            path.MoveTo((float)loop[0].X, (float)loop[0].Z);
            for (int i = 1; i < loop.Count; i++) path.LineTo((float)loop[i].X, (float)loop[i].Z);
            path.Close();
        }
        return path;
    }

    static void Fill(SKCanvas canvas, IEnumerable<IReadOnlyList<Vec2>> loops, SKColor colour, SKPathFillType fill = SKPathFillType.Winding)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = colour };
        using var path = Polygons(loops, fill);
        canvas.DrawPath(path, paint);
    }

    static void Stroke(SKCanvas canvas, IEnumerable<Ribbon> ribbons, SKColor colour)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = colour, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        foreach (var r in ribbons)
        {
            if (r.Points.Count < 2) continue;
            using var path = new SKPath();
            path.MoveTo((float)r.Points[0].X, (float)r.Points[0].Z);
            for (int i = 1; i < r.Points.Count; i++) path.LineTo((float)r.Points[i].X, (float)r.Points[i].Z);
            paint.StrokeWidth = (float)r.Width;
            canvas.DrawPath(path, paint);
        }
    }

    static void Dab(SKCanvas canvas, IEnumerable<Disc> discs, SKColor colour)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = colour };
        foreach (var d in discs) canvas.DrawCircle((float)d.Centre.X, (float)d.Centre.Z, (float)d.Radius, paint);
    }

    static SKColor Mix(SKColor a, SKColor b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new SKColor((byte)(a.Red + (b.Red - a.Red) * t), (byte)(a.Green + (b.Green - a.Green) * t), (byte)(a.Blue + (b.Blue - a.Blue) * t));
    }

    static double Smooth(double from, double to, double x)
    {
        double t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Smooth value noise, 0–1, from a lattice hash: the same picture every build.</summary>
    static double Noise(double x, double z)
    {
        int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
        double tx = x - ix, tz = z - iz;
        tx = tx * tx * (3 - 2 * tx); tz = tz * tz * (3 - 2 * tz);
        double a = Hash(ix, iz), b = Hash(ix + 1, iz), c = Hash(ix, iz + 1), d = Hash(ix + 1, iz + 1);
        return (a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz;
    }

    static double Hash(int x, int z)
    {
        uint n = unchecked((uint)(x * 374761393 + z * 668265263));
        n = (n ^ (n >> 13)) * 1274126177;
        return ((n ^ (n >> 16)) & 0xffffff) / (double)0xffffff;
    }
}
