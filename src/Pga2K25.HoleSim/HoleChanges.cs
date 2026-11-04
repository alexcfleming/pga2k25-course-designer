namespace Pga2K25.HoleSim;

/// <summary>What a new save changed on one hole, in words, and which results that invalidates.</summary>
public sealed record HoleChangeSet(IReadOnlyList<string> Lines, bool TeesOrPins, bool Surfaces, bool Terrain)
{
    public bool Any => TeesOrPins || Surfaces || Terrain;

    /// <summary>Results depend on surfaces, tees, pins and (from Stage 3) terrain: any change clears them.</summary>
    public bool InvalidatesResults => Any;

    /// <summary>The read-back has to be confirmed again when what it shows changed.</summary>
    public bool InvalidatesReadBack => TeesOrPins || Surfaces;
}

public static class HoleChanges
{
    /// <summary>Compares two reads of the same hole from the same tee.</summary>
    public static HoleChangeSet Compare(HoleModel before, HoleModel after)
    {
        var lines = new List<string>();
        bool teesPins = false, surfaces = false, terrain = false;
        string Yd(double v) => $"{v:0}";

        if (before.Par != after.Par) { lines.Add($"Par {before.Par} → {after.Par}"); teesPins = true; }
        for (int i = 0; i < Math.Max(before.Tees.Count, after.Tees.Count); i++)
        {
            var a = i < before.Tees.Count ? before.Tees[i] : null; var b = i < after.Tees.Count ? after.Tees[i] : null;
            if (a is null) { lines.Add($"New tee {b!.Name}, {b.Yards} yds"); teesPins = true; }
            else if (b is null) { lines.Add($"Tee {a.Name} removed"); teesPins = true; }
            else if (a.Yards != b.Yards || Vec(a.World, b.World) > 0.5) { lines.Add($"Tee {a.Name}: {a.Yards} → {b.Yards} yds"); teesPins = true; }
        }
        for (int i = 0; i < Math.Max(before.Pins.Count, after.Pins.Count); i++)
        {
            var a = i < before.Pins.Count ? before.Pins[i] : null; var b = i < after.Pins.Count ? after.Pins[i] : null;
            if (a is null) { lines.Add($"New pin {b!.Number} ({b.Where})"); teesPins = true; }
            else if (b is null) { lines.Add($"Pin {a.Number} removed"); teesPins = true; }
            else if (Vec(a.World, b.World) > 0.45) { lines.Add($"Pin {a.Number} moved {Vec(a.World, b.World) / HoleFrame.Yard:0.#} yds, now {b.Where}"); teesPins = true; }
        }
        if (Vec(before.AimMarker, after.AimMarker) > 0.45)
        {
            lines.Add($"Aim marker moved {Vec(before.AimMarker, after.AimMarker) / HoleFrame.Yard:0} yds, now {after.AimFromTeeYards} from {after.Tee.Name}");
            teesPins = true;
        }
        if (Vec(before.Green, after.Green) > 0.45) { lines.Add($"Green centre moved {Vec(before.Green, after.Green) / HoleFrame.Yard:0} yds"); teesPins = true; }

        foreach (var wb in before.Widths)
        {
            var wa = after.Widths.FirstOrDefault(w => w.Station == wb.Station);
            if (wa is not null && wa.Yards != wb.Yards) { lines.Add($"Fairway width at {wb.Station}: {wb.Yards} → {wa.Yards} yds"); surfaces = true; }
        }
        if (before.GreenDepthYards != after.GreenDepthYards) { lines.Add($"Green depth {before.GreenDepthYards} → {after.GreenDepthYards} yds"); surfaces = true; }

        // Hazards: match by ground and overlap along the hole.
        var unmatched = after.Hazards.ToList();
        foreach (var hb in before.Hazards)
        {
            var ha = unmatched.Where(h => h.Ground == hb.Ground && h.FromYards <= hb.ToYards + 5 && h.ToYards >= hb.FromYards - 5)
                .OrderBy(h => Math.Abs(h.FromYards - hb.FromYards) + Math.Abs(h.ToYards - hb.ToYards)).FirstOrDefault();
            if (ha is null) { lines.Add($"{hb.Text}: gone"); surfaces = true; continue; }
            unmatched.Remove(ha);
            if (Math.Abs(ha.FromYards - hb.FromYards) >= 2 || Math.Abs(ha.ToYards - hb.ToYards) >= 2 || ha.Side != hb.Side || Math.Abs(ha.AreaSqYd - hb.AreaSqYd) > Math.Max(15, hb.AreaSqYd / 10))
            {
                lines.Add($"{Grounds.Name(hb.Ground)} {hb.Side.ToLowerInvariant()} {hb.FromYards}–{hb.ToYards} → {ha.Side.ToLowerInvariant()} {ha.FromYards}–{ha.ToYards} ({ha.AreaSqYd - hb.AreaSqYd:+#;−#;0} sq yds)");
                surfaces = true;
            }
        }
        foreach (var h in unmatched) { lines.Add($"New: {h.Text}"); surfaces = true; }

        // Surface areas on the grid within reach of the play line (catches edits the lists above don't name).
        if (before.Grid.Width == after.Grid.Width && before.Grid.Height == after.Grid.Height)
        {
            var cb = before.Grid.Counts(); var ca = after.Grid.Counts();
            foreach (var g in new[] { Ground.Fairway, Ground.Green, Ground.Rough, Ground.HeavyRough })
            {
                int d = ca[(int)g] - cb[(int)g];
                if (Math.Abs(d) >= 25) { lines.Add($"{Grounds.Name(g)} area {(d > 0 ? "+" : "−")}{Math.Abs(d):#,0} sq yds near the hole"); surfaces = true; }
            }
        }
        var sb = before.SurfaceSignatures.Select(s => s.Signature).ToHashSet();
        var sa = after.SurfaceSignatures.Select(s => s.Signature).ToHashSet();
        if (!surfaces && !sb.SetEquals(sa)) { lines.Add("Surface splines or brushes near the hole changed slightly"); surfaces = true; }

        // Terrain: height brushes near the line of play.
        var hb2 = before.HeightStamps.Select(s => s.Signature).ToHashSet();
        var added = after.HeightStamps.Where(s => !hb2.Contains(s.Signature)).ToList();
        var ha2 = after.HeightStamps.Select(s => s.Signature).ToHashSet();
        int removed = before.HeightStamps.Count(s => !ha2.Contains(s.Signature));
        if (added.Count > 0 || removed > 0)
        {
            terrain = true;
            double lo = added.Count > 0 ? added.Min(s => s.Where.Along) : 0, hi = added.Count > 0 ? added.Max(s => s.Where.Along) : 0;
            string where = added.Count == 0 ? "" : Math.Round(hi) - Math.Round(lo) < 1 ? $" at {Yd(lo)} yds" : $" at {Yd(lo)}–{Yd(hi)} yds";
            lines.Add($"Height brushes near the hole: {added.Count} added{where}, {removed} removed");
        }

        if (lines.Count == 0) lines.Add($"Nothing changed on hole {after.Number}. The save changed other holes or the scenery.");
        return new HoleChangeSet(lines, teesPins, surfaces, terrain);
    }

    static double Vec(CourseFormat.Vec2 a, CourseFormat.Vec2 b) => CourseFormat.Vec2.Distance(a, b);
}
