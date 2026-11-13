using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.HoleSim;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// Approach map (Stage 4c): for the chosen pin, the heat map of where to approach from (par 4s and 5s) or tee shots
/// from every tee (par 3s), and the leave map round the green with the ideal angle in from its contours. Built in the
/// background when a pin is opened (a few seconds) and kept until the green, the terrain or the assumptions change.
/// </summary>
public partial class HoleSimViewModel
{
    public const string ApproachMapStep = "Approach map";

    /// <param name="AngleYards">The distance the angles were played from: 150 yds, or on a par 3 the chosen tee's.</param>
    /// <param name="LineName">The line they're measured from: the line of play, or on a par 3 the back tee's line.</param>
    /// <param name="TeeAngles">Par 3: each tee's own angle against that line.</param>
    sealed record PinMaps(ApproachHeatMap? Heat, IReadOnlyList<TeeShotOption>? Tees, LeaveMap Leave, IReadOnlyList<AngleOption> Angles, byte[]? HeatPng, byte[] LeavePng,
        double AngleYards, string LineName, IReadOnlyList<(string Tee, double Degrees, bool Selected)> TeeAngles);

    [ObservableProperty] private string _angleNote = "";

    readonly Dictionary<int, PinMaps> _pinMaps = [];
    string _pinMapsKey = "";
    CancellationTokenSource? _pinMapCancel;
    int _mapPin = 1;

    public bool IsApproachMap => Step == ApproachMapStep;
    [RelayCommand] private void GoApproachMap() => Step = ApproachMapStep;

    public ObservableCollection<TabViewModel> MapPinButtons { get; } = [];
    public ObservableCollection<BandRowViewModel> BandRows { get; } = [];
    public ObservableCollection<TeeShotRowViewModel> TeeShotRows { get; } = [];
    public ObservableCollection<AngleRowViewModel> AngleRows { get; } = [];

