using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.HoleSim;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// Second shot (Stage 4d): on a par 5, from where the drive finishes, go for the green with the player's longest
/// fairway club or lay up to a fairway spot, each valued by the third shot it leaves (the approach heat map).
/// </summary>
public partial class HoleSimViewModel
{
    public const string SecondShotStep = "Second shot";

    CancellationTokenSource? _secondCancel;
    int _secondPin = 1;
    string _secondFrom = "drive";

    public bool IsSecondShot => Step == SecondShotStep;
    [RelayCommand] private void GoSecondShot() => Step = SecondShotStep;

    public ObservableCollection<TabViewModel> SecondPinButtons { get; } = [];
    public ObservableCollection<TabViewModel> SecondFromButtons { get; } = [];
    public ObservableCollection<LayupRowViewModel> LayupRows { get; } = [];

    [ObservableProperty] private string _secondYards = "230";
    [ObservableProperty] private string _secondSide = "0";
    [ObservableProperty] private string _secondSkillText = "1.0";
    [ObservableProperty] private bool _secondBusy;
    [ObservableProperty] private string _secondStatus = "";
    [ObservableProperty] private string _secondIntro = "";
    [ObservableProperty] private string _secondFromText = "";
    [ObservableProperty] private bool _hasSecond;
    [ObservableProperty] private Bitmap? _secondMap;
    [ObservableProperty] private string _secondTitle = "";
    [ObservableProperty] private string _secondBadge = "";
    [ObservableProperty] private string _secondVerdict = "";
    [ObservableProperty] private IBrush _secondVerdictBrush = Brush.Parse("#2D6A42");
    [ObservableProperty] private string _secondGo = "";
    [ObservableProperty] private string _secondLayup = "";
    [ObservableProperty] private string _secondDepends = "";

    public bool ShowSecondYards => _secondFrom == "yards";
    public bool SecondIsPar5 => _model?.Par == 5;

    void RefreshSecondShot()
    {
        if (_model is null) return;
        if (_secondPin > _model.Pins.Count) _secondPin = 1;
        SecondPinButtons.Clear();
        foreach (var p in _model.Pins) { int n = p.Number; SecondPinButtons.Add(new TabViewModel($"Pin {n}  {p.Where}", n == _secondPin, () => SelectSecondPin(n))); }
        SecondFromButtons.Clear();
        SecondFromButtons.Add(new TabViewModel("Where the best drive finishes", _secondFrom == "drive", () => SetSecondFrom("drive")));
        SecondFromButtons.Add(new TabViewModel("Yards and side", _secondFrom == "yards", () => SetSecondFrom("yards")));
        double reach = _sheet.Reach(_player);
        string player = _player == "power" ? "Power Drive" : "Normal";
        SecondIntro = _model.Par == 5
            ? $"{player}'s longest club off the fairway: {reach:0} yds, carry and roll [{AssumptionRow.Tag(_sheet[_player == "power" ? "player.power.reach" : "player.normal.reach"].Source)}]. Change the player in step 6 and the club in the assumptions."
            : $"Hole {_model.Number} is a par {_model.Par}: the second-shot choice is for par 5s. It still runs from any spot you give it.";
        OnPropertyChanged(nameof(SecondIsPar5)); OnPropertyChanged(nameof(ShowSecondYards));
        UpdateSecondFrom();
    }

    void SelectSecondPin(int pin)
    {
        _secondPin = pin;
        foreach (var b in SecondPinButtons) b.IsSelected = b.Label.StartsWith($"Pin {pin} ");
        UpdateSecondFrom();
    }

    void SetSecondFrom(string mode)
    {
        _secondFrom = mode;
        foreach (var b in SecondFromButtons) b.IsSelected = (mode == "drive") == b.Label.StartsWith("Where");
        OnPropertyChanged(nameof(ShowSecondYards));
        UpdateSecondFrom();
    }

    partial void OnSecondYardsChanged(string value) => UpdateSecondFrom();
    partial void OnSecondSideChanged(string value) => UpdateSecondFrom();

