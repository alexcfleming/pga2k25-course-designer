using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.HoleSim;
using SkiaSharp;

namespace Pga2K25.Studio.ViewModels;

/// <summary>Live checks (MEASURE readings, tee sightline, drives) and the terrain gate that decides whether runs use the ground.</summary>
public partial class HoleSimViewModel
{
    public enum TerrainMode { GeometryOnly, On, Override }

    LiveChecks _live = new();
    string _livePath = "";
    ReplayTerrain? _replay, _smoothed;
    ITerrainModel? _chosenTerrain;
    GateResult? _chosenGate;

    // ───────── live checks ─────────
    public ObservableCollection<SpotRowViewModel> SpotRows { get; } = [];
    public ObservableCollection<TabViewModel> ZeroButtons { get; } = [];
    public ObservableCollection<TabViewModel> SightlineButtons { get; } = [];
    public ObservableCollection<DriveRowViewModel> DriveRows { get; } = [];
    [ObservableProperty] private Bitmap? _spotMap;
    [ObservableProperty] private string _sightlineQuestion = "";
    [ObservableProperty] private string _driveSummary = "";
    [ObservableProperty] private string _liveSummary = "";
    [ObservableProperty] private string _liveFileText = "";
    int _zeroPin;   // 0 = green centre, else pin number

    void LoadLiveChecks()
    {
        var m = _model!;
        _livePath = LiveChecks.PathFor(_outputDir, m.Number);
        _live = LiveChecks.Load(_livePath, m.Number);
        LiveFileText = $"Saved as you type to {Path.Combine("Hole Sim", Path.GetFileName(_livePath))} beside the products.";
        var saved = _live.Readings.FirstOrDefault();
        _zeroPin = saved?.Pin ?? 0;
        ZeroButtons.Clear();
        ZeroButtons.Add(new TabViewModel("Green centre", _zeroPin == 0, () => SetZero(0)));
        foreach (var p in m.Pins) { int n = p.Number; ZeroButtons.Add(new TabViewModel($"Pin {n}", _zeroPin == n, () => SetZero(n))); }

        SightlineQuestion = m.Par == 3 ? $"From {m.Tee.Name}, how much of the green can you see?"
            : $"From {m.Tee.Name}, how much of the fairway at 300–350 can you see?";
        SightlineButtons.Clear();
        foreach (var a in new[] { "All", "Most", "Some", "None" })
        {
            string answer = a;
            SightlineButtons.Add(new TabViewModel(a, _live.Sightline == a && _live.SightlineTee == m.Tee.Name, () => SetSightline(answer)));
        }

        DriveRows.Clear();
        for (int i = 0; i < 5; i++)
        {
            var d = i < _live.Drives.Count ? _live.Drives[i] : null;
            DriveRows.Add(new DriveRowViewModel(i + 1, d, SaveDrives));
        }
        UpdateDriveSummary();
        BuildSpotRows();
    }

    void BuildSpotRows()
    {
        var m = _model!;
        var spots = TerrainGate.SuggestSpots(m, _smoothed);
        SpotRows.Clear();
        foreach (var s in spots)
        {
            var saved = _live.Readings.FirstOrDefault(r => r.Spot == s.Name);
            SpotRows.Add(new SpotRowViewModel(s, saved?.Label ?? "", saved?.HeldOut ?? s.HeldOut, ReadingsChanged));
        }
        RenderSpotMap();
        UpdateLiveSummary();
    }

    void RenderSpotMap()
    {
        if (_model is null) return;
        var labels = SpotRows.Select(r => (r.Spot.Where, r.Spot.Name, r.HasReading ? SKColor.Parse("#2D6A42") : SKColor.Parse("#B09766"))).ToList();
        var png = HoleMapRenderer.RenderPng(_model, new MapView { PixelsPerYard = 1.3, SelectedPin = _zeroPin == 0 ? 1 : _zeroPin, ShowWidths = false, Overlay = new MapOverlay([], [], labels) });
        SpotMap = new Bitmap(new MemoryStream(png));
    }

    FramePoint ZeroPoint => _zeroPin == 0 || _model is null ? _model!.GreenFrame : _model.Pins[_zeroPin - 1].Frame;

    void SetZero(int pin)
    {
        _zeroPin = pin;
        foreach (var b in ZeroButtons) b.IsSelected = pin == 0 ? b.Label == "Green centre" : b.Label == $"Pin {pin}";
        ReadingsChanged();
    }

    void SetSightline(string answer)
    {
        foreach (var b in SightlineButtons) b.IsSelected = b.Label == answer;
        _live.Sightline = answer; _live.SightlineTee = _model?.Tee.Name;
        SaveLive();
    }

