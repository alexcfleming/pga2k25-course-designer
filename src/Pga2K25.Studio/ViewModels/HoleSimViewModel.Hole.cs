using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.CourseFormat;
using Pga2K25.HoleSim;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// The Hole section (Stage 4e): every pin played tee to cup for the player in the bar (Summary), and design changes
/// played against the hole as it is with the same draws (Experiments), including your previous save.
/// </summary>
public partial class HoleSimViewModel
{
    (CourseModel Course, HoleModel Model, FileStamp Stamp)? _previousSave;
    CancellationTokenSource? _holeCancel;

    public ObservableCollection<SummaryRowViewModel> SummaryRows { get; } = [];
    public ObservableCollection<ExperimentRowViewModel> ExperimentRows { get; } = [];

    [ObservableProperty] private bool _holeBusy;
    [ObservableProperty] private string _holeStatus = "";
    [ObservableProperty] private bool _hasSummary;
    [ObservableProperty] private string _summaryTitle = "";
    [ObservableProperty] private string _summaryHeadline = "";
    [ObservableProperty] private string _summaryPhases = "";
    [ObservableProperty] private string _summaryPlan = "";
    [ObservableProperty] private string _holeBadge = "";
    [ObservableProperty] private bool _hasExperiments;
    [ObservableProperty] private string _experimentsTitle = "";
    [ObservableProperty] private string _experimentsHeadline = "";

    public string PreviousSaveText => _previousSave is { } p && _model is not null && p.Model.Number == _model.Number
        ? $"Your previous save ({p.Stamp.ModifiedUtc.ToLocalTime():HH:mm}) is kept, so the experiments include it against this one."
        : "Reload a new save while this section is open and the experiments will include your last edit, played against the save before it.";

    /// <summary>The plan for a play-out on this hole: the shared shot context (fitted golfer), the bar's player and the drive aim.</summary>
    static HolePlan PlanFor(ShotContext ctx, AssumptionSheet sheet, string playerId, double driveAim, TerrainUse? terrain) => new()
    {
        Context = ctx, Player = sheet.Profile(playerId), Reach = sheet.Reach(playerId), DriveAim = driveAim, Terrain = terrain,
    };

    /// <summary>The drive aim: the Drive tab's best line when it ran for this player and pin, else a quick sweep.</summary>
    static double DriveAimFor(HoleModel m, AssumptionSheet sheet, string playerId, int pin, GroundTable ground, IBaseline baseline, TerrainUse? terrain, GeometryRunResult? last)
    {
        if (m.Par == 3) return 0;
        if (last is not null && last.Player.Id == playerId && last.Pin == pin) return last.Best.Aim;
        return GeometryRun.Run(m, sheet.Profile(playerId), pin, ground, sheet.Lies(), baseline, new RunSettings { SamplesPerAim = 400 }, terrain: terrain).Best.Aim;
    }

    (ShotContext Ctx, GolferFit? Fit) ContextNow(HoleModel m, GreenSurface green, ITerrainModel? fairway, AssumptionSheet sheet, double stimp,
        ShotContext? baseCtx, List<KeyValuePair<int, PuttingMap>> putts, IProgress<string> progress, CancellationToken cancel)
    {
        if (baseCtx is not null) return (baseCtx, _golferFit);
        var (ctx, fit) = ApproachSetup.Build(m, green, fairway, sheet, TableBaseline.LoadShipped(sheet.BaselineId), stimp, FitCacheFolder, progress, cancel);
        foreach (var (k, v) in putts) ctx.PuttingMaps[k] = v;
        return (ctx, fit);
    }

    string HoleBadgeText() => (_greenGate is null || _greenGate.Band == GateBand.Check ? "Green contours unverified" : $"Green contours {_greenGate.BandText}")
        + (CurrentTerrain() is { } tu ? $" · terrain {tu.Label}" : " · flat outside the green") + " · placeholder physics and club distances, sparse secondary baseline";