    (FramePoint? From, string Text) SecondFromSpot()
    {
        var m = _model!; var pinAt = m.Pins[_secondPin - 1].Frame;
        if (_secondFrom == "drive")
        {
            if (_lastRun is null) return (null, "Run the tee shots in step 6 first; this plays from where the best line's drives typically finish.");
            var fins = _lastRun.Best.Samples.Where(s => s.Ground == Ground.Fairway).Select(s => s.Finish).ToList();
            if (fins.Count == 0) fins = _lastRun.Best.Samples.Select(s => s.Finish).ToList();
            var mid = fins.OrderBy(f => f.DistanceTo(pinAt)).ToList()[fins.Count / 2];
            return (mid, $"From the typical finish of the best line ({_lastRun.Best.Aim:+0;-0;0} off the aim marker, {_lastRun.Player.Name}): {mid.DistanceTo(pinAt):0} yds from pin {_secondPin}.");
        }
        if (Num(SecondYards) is not { } y || y < 60) return (null, "Enter the yards to the pin (60 or more).");
        double side = Num(SecondSide) ?? 0;
        var spot = ApproachSetup.SpotOnRoute(m, _secondPin, y, side);
        return (spot, $"{y:0} yds from pin {_secondPin}" + (Math.Abs(side) < 0.5 ? "" : $", {Math.Abs(side):0} yds {(side > 0 ? "right" : "left")} of the line") + $": {Grounds.Name(m.Grid.At(spot)).ToLowerInvariant()}.");
    }

    void UpdateSecondFrom() { if (_model is not null) SecondFromText = SecondFromSpot().Text; }