    void SaveDrives()
    {
        _live.Drives = DriveRows.Where(d => d.Carry is not null || d.Total is not null).Select(d => new LiveChecks.Drive(d.Carry, d.Total, d.Side, DateTime.Now)).ToList();
        SaveLive();
        UpdateDriveSummary();
    }

    void UpdateDriveSummary()
    {
        var carries = DriveRows.Where(d => d.Carry is not null).Select(d => d.Carry!.Value).ToList();
        var assumed = _sheet.Profile(_player).Carry;
        DriveSummary = carries.Count == 0 ? "Playtest shows carry and total; pick the finish side by eye."
            : $"{carries.Count} drive{(carries.Count == 1 ? "" : "s")}: mean carry {carries.Average():0} yds against the assumed {assumed:0} ({AssumptionRow.Tag(_sheet[_player == "power" ? "player.power.carry" : "player.normal.carry"].Source)})."
              + (carries.Count >= 3 ? $" Spread (SD) {Sd(carries):0} yds." : " Three or more give a spread too.");
    }

    static double Sd(List<double> v) { double m = v.Average(); return Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / Math.Max(1, v.Count - 1)); }

    void ReadingsChanged()
    {
        if (_model is null) return;
        var zero = _model.Frame.ToWorld(ZeroPoint);
        _live.Readings = SpotRows.Where(r => r.Feet is not null).Select(r =>
        {
            var w = _model.Frame.ToWorld(r.Spot.Where);
            return new LiveChecks.SavedReading(r.Spot.Name, w.X, w.Z, zero.X, zero.Z, r.Feet!.Value, r.Label, r.HeldOut,
                _zeroPin == 0 ? "green centre" : $"pin {_zeroPin}", _zeroPin, DateTime.Now);
        }).ToList();
        _live.FileSha256 = _stamp?.Sha256 ?? "";
        SaveLive();
        RenderSpotMap();
        UpdateLiveSummary();
        EvaluateGate();
    }

    void SaveLive() { try { _live.Save(_livePath); } catch (Exception ex) { Show($"Could not save the readings: {ex.Message}", error: true); } }

    void UpdateLiveSummary()
    {
        int n = SpotRows.Count(r => r.Feet is not null), held = SpotRows.Count(r => r.Feet is not null && r.HeldOut);
        LiveSummary = n == 0 ? $"No readings yet. Take at least {TerrainGate.MinReadings}; {SpotRows.Count} spots are suggested."
            : $"{n} reading{(n == 1 ? "" : "s")} ({held} held back to test the fit), zero at {(_zeroPin == 0 ? "the green centre" : $"pin {_zeroPin}")}.";
    }

    // ───────── terrain models and the gate ─────────
    public ObservableCollection<GateCandidateViewModel> GateCandidates { get; } = [];
    public ObservableCollection<GateCheckRowViewModel> GateChecks { get; } = [];
    public ObservableCollection<TabViewModel> ModeButtons { get; } = [];
    [ObservableProperty] private string _gateTitle = "";
    [ObservableProperty] private string _gateDetail = "";
    [ObservableProperty] private IBrush _gateBrush = Brush.Parse("#B4471F");
    [ObservableProperty] private string _terrainStatus = "Terrain: not checked, so runs use flat ground.";
    [ObservableProperty] private TerrainMode _mode = TerrainMode.GeometryOnly;
    [ObservableProperty] private string _modeNote = "";
    string _chosenId = "";
    bool _userChose;   // until you pick a candidate, the gate keeps choosing the best one

    void BuildTerrain()
    {
        var course = _course; var m = _model;
        if (course is null || m is null) return;
        double sigma = 8;
        try { sigma = _sheet["terrain.smoothing"].Number; } catch (Exception) { }
        _replay = _smoothed = null; _chosenTerrain = null; _chosenGate = null; _userChose = false;
        if (Mode != TerrainMode.GeometryOnly) Mode = TerrainMode.GeometryOnly;
        EvaluateGate();
        Task.Run(() =>
        {
            try
            {
                var field = ReplayTerrain.BuildField(course, m);
                var replay = ReplayTerrain.FromField(m, field);
                var smoothed = ReplayTerrain.Smoothed(m, field, sigma);
                Dispatcher.UIThread.Post(() =>
                {
                    if (!ReferenceEquals(m, _model)) return;
                    _replay = replay; _smoothed = smoothed;
                    BuildSpotRows();
                    EvaluateGate();
                });
            }
            catch (Exception ex) { Dispatcher.UIThread.Post(() => Show($"Could not build the terrain: {ex.Message}", error: true)); }
        });
    }

    void EvaluateGate()
    {
        GateCandidates.Clear(); GateChecks.Clear();
        if (_replay is null || _smoothed is null || _model is null)
        {
            GateTitle = "Building the terrain…"; GateDetail = ""; ModeButtons.Clear(); UpdateModeText();
            return;
        }
        var readings = _live.ToReadings(_model);
        var options = new List<(ITerrainModel Model, GateResult Gate)>
        {
            (_replay, TerrainGate.Evaluate(_replay, readings)),
            (_smoothed, TerrainGate.Evaluate(_smoothed, readings)),
        };
        var fitReadings = readings.Where(r => !r.HeldOut).ToList();
        if (fitReadings.Count >= 2)
            foreach (var b in new ITerrainModel[] { _smoothed, _replay })
            {
                var fitted = new CorrectedTerrain(b, fitReadings);
                options.Add((fitted, TerrainGate.Evaluate(fitted, readings, fitted: true)));
            }
        // Default pick: the best band, then the lowest error; a fitted model only counts once it has held-out readings to test it.
        if (!_userChose || !options.Any(o => o.Model.Id == _chosenId))
            _chosenId = options.OrderBy(o => o.Gate.Band).ThenBy(o => double.IsNaN(o.Gate.RmsInches) ? double.MaxValue : o.Gate.RmsInches).First().Model.Id;
        foreach (var (model, gate) in options)
        {
            string id = model.Id;
            GateCandidates.Add(new GateCandidateViewModel(model.Name, gate, id == _chosenId, () => { _chosenId = id; _userChose = true; ClearResults(); EvaluateGate(); }));
        }
        (_chosenTerrain, _chosenGate) = options.First(o => o.Model.Id == _chosenId);
        foreach (var c in _chosenGate.Checks) GateChecks.Add(new GateCheckRowViewModel(c));
        GateTitle = $"{_chosenGate.BandText}: {_chosenTerrain.Name}";
        GateDetail = _chosenGate.Reason + (_chosenGate.HeldOutRmsInches is { } h && !_chosenTerrain.Id.EndsWith("+fit") ? $" Held-back readings alone: RMS {h:0.#} in." : "");
        GateBrush = Brush.Parse(_chosenGate.Band switch { GateBand.Trustworthy => "#2D6A42", GateBand.Indicative => "#B08A2E", _ => "#B4471F" });

        ModeButtons.Clear();
        bool pass = _chosenGate.Band != GateBand.Check;
        if (!pass && Mode == TerrainMode.On) Mode = TerrainMode.GeometryOnly;
        if (pass) ModeButtons.Add(new TabViewModel(_chosenGate.Band == GateBand.Trustworthy ? "Use the terrain" : "Use the terrain, with a warning", Mode == TerrainMode.On, () => SetMode(TerrainMode.On)));
        ModeButtons.Add(new TabViewModel("Geometry only", Mode == TerrainMode.GeometryOnly, () => SetMode(TerrainMode.GeometryOnly)));
        if (!pass) ModeButtons.Add(new TabViewModel("Override: terrain unverified", Mode == TerrainMode.Override, () => SetMode(TerrainMode.Override)));
        UpdateModeText();
    }

    void SetMode(TerrainMode mode)
    {
        if (Mode != mode) ClearResults();
        Mode = mode;
        EvaluateGate();
    }

    void UpdateModeText()
    {
        string band = _chosenGate?.BandText ?? "not checked";
        (TerrainStatus, ModeNote) = Mode switch
        {
            TerrainMode.On => ($"Terrain: {_chosenTerrain!.Name}, {band}" + (_chosenGate!.Band == GateBand.Indicative ? " (results carry a warning)." : "."),
                "Runs roll the ball on this ground and change carry for height."),
            TerrainMode.Override => ($"Terrain: {_chosenTerrain!.Name}, UNVERIFIED (override; the gate says {band}).",
                "Every result is labelled \"terrain unverified\"."),
            _ => ("Terrain: off. Runs use flat ground (geometry only).",
                _chosenGate?.Band == GateBand.Check ? "The gate says Check: take more readings, run geometry only, or override with a visible label." : "Flat ground, as in Stage 2."),
        };
        UpdateContext();
    }

    [RelayCommand] private void GoLiveChecks() => Step = LiveChecksStep;
    [RelayCommand] private void GoGate() => Step = GateStep;

    TerrainUse? CurrentTerrain() => Mode switch
    {
        TerrainMode.On when _chosenTerrain is not null => new TerrainUse(_chosenTerrain, _chosenGate!.Band == GateBand.Trustworthy ? "Trustworthy" : "Indicative: results carry a warning", ElevationRule()),
        TerrainMode.Override when _chosenTerrain is not null => new TerrainUse(_chosenTerrain, "terrain unverified", ElevationRule()),
        _ => null,
    };

    double ElevationRule() { try { return _sheet["ground.elevationCarry"].Number; } catch (Exception) { return 1 / 3.0; } }
}