    [RelayCommand] private void PlayHole()
    {
        if (_model is null) return;
        if (_greenSurface is null) { HoleStatus = "The green is still building; try again in a moment."; return; }
        var m = _model; var green = _greenSurface; var terrain = CurrentTerrain(); var sheet = _sheet; double stimp = StimpNow;
        string playerId = _player; int pin = _pin; var last = _lastRun;
        string key = ShotKey();
        if (key != _shotKey) { _shotContext = null; _shotKey = key; }
        var baseCtx = _shotContext; var putts = _puttingMaps.ToList();
        _sheet.SavePriors(_priorsPath);
        _holeCancel?.Cancel();
        var cancel = new CancellationTokenSource(); _holeCancel = cancel;
        HoleBusy = true; HoleStatus = "Getting ready…";
        var progress = new Progress<string>(s => { if (!cancel.IsCancellationRequested) HoleStatus = s; });
        IProgress<string> report = progress;
        Task.Run(() =>
        {
            try
            {
                var (ctx, fit) = ContextNow(m, green, terrain?.Model, sheet, stimp, baseCtx, putts, progress, cancel.Token);
                report.Report("Finding the drive's best line…");
                double aim = DriveAimFor(m, sheet, playerId, pin, ctx.Ground, ctx.Baseline, terrain, last);
                var plan = PlanFor(ctx, sheet, playerId, aim, terrain);
                var list = new List<HoleSummary>();
                foreach (var p in m.Pins)
                {
                    report.Report($"Playing pin {p.Number} tee to cup, 600 times…");
                    list.Add(HolePlay.Play(plan, p.Number, 600, cancel: cancel.Token));
                }
                Dispatcher.UIThread.Post(() =>
                {
                    if (cancel.IsCancellationRequested || !ReferenceEquals(m, _model)) return;
                    if (key == _shotKey) _shotContext = ctx;
                    _golferFit = fit;
                    HoleBusy = false; HoleStatus = "";
                    ShowSummary(list, playerId);
                });
            }
            catch (OperationCanceledException) { Dispatcher.UIThread.Post(() => { if (_holeCancel == cancel) HoleBusy = false; }); }
            catch (Exception ex) { Dispatcher.UIThread.Post(() => { HoleBusy = false; HoleStatus = $"The play-out failed: {ex.Message}"; }); }
        });
    }

    void ShowSummary(IReadOnlyList<HoleSummary> list, string playerId)
    {
        var m = _model!;
        string player = playerId == "power" ? "Power Drive" : "Normal";
        SummaryRows.Clear();
        foreach (var s in list) SummaryRows.Add(new SummaryRowViewModel(s, m.Pins[s.Pin - 1].Where));
        double avg = list.Average(s => s.Expected);
        var hard = list.MaxBy(s => s.Expected)!; var easy = list.MinBy(s => s.Expected)!;
        SummaryTitle = $"Hole {m.Number} · par {m.Par} · {m.TeeLabel} · {player} · every pin played 600 times";
        HoleBadge = HoleBadgeText();
        SummaryHeadline = $"{avg:0.00} on average over the {list.Count} pins ({avg - m.Par:+0.00;-0.00;0.00} to par)."
            + (list.Count > 1 ? $" Hardest: pin {hard.Pin} ({hard.Expected:0.00}); easiest: pin {easy.Pin} ({easy.Expected:0.00}), {hard.Expected - easy.Expected:0.00} apart." : "");
        double tee = list.Average(s => s.Sg.Tee), app = list.Average(s => s.Sg.Approach), sh = list.Average(s => s.Sg.Short), pu = list.Average(s => s.Sg.Putting);
        var phases = new List<(string Name, double V)> { ("off the tee", tee), ("approach", app), ("around the green", sh), ("putting", pu) };
        if (m.Par == 3) phases.RemoveAt(0);
        var worst = phases.MinBy(p => p.V);
        SummaryPhases = "Strokes gained against the baseline, averaged over the pins: " + string.Join(", ", phases.Select(p => $"{p.Name} {p.V:+0.00;-0.00;0.00}"))
            + $". Most is lost {worst.Name}" + (worst.Name == "putting" && (_greenGate is null || _greenGate.Band == GateBand.Check) ? ", on green contours not yet read in the game." : ".");
        var plans = list.Select(s => s.Plan).Where(t => t.Length > 0).Distinct().ToList();
        SummaryPlan = (m.Par == 3 ? "" : $"Drive aimed {list[0].DriveAim:+0;-0;0} yds off the aim marker. ") + (plans.Count == 0 ? "" : "Plan: " + string.Join("; ", plans.Select((t, i) => list.Count(s => s.Plan == t) == list.Count ? t : $"{t} (pin {string.Join(", ", list.Where(s => s.Plan == t).Select(s => s.Pin))})")) + ".");
        HasSummary = true;
    }

