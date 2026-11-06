using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.HoleSim;
using SkiaSharp;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// Approach (Stage 4b): shots at the green from a spot the user picks (yards and side, the tee on a par 3, or where
/// the best drive typically finishes), to the best aim, the pin, the green's centre, an aim distance or a point. The
/// golfer is fitted to the baseline's anchors once (cached on disk); the short game round each pin is built once per
/// pin and kept until the green, the terrain or the assumptions change.
/// </summary>
public partial class HoleSimViewModel
{
    public const string ApproachStep = "Approach";

    static readonly string FitCacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CourseDesignerStudio", "holesim");

    GeometryRunResult? _lastRun;
    ShotContext? _shotContext;
    GolferFit? _golferFit;
    string _shotKey = "";
    CancellationTokenSource? _approachCancel;
    int _approachPin = 1;
    string _fromMode = "yards", _aimMode = "best";
    (int Hole, int Tee) _approachHole = (0, -1);

    public bool IsApproach => Step == ApproachStep;
    [RelayCommand] private void GoApproach() => Step = ApproachStep;

    public ObservableCollection<TabViewModel> ApproachPinButtons { get; } = [];
    public ObservableCollection<TabViewModel> FromButtons { get; } = [];
    public ObservableCollection<TabViewModel> AimButtons { get; } = [];
    public ObservableCollection<AimOptionRowViewModel> ApproachAimRows { get; } = [];
    public ObservableCollection<CalibrationRowViewModel> GolferRows { get; } = [];
    public ObservableCollection<LegendItemViewModel> ApproachLegend { get; } = [];

    [ObservableProperty] private string _fromYards = "150";
    [ObservableProperty] private string _fromSide = "0";
    [ObservableProperty] private string _aimDistanceText = "150";
    [ObservableProperty] private string _aimRightText = "0";
    [ObservableProperty] private string _aimLongText = "0";
    [ObservableProperty] private string _skillText = "1.0";
    [ObservableProperty] private bool _approachBusy;
    [ObservableProperty] private string _approachStatus = "";
    [ObservableProperty] private Bitmap? _approachMap;
    [ObservableProperty] private bool _hasApproach;
    [ObservableProperty] private bool _hasAimRows;
    [ObservableProperty] private string _approachTitle = "";
    [ObservableProperty] private string _approachBadge = "";
    [ObservableProperty] private string _approachHeadline = "";
    [ObservableProperty] private string _approachShape = "";
    [ObservableProperty] private string _approachSg = "";
    [ObservableProperty] private string _approachDepends = "";
    [ObservableProperty] private string _approachFromText = "";
    [ObservableProperty] private string _golferTitle = "The golfer isn't fitted yet: it is fitted to the anchors the first time you run (a few seconds), then kept.";
    [ObservableProperty] private string _golferDetail = "";

    public bool ShowFromYards => _fromMode == "yards";
    public bool ShowAimDistance => _aimMode == "distance";
    public bool ShowAimPoint => _aimMode == "point";

    /// <summary>Called when the step opens: pins, the from choices for this hole, and what the run will use.</summary>
    void RefreshApproach()
    {
        if (_model is null) return;
        if (_approachHole != (_model.Number, _model.TeeIndex))
        {
            // Another hole or tee: start from its natural spot (the tee on a par 3, the best drive's finish once there's a run).
            _approachHole = (_model.Number, _model.TeeIndex);
            _approachPin = 1;
            _fromMode = _model.Par == 3 ? "tee" : _lastRun is not null ? "drive" : "yards";
        }
        if (_approachPin > _model.Pins.Count) _approachPin = 1;
        ApproachPinButtons.Clear();
        foreach (var p in _model.Pins) { int n = p.Number; ApproachPinButtons.Add(new TabViewModel($"Pin {n}  {p.Where}", n == _approachPin, () => SelectApproachPin(n))); }
        bool par3 = _model.Par == 3;
        if (par3 && _fromMode == "drive") _fromMode = "tee";
        if (!par3 && _fromMode == "tee") _fromMode = "yards";
        FromButtons.Clear();
        if (par3) FromButtons.Add(new TabViewModel("The tee", _fromMode == "tee", () => SetFrom("tee")));
        else FromButtons.Add(new TabViewModel("Where the best drive finishes", _fromMode == "drive", () => SetFrom("drive")));
        FromButtons.Add(new TabViewModel("Yards and side", _fromMode == "yards", () => SetFrom("yards")));
        AimButtons.Clear();
        foreach (var (id, label) in new[] { ("best", "Best aim"), ("pin", "The pin"), ("centre", "Green centre"), ("distance", "Aim distance"), ("point", "A point off the pin") })
            AimButtons.Add(new TabViewModel(label, _aimMode == id, () => SetAim(id)));
        UpdateFromText();
        OnPropertyChanged(nameof(ShowFromYards)); OnPropertyChanged(nameof(ShowAimDistance)); OnPropertyChanged(nameof(ShowAimPoint));
    }