public sealed partial class SpotRowViewModel : ObservableObject
{
    readonly Action _changed;

    public SpotRowViewModel(MeasureSpot spot, string label, bool heldOut, Action changed)
    {
        Spot = spot; _changed = changed; _label = label; _heldOut = heldOut;
        Parse();
    }

    public MeasureSpot Spot { get; }
    public string Name => Spot.Name;
    public string FromTee => $"{Spot.FromTeeYards} yds";
    /// <summary>Shown only once a reading is in, so the model's number can't steer what gets typed.</summary>
    [ObservableProperty] private string _model = "";
    [ObservableProperty] private string _label;
    [ObservableProperty] private bool _heldOut;
    [ObservableProperty] private string _parsed = "";
    [ObservableProperty] private IBrush _parsedInk = Brush.Parse("#56605A");
    public double? Feet { get; private set; }
    public bool HasReading => Feet is not null;

    partial void OnLabelChanged(string value) { Parse(); _changed(); }
    partial void OnHeldOutChanged(bool value) => _changed();

    void Parse()
    {
        if (string.IsNullOrWhiteSpace(Label)) { Feet = null; Parsed = ""; Model = ""; return; }
        if (HeightLabel.TryParse(Label, out var ft)) { Feet = ft; Parsed = "reads as " + HeightLabel.Describe(ft); ParsedInk = Brush.Parse("#1F4D30"); }
        else { Feet = null; Parsed = "Can't read this. Try +1'4\", -0'7 or -5.3"; ParsedInk = Brush.Parse("#B4471F"); }
        Model = Feet is null || double.IsNaN(Spot.ModelFt) ? "" : $"smoothed model {Spot.ModelFt:+0.0;-0.0} ft";
    }
}

