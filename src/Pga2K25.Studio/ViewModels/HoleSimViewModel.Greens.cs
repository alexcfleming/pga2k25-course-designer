using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.HoleSim;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// Greens (Stage 4a): readings round the pins, the green gate on the contours, and the Green read step, which shows
/// expected putts from every spot on the green to the chosen pin. Putting maps are built in the background when a pin
/// is opened and kept until the green, the green speed or the putting assumptions change.
/// </summary>
public partial class HoleSimViewModel
{
    public const string GreenStep = "Green";

    GreenSurface? _greenBase;                 // the green from the base terrain, for spots and the gate
    GreenSurface? _greenSurface;              // the green the putting maps roll on (the chosen green model)
    ITerrainModel? _greenTerrain;
    GateResult? _greenGate;
    PuttingModel? _putting;
    readonly Dictionary<int, PuttingMap> _puttingMaps = [];
    string _puttingKey = "";
    CancellationTokenSource? _mapCancel;
    string _greenChosenId = "";
    bool _greenUserChose;
    int _greenPin = 1;

    public bool IsGreen => Step == GreenStep;
    [RelayCommand] private void GoGreen() => Step = GreenStep;

    // ───────── readings round the pins (shown in Live checks) ─────────
    public ObservableCollection<SpotRowViewModel> GreenSpotRows { get; } = [];
    [ObservableProperty] private string _greenSpotSummary = "";

    double StimpNow => _model?.GreenSpeed is { } gs ? AssumptionSheet.Stimp(gs) ?? 10 : 10;
    double GreenFriction => GreenPhysics.RollingFriction(StimpNow);

    void BuildGreenSpotRows()
    {
        GreenSpotRows.Clear();
        if (_model is null || _greenBase is null) { GreenSpotSummary = "Building the green…"; return; }
        foreach (var s in GreenGate.SuggestSpots(_model, _greenBase))
        {
            var saved = _live.Readings.FirstOrDefault(r => r.Spot == s.Name);
            var row = new SpotRowViewModel(s, _model, saved?.Label ?? "", saved?.HeldOut ?? s.HeldOut, ReadingsChanged);
            row.SetWhere(GreenSpotText(s));
            GreenSpotRows.Add(row);
        }
        UpdateZeroModel();
        UpdateGreenSpotSummary();
    }

    string GreenSpotText(MeasureSpot s)
    {
        int pin = int.Parse(s.Name[1..^1]);
        string where = s.Name[^1] switch { 'a' => "above the pin (uphill)", 'b' => "below the pin (downhill)", _ => "across the slope" };
        double yds = s.Where.DistanceTo(_model!.Pins[pin - 1].Frame);
        return $"Pin {pin}: {yds:0.#} yds {where} · {s.FromTeeYards} to {_model.Tee.Name} · {s.ToGreenYards(_model)} to green";
    }

    void UpdateGreenSpotSummary()
    {
        int n = GreenSpotRows.Count(r => r.Feet is not null);
        bool g = SpotRows.Any(r => r.Spot.Name == "G" && r.Feet is not null);
        GreenSpotSummary = n == 0
            ? $"No green readings yet. Two or three per pin ({GreenSpotRows.Count} spots); the third (across) is held back to test the fit."
            : $"{n} green reading{(n == 1 ? "" : "s")}." + (g ? " Compared against G, so the tee's height doesn't count." : " Read G too, so the contours are judged against the green itself.");
    }

    // ───────── the green model and its gate ─────────
    public ObservableCollection<GateCandidateViewModel> GreenCandidates { get; } = [];
    public ObservableCollection<GateCheckRowViewModel> GreenChecks { get; } = [];
    [ObservableProperty] private string _greenGateTitle = "";
    [ObservableProperty] private string _greenGateDetail = "";
    [ObservableProperty] private IBrush _greenGateBrush = Brush.Parse("#B4471F");