    void SelectApproachPin(int pin)
    {
        _approachPin = pin;
        foreach (var b in ApproachPinButtons) b.IsSelected = b.Label.StartsWith($"Pin {pin} ");
        UpdateFromText();
    }

    void SetFrom(string mode)
    {
        _fromMode = mode;
        foreach (var b in FromButtons) b.IsSelected = (mode, b.Label) is ("tee", "The tee") or ("drive", "Where the best drive finishes") or ("yards", "Yards and side");
        OnPropertyChanged(nameof(ShowFromYards));
        UpdateFromText();
    }

    void SetAim(string mode)
    {
        _aimMode = mode;
        string label = mode switch { "best" => "Best aim", "pin" => "The pin", "centre" => "Green centre", "distance" => "Aim distance", _ => "A point off the pin" };
        foreach (var b in AimButtons) b.IsSelected = b.Label == label;
        OnPropertyChanged(nameof(ShowAimDistance)); OnPropertyChanged(nameof(ShowAimPoint));
    }

    partial void OnFromYardsChanged(string value) => UpdateFromText();
    partial void OnFromSideChanged(string value) => UpdateFromText();

    static double? Num(string s) => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v
        : double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out v) ? v : null;

    /// <summary>The spot the shot is played from, or why there isn't one.</summary>
    (FramePoint? From, bool Tee, string Text) ApproachFrom()
    {
        var m = _model!;
        var pinAt = m.Pins[_approachPin - 1].Frame;
        switch (_fromMode)
        {
            case "tee":
                return (new FramePoint(0, 0), true, $"From {m.Tee.Name}, {pinAt.Length:0} yds to pin {_approachPin}.");
            case "drive":
                if (_lastRun is null) return (null, false, "Run the tee shots in step 6 first; this plays from where the best line's drives typically finish.");
                var best = _lastRun.Best;
                var finishes = best.Samples.Where(s => s.Ground == Ground.Fairway).Select(s => s.Finish).ToList();
                if (finishes.Count == 0) finishes = best.Samples.Select(s => s.Finish).ToList();
                var ordered = finishes.OrderBy(f => f.DistanceTo(pinAt)).ToList();
                var mid = ordered[ordered.Count / 2];
                return (mid, false, $"From the typical finish of the best line ({best.Aim:+0;-0;0} off the aim marker, {_lastRun.Player.Name}): the median of its fairway finishes, {mid.DistanceTo(pinAt):0} yds from pin {_approachPin}.");
            default:
                if (Num(FromYards) is not { } y || y < 5) return (null, false, "Enter the yards to the pin (5 or more).");
                double side = Num(FromSide) ?? 0;
                var spot = ApproachSetup.SpotOnRoute(m, _approachPin, y, side);
                return (spot, false, $"{y:0} yds from pin {_approachPin} on the line of play" + (Math.Abs(side) < 0.5 ? "" : $", {Math.Abs(side):0} yds {(side > 0 ? "right" : "left")} of it")
                    + $": {Grounds.Name(m.Grid.At(spot)).ToLowerInvariant()}.");
        }
    }

    void UpdateFromText()
    {
        if (_model is null) { ApproachFromText = ""; return; }
        ApproachFromText = ApproachFrom().Text;
    }

    string ShotKey() => FormattableString.Invariant(
        $"{_greenSurface?.TerrainId}|{_greenSurface?.SquareYards}|{CurrentTerrain()?.Model.Id}|{StimpNow:F2}|{_sheet.BaselineId}|{_sheet.Putting()}|{_sheet.ShotRules()}|{_sheet.GolferStart()}|{string.Join(",", _sheet.GroundRules(GreenFriction).ByGround.Select(kv => $"{kv.Key}:{kv.Value}"))}|{_sheet.Lies()}");

    /// <summary>Drops the approach context (another hole, green, terrain or assumptions).</summary>
    void ResetApproach()
    {
        _approachCancel?.Cancel();
        _shotContext = null; _shotKey = "";
        ResetApproachMaps();
        HasApproach = false; ApproachMap = null; ApproachAimRows.Clear(); ApproachLegend.Clear(); ApproachStatus = "";
    }

    [RelayCommand] private void RunApproach()
    {
        if (_model is null) return;
        if (_greenSurface is null) { ApproachStatus = "The green is still building; try again in a moment."; return; }
        var (from, tee, fromText) = ApproachFrom();
        if (from is null) { ApproachStatus = fromText; return; }
        double skill = Num(SkillText) is { } sk && sk > 0.2 && sk < 5 ? sk : 1;
        var m = _model; int pin = _approachPin; var pinAt = m.Pins[pin - 1].Frame;
        string aimMode = _aimMode; double aimDist = Num(AimDistanceText) ?? pinAt.DistanceTo(from.Value);
        double aimRight = Num(AimRightText) ?? 0, aimLong = Num(AimLongText) ?? 0;
        var green = _greenSurface; var fairway = CurrentTerrain()?.Model; var sheet = _sheet; double stimp = StimpNow;
        string key = ShotKey();
        if (key != _shotKey) { _shotContext = null; _shotKey = key; }
        var baseCtx = _shotContext;
        var maps = _puttingMaps.ToList();   // the Green read step's maps roll on this same green
        _sheet.SavePriors(_priorsPath);
        _approachCancel?.Cancel();
        var cancel = new CancellationTokenSource(); _approachCancel = cancel;
        ApproachBusy = true; ApproachStatus = baseCtx is null ? "Fitting the golfer to the anchors (cached after the first time)…" : "Playing shots…";
        var progress = new Progress<string>(s => { if (!cancel.IsCancellationRequested) ApproachStatus = s; });
        Task.Run(() =>
        {
            try
            {
                var ctx = baseCtx; GolferFit? fit = _golferFit;
                if (ctx is null)
                {
                    var baseline = TableBaseline.LoadShipped(sheet.BaselineId);
                    (ctx, fit) = ApproachSetup.Build(m, green, fairway, sheet, baseline, stimp, FitCacheFolder, progress, cancel.Token);
                    foreach (var (k, v) in maps) ctx.PuttingMaps[k] = v;
                }
                var run = Math.Abs(skill - 1) < 1e-9 ? ctx : ctx.WithGolfer(ctx.Golfer with { Skill = skill });
                if (!run.ShortMaps.ContainsKey(pin)) ((IProgress<string>)progress).Report($"Building the short game round pin {pin} (chips from every spot within {ShortGameMap.Reach} yds)…");
                run.ShortMap(pin);
                cancel.Token.ThrowIfCancellationRequested();
                FramePoint aim; IReadOnlyList<AimOption>? sweep = null;
                var dir = (pinAt - from.Value) * (1 / Math.Max((pinAt - from.Value).Length, 0.5)); var right = new FramePoint(dir.Along, -dir.Lat);
                switch (aimMode)
                {
                    case "best":
                        ((IProgress<string>)progress).Report("Trying 25 aims round the pin…");
                        sweep = ApproachSim.Sweep(run, from.Value, pin, fromTee: tee); aim = sweep[0].Aim; break;
                    case "pin": aim = pinAt; break;
                    case "centre": aim = m.GreenFrame; break;
                    case "distance": aim = from.Value + dir * aimDist; break;
                    default: aim = pinAt + right * aimRight + dir * aimLong; break;
                }
                cancel.Token.ThrowIfCancellationRequested();
                var res = ApproachSim.Run(run, from.Value, aim, pin, fromTee: tee);
                var png = RenderApproach(m, run, res, from.Value, aim, pin);
                Dispatcher.UIThread.Post(() =>
                {
                    if (cancel.IsCancellationRequested || !ReferenceEquals(m, _model)) return;
                    if (key == _shotKey) _shotContext = ctx;
                    _golferFit = fit;
                    ApproachBusy = false; ApproachStatus = "";
                    ShowApproach(res, sweep, png, fromText, aimMode, skill, run.Golfer);
                });
            }
            catch (OperationCanceledException) { Dispatcher.UIThread.Post(() => { if (_approachCancel == cancel) ApproachBusy = false; }); }
            catch (Exception ex) { Dispatcher.UIThread.Post(() => { ApproachBusy = false; ApproachStatus = $"The approach run failed: {ex.Message}"; }); }
        });
    }

    static readonly SKColor OnGreenInk = SKColor.Parse("#1F1D1A"), MissInk = SKColor.Parse("#B5473A"), FromInk = SKColor.Parse("#E06A3A"), AimInk = SKColor.Parse("#3A6EA5");

    static byte[] RenderApproach(HoleModel m, ShotContext c, ApproachResult res, FramePoint from, FramePoint aim, int pin)
    {
        var pts = res.Shots.Select(s => (s.Finish, s.Lie == Ground.Green && c.Green.OnGreen(s.Finish) ? OnGreenInk : MissInk)).ToList();
        // A close-up round the green and where the shots finish; a far-off spot is marked on the edge, on the line to it.
        var finishes = res.Shots.Select(s => s.Finish).Concat(c.Green.Cells).ToList();
        double lat0 = finishes.Min(p => p.Lat) - 10, lat1 = finishes.Max(p => p.Lat) + 10;
        double al0 = finishes.Min(p => p.Along) - 10, al1 = finishes.Max(p => p.Along) + 10;
        void Grow(ref double lo, ref double hi, double min) { double extra = (min - (hi - lo)) / 2; if (extra > 0) { lo -= extra; hi += extra; } }
        Grow(ref lat0, ref lat1, 70); Grow(ref al0, ref al1, 80);
        lat0 = Math.Max(lat0, m.Grid.LatMin); lat1 = Math.Min(lat1, m.Grid.LatMax); al0 = Math.Max(al0, m.Grid.AlongMin); al1 = Math.Min(al1, m.Grid.AlongMax);
        bool inside = from.Lat > lat0 && from.Lat < lat1 && from.Along > al0 && from.Along < al1;
        var label = from;
        if (!inside)
        {
            // Walk from the aim toward the spot until the window's edge (less a margin for the label).
            var d = from - aim; double len = d.Length; var u = d * (1 / Math.Max(len, 1e-9));
            double s = len;
            foreach (var (lo, hi, comp, start) in new[] { (lat0 + 4, lat1 - 4, u.Lat, aim.Lat), (al0 + 3, al1 - 3, u.Along, aim.Along) })
                if (Math.Abs(comp) > 1e-9) s = Math.Min(s, ((comp > 0 ? hi : lo) - start) / comp);
            label = aim + u * Math.Max(0, s);
        }
        string text = inside ? "from" : $"from {res.Yards:0} yds";
        var overlay = new MapOverlay(pts, inside ? [(from, FromInk), (aim, AimInk)] : [(aim, AimInk)], [(label, text, FromInk)]);
        double ppy = Math.Min(560 / (al1 - al0), 500 / (lat1 - lat0));
        return HoleMapRenderer.RenderPng(m, new MapView { PixelsPerYard = ppy, SelectedPin = pin, Overlay = overlay, AlongFrom = al0, AlongTo = al1, LatFrom = lat0, LatTo = lat1, ShowWidths = false });
    }

    void ShowApproach(ApproachResult r, IReadOnlyList<AimOption>? sweep, byte[] png, string fromText, string aimMode, double skill, GolferParams golfer)
    {
        var m = _model!;
        ApproachMap = new Bitmap(new MemoryStream(png));
        ApproachLegend.Clear();
        ApproachLegend.Add(new LegendItemViewModel("finished on the green", Swatch(OnGreenInk)));
        ApproachLegend.Add(new LegendItemViewModel("missed", Swatch(MissInk)));
        ApproachLegend.Add(new LegendItemViewModel("from", Swatch(FromInk)));
        ApproachLegend.Add(new LegendItemViewModel("aim", Swatch(AimInk)));
        var pinAt = m.Pins[r.Pin - 1].Frame;
        var dir = (pinAt - r.From) * (1 / Math.Max((pinAt - r.From).Length, 0.5));
        var rel = r.Aim - pinAt;
        double aimLong = rel.Lat * dir.Lat + rel.Along * dir.Along, aimSide = rel.Lat * dir.Along - rel.Along * dir.Lat;
        string aimText = Math.Abs(aimLong) < 0.5 && Math.Abs(aimSide) < 0.5 ? "at the pin"
            : string.Join(", ", new[] { Math.Abs(aimSide) >= 0.5 ? $"{Math.Abs(aimSide):0} yds {(aimSide > 0 ? "right" : "left")}" : null, Math.Abs(aimLong) >= 0.5 ? $"{Math.Abs(aimLong):0} {(aimLong > 0 ? "beyond" : "short of")}" : null }.Where(s => s is not null)) + " the pin";
        string lie = r.Lie == Ground.Fairway && _fromMode == "tee" ? "tee" : Grounds.Name(r.Lie).ToLowerInvariant();
        ApproachTitle = $"Hole {m.Number} · pin {r.Pin} · {r.Yards:0} yds from the {lie} · plays like {r.PlaysLike:0} · {r.Samples} shots · aim {aimText}" + (Math.Abs(skill - 1) > 1e-9 ? $" · spreads ×{skill:0.##}" : "");
        ApproachBadge = (_greenGate is null || _greenGate.Band == GateBand.Check ? "Green contours unverified" : $"Green contours {_greenGate.BandText}")
            + (CurrentTerrain() is { } t ? $" · terrain {t.Label}" : " · flat outside the green") + " · placeholder physics, sparse secondary baseline";
        ApproachHeadline = $"{r.GreenHit:P0} finish on the green, {(double.IsNaN(r.ProximityFt) ? "—" : $"{r.ProximityFt:0} ft")} from the hole on average when they do. Expected strokes to hole out from here: {r.Expected:0.00} (± {r.SgCi95:0.00}).";
        double hazards = r.Share(Ground.Bunker) + r.Share(Ground.Water) + r.Share(Ground.Scrub);
        ApproachShape = $"Misses: short {r.Short:P0}, long {r.Long:P0}, left {r.Left:P0}, right {r.Right:P0}."
            + (hazards > 0.005 ? $" Bunker {r.Share(Ground.Bunker):P0}, water {r.Share(Ground.Water):P0}, scrub {r.Share(Ground.Scrub):P0}." : " No hazards reached.");
        ApproachSg = r.StrokesGained is { } sg
            ? $"Strokes gained for the shot: {sg:+0.00;-0.00;0.00}. The baseline expects {r.Expected + sg:0.00} from {r.Yards:0} yds in the {(lie == "tee" ? "tee" : lie)}; this green, its pins and what surrounds it make it {(sg >= 0 ? "easier" : "harder")} than that by {Math.Abs(sg):0.00}."
            : $"The baseline has no row for this start ({r.StartCoverage}), so there's no strokes gained for the shot; the expected strokes still stand.";
        ApproachFromText = fromText;
        ApproachAimRows.Clear();
        if (sweep is not null)
        {
            var atPin = sweep.FirstOrDefault(o => o.Offset.Length < 1e-9);
            foreach (var o in sweep.Take(5)) ApproachAimRows.Add(new AimOptionRowViewModel(o, ReferenceEquals(o, sweep[0]), false));
            if (atPin is not null && !sweep.Take(5).Contains(atPin)) ApproachAimRows.Add(new AimOptionRowViewModel(atPin, false, true));
        }
        HasAimRows = ApproachAimRows.Count > 0;
        if (_golferFit is { } fit)
        {
            GolferRows.Clear();
            foreach (var row in fit.Rows) GolferRows.Add(new CalibrationRowViewModel(row));
            var worst = fit.Rows.OrderByDescending(x => Math.Abs(x.Error)).First();
            GolferTitle = $"Golfer fitted to {fit.BaselineId}: within {fit.RmsError:0.00} strokes of the {fit.Rows.Count} anchors off the green on average (RMS); the worst is {worst.Lie.ToString().ToLowerInvariant()} at {worst.Yards:0} yds, {worst.Error:+0.00;-0.00;0.00}.";
            GolferDetail = $"Spread {golfer.DistPct:P1} of the shot long and short, {golfer.SidePct:P1} side to side, growing {golfer.LongGrowth:0.00} per 100 yds past 150, plus {golfer.FloorYds:0.0} yds on every shot. "
                + $"Rough ×{golfer.RoughMult:0.00}, sand ×{golfer.SandMult:0.00}, scrub (recovery) ×{golfer.RecoveryMult:0.00}, tee ×{golfer.TeeMult:0.00}. Longest shot {golfer.MaxCarry:0} yds, out of scrub {golfer.RecoveryCarry:0}.";
        }
        var ph = _sheet.Rows.Where(x => x.IsPlaceholder && x.Group is "Approach" or "Greens" or "Ground" or "Scoring" && !x.Id.StartsWith("ground.landingSpeed")).Select(x => x.Label).ToList();
        ApproachDepends = ph.Count == 0 ? "" : $"Depends on {ph.Count} placeholder values: {string.Join("; ", ph)}.";
        HasApproach = true;
        LogApproach(r, aimMode);
    }

    static IBrush Swatch(SKColor c) => new SolidColorBrush(Color.FromRgb(c.Red, c.Green, c.Blue));
}