public sealed partial class DriveRowViewModel : ObservableObject
{
    readonly Action _changed;
    public static IReadOnlyList<string> Sides { get; } = ["L", "C", "R"];

    public DriveRowViewModel(int number, LiveChecks.Drive? saved, Action changed)
    {
        Number = number; _changed = changed;
        _carryText = saved?.Carry?.ToString("0") ?? ""; _totalText = saved?.Total?.ToString("0") ?? ""; _side = saved?.Side ?? "C";
    }

    public int Number { get; }
    [ObservableProperty] private string _carryText;
    [ObservableProperty] private string _totalText;
    [ObservableProperty] private string _side;
    public double? Carry => double.TryParse(CarryText, out var v) && v > 0 ? v : null;
    public double? Total => double.TryParse(TotalText, out var v) && v > 0 ? v : null;
    partial void OnCarryTextChanged(string value) => _changed();
    partial void OnTotalTextChanged(string value) => _changed();
    partial void OnSideChanged(string value) => _changed();
}

public sealed partial class GateCandidateViewModel(string name, GateResult gate, bool chosen, Action choose) : ObservableObject
{
    public string Name { get; } = name;
    public string Band { get; } = gate.BandText;
    public string Error { get; } = double.IsNaN(gate.RmsInches) ? "—" : $"RMS {gate.RmsInches:0.#} in · {gate.Percent:0}%";
    public string Reason { get; } = gate.Reason;
    public IBrush BandInk { get; } = Brush.Parse(gate.Band switch { GateBand.Trustworthy => "#2D6A42", GateBand.Indicative => "#8A6A1E", _ => "#B4471F" });
    public bool IsChosen { get; } = chosen;
    [RelayCommand] private void Choose() => choose();
}

public sealed class GateCheckRowViewModel(ReadingCheck c)
{
    public string Spot { get; } = c.Reading.Spot;
    public string Model { get; } = $"{c.ModelFt * 12:+0;-0;0} in";
    public string Reading { get; } = $"{c.Reading.Inches:+0;-0;0} in";
    public string Error { get; } = $"{c.ErrorInches:+0;-0;0} in";
    public string Use { get; } = c.Reading.HeldOut ? "held back" : "fit";
    public IBrush ErrorInk { get; } = Math.Abs(c.ErrorInches) > 12 ? Brush.Parse("#B4471F") : Brush.Parse("#18211C");
}
