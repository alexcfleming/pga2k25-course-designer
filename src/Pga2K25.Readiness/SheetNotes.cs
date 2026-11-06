using Pga2K25.CourseFormat;
using Pga2K25.TerrainMap;

namespace Pga2K25.Readiness;

/// <summary>The hole design notes as the hole sheets print them in their notes column.</summary>
public static class SheetNotes
{
    /// <summary>
    /// Per hole, each design note asked of it (by par) with what's written on the hole's card, then the hole's own
    /// note when there is one. "Why wide" is asked only of holes wide in the landing zone, which needs the readiness
    /// measures; the sheet shows it only once it's written.
    /// </summary>
    public static Dictionary<int, IReadOnlyList<HoleSheetNote>> For(ReadinessState state, IEnumerable<Hole> holes)
    {
        var design = Checklist.HoleChecks.Where(c => c.Note).ToList();
        var result = new Dictionary<int, IReadOnlyList<HoleSheetNote>>();
        foreach (var h in holes)
        {
            var hs = state.Holes.GetValueOrDefault(h.Number);
            string Written(string id) => hs is not null && hs.Notes.TryGetValue(id, out var t) ? t.Trim() : "";
            var list = design.Where(c => h.Par >= c.MinPar && (!c.WhenWide || Written(c.Id).Length > 0))
                .Select(c => new HoleSheetNote(c.Label, c.Text, Written(c.Id))).ToList();
            if (hs is not null && hs.Note.Trim().Length > 0) list.Add(new HoleSheetNote("Hole note", "", hs.Note.Trim()));
            result[h.Number] = list;
        }
        return result;
    }

    /// <summary>The notes from the readiness record in <paramref name="outputDir"/>; empty notes for every hole when there's none yet.</summary>
    public static Dictionary<int, IReadOnlyList<HoleSheetNote>> Load(string outputDir, string coursePath)
    {
        var course = CourseFile.Load(coursePath).ReadModel();
        return For(ReadinessStore.Load(ReadinessStore.PathFor(outputDir)), course.Holes);
    }
}
