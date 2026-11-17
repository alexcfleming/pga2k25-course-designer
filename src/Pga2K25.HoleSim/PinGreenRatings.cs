using Pga2K25.CourseFormat;

namespace Pga2K25.HoleSim;

/// <summary>One pin as the green putting model plays it: average putts from the whole green and the cup's slope.</summary>
public sealed record PinGreenResult(int Hole, int Pin, double AveragePutts, double HardShare, double CupSlopePct, bool TooSteep);

/// <summary>
/// Pin difficulty from the greens, for Publish readiness. Every pin on every hole is played by the simulator's green
/// putting model (the same terrain, Stimp and putter fit as its green step's "play every pin"), then rated across the
/// whole course: the hardest third of pins by average putts from the whole green are Hard (R), the easiest third Easy
/// (G), the rest Medium (A). A pin whose cup is steeper than a ball can stop on is always Hard.
/// </summary>
public static class PinGreenRatings
{
    /// <summary>Plays every pin on every hole. About a second or two a hole on Cassimbo's greens.</summary>
    public static List<PinGreenResult> Measure(string coursePath, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        var stamp = FileStamp.Read(coursePath);
        var file = CourseFile.Load(coursePath);
        var course = file.ReadModel();
        var sheet = AssumptionSheet.Load();
        var baseline = TableBaseline.LoadShipped(sheet.BaselineId);
        double stimp = AssumptionSheet.Stimp(course.GreenSpeed) ?? 10;
        var putting = PuttingModel.Create(stimp, baseline, sheet.Putting());
        double hold = putting.Friction * putting.Settings.StaticFactor * 100;
        double smoothing = sheet["terrain.smoothing"].Number;
        var layers = Maps.CourseLayers.From(course);   // built once, shared by every hole
        var results = new List<PinGreenResult>();
        foreach (var hole in course.Holes)
        {
            cancel.ThrowIfCancellationRequested();
            if (hole.Pins.Count == 0) continue;
            progress?.Report($"Playing the pins on hole {hole.Number} of {course.Holes.Count}…");
            var m = HoleModelBuilder.Build(course, file.Description, hole.Number, new HoleModelOptions(), stamp, layers);
            if (m.Pins.Count == 0) continue;
            var terrain = ReplayTerrain.Smoothed(course, m, smoothing);
            var surface = GreenSurface.Build(m, terrain);
            foreach (var pin in m.Pins)
            {
                cancel.ThrowIfCancellationRequested();
                var map = PuttingMap.Build(surface, putting, pin.Number, pin.Frame, cancel: cancel);
                double cup = surface.Slope(pin.Frame).Length * 100;
                results.Add(new PinGreenResult(hole.Number, pin.Number, map.Average, map.HardShare(), cup, cup > hold));
            }
        }
        return results;
    }

    /// <summary>R, A or G for each pin, by thirds of the whole course's average putts (hardest first); steep cups always R.</summary>
    public static Dictionary<(int Hole, int Pin), string> Rate(IReadOnlyList<PinGreenResult> pins)
    {
        var sorted = pins.OrderByDescending(p => p.AveragePutts).ThenBy(p => p.Hole).ThenBy(p => p.Pin).ToList();
        int third = (int)Math.Round(sorted.Count / 3.0);
        var rated = new Dictionary<(int, int), string>();
        for (int i = 0; i < sorted.Count; i++)
        {
            var p = sorted[i];
            rated[(p.Hole, p.Pin)] = p.TooSteep || i < third ? "R" : i >= sorted.Count - third ? "G" : "A";
        }
        return rated;
    }
}