    [RelayCommand] private void RunExperiments()
    {
        if (_model is null || _course is null) return;
        if (_greenSurface is null) { HoleStatus = "The green is still building; try again in a moment."; return; }
        var m = _model; var green = _greenSurface; var terrain = CurrentTerrain(); var sheet = _sheet; double stimp = StimpNow;
        string playerId = _player; int pin = _pin; var last = _lastRun; var course = _course;
        var prev = _previousSave is { } ps && ps.Model.Number == m.Number && ps.Model.TeeIndex == m.TeeIndex ? ps : ((CourseModel, HoleModel, FileStamp)?)null;
        string key = ShotKey();
        if (key != _shotKey) { _shotContext = null; _shotKey = key; }
        var baseCtx = _shotContext; var putts = _puttingMaps.ToList();
        _sheet.SavePriors(_priorsPath);
        _holeCancel?.Cancel();
        var cancel = new CancellationTokenSource(); _holeCancel = cancel;
        HoleBusy = true; HoleStatus = "Getting ready…";
        var progress = new Progress<string>(s => { if (!cancel.IsCancellationRequested) HoleStatus = s; });
        IProgress<string> report = progress;
        Task.Run(() =>
        {
            try
            {
                var (ctx, fit) = ContextNow(m, green, terrain?.Model, sheet, stimp, baseCtx, putts, progress, cancel.Token);
                report.Report("Finding the drive's best line…");
                double aim = DriveAimFor(m, sheet, playerId, pin, ctx.Ground, ctx.Baseline, terrain, last);
                var plan = PlanFor(ctx, sheet, playerId, aim, terrain);
                report.Report("Playing the hole as it is…");
                var before = m.Pins.Select(p => HolePlay.Play(plan, p.Number, 400, cancel: cancel.Token)).ToList();
                var results = new List<ExperimentResult>();
                foreach (var e in Experiments.Suggested(plan))
                {
                    cancel.Token.ThrowIfCancellationRequested();
                    results.Add(Experiments.Compare(e.Name, e.Detail, plan, Experiments.OnGrid(plan, e.Apply(m)), before, 400, progress, cancel.Token));
                }
                if (prev is { } pv)
                {
                    // Both saves on the same footing: the replay's contours on the green, the bar's terrain choice outside it.
                    report.Report("Your last save against the one before…");
                    double smooth = sheet["terrain.smoothing"].Number;
                    var oldGreen = GreenSurface.Build(pv.Item2, ReplayTerrain.Smoothed(pv.Item1, pv.Item2, smooth));
                    var newGreen = GreenSurface.Build(m, ReplayTerrain.Smoothed(course, m, smooth));
                    var baseline = TableBaseline.LoadShipped(sheet.BaselineId);
                    var oldCtx = ApproachSetup.Build(pv.Item2, oldGreen, null, sheet, baseline, stimp, FitCacheFolder).Context;
                    var newCtx = ApproachSetup.Build(m, newGreen, null, sheet, baseline, stimp, FitCacheFolder).Context;
                    var oldPlan = PlanFor(oldCtx, sheet, playerId, aim, null); var newPlan = PlanFor(newCtx, sheet, playerId, aim, null);
                    results.Insert(0, Experiments.Compare($"Your last save ({_stamp?.ModifiedUtc.ToLocalTime():HH:mm}) against the one before ({pv.Item3.ModifiedUtc.ToLocalTime():HH:mm})",
                        "Both played with the replay's green contours and flat ground outside the green, so only your edit differs.", oldPlan, newPlan, null, 400, progress, cancel.Token));
                }
                Dispatcher.UIThread.Post(() =>
                {
                    if (cancel.IsCancellationRequested || !ReferenceEquals(m, _model)) return;
                    if (key == _shotKey) _shotContext = ctx;
                    _golferFit = fit;
                    HoleBusy = false; HoleStatus = "";
                    ShowExperiments(results, before, playerId);
                });
            }
            catch (OperationCanceledException) { Dispatcher.UIThread.Post(() => { if (_holeCancel == cancel) HoleBusy = false; }); }
            catch (Exception ex) { Dispatcher.UIThread.Post(() => { HoleBusy = false; HoleStatus = $"The experiments failed: {ex.Message}"; }); }
        });
    }