    [ObservableProperty] private bool _mapBusy;
    [ObservableProperty] private string _mapStatus = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowHeat))] private bool _hasPinMaps;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowHeat))] private bool _isPar3Map;

    /// <summary>The heat map shows on par 4s and 5s once built; a par 3 shows its tee shots instead.</summary>
    public bool ShowHeat => HasPinMaps && !IsPar3Map;
    [ObservableProperty] private string _mapTitle = "";
    [ObservableProperty] private string _mapBadge = "";
    [ObservableProperty] private Bitmap? _heatImage;
    [ObservableProperty] private string _heatSummary = "";
    [ObservableProperty] private string _heatBest = "";
    [ObservableProperty] private string _teeShotSummary = "";
    [ObservableProperty] private Bitmap? _leaveImage;
    [ObservableProperty] private string _leaveLegend = "";
    [ObservableProperty] private string _angleSummary = "";

    void RefreshApproachMap()
    {
        if (_model is null) return;
        if (_mapPin > _model.Pins.Count) _mapPin = 1;
        MapPinButtons.Clear();
        foreach (var p in _model.Pins) { int n = p.Number; MapPinButtons.Add(new TabViewModel($"Pin {n}  {p.Where}", n == _mapPin, () => SelectMapPin(n))); }
        IsPar3Map = _model.Par == 3;
        MapTitle = IsPar3Map ? $"Hole {_model.Number} (par 3): tee shots and where to leave them" : $"Hole {_model.Number} (par {_model.Par}): where to approach from";
        if (ShotKey() != _pinMapsKey) { _pinMaps.Clear(); _pinMapsKey = ShotKey(); }
        if (_pinMaps.TryGetValue(_mapPin, out var maps)) ShowPinMaps(maps); else BuildPinMaps(_mapPin);
    }

    void SelectMapPin(int pin)
    {
        _mapPin = pin;
        foreach (var b in MapPinButtons) b.IsSelected = b.Label.StartsWith($"Pin {pin} ");
        RefreshApproachMap();
    }

    void ResetApproachMaps()
    {
        _pinMapCancel?.Cancel();
        _pinMaps.Clear(); _pinMapsKey = "";
        HasPinMaps = false; HeatImage = null; LeaveImage = null; BandRows.Clear(); TeeShotRows.Clear(); AngleRows.Clear(); MapStatus = ""; MapBusy = false;
        _secondCancel?.Cancel();
        HasSecond = false; SecondMap = null; LayupRows.Clear(); SecondStatus = ""; SecondBusy = false;
    }

    void BuildPinMaps(int pin)
    {
        if (_model is null) return;
        if (_greenSurface is null) { MapStatus = "The green is still building; open the step again in a moment."; return; }
        var m = _model; var green = _greenSurface; var fairway = CurrentTerrain()?.Model; var sheet = _sheet; double stimp = StimpNow;
        string key = ShotKey();
        if (key != _shotKey) { _shotContext = null; _shotKey = key; }
        var baseCtx = _shotContext; var putts = _puttingMaps.ToList();
        _pinMapCancel?.Cancel();
        var cancel = new CancellationTokenSource(); _pinMapCancel = cancel;
        MapBusy = true; HasPinMaps = false;
        MapStatus = baseCtx is null ? "Fitting the golfer to the anchors (cached after the first time)…" : $"Building the maps for pin {pin}…";
        var progress = new Progress<string>(s => { if (!cancel.IsCancellationRequested) MapStatus = s; });
        IProgress<string> report = progress;
        Task.Run(() =>
        {
            try
            {
                var ctx = baseCtx; GolferFit? fit = _golferFit;
                if (ctx is null)
                {
                    (ctx, fit) = ApproachSetup.Build(m, green, fairway, sheet, TableBaseline.LoadShipped(sheet.BaselineId), stimp, FitCacheFolder, progress, cancel.Token);
                    foreach (var (k, v) in putts) ctx.PuttingMaps[k] = v;
                }
                report.Report($"Building the short game round pin {pin}…");
                ctx.ShortMap(pin);
                ApproachHeatMap? heat = null; IReadOnlyList<TeeShotOption>? tees = null; byte[]? heatPng = null;
                if (m.Par == 3)
                {
                    report.Report("Playing tee shots from every tee (49 aims each)…");
                    tees = ApproachMaps.TeeShots(ctx, pin);
                }
                else
                {
                    var spots = ApproachMaps.Spots(m, pin, ctx.Golfer.MaxCarry);
                    report.Report($"Playing approaches from {spots.Count} spots, each with its best aim…");
                    heat = ctx.HeatMap(pin, cancel.Token);   // the same spots; whole-hole play-outs on this pin reuse it
                    heatPng = HoleMapRenderer.RenderHeatPng(m, heat, green);
                }
                cancel.Token.ThrowIfCancellationRequested();
                report.Report("Finding the ideal angle in…");
                // A par 3 is played from the chosen tee: the angles at that tee's distance, against the back tee's line,
                // so the ideal direction belongs to the green and each tee shows where it comes in from.
                var pinAt = m.Pins[pin - 1].Frame;
                bool par3 = m.Par == 3;
                var reference = par3 ? ApproachMaps.BackTeeLine(m, pin) : (FramePoint?)null;
                double angleYards = par3 ? pinAt.Length : 150;
                string lineName = !par3 ? "the line of play" : m.TeeIndex == m.Tees.Count - 1 ? "this tee's line" : $"{m.BackTee.Name}'s line";
                var angles = ApproachMaps.Angles(ctx, pin, angleYards, cancel: cancel.Token, reference: reference);
                var teeAngles = par3
                    ? m.Tees.Select(t => (t.Name, ApproachMaps.AngleOf(pinAt, reference!.Value, m.Frame.ToFrame(t.World)), t.Index == m.TeeIndex)).ToList()
                    : new List<(string, double, bool)>();
                var leave = ApproachMaps.Leave(ctx, pin);
                var leavePng = HoleMapRenderer.RenderLeavePng(m, leave, green, angles,
                    tees: par3 ? m.Tees.Select(t => (m.Frame.ToFrame(t.World), t.Name, t.Index == m.TeeIndex)).ToList() : null);
                var maps = new PinMaps(heat, tees, leave, angles, heatPng, leavePng, angleYards, lineName, teeAngles);
                Dispatcher.UIThread.Post(() =>
                {
                    if (cancel.IsCancellationRequested || !ReferenceEquals(m, _model)) return;
                    if (key != ShotKey())
                    {
                        // The setup changed while this built (the terrain switched on, say): build again for the
                        // setup as it is now, rather than dropping the result and leaving the step busy.
                        MapBusy = false;
                        if (IsApproachMap) RefreshApproachMap();
                        return;
                    }
                    _shotContext = ctx; _shotKey = key; _golferFit = fit;
                    _pinMapsKey = key; _pinMaps[pin] = maps;
                    MapBusy = false; MapStatus = "";
                    if (pin == _mapPin) ShowPinMaps(maps);
                    LogMaps(pin, maps.Heat, maps.Tees, maps.Angles, maps.AngleYards, maps.LineName, maps.TeeAngles);
                });
            }
            catch (OperationCanceledException) { Dispatcher.UIThread.Post(() => { if (_pinMapCancel == cancel) MapBusy = false; }); }
            catch (Exception ex) { Dispatcher.UIThread.Post(() => { MapBusy = false; MapStatus = $"The approach map failed: {ex.Message}"; }); }
        });
    }

    void ShowPinMaps(PinMaps maps)
    {
        var m = _model!;
        var pin = m.Pins[_mapPin - 1];
        MapBadge = (_greenGate is null || _greenGate.Band == GateBand.Check ? "Green contours unverified" : $"Green contours {_greenGate.BandText}")
            + (CurrentTerrain() is { } tu ? $" · terrain {tu.Label}" : " · flat outside the green") + " · placeholder physics, sparse secondary baseline";
        BandRows.Clear(); TeeShotRows.Clear(); AngleRows.Clear();
        HeatImage = null; HeatSummary = ""; HeatBest = ""; TeeShotSummary = "";
        if (maps.Heat is { } heat)
        {
            HeatImage = maps.HeatPng is null ? null : new Bitmap(new MemoryStream(maps.HeatPng));
            double off = heat.MedianGain;
            HeatSummary = $"{heat.Spots.Count} spots every {heat.Spacing:0} yds within the golfer's reach, each played with its best of nine aims. "
                + $"Green: better than the other spots the same distance from pin {pin.Number}; red: worse. Overall this pin plays {Math.Abs(off):0.00} {(off < 0 ? "harder" : "easier")} than the baseline from the typical spot"
                + (off < -0.1 ? " (the golfer was fitted on a flat round green, so real greens and their surrounds read harder than Tour)." : ".");
            if (heat.BestAngle is { } best)
                HeatBest = $"Best angle: {best.Yards:0} yds out, {Math.Abs(best.Where.Lat):0} yds {(best.Where.Lat < 0 ? "left" : "right")} of the tee line ({Grounds.Name(best.Lie).ToLowerInvariant()}), "
                    + $"{heat.Relative[best]:0.00} strokes better than the median spot the same distance away.";
            else HeatBest = "No spot is clearly better than the others at its distance: for this pin the angle in hardly matters.";
            foreach (var b in heat.Bands()) BandRows.Add(new BandRowViewModel(b));
        }
        if (maps.Tees is { } tees)
        {
            foreach (var t in tees) TeeShotRows.Add(new TeeShotRowViewModel(t, t.Tee.Index == m.TeeIndex));
            var sel = tees.FirstOrDefault(t => t.Tee.Index == m.TeeIndex) ?? tees[0];
            double save = sel.AtPin.Expected - sel.Best.Expected;
            TeeShotSummary = $"From {sel.Tee.Name} ({sel.Yards:0} yds): the best aim is {AimWords(sel.Best.Offset)}, {sel.Best.Expected:0.00} strokes with {sel.Best.GreenHit:P0} on the green"
                + (save >= 0.02 ? $"; firing at the pin costs {save:0.00}." : "; the pin itself is as good a target as any.");
        }
        LeaveImage = new Bitmap(new MemoryStream(maps.LeavePng));
        var (lo, hi) = maps.Leave.Range;
        string line = maps.LineName; double yds = maps.AngleYards;
        LeaveLegend = $"Strokes still to play by where a shot finishes: green {lo:0.00} or less, gold {(lo + hi) / 2:0.00}, red {hi:0.00} or more. The spokes round the pin are directions in from {yds:0} yds: ink is the ideal one, the rest coloured against it."
            + (maps.TeeAngles.Count > 0 ? " Blue lines: each tee's own line into the pin, the chosen tee's heavier." : "");
        var bestA = maps.Angles.MinBy(a => a.Expected)!; var worstA = maps.Angles.MaxBy(a => a.Expected)!;
        var mine = maps.TeeAngles.FirstOrDefault(t => t.Selected);
        AngleOption? nearest = maps.TeeAngles.Count > 0 ? maps.Angles.MinBy(a => Math.Abs(a.Degrees - mine.Degrees)) : null;
        foreach (var a in maps.Angles) AngleRows.Add(new AngleRowViewModel(a, ReferenceEquals(a, bestA), ReferenceEquals(a, nearest) ? $"{mine.Tee} comes in here" : ""));
        AngleSummary = (Math.Abs(worstA.Expected - bestA.Expected) < 0.03
            ? $"Pin {pin.Number}: every direction in plays about the same ({bestA.Expected:0.00} from {yds:0} yds)."
            : $"Pin {pin.Number}: best approached {DegreesWords(bestA.Degrees, line)} ({bestA.Expected:0.00} from {yds:0} yds, {bestA.GreenHit:P0} on the green), worst {DegreesWords(worstA.Degrees, line)} ({worstA.Expected:0.00}). The angle is worth {worstA.Expected - bestA.Expected:0.00} strokes. Every direction is played from a fairway lie, so only the green and its surrounds count.")
            + (nearest is null ? "" : Math.Abs(mine.Degrees) > 82
                ? $" {mine.Tee} comes in {DegreesWords(mine.Degrees, line)}, outside the directions tried."
                : $" {mine.Tee} comes in {DegreesWords(mine.Degrees, line)}: about {nearest.Expected:0.00} from there, {nearest.Expected - bestA.Expected:0.00} off the ideal.");
        AngleNote = $"Degrees right (R) or left (L) of {line}, seen looking at the green, all from {yds:0} yds with each direction's best aim."
            + (maps.TeeAngles.Count > 0 ? " The tees: " + string.Join(", ", maps.TeeAngles.Select(t => $"{t.Tee} {(Math.Abs(t.Degrees) < 0.5 ? "0°" : $"{Math.Abs(t.Degrees):0}° {(t.Degrees > 0 ? "R" : "L")}")}")) + "." : "");
        HasPinMaps = true;
    }

    static string AimWords(FramePoint o) => o.Length < 1e-9 ? "at the pin"
        : string.Join(" and ", new[] { Math.Abs(o.Lat) > 0.1 ? $"{Math.Abs(o.Lat):0} yds {(o.Lat > 0 ? "right" : "left")}" : null, Math.Abs(o.Along) > 0.1 ? $"{Math.Abs(o.Along):0} yds {(o.Along > 0 ? "long" : "short")}" : null }.Where(s => s is not null)) + " of the pin";

    static string DegreesWords(double d, string line = "the line of play") => Math.Abs(d) < 0.5 ? $"straight down {line}" : $"from {Math.Abs(d):0}° {(d > 0 ? "right" : "left")} of {line}";
}

