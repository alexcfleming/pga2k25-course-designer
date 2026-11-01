using System.Text.Json;

namespace Pga2K25.CourseFormat;

/// <summary>The shapes a course logo comes in: a round seal with the name, a mark without text, a mark beside a wordmark.</summary>
public enum LogoKind { Seal, Mark, Lockup }

/// <summary>A logo's colouring: full colour, one dark ink (for engraved and black-and-white products), or light for dark backgrounds.</summary>
public enum LogoTone { Colour, Ink, Light }

/// <summary>
/// The course's logo set: up to one image file for each <see cref="LogoKind"/> and <see cref="LogoTone"/>, copied into
/// the Logos folder beside the products and listed in Logos.json, written by the Studio's Logo card. Each product asks
/// for the kind and tone that suit its background and gets the nearest file the designer supplied (<see cref="Pick"/>).
/// </summary>
public sealed class CourseLogos
{
    /// <summary>File name in the Logos folder by slot, e.g. "seal-colour" → "seal-colour.png".</summary>
    public Dictionary<string, string> Files { get; set; } = [];

    public static string Slot(LogoKind kind, LogoTone tone) => $"{kind}-{tone}".ToLowerInvariant();

    public bool IsEmpty => Files.Count == 0;

    /// <summary>The slot's file name, or null when empty.</summary>
    public string? File(LogoKind kind, LogoTone tone) => Files.TryGetValue(Slot(kind, tone), out var f) && f.Length > 0 ? f : null;

    /// <summary>
    /// The nearest supplied logo to <paramref name="kind"/> in <paramref name="tone"/>, or null when the set is empty. The
    /// tone is kept first: ink falls back to colour, colour to ink, and light to colour (never ink, which would vanish on a
    /// dark ground). Then the kind: a seal and a mark stand in for each other, and a lockup falls back to the seal, then
    /// the mark (the product then prints the course name itself).
    /// </summary>
    public (LogoKind Kind, LogoTone Tone, string File)? Pick(LogoKind kind, LogoTone tone)
    {
        LogoTone[] tones = tone switch
        {
            LogoTone.Ink => [LogoTone.Ink, LogoTone.Colour],
            LogoTone.Light => [LogoTone.Light, LogoTone.Colour],
            _ => [LogoTone.Colour, LogoTone.Ink],
        };
        LogoKind[] kinds = kind switch
        {
            LogoKind.Mark => [LogoKind.Mark, LogoKind.Seal],
            LogoKind.Lockup => [LogoKind.Lockup, LogoKind.Seal, LogoKind.Mark],
            _ => [LogoKind.Seal, LogoKind.Mark],
        };
        foreach (var k in kinds)
            foreach (var t in tones)
                if (File(k, t) is { } f) return (k, t, f);
        return null;
    }
}

public static class CourseLogosFile
{
    public const string FileName = "Logos.json", Folder = "Logos";

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string PathFor(string outputDir) => Path.Combine(outputDir, FileName);

    /// <summary>The full path of a logo file named in the set.</summary>
    public static string ImagePath(string outputDir, string file) => Path.Combine(outputDir, Folder, file);

    /// <summary>The course's logo set, without slots whose file has gone; empty when there's no file (or it can't be read).</summary>
    public static CourseLogos Load(string outputDir)
    {
        string path = PathFor(outputDir);
        if (!File.Exists(path)) return new CourseLogos();
        try
        {
            var logos = JsonSerializer.Deserialize<CourseLogos>(File.ReadAllText(path), Json) ?? new CourseLogos();
            logos.Files = logos.Files.Where(kv => File.Exists(ImagePath(outputDir, kv.Value))).ToDictionary(kv => kv.Key, kv => kv.Value);
            return logos;
        }
        catch (JsonException) { return new CourseLogos(); }
    }

    public static void Save(string outputDir, CourseLogos logos)
    {
        Directory.CreateDirectory(outputDir);
        string path = PathFor(outputDir), tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(logos, Json));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Copies <paramref name="source"/> into the Logos folder as the slot's file and saves the set; returns it.</summary>
    public static CourseLogos Import(string outputDir, LogoKind kind, LogoTone tone, string source)
    {
        var logos = Load(outputDir);
        string slot = CourseLogos.Slot(kind, tone), file = slot + Path.GetExtension(source).ToLowerInvariant();
        Directory.CreateDirectory(Path.Combine(outputDir, Folder));
        if (logos.File(kind, tone) is { } old && old != file) File.Delete(ImagePath(outputDir, old));
        File.Copy(source, ImagePath(outputDir, file), overwrite: true);
        logos.Files[slot] = file;
        Save(outputDir, logos);
        return logos;
    }

    /// <summary>Empties the slot, deleting its copied file, and saves the set; returns it.</summary>
    public static CourseLogos Remove(string outputDir, LogoKind kind, LogoTone tone)
    {
        var logos = Load(outputDir);
        if (logos.File(kind, tone) is { } file)
        {
            File.Delete(ImagePath(outputDir, file));
            logos.Files.Remove(CourseLogos.Slot(kind, tone));
            Save(outputDir, logos);
        }
        return logos;
    }
}
