using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using SkiaSharp;

namespace Pga2K25.YardageBook;

/// <summary>
/// Illustrated relief for a yardage page, instead of contour lines that would fight the distance circles: a soft
/// hillshade wash lit from the top left of the page, and short hachures running down the fall line, longer and
/// closer on steeper ground and none on the flat. Both come from the hole's terrain model, the same one as the
/// spot heights. The wash is a small raster drawn smoothed (so a PDF page stays light); the hachures are vectors.
/// </summary>
sealed class Relief : IDisposable
{
    /// <summary>Page points per wash pixel, and between hachure sites.</summary>
    const float ShadeCell = 2f, HachureCell = 6f;
    /// <summary>Slope (rise over run) below which ground counts as flat: no hachures.</summary>
    const double FlatSlope = 0.08;
    /// <summary>
    /// Slopes are read across this much ground (m), so the hillsides show and the small bumps the brush replay
    /// leaves on the flat don't.
    /// </summary>
    const double ReachMetres = 5;

    static readonly SKColor Shadow = new(90, 80, 60), Light = SKColors.White, HachureInk = SKColor.Parse("#b3aa98");

    readonly SKImage _shade;
    readonly SKRect _rect;
    readonly SKPath _hachures;

    Relief(SKImage shade, SKRect rect, SKPath hachures) => (_shade, _rect, _hachures) = (shade, rect, hachures);

    public static Relief? Build(HoleAnalysis a, MapProjection p, SKRect rect)
    {
        if (a.Field is not { } field) return null;
        double perPoint = 1 / p.PointsPerMetre;   // metres per page point
        float reach = (float)(ReachMetres * p.PointsPerMetre);
        double H(float x, float y) => field.Sample(p.Unmap(new SKPoint(x, y)));
        // Slope along the page axes (page y runs down), as rise over run.
        (double Gx, double Gy) Gradient(float x, float y, float d) =>
            ((H(x + d, y) - H(x - d, y)) / (2 * d * perPoint), (H(x, y + d) - H(x, y - d)) / (2 * d * perPoint));

        int w = (int)Math.Ceiling(rect.Width / ShadeCell), h = (int)Math.Ceiling(rect.Height / ShadeCell);
        using var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                float x = rect.Left + (i + 0.5f) * ShadeCell, y = rect.Top + (j + 0.5f) * ShadeCell;
                var (gx, gy) = Gradient(x, y, reach);
                // Ground rising toward the bottom right faces the top-left light.
                double lit = 0.7071 * (gx + gy);
                var (c, peak) = lit >= 0 ? (Light, 0.4) : (Shadow, 0.12);
                byte alpha = (byte)Math.Round(Math.Min(1, Math.Abs(lit) / 0.2) * peak * 255);
                bitmap.SetPixel(i, j, new SKColor((byte)(c.Red * alpha / 255), (byte)(c.Green * alpha / 255), (byte)(c.Blue * alpha / 255), alpha));
            }

        using var b = new SKPathBuilder();
        int row = 0;
        for (float y = rect.Top + HachureCell / 2; y < rect.Bottom; y += HachureCell, row++)
        {
            int col = 0;
            for (float x = rect.Left + HachureCell / 2; x < rect.Right; x += HachureCell, col++)
            {
                var (gx, gy) = Gradient(x, y, reach);
                double slope = Math.Sqrt(gx * gx + gy * gy);
                // Gentle ground gets every other site, steep ground every one.
                if (slope < FlatSlope || (slope < 0.15 && (row + col) % 2 == 1)) continue;
                float len = (float)Math.Clamp(2 + slope * 20, 2, 6.5);
                float ux = (float)(-gx / slope), uy = (float)(-gy / slope);
                // A steady jitter, so the strokes don't line up in a grid.
                float jx = Jitter(row, col, 1) * 2.4f, jy = Jitter(row, col, 2) * 2.4f;
                b.MoveTo(x + jx, y + jy);
                b.LineTo(x + jx + ux * len, y + jy + uy * len);
            }
        }
        return new Relief(SKImage.FromBitmap(bitmap), rect, b.Detach());
    }

    static float Jitter(int row, int col, int salt)
    {
        uint n = (uint)(row * 73856093 ^ col * 19349663 ^ salt * 83492791);
        n = (n ^ (n >> 13)) * 1274126177;
        return ((n & 0xffff) / 65535f) - 0.5f;
    }

    /// <summary>The hillshade wash, at <paramref name="opacity"/> of full strength.</summary>
    public void DrawShade(SKCanvas canvas, float opacity)
    {
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * opacity)) };
        canvas.DrawImage(_shade, new SKRect(_rect.Left, _rect.Top, _rect.Left + _shade.Width * ShadeCell, _rect.Top + _shade.Height * ShadeCell),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
    }

    public void DrawHachures(SKCanvas canvas, float opacity)
    {
        using var paint = new SKPaint
        {
            Color = HachureInk.WithAlpha((byte)(230 * opacity)), StrokeWidth = 0.5f, IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round,
        };
        canvas.DrawPath(_hachures, paint);
    }

    public void Dispose() { _shade.Dispose(); _hachures.Dispose(); }
}