public sealed class BandRowViewModel(DistanceBand b)
{
    public string Yards { get; } = $"{b.From:0}–{b.To:0}";
    public string Best { get; } = $"{b.Best.Expected:0.00} at {Side(b.Best.Where.Lat)}";
    public string Worst { get; } = $"{b.Worst.Expected:0.00} at {Side(b.Worst.Where.Lat)}";
    public string Premium { get; } = $"{b.Premium:0.00}";
    public FontWeight Weight { get; } = b.Premium >= 0.1 ? FontWeight.SemiBold : FontWeight.Normal;
    static string Side(double lat) => Math.Abs(lat) < 2.5 ? "centre" : $"{Math.Abs(lat):0} {(lat < 0 ? "L" : "R")}";
}

public sealed class TeeShotRowViewModel(TeeShotOption t, bool selected)
{
    public string Tee { get; } = t.Tee.Name + (selected ? " ●" : "");
    public string Yards { get; } = $"{t.Yards:0}";
    public string PlaysLike { get; } = $"{t.PlaysLike:0}";
    public string Aim { get; } = t.Best.Offset.Length < 1e-9 ? "pin" : $"{t.Best.Offset.Lat:+0;-0;0} / {t.Best.Offset.Along:+0;-0;0}";
    public string Expected { get; } = $"{t.Best.Expected:0.00}";
    public string Green { get; } = $"{t.Best.GreenHit:P0}";
    public string AtPin { get; } = $"{t.AtPin.Expected:0.00}";
    public string Sg { get; } = t.StrokesGained is { } sg ? $"{sg:+0.00;-0.00;0.00}" : "—";
}

public sealed class AngleRowViewModel(AngleOption a, bool best, string tee = "")
{
    public string Degrees { get; } = Math.Abs(a.Degrees) < 0.5 ? "0°" : $"{Math.Abs(a.Degrees):0}° {(a.Degrees > 0 ? "R" : "L")}";
    public string Expected { get; } = $"{a.Expected:0.00}";
    public string Green { get; } = $"{a.GreenHit:P0}";
    public string Note { get; } = string.Join(" · ", new[] { best ? "ideal" : "", tee }.Where(s => s.Length > 0));
    public FontWeight Weight { get; } = best ? FontWeight.SemiBold : FontWeight.Normal;
}
