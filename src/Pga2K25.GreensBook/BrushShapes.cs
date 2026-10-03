using System.Collections.Concurrent;
using SkiaSharp;

namespace Pga2K25.GreensBook;

/// <summary>
/// The Course Designer's landscaping brush images (assets/brushes), as weight masks. A height stamp's
/// <c>type</c> picks one: palette slot + 8 for slots 0–29, + 9 for slots 30–68 (found on a test plot with one
/// dab of each brush). The mask spans ±scale metres around the stamp, top towards +Z at rotation 0.
/// </summary>
public sealed class BrushShape
{
    public int Size { get; }
    readonly float[] _w;

    /// <summary>Brightest weight in the image: soft brushes' icons are dimmer (the soft round peaks near 0.45).</summary>
    public double Peak { get; }

    BrushShape(int size, float[] w) { Size = size; _w = w; Peak = w.Max(); }

    /// <summary>Palette slot for a stamp type, or null when the type isn't a palette brush.</summary>
    public static int? Slot(int type) => type switch
    {
        >= 8 and <= 37 => type - 8,
        >= 39 and <= 77 => type - 9,
        _ => null,
    };

    static readonly ConcurrentDictionary<int, BrushShape?> Cache = new();

    /// <summary>The brush for a stamp type, or null when it has no known shape.</summary>
    public static BrushShape? ForType(int type) => Slot(type) is int slot ? Cache.GetOrAdd(slot, Load) : null;

    static BrushShape? Load(int slot)
    {
        using var stream = typeof(BrushShape).Assembly.GetManifestResourceStream($"brush.shape_{slot:00}.png");
        if (stream is null) return null;
        using var bitmap = SKBitmap.Decode(stream);
        if (bitmap is null) return null;
        int n = bitmap.Width;
        var w = new float[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var c = bitmap.GetPixel(x, Math.Min(y, bitmap.Height - 1));
                // The screenshots leave a ~1 % background inside the tile; tapering the outer few pixels to zero
                // stops it ending in a hard square edge under every large brush, without trimming soft tails.
                const int taper = 4;
                int edge = Math.Min(Math.Min(x, y), Math.Min(n - 1 - x, n - 1 - y));
                float fade = edge >= taper ? 1 : (float)(0.5 - 0.5 * Math.Cos(Math.PI * edge / taper));
                w[y * n + x] = fade * (c.Red + c.Green + c.Blue) / (3f * 255f);
            }
        return new BrushShape(n, w);
    }

    /// <summary>Weight at brush-local (u, v) in [-1, 1] (v up = +Z), bilinear; 0 outside.</summary>
    public double Weight(double u, double v)
    {
        double x = (u + 1) / 2 * (Size - 1), y = (1 - v) / 2 * (Size - 1);
        if (x < 0 || y < 0 || x > Size - 1 || y > Size - 1) return 0;
        int i = (int)x, j = (int)y, i1 = Math.Min(i + 1, Size - 1), j1 = Math.Min(j + 1, Size - 1);
        double tx = x - i, ty = y - j;
        return (_w[j * Size + i] * (1 - tx) + _w[j * Size + i1] * tx) * (1 - ty)
             + (_w[j1 * Size + i] * (1 - tx) + _w[j1 * Size + i1] * tx) * ty;
    }
}