    /// <summary>After the terrain is built: the green surface, its spots and the green gate.</summary>
    void BuildGreen()
    {
        ResetGreen();
        var m = _model; var baseTerrain = _smoothed;
        if (m is null || baseTerrain is null) return;
        Task.Run(() =>
        {
            try
            {
                var green = GreenSurface.Build(m, baseTerrain);
                Dispatcher.UIThread.Post(() =>
                {
                    if (!ReferenceEquals(m, _model)) return;
                    _greenBase = green;
                    BuildGreenSpotRows();
                    EvaluateGreenGate();
                });
            }
            catch (Exception ex) { Dispatcher.UIThread.Post(() => { GreenGateTitle = "No green to read"; GreenGateDetail = ex.Message; }); }
        });
    }

    /// <summary>Drops everything about the last green. Called as soon as the terrain starts rebuilding (another hole,
    /// tee or save), so no step shows the old green while the new one builds.</summary>
    void ResetGreen()
    {
        _greenBase = null; _greenSurface = null; _greenTerrain = null; _greenGate = null; _greenUserChose = false;
        ResetApproach();
        ClearPuttingMaps();
        // Nothing from the last hole may linger while this one builds: its spots, gate and pins belong to another green.
        GreenSpotRows.Clear(); GreenCandidates.Clear(); GreenChecks.Clear(); GreenPinButtons.Clear();
        GreenGateTitle = "Building the green…"; GreenGateDetail = ""; GreenSpotSummary = "Building the green…";
        GreenTitle = "Building the green…"; GreenSummary = ""; GreenPinText = ""; GreenStatus = "";
        if (_model is not null && _greenPin > _model.Pins.Count) _greenPin = 1;
    }

    void EvaluateGreenGate()
    {
        GreenCandidates.Clear(); GreenChecks.Clear();
        if (_model is null || _greenBase is null || _smoothed is null) { GreenGateTitle = "Building the green…"; GreenGateDetail = ""; return; }
        var all = _live.ToReadings(_model);
        var options = GreenGate.Evaluate(_smoothed, _greenBase, all);
        if (!_greenUserChose || !options.Any(o => o.Model.Id == _greenChosenId))
            _greenChosenId = options.OrderBy(o => o.Gate.Band).ThenBy(o => o.Model.Id == "green-replay" ? 0 : 1)
                .ThenBy(o => double.IsNaN(o.Gate.RmsInches) ? double.MaxValue : o.Gate.RmsInches).First().Model.Id;
        foreach (var o in options)
        {
            string id = o.Model.Id;
            GreenCandidates.Add(new GateCandidateViewModel(o.Model.Name, o.Gate, id == _greenChosenId, () => { _greenChosenId = id; _greenUserChose = true; EvaluateGreenGate(); }));
        }
        var chosen = options.First(o => o.Model.Id == _greenChosenId);
        foreach (var c in chosen.Gate.Checks) GreenChecks.Add(new GateCheckRowViewModel(c));
        _greenGate = chosen.Gate;
        GreenGateTitle = $"{chosen.Gate.BandText}: {chosen.Model.Name}";
        GreenGateDetail = chosen.Gate.Reason + (chosen.Gate.Band == GateBand.Check ? " Putting maps still draw, labelled \"contours unverified\"." : "");
        GreenGateBrush = Brush.Parse(chosen.Gate.Band switch { GateBand.Trustworthy => "#2D6A42", GateBand.Indicative => "#B08A2E", _ => "#B4471F" });
        if (_greenTerrain?.Id != chosen.Model.Id || (chosen.Model is ScaledGreenTerrain s && _greenTerrain is ScaledGreenTerrain old && Math.Abs(s.K - old.K) > 1e-6))
        {
            _greenTerrain = chosen.Model;
            var m = _model; var t = chosen.Model;
            Task.Run(() =>
            {
                var surface = GreenSurface.Build(m, t);
                Dispatcher.UIThread.Post(() => { if (!ReferenceEquals(m, _model)) return; _greenSurface = surface; ClearPuttingMaps(); ResetApproach(); RefreshGreenRead(); RefreshShotSteps(); });
            });
        }
        UpdateGreenStatus();
    }

    void ClearPuttingMaps()
    {
        _mapCancel?.Cancel();
        _puttingMaps.Clear();
        GreenMap = null; GreenPinRows.Clear();
    }