public sealed class AimOptionRowViewModel(AimOption o, bool best, bool atPin)
{
    public string Aim { get; } = o.Offset.Length < 1e-9 ? "at the pin"
        : string.Join(", ", new[] { Math.Abs(o.Offset.Lat) > 0.1 ? $"{Math.Abs(o.Offset.Lat):0} {(o.Offset.Lat > 0 ? "right" : "left")}" : null, Math.Abs(o.Offset.Along) > 0.1 ? $"{Math.Abs(o.Offset.Along):0} {(o.Offset.Along > 0 ? "beyond" : "short")}" : null }.Where(s => s is not null));
    public string Expected { get; } = $"{o.Expected:0.000}";
    public string Green { get; } = $"{o.GreenHit:P0}";
    public string Note { get; } = best ? "best" : atPin ? "at the pin" : "";
    public FontWeight Weight { get; } = best ? FontWeight.SemiBold : FontWeight.Normal;
}

public sealed class CalibrationRowViewModel(CalibrationRow r)
{
    public string Lie { get; } = r.Lie.ToString();
    public string Yards { get; } = $"{r.Yards:0}";
    public string Anchor { get; } = $"{r.Anchor:0.00}";
    public string Model { get; } = $"{r.Model:0.00}";
    public string Error { get; } = $"{r.Error:+0.00;-0.00;0.00}";
    public IBrush ErrorInk { get; } = Brush.Parse(Math.Abs(r.Error) > 0.15 ? "#B4471F" : "#18211C");
}
