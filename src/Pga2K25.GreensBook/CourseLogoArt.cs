using Pga2K25.CourseFormat;
using SkiaSharp;

namespace Pga2K25.GreensBook;

/// <summary>
/// The course's logo images (Logos.json in the output folder), decoded once and shared by a product's PNG and PDF.
/// Each product asks for the kind and tone that suit its background (<see cref="CourseLogos.Pick"/>); with no logo set
/// every request is null and the product prints as before.
/// </summary>
public sealed class CourseLogoArt : IDisposable
{
    readonly string _dir;
    readonly CourseLogos _set;
    readonly Dictionary<string, SKImage?> _images = [];

    CourseLogoArt(string outputDir, CourseLogos set) { _dir = outputDir; _set = set; }

    /// <summary>No logo: every product prints as it did without one.</summary>
    public static CourseLogoArt None { get; } = new("", new CourseLogos());

    public static CourseLogoArt Load(string outputDir) => new(outputDir, CourseLogosFile.Load(outputDir));

    public bool IsEmpty => _set.IsEmpty;

    /// <summary>The nearest supplied logo to the kind and tone asked for, with the kind it actually is; null when none.</summary>
    public (SKImage Image, LogoKind Kind)? Get(LogoKind kind, LogoTone tone)
    {
        if (_set.Pick(kind, tone) is not { } pick) return null;
        if (!_images.TryGetValue(pick.File, out var image))
        {
            try { image = SKImage.FromEncodedData(CourseLogosFile.ImagePath(_dir, pick.File)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { image = null; }
            _images[pick.File] = image;
        }
        return image is null ? null : (image, pick.Kind);
    }

    /// <summary>Draws <paramref name="image"/> as large as fits <paramref name="box"/>, keeping its shape, against the box's left edge; returns where it went.</summary>
    public static SKRect Draw(SKCanvas canvas, SKImage image, SKRect box)
    {
        float s = Math.Min(box.Width / image.Width, box.Height / image.Height);
        float w = image.Width * s, h = image.Height * s;
        var at = new SKRect(box.Left, box.MidY - h / 2, box.Left + w, box.MidY + h / 2);
        canvas.DrawImage(image, at, new SKSamplingOptions(SKCubicResampler.Mitchell));
        return at;
    }

    public void Dispose()
    {
        foreach (var image in _images.Values) image?.Dispose();
        _images.Clear();
    }
}