    // ───────── Green read step ─────────
    public ObservableCollection<TabViewModel> GreenPinButtons { get; } = [];
    public ObservableCollection<GreenPinRowViewModel> GreenPinRows { get; } = [];
    [ObservableProperty] private Bitmap? _greenMap;
    [ObservableProperty] private bool _greenBusy;
    [ObservableProperty] private double _greenProgress;
    [ObservableProperty] private string _greenTitle = "";
    [ObservableProperty] private string _greenSummary = "";
    [ObservableProperty] private string _greenPinText = "";
    [ObservableProperty] private string _greenStatus = "";
    [ObservableProperty] private string _putterText = "";

    void UpdateGreenStatus() =>
        GreenStatus = _greenGate is null ? "Green contours: not checked yet."
            : _greenGate.Band == GateBand.Check ? $"Green contours: unverified ({_greenGate.Reason})"
            : $"Green contours: {_greenGate.BandText}.";

    void RefreshGreenRead()
    {
        if (_model is null) return;
        GreenPinButtons.Clear();
        foreach (var p in _model.Pins) { int n = p.Number; GreenPinButtons.Add(new TabViewModel($"Pin {n}  {p.Where}", n == _greenPin, () => SelectGreenPin(n))); }
        if (_greenPin > _model.Pins.Count) _greenPin = 1;
        if (_greenSurface is null) { GreenTitle = "Building the green…"; return; }
        string key = FormattableString.Invariant($"{_greenSurface.TerrainId}|{StimpNow:F2}|{_sheet.Putting()}|{_sheet.BaselineId}");
        if (key != _puttingKey) { ClearPuttingMaps(); _putting = null; _puttingKey = key; }
        var (p50, p90, steep) = _greenSurface.SlopeStats(GreenFriction, _sheet.Putting().StaticFactor);
        GreenTitle = $"Hole {_model.Number} green · {_greenSurface.SquareYards:0} sq yd · Stimp {StimpNow:0.0}";
        GreenSummary = $"Slope {p50:0.0}% typical, {p90:0.0}% at the steepest tenth. {steep:P0} of the green is steeper than a stopped ball can stay on (about {GreenFriction * _sheet.Putting().StaticFactor * 100:0.0}% at this speed)"
            + (steep > 0.1 ? ": shots and putts that stop there roll on, so check it in game." : ".");
        if (_puttingMaps.TryGetValue(_greenPin, out var map)) ShowGreenMap(map); else BuildPuttingMap(_greenPin);
    }

    void SelectGreenPin(int pin)
    {
        _greenPin = pin;
        foreach (var b in GreenPinButtons) b.IsSelected = b.Label.StartsWith($"Pin {pin} ");
        RefreshGreenRead();
    }

