using SkiaSharp;

namespace Pga2K25.CourseCard;

public sealed partial class CourseCardRenderer
{
    /// <summary>The Course Details Card's rows (<see cref="CourseDetails.For"/>); empty for no card.</summary>
    public (IReadOnlyList<DetailRow> Players, IReadOnlyList<DetailRow> Settings) Details { get; init; } = ([], []);

    /// <summary>
    /// Course details card: the game's course details panel as a player sees it on the left, and the settings behind it
    /// on the right. Every figure is from the course file.
    /// </summary>
    public void DrawDetailsPage(SKCanvas canvas)
    {
        var (w, h) = (CardPage.Width, CardPage.Height);
        canvas.Clear(_salt);
        float x0 = (_logo is null ? 0.030f : 0.082f) * w;
        Text(canvas, Title, x0, (1 - 0.955f) * h, 30, _bold, _ink, tracking: 0.32f * 30);
        Text(canvas, "Course details card  ·  what players see before a round, and the settings behind it", x0, (1 - 0.925f) * h, 12.5f, _italic, Muted);
        Text(canvas, "From the course file. The game's Fairways and Greens ratings, Slope and Handicap aren't stored before the course is played, so they're left out.",
            x0, (1 - 0.902f) * h, 9, _serif, Faint);
        var (main, _, halves) = HeaderTotals();
        Text(canvas, main, 0.970f * w, (1 - 0.955f) * h, 14, _bold, _ink, SKTextAlign.Right);
        Text(canvas, $"{ParLine}  ·  {halves}", 0.970f * w, (1 - 0.925f) * h, 10.5f, _serif, Muted, SKTextAlign.Right);
        using (var rule = Stroke(_fog, 1.3f)) canvas.DrawLine(0.024f * w, (1 - 0.884f) * h, 0.970f * w, (1 - 0.884f) * h, rule);
        if (_logo is not null) DrawLogo(canvas, FigRect(0.024f, 0.898f, 0.046f, 0.0674f, CardPage));

        DrawDetailColumn(canvas, FigRect(0.055f, 0.08f, 0.42f, 0.76f, CardPage), "AS PLAYERS SEE IT", Details.Players, big: true);
        DrawDetailColumn(canvas, FigRect(0.535f, 0.08f, 0.435f, 0.76f, CardPage), "SETTINGS BEHIND IT", Details.Settings, big: false);
    }

    void DrawDetailColumn(SKCanvas canvas, SKRect r, string heading, IReadOnlyList<DetailRow> rows, bool big)
    {
        Text(canvas, heading, r.Left, r.Top + 10, 9, _serif, Muted, tracking: 1.2f);
        float y = r.Top + 22;
        using (var rule = Stroke(_fog, 1)) canvas.DrawLine(r.Left, y, r.Right, y, rule);
        float labelW = 0.30f * r.Width, valueSize = big ? 19 : 13.5f, noteSize = big ? 9.5f : 9f;
        float rowH = Math.Min(big ? 70 : 62, (r.Height - 30) / Math.Max(1, rows.Count));
        foreach (var row in rows)
        {
            float baseLine = y + rowH * 0.48f;
            Text(canvas, row.Label, r.Left, baseLine, 11, _serif, Muted);
            var (value, size) = FitLine(row.Value, valueSize, valueSize * 0.6f, r.Width - labelW, big ? _bold : _serif);
            Text(canvas, value, r.Left + labelW, baseLine, size, big ? _bold : _serif, _ink);
            if (row.Note is { Length: > 0 } note)
            {
                var (fit, ns) = FitLine(note, noteSize, 7, r.Width - labelW, _italic);
                Text(canvas, fit, r.Left + labelW, baseLine + noteSize + 6, ns, _italic, Faint);
            }
            y += rowH;
            using var line = Stroke(_fog.WithAlpha(120), 0.6f);
            canvas.DrawLine(r.Left, y, r.Right, y, line);
        }
    }
}
