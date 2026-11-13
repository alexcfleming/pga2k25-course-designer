using Pga2K25.CourseFormat;

namespace Pga2K25.Studio.Services;

/// <summary>
/// One line of the Surfaces card: what it is, the material (or "theme default"), where the material comes from, and a
/// figure. <see cref="Key"/> is what a note on the line is kept under: the material's own name (so the note follows it
/// to every course that uses it), or for a theme default the theme and the line ("theme 2 · Create surfaces · Path ③ ④ ⑤").
/// Empty for a line that takes no note of its own.
/// </summary>
public sealed record SurfaceRow(string Label, string Value, string Source, string Detail, bool IsDefault, string Key = "");

public sealed record SurfaceGroup(string Title, IReadOnlyList<SurfaceRow> Rows);

/// <summary>
/// The terrain map's Surfaces card, from the course file (<see cref="CourseSurfaces"/>): the theme and backdrop,
/// Create Surfaces, Terrain Painting with its strokes, and Surface Weathering. A slot the designer never changed has
/// no name in the file, so it reads "theme default"; runs of them fold into one line.
/// </summary>
public static class SurfacesSummary
{
    public const string ThemeDefault = "theme default";

    public static (string Heading, string Summary, IReadOnlyList<SurfaceGroup> Groups) Build(CourseModel course)
    {
        var look = course.Look;
        var paint = course.SurfaceBrushes.GroupBy(b => b.Category).ToDictionary(g => g.Key, g => g.Count());
        int Strokes(int surface) => paint.GetValueOrDefault(surface);
        static string Count(int n) => n == 0 ? "" : $"{n:N0} stroke{(n == 1 ? "" : "s")}";

        SurfaceRow Row(string label, SurfaceMaterial m, string detail = "") => m.IsThemeDefault
            ? new SurfaceRow(label, ThemeDefault, "", detail, true)
            : new SurfaceRow(label, m.Material!, m.Source ?? "", detail, false, m.Name!);

        var create = Fold(
        [
            Row("Bunker", look.Materials[CourseSurfaces.Bunker]),
            Row("Green", look.Materials[CourseSurfaces.Green]),
            Row("Fringe", look.Materials[CourseSurfaces.Fringe]),
            Row("Fairway", look.Materials[CourseSurfaces.Fairway]),
            Row("Light rough", look.Materials[CourseSurfaces.LightRough]),
            Row("Heavy rough", look.Materials[CourseSurfaces.HeavyRough]),
            .. Enumerable.Range(1, 5).Select(i => Row($"Path {"①②③④⑤"[i - 1]}", look.Materials[CourseSurfaces.Path1 + i - 1])),
        ]);

        var painting = Fold(CourseSurfaces.PaintingLayers
            .Select(l => Row(l.Name, look.Materials[l.Slot], Count(Strokes(l.PaintSurface)))).ToList());
        if (painting.Count == 1 && painting[0].IsDefault && CourseSurfaces.PaintingLayers.All(l => Strokes(l.PaintSurface) == 0))
            painting = [painting[0] with { Detail = "none painted" }];

        // The designer's weathering panel lists green, fairway and the roughs; a weathered fringe goes with the green.
        var w = look.Weathered;
        var green = Row("Green", w[CourseSurfaces.Green]);
        if (!w[CourseSurfaces.Fringe].IsThemeDefault)
            green = green with { Detail = $"fringe {w[CourseSurfaces.Fringe].Material}" };
        int weatherStrokes = Strokes(CourseSurfaces.WeatheringPaintSurface);
        var weathering = Fold(
        [
            green,
            Row("Fairway", w[CourseSurfaces.Fairway]),
            Row("Light rough", w[CourseSurfaces.LightRough]),
            Row("Heavy rough", w[CourseSurfaces.HeavyRough]),
        ]);
        weathering.Add(new SurfaceRow("Painted", weatherStrokes == 0 ? "none" : Count(weatherStrokes), "",
            look.WeatheringEdgeBlend is { } e ? $"edge blending {e * 100:0}" : "", weatherStrokes == 0));

        int slots = CourseSurfaces.SlotCount + 4;   // the table and the four weathered grasses
        int chosen = look.Materials.Count(m => !m.IsThemeDefault)
            + new[] { CourseSurfaces.Green, CourseSurfaces.Fairway, CourseSurfaces.LightRough, CourseSurfaces.HeavyRough }.Count(i => !w[i].IsThemeDefault);
        string heading = string.Join(" · ", new[] { look.ThemeLabel, look.BackdropLabel }.Where(s => s.Length > 0));
        string summary = $"{look.ThemeLabel} · {(chosen == 0 ? "all theme defaults" : $"{chosen} of {slots} chosen")}"
            + (weatherStrokes > 0 ? " · weathered" : "");
        SurfaceGroup Group(string title, List<SurfaceRow> rows, int keep) => new(title, rows.Select((r, i) =>
            r.Key.Length > 0 || i >= keep ? r : r with { Key = DefaultKey(look.Theme, title, r.Label) }).ToList());
        return (heading, summary,
        [
            Group("Create surfaces", create, create.Count),
            Group("Terrain painting", painting, painting.Count),
            Group("Surface weathering", weathering, weathering.Count - 1),   // "Painted" takes the group's note
        ]);
    }

    /// <summary>A theme default's note key: the file doesn't name the material, so the note goes with the theme's line.</summary>
    public static string DefaultKey(int theme, string group, string label) => $"theme {theme} · {group} · {label}";

    /// <summary>Runs of theme-default rows with no figure fold into one: "Path ③ ④ ⑤", or "Bunker, green, fringe".</summary>
    static List<SurfaceRow> Fold(List<SurfaceRow> rows)
    {
        var result = new List<SurfaceRow>();
        var run = new List<SurfaceRow>();
        void Flush()
        {
            if (run.Count == 0) return;
            if (run.Count == 1) result.Add(run[0]);
            else if (run.All(r => r.Label.StartsWith("Path ", StringComparison.Ordinal)))
                result.Add(run[0] with { Label = "Path " + string.Join(" ", run.Select(r => r.Label[5..])) });
            else if (run.Count == rows.Count)
                result.Add(run[0] with { Label = rows.Count == 4 && rows[0].Label == "Grass / topsoil" ? "All four layers" : "All" });
            else
                result.Add(run[0] with { Label = string.Join(", ", run.Select((r, i) => i == 0 ? r.Label : r.Label.ToLowerInvariant())) });
            run.Clear();
        }
        foreach (var r in rows)
        {
            bool pathRunBreak = run.Count > 0 && run[0].Label.StartsWith("Path ", StringComparison.Ordinal) != r.Label.StartsWith("Path ", StringComparison.Ordinal);
            if (r.IsDefault && r.Detail.Length == 0 && !pathRunBreak) { run.Add(r); continue; }
            Flush();
            if (r.IsDefault && r.Detail.Length == 0) { run.Add(r); continue; }
            result.Add(r);
        }
        Flush();
        return result;
    }
}
