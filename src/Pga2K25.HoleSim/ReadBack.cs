namespace Pga2K25.HoleSim;

/// <summary>One line of the read-back: what the file says, and where that came from.</summary>
public sealed record ReadBackRow(string Id, string Item, string Value, SourceTag Source, string Note = "");

/// <summary>The hole as read from the file, for the designer to check against what's open in the game.</summary>
public static class ReadBack
{
    public static IReadOnlyList<ReadBackRow> Rows(HoleModel m)
    {
        var back = m.BackTee;
        bool fromBack = m.TeeIndex == m.Tees.Count - 1;
        var rows = new List<ReadBackRow>
        {
            new("par", "Par", m.Par.ToString(), SourceTag.File),
            new("yards", $"Yards, {m.Tees[0].Name} to {back.Name}", string.Join(" · ", m.Tees.Select(t => t.Yards)), SourceTag.File),
            new("tee", "Playing from", $"{m.Tee.Name}, {m.Tee.Yards} yds", SourceTag.File),
            new("elev", $"{m.Tee.Name} vs the green", Math.Abs(m.TeeElevationFt) < 1 ? "about level with the green"
                : $"about {Math.Abs(m.TeeElevationFt):0} ft {(m.TeeElevationFt > 0 ? "above" : "below")} the green", SourceTag.Placeholder,
                "From the terrain model, not yet checked in game."),
        };
        if (m.HasAimMarker)
            rows.Add(new("aim", "Aim marker", $"{m.AimFromTeeYards} from {m.Tee.Name} · {m.AimToGreenYards} to the green", SourceTag.File));
        if (m.Widths.Count > 0)
            rows.Add(new("widths", "Fairway width", string.Join(" · ", m.Widths.Select(w => $"{w.Station}: {w.Yards}")), SourceTag.File,
                $"Stations in yards from {back.Name}, measured like the yardage book."));
        rows.Add(new("green", "Green", $"{m.GreenDepthYards} deep · front {m.GreenFrontYards} · back {m.GreenBackYards}", SourceTag.File,
            fromBack ? "" : $"Front and back from {m.Tee.Name}."));
        rows.Add(new("pins", "Pins", m.Pins.Count == 0 ? "none" : string.Join(" · ", m.Pins.Select(p => $"{p.Number}: {p.Where}")), SourceTag.File,
            "Back/short and left/right of the green centre, seen from the tee."));
        rows.Add(new("hazards", "Near the line", m.Hazards.Count == 0 ? "no bunkers, scrub or water within 40 yds of the line"
            : string.Join(" · ", m.Hazards.Select(h => h.Text)), SourceTag.File, $"Yards from {m.Tee.Name} along the tee-to-green line."));
        return rows;
    }
}