    [RelayCommand] private void RunSecondShot()
    {
        if (_model is null) return;
        if (_greenSurface is null) { SecondStatus = "The green is still building; try again in a moment."; return; }
        var (from, fromText) = SecondFromSpot();
        if (from is null) { SecondStatus = fromText; return; }
        double skill = Num(SecondSkillText) is { } sk && sk > 0.2 && sk < 5 ? sk : 1;
        var m = _model; int pin = _secondPin; var green = _greenSurface; var fairway = CurrentTerrain()?.Model; var sheet = _sheet; double stimp = StimpNow;
        double reach = _sheet.Reach(_player); string playerId = _player;
        string key = ShotKey();
        if (key != _shotKey) { _shotContext = null; _shotKey = key; }
        var baseCtx = _shotContext; var putts = _puttingMaps.ToList();
        // Step 9's heat map for this pin is the third-shot value field when it was built for the fitted golfer.
        var cachedHeat = Math.Abs(skill - 1) < 1e-9 && _pinMapsKey == key && _pinMaps.TryGetValue(pin, out var pm) ? pm.Heat : null;
        _sheet.SavePriors(_priorsPath);
        _secondCancel?.Cancel();
        var cancel = new CancellationTokenSource(); _secondCancel = cancel;
        SecondBusy = true; SecondStatus = baseCtx is null ? "Fitting the golfer to the anchors (cached after the first time)…" : "Building the third-shot map…";
        var progress = new Progress<string>(s => { if (!cancel.IsCancellationRequested) SecondStatus = s; });
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
                var run = Math.Abs(skill - 1) < 1e-9 ? ctx : ctx.WithGolfer(ctx.Golfer with { Skill = skill });
                report.Report($"Building the short game round pin {pin}…");
                run.ShortMap(pin);
                var heat = cachedHeat;
                if (heat is null)
                {
                    report.Report("Valuing every third-shot spot (the approach heat map)…");
                    heat = ApproachMaps.Heat(run, pin, ApproachMaps.Spots(m, pin, run.Golfer.MaxCarry), cancel: cancel.Token);
                }
                report.Report("Going for it against every lay-up in reach…");
                var plan = SecondShot.Plan(run, heat, from.Value, pin, reach, cancel: cancel.Token);
                var png = HoleMapRenderer.RenderSecondShotPng(m, plan, green, heat.Spacing);
                Dispatcher.UIThread.Post(() =>
                {
                    if (cancel.IsCancellationRequested || !ReferenceEquals(m, _model)) return;
                    if (key == _shotKey) _shotContext = ctx;
                    _golferFit = fit;
                    SecondBusy = false; SecondStatus = "";
                    ShowSecondShot(plan, png, fromText, playerId, skill);
                });
            }
            catch (OperationCanceledException) { Dispatcher.UIThread.Post(() => { if (_secondCancel == cancel) SecondBusy = false; }); }
            catch (Exception ex) { Dispatcher.UIThread.Post(() => { SecondBusy = false; SecondStatus = $"The second-shot run failed: {ex.Message}"; }); }
        });
    }

    void ShowSecondShot(SecondShotPlan plan, byte[] png, string fromText, string playerId, double skill)
    {
        var m = _model!;
        SecondMap = new Bitmap(new MemoryStream(png));
        string player = playerId == "power" ? "Power Drive" : "Normal";
        SecondTitle = $"Hole {m.Number} · pin {_secondPin} · {player} · {plan.Yards:0} yds from the {Grounds.Name(plan.Lie).ToLowerInvariant()} · longest club {plan.Reach:0}"
            + (Math.Abs(skill - 1) > 1e-9 ? $" · spreads ×{skill:0.##}" : "");
        SecondBadge = (_greenGate is null || _greenGate.Band == GateBand.Check ? "Green contours unverified" : $"Green contours {_greenGate.BandText}")
            + (CurrentTerrain() is { } tu ? $" · terrain {tu.Label}" : " · flat outside the green") + " · placeholder club distance and physics";
        var b = plan.BestLayup!;
        double gain = Math.Abs(plan.GoGain);
        SecondVerdict = (plan.ShouldGo ? (plan.Go.InReach ? "Go for the green" : "Hit it as close as it goes") : $"Lay up to {b.TargetYards:0} yds")
            + $": {gain:0.00} strokes better" + (gain < 0.03 ? " — a coin toss within the run's noise." : ".");
        SecondVerdictBrush = Brush.Parse(gain < 0.03 ? "#B08A2E" : plan.ShouldGo ? "#2D6A42" : "#3A6EA5");
        SecondGo = (plan.Go.InReach ? $"Going for it (best of nine aims round the pin): " : $"The green is {plan.Yards - plan.Reach:0} yds out of reach; the longest club at it: ")
            + $"{plan.Go.Expected:0.00} expected, {plan.Go.GreenHit:P0} on the green, {plan.Go.HazardShare:P0} in bunkers, water or scrub.";
        SecondLayup = $"Best lay-up: to {b.TargetYards:0} yds, {Math.Abs(b.Target.Lat):0} yds {(b.Target.Lat < 0 ? "left" : "right")} of the tee line: {b.Expected:0.00} expected, {b.FairwayShare:P0} find the fairway, {b.HazardShare:P0} hazards. "
            + $"{plan.Layups.Count} fairway targets tried, the best five again on fresh shots.";
        LayupRows.Clear();
        foreach (var l in plan.ByDistance()) LayupRows.Add(new LayupRowViewModel(l, plan.Go.Expected, ReferenceEquals(l, b) || (l.Target == b.Target)));
        SecondFromText = fromText;
        var ph = _sheet.Rows.Where(x => x.IsPlaceholder && (x.Id == (playerId == "power" ? "player.power.reach" : "player.normal.reach") || x.Group is "Approach" or "Greens")).Select(x => x.Label).ToList();
        SecondDepends = ph.Count == 0 ? "" : $"Depends on {ph.Count} placeholder values, the longest club first: {string.Join("; ", ph)}.";
        HasSecond = true;
    }
}

public sealed class LayupRowViewModel(LayupOption l, double go, bool best)
{
    public string Yards { get; } = $"{l.TargetYards:0}";
    public string Where { get; } = Math.Abs(l.Target.Lat) < 2.5 ? "centre" : $"{Math.Abs(l.Target.Lat):0} {(l.Target.Lat < 0 ? "L" : "R")}";
    public string Expected { get; } = $"{l.Expected:0.00}";
    public string VsGo { get; } = $"{go - l.Expected:+0.00;-0.00;0.00}";
    public IBrush VsGoInk { get; } = Brush.Parse(go - l.Expected > 0 ? "#2D6A42" : "#B4471F");
    public string Fairway { get; } = $"{l.FairwayShare:P0}";
    public FontWeight Weight { get; } = best ? FontWeight.SemiBold : FontWeight.Normal;
}

public partial class HoleSimViewModel
{
    /// <summary>When a new green is ready (another hole, tee or save), the shot step that's open starts over on it.</summary>
    void RefreshShotSteps()
    {
        if (IsApproach) RefreshApproach();
        if (IsApproachMap) RefreshApproachMap();
        if (IsSecondShot) RefreshSecondShot();
    }
}