    void ShowExperiments(IReadOnlyList<ExperimentResult> results, IReadOnlyList<HoleSummary> before, string playerId)
    {
        var m = _model!;
        string player = playerId == "power" ? "Power Drive" : "Normal";
        ExperimentRows.Clear();
        foreach (var r in results) ExperimentRows.Add(new ExperimentRowViewModel(r));
        ExperimentsTitle = $"Hole {m.Number} · {m.TeeLabel} · {player} · {before.Average(s => s.Expected):0.00} as it is · 400 play-outs per pin, the same draws for every version";
        HoleBadge = HoleBadgeText();
        var clear = results.Where(r => Math.Abs(r.Delta) > Math.Max(r.Ci95, 0.005)).OrderBy(r => r.Delta).ToList();
        ExperimentsHeadline = clear.Count == 0 ? "None of these changes moves the hole by more than the run's noise for this player."
            : $"Biggest effects: " + string.Join("; ", clear.OrderByDescending(r => Math.Abs(r.Delta)).Take(3).Select(r => $"{r.Name.ToLowerInvariant()} {r.Delta:+0.000;-0.000} strokes")) + ". Negative makes the hole easier.";
        OnPropertyChanged(nameof(PreviousSaveText));
        HasExperiments = true;
    }
}

public sealed class SummaryRowViewModel(HoleSummary s, string where)
{
    public string Pin { get; } = s.Pin.ToString();
    public string Where { get; } = where;
    public string Expected { get; } = $"{s.Expected:0.00}";
    public string ToPar { get; } = $"{s.ToPar:+0.00;-0.00;0.00}";
    public string Tee { get; } = s.Par == 3 ? "—" : $"{s.Sg.Tee:+0.00;-0.00;0.00}";
    public string Approach { get; } = $"{s.Sg.Approach:+0.00;-0.00;0.00}";
    public string Short { get; } = $"{s.Sg.Short:+0.00;-0.00;0.00}";
    public string Putting { get; } = $"{s.Sg.Putting:+0.00;-0.00;0.00}";
    public string Fairway { get; } = double.IsNaN(s.FairwayHit) ? "—" : $"{s.FairwayHit:P0}";
    public string Gir { get; } = $"{s.GreenInRegulation:P0}";
    public string Scrambling { get; } = double.IsNaN(s.Scrambling) ? "—" : $"{s.Scrambling:P0}";
    public string Birdie { get; } = $"{s.Birdie:P0}";
    public string Bogey { get; } = $"{s.BogeyOrWorse:P0}";
}

public sealed class ExperimentRowViewModel(ExperimentResult r)
{
    public string Name { get; } = r.Name;
    public string Detail { get; } = r.Detail + (r.CellsChanged > 0 ? $" {r.CellsChanged:#,0} sq yd changed." : "");
    public string Delta { get; } = $"{r.Delta:+0.000;-0.000;0.000}";
    public string Ci { get; } = $"± {r.Ci95:0.000}";
    public string Pins { get; } = string.Join("   ", r.Pins.Select(p => $"pin {p.Pin} {p.Delta:+0.00;-0.00;0.00}"));
    public IBrush DeltaInk { get; } = Brush.Parse(Math.Abs(r.Delta) <= Math.Max(r.Ci95, 0.005) ? "#56605A" : r.Delta < 0 ? "#2D6A42" : "#B4471F");
    public string Verdict { get; } = Math.Abs(r.Delta) <= Math.Max(r.Ci95, 0.005) ? "no clear effect" : r.Delta < 0 ? "easier" : "harder";
}