    void BuildPuttingMap(int pin)
    {
        if (_model is null || _greenSurface is null) return;
        _mapCancel?.Cancel();
        var cancel = new CancellationTokenSource(); _mapCancel = cancel;
        var surface = _greenSurface; var m = _model; double stimp = StimpNow; var settings = _sheet.Putting(); string baselineId = _sheet.BaselineId;
        var pinAt = m.Pins[pin - 1].Frame; string key = _puttingKey;
        GreenBusy = true; GreenProgress = 0; GreenMap = null;
        GreenPinText = $"Rolling putts from every spot on the green to pin {pin}…";
        var progress = new Progress<double>(v => GreenProgress = v * 100);
        Task.Run(() =>
        {
            try
            {
                var putting = _putting ?? PuttingModel.Create(stimp, TableBaseline.LoadShipped(baselineId), settings);
                var map = PuttingMap.Build(surface, putting, pin, pinAt, progress, cancel.Token);
                Dispatcher.UIThread.Post(() =>
                {
                    if (cancel.IsCancellationRequested || key != _puttingKey) return;
                    _putting = putting; _puttingMaps[pin] = map; GreenBusy = false;
                    if (pin == _greenPin) ShowGreenMap(map);
                    UpdateGreenPinRows();
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Dispatcher.UIThread.Post(() => { GreenBusy = false; GreenPinText = $"Could not build the putting map: {ex.Message}"; }); }
        });
    }

    void ShowGreenMap(PuttingMap map)
    {
        if (_model is null || _greenSurface is null || _putting is null) return;
        var png = HoleMapRenderer.RenderGreenPng(_model, _greenSurface, map, map.Pin, _putting.Friction * _putting.Settings.StaticFactor * 100);
        GreenMap = new Bitmap(new MemoryStream(png));
        var pin = _model.Pins[map.Pin - 1];
        var (above, below) = map.AboveBelow(_greenSurface);
        double cup = _greenSurface.Slope(pin.Frame).Length * 100, hold = _putting.Friction * _putting.Settings.StaticFactor * 100;
        string side = double.IsNaN(above) || double.IsNaN(below) ? "" : Math.Abs(above - below) < 0.05 ? " Above and below the hole play about the same."
            : above > below ? $" Leave it below the hole: from 15–30 ft, putts from above average {above:0.00}, from below {below:0.00}."
            : $" Unusually, putts from above the hole play easier here ({above:0.00} against {below:0.00} from below, 15–30 ft).";
        GreenPinText = $"Pin {map.Pin} ({pin.Where}): {map.Average:0.00} putts on average from the whole green; {map.HardShare():P0} of the green needs 2.3 or more. "
            + $"Slope at the cup {cup:0.0}%" + (cup > hold ? " — steeper than a ball can stop on, so this pin can't really be played." : cup > hold * 0.7 ? ", close to the most a ball can stop on." : ".") + side;
        PutterText = $"Putter fitted to {_sheet.BaselineId}: aim error {_putting.Skill.AimSdDegrees:0.0}°, pace error {_putting.Skill.PaceSd:P0}; on a flat green "
            + string.Join(", ", _putting.Skill.Fit.Select(f => $"{f.Feet:0} ft {f.Model:0.00} (anchor {f.Baseline:0.00})")) + ".";
        UpdateGreenPinRows();
    }

    void UpdateGreenPinRows()
    {
        GreenPinRows.Clear();
        if (_model is null || _greenSurface is null || _putting is null) return;
        double hold = _putting.Friction * _putting.Settings.StaticFactor * 100;
        foreach (var p in _model.Pins)
        {
            if (!_puttingMaps.TryGetValue(p.Number, out var map)) { GreenPinRows.Add(new GreenPinRowViewModel(p.Number, p.Where, "—", "—", "—", $"{_greenSurface.Slope(p.Frame).Length * 100:0.0}%", false)); continue; }
            var (above, below) = map.AboveBelow(_greenSurface);
            double cup = _greenSurface.Slope(p.Frame).Length * 100;
            GreenPinRows.Add(new GreenPinRowViewModel(p.Number, p.Where, $"{map.Average:0.00}", $"{map.HardShare():P0}",
                double.IsNaN(above) || double.IsNaN(below) ? "—" : $"{above:0.00} / {below:0.00}", $"{cup:0.0}%", cup > hold));
        }
    }

    [RelayCommand] private void BuildAllPins()
    {
        if (_model is null || _greenSurface is null) return;
        var surface = _greenSurface; var m = _model; double stimp = StimpNow; var settings = _sheet.Putting(); string baselineId = _sheet.BaselineId; string key = _puttingKey;
        var missing = m.Pins.Where(p => !_puttingMaps.ContainsKey(p.Number)).ToList();
        if (missing.Count == 0) return;
        GreenBusy = true;
        Task.Run(() =>
        {
            var putting = _putting ?? PuttingModel.Create(stimp, TableBaseline.LoadShipped(baselineId), settings);
            var built = missing.Select(p => PuttingMap.Build(surface, putting, p.Number, p.Frame)).ToList();
            Dispatcher.UIThread.Post(() =>
            {
                if (key != _puttingKey) return;
                _putting = putting; foreach (var map in built) _puttingMaps[map.Pin] = map;
                GreenBusy = false; UpdateGreenPinRows();
                if (_puttingMaps.TryGetValue(_greenPin, out var sel)) ShowGreenMap(sel);
            });
        });
    }
}

public sealed class GreenPinRowViewModel(int pin, string where, string average, string hard, string aboveBelow, string cupSlope, bool tooSteep)
{
    public string Pin { get; } = pin.ToString();
    public string Where { get; } = where;
    public string Average { get; } = average;
    public string Hard { get; } = hard;
    public string AboveBelow { get; } = aboveBelow;
    public string CupSlope { get; } = cupSlope;
    public IBrush CupInk { get; } = Brush.Parse(tooSteep ? "#B4471F" : "#18211C");
}
