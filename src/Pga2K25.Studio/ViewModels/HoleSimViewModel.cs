using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.CourseFormat;
using Pga2K25.HoleSim;
using Pga2K25.Maps;
using Pga2K25.YardageBook;
using SkiaSharp;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// Hole design simulator (Stage 2, read-only slice): pick one hole, tee and pin; check the read-back against the game;
/// confirm every assumption; run geometry-only tee shots. While the section is open it watches the course file and,
/// when the designer saves again, shows what changed on this hole and reloads only when asked. Nothing runs by itself.
/// </summary>
public partial class HoleSimViewModel : ObservableObject
{
    public const string PickStep = "1  Pick", ReadBackStep = "2  Read back", AssumptionsStep = "3  Assumptions", LiveChecksStep = "4  Live checks",
        GateStep = "5  Terrain gate", RunStep = "6  Run";

    static readonly IBrush Ok = Brush.Parse("#1F4D30"), Warn = Brush.Parse("#B4471F"), MutedBrush = Brush.Parse("#56605A");

    readonly string _coursePath, _outputDir, _priorsPath;
    readonly DispatcherTimer _timer;
    CourseModel? _course;
    JsonObject? _desc;
    CourseLayers? _layers;
    GreensBook.WaterMap? _water;
    FileStamp? _stamp;
    HoleModel? _model;
    CourseSaveWatcher? _watcher;
    bool _checking, _active;
    (CourseModel Course, JsonObject Desc, CourseLayers Layers, FileStamp Stamp, HoleModel Model, HoleChangeSet Changes)? _pendingSave;
    GreensBook.WaterMap? _pendingWater;
    AssumptionSheet _sheet;
    int _hole = 1, _pin = 1;
    int? _tee;
    string _player = "power";
    readonly Dictionary<int, Ground?> _treatments = [];

    public HoleSimViewModel(string coursePath, string courseName, string outputDir)
    {
        _coursePath = coursePath; _outputDir = outputDir;
        CourseName = courseName;
        _priorsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CourseDesignerStudio", "holesim", "assumptions-confirmed.json");
        _sheet = AssumptionSheet.Load(_priorsPath);
        foreach (var s in new[] { PickStep, ReadBackStep, AssumptionsStep, LiveChecksStep, GateStep, RunStep, GreenStep, ApproachStep }) StepButtons.Add(new TabViewModel(s, s == Step, () => Step = s));
        PlayerButtons.Add(new TabViewModel("Power Drive", true, () => SelectPlayer("power")));
        PlayerButtons.Add(new TabViewModel("Normal", false, () => SelectPlayer("normal")));
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => CheckForSave();
        LoadCourse();
    }

    public string CourseName { get; }

    // ───────── steps ─────────
    public ObservableCollection<TabViewModel> StepButtons { get; } = [];
    [ObservableProperty] private string _step = PickStep;
    public bool IsPick => Step == PickStep;
    public bool IsReadBack => Step == ReadBackStep;
    public bool IsAssumptions => Step == AssumptionsStep;
    public bool IsLiveChecks => Step == LiveChecksStep;
    public bool IsGate => Step == GateStep;
    public bool IsRun => Step == RunStep;
    partial void OnStepChanged(string value)
    {
        foreach (var b in StepButtons) b.IsSelected = b.Label == value;
        OnPropertyChanged(nameof(IsPick)); OnPropertyChanged(nameof(IsReadBack)); OnPropertyChanged(nameof(IsAssumptions));
        OnPropertyChanged(nameof(IsLiveChecks)); OnPropertyChanged(nameof(IsGate)); OnPropertyChanged(nameof(IsRun));
        OnPropertyChanged(nameof(IsGreen)); OnPropertyChanged(nameof(IsApproach));
        if (value == GreenStep) RefreshGreenRead();
        if (value == ApproachStep) RefreshApproach();
    }
    [RelayCommand] private void GoReadBack() => Step = ReadBackStep;
    [RelayCommand] private void GoAssumptions() => Step = AssumptionsStep;
    [RelayCommand] private void GoRun() => Step = RunStep;

    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private IBrush _messageBrush = Ok;
    [ObservableProperty] private string _contextText = "";

    // ───────── file and watcher ─────────
    [ObservableProperty] private string _fileText = "";
    [ObservableProperty] private string _watchText = "";
    [ObservableProperty] private bool _hasNewSave;
    [ObservableProperty] private string _newSaveTitle = "";
    public ObservableCollection<string> NewSaveLines { get; } = [];
    [ObservableProperty] private string _newSaveNote = "";
    [ObservableProperty] private string _reloadedText = "";

    /// <summary>The workspace calls this when the section opens or closes: the watcher only runs while it's open.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        if (active) { _timer.Start(); CheckForSave(); } else _timer.Stop();
    }

    void LoadCourse()
    {
        Busy = true;
        Task.Run(() =>
        {
            try
            {
                var stamp = FileStamp.Read(_coursePath);
                var file = CourseFile.Load(_coursePath);
                var course = file.ReadModel();
                var layers = CourseLayers.From(course);
                var water = HoleModelBuilder.TraceWater(course);   // once per course: about a minute on big courses with water planes
                Dispatcher.UIThread.Post(() =>
                {
                    _course = course; _desc = file.Description; _layers = layers; _stamp = stamp; _water = water;
                    _watcher = new CourseSaveWatcher(stamp);
                    UpdateFileText();
                    BuildHoleButtons();
                    SelectHole(course.Holes.FirstOrDefault()?.Number ?? 1);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => { Busy = false; Show($"Could not read the course: {ex.Message}", error: true); });
            }
        });
    }

    void UpdateFileText()
    {
        if (_stamp is null) return;
        FileText = $"{Path.GetFileName(_coursePath)} · saved {_stamp.ModifiedUtc.ToLocalTime():d MMM yyyy, HH:mm} · read only";
        WatchText = _active || _watcher is not null ? "Watching for new saves while this section is open. Save in the designer before testing a change." : "";
    }

    void CheckForSave()
    {
        if (_watcher is null || _checking || _pendingSave is not null || !_active) return;
        _checking = true;
        var watcher = _watcher; int hole = _hole; int? tee = _tee; var treatments = new Dictionary<int, Ground?>(_treatments); var current = _model;
        Task.Run(() =>
        {
            try
            {
                var check = watcher.Check();
                if (check.State == SaveCheckState.Missing) { Dispatcher.UIThread.Post(() => WatchText = "The course file can't be found. Was it moved or renamed?"); return; }
                if (check.State != SaveCheckState.NewSave || current is null) return;
                var file = CourseFile.Load(_coursePath);
                var course = file.ReadModel();
                var layers = CourseLayers.From(course);
                var water = HoleModelBuilder.TraceWater(course);
                var model = HoleModelBuilder.Build(course, file.Description, hole, Options(tee, treatments) with { Water = water }, check.Stamp, layers);
                var changes = HoleChanges.Compare(current, model);
                Dispatcher.UIThread.Post(() =>
                {
                    _pendingSave = (course, file.Description, layers, check.Stamp!, model, changes);
                    _pendingWater = water;
                    NewSaveTitle = $"New save of {CourseName} at {check.Stamp!.ModifiedUtc.ToLocalTime():HH:mm}." + (changes.Any ? $" Hole {hole} changed:" : "");
                    NewSaveLines.Clear(); foreach (var l in changes.Lines) NewSaveLines.Add(l);
                    NewSaveNote = !changes.Any ? "Reloading changes nothing here."
                        : "Reloading keeps your confirmed assumptions. " + (changes.InvalidatesReadBack ? "The read-back needs checking again. " : "")
                          + (HasResults ? "The current results are kept as \"previous save\" for comparison. " : "") + "Nothing runs until you say so.";
                    HasNewSave = true;
                    WatchText = $"A newer save ({check.Stamp.ModifiedUtc.ToLocalTime():HH:mm}) is waiting.";
                });
            }
            catch (Exception)
            {
                // A half-written save: try again on the next tick.
            }
            finally { Dispatcher.UIThread.Post(() => _checking = false); }
        });
    }

    [RelayCommand] private void ReloadSave()
    {
        if (_pendingSave is not { } p) return;
        string? previous = HasResults ? $"Previous save ({_stamp!.ModifiedUtc.ToLocalTime():HH:mm}): {BestText}" : null;
        _course = p.Course; _desc = p.Desc; _layers = p.Layers; _stamp = p.Stamp; _water = _pendingWater;
        _watcher!.Accept(p.Stamp);
        _pendingSave = null; HasNewSave = false;
        _model = p.Model;
        _sheet.SetCourseSettings(p.Model);
        if (p.Changes.InvalidatesReadBack) ReadBackAccepted = false;
        if (p.Changes.InvalidatesResults) ClearResults();
        PreviousResultText = previous ?? "";
        ReloadedText = $"Reloaded the {p.Stamp.ModifiedUtc.ToLocalTime():HH:mm} save. " + (p.Changes.Any ? string.Join("; ", p.Changes.Lines) + "." : p.Changes.Lines[0]);
        UpdateFileText();
        BuildHoleButtons();
        ShowModel();
        if (p.Changes.Terrain || p.Changes.Surfaces || p.Changes.TeesOrPins) BuildTerrain();   // the gate re-checks the readings on the new ground
    }

    [RelayCommand] private void NotNow()
    {
        _watcher?.Dismiss();
        if (_pendingSave is { } p) WatchText = $"The {p.Stamp.ModifiedUtc.ToLocalTime():HH:mm} save isn't loaded. Results are from the {_stamp!.ModifiedUtc.ToLocalTime():HH:mm} save.";
        _pendingSave = null; HasNewSave = false;
    }

    // ───────── pick ─────────
    public ObservableCollection<TabViewModel> HoleButtons { get; } = [];
    public ObservableCollection<TabViewModel> TeeButtons { get; } = [];
    public ObservableCollection<TabViewModel> PinButtons { get; } = [];
    public ObservableCollection<TabViewModel> PlayerButtons { get; } = [];

    void BuildHoleButtons()
    {
        HoleButtons.Clear();
        foreach (var h in _course!.Holes) { int n = h.Number; HoleButtons.Add(new TabViewModel(n.ToString(), n == _hole, () => SelectHole(n))); }
    }

    HoleModelOptions Options(int? tee, IReadOnlyDictionary<int, Ground?> treatments) => new()
    {
        TeeIndex = tee, Treatments = treatments, Water = _water,
        Yardage = new YardageOptions { Holes = CourseHolesFile.Load(_outputDir) },
    };

    void SelectHole(int hole)
    {
        if (hole != _hole) { _tee = null; _pin = 1; ReadBackAccepted = false; }
        _hole = hole;
        foreach (var b in HoleButtons) b.IsSelected = b.Label == hole.ToString();
        Rebuild(resetReadBack: true);
    }

    void SelectTee(int index) { _tee = index; Rebuild(resetReadBack: true); }

    void SelectPin(int pin)
    {
        _pin = pin;
        foreach (var b in PinButtons) b.IsSelected = b.Label.StartsWith($"Pin {pin}");
        ClearResults();
        UpdateContext();
        RenderReadBackMap();
    }

    void SelectPlayer(string id)
    {
        _player = id;
        PlayerButtons[0].IsSelected = id == "power"; PlayerButtons[1].IsSelected = id != "power";
        ClearResults();
        UpdateContext();
    }

    /// <summary>Re-reads the hole from the loaded course (another hole, tee or surface treatment). Results depending on it are cleared.</summary>
    void Rebuild(bool resetReadBack)
    {
        if (_course is null) return;
        Busy = true;
        var course = _course; var desc = _desc; var layers = _layers; var stamp = _stamp; int hole = _hole; int? tee = _tee;
        var treatments = new Dictionary<int, Ground?>(_treatments);
        Task.Run(() =>
        {
            try
            {
                var model = HoleModelBuilder.Build(course, desc, hole, Options(tee, treatments), stamp, layers);
                Dispatcher.UIThread.Post(() =>
                {
                    _model = model;
                    if (resetReadBack) ReadBackAccepted = false;
                    ClearResults();
                    _sheet.SetCourseSettings(model);
                    ShowModel();
                    BuildTerrain();      // clears the last hole's terrain first, so no spot shows its heights
                    LoadLiveChecks();
                    Busy = false;
                });
            }
            catch (Exception ex) { Dispatcher.UIThread.Post(() => { Busy = false; Show($"Could not read hole {hole}: {ex.Message}", error: true); }); }
        });
    }

    void ShowModel()
    {
        var m = _model!;
        TeeButtons.Clear();
        foreach (var t in m.Tees) { int i = t.Index; TeeButtons.Add(new TabViewModel($"{t.Name}  {t.Yards}", i == m.TeeIndex, () => SelectTee(i))); }
        if (_pin > m.Pins.Count) _pin = 1;
        PinButtons.Clear();
        foreach (var p in m.Pins) { int n = p.Number; PinButtons.Add(new TabViewModel($"Pin {n}  {p.Where}", n == _pin, () => SelectPin(n))); }
        PickSummary = $"Hole {m.Number} · par {m.Par} · {m.Tees.Count} tee{(m.Tees.Count == 1 ? "" : "s")} · {m.Pins.Count} pin{(m.Pins.Count == 1 ? "" : "s")}";

        ReadBackRows.Clear();
        foreach (var r in ReadBack.Rows(m)) ReadBackRows.Add(new SourcedRowViewModel(r.Item, r.Value, r.Source, r.Note));
        UnknownRows.Clear();
        foreach (var u in m.UnknownSurfaces)
            UnknownRows.Add(new UnknownSurfaceViewModel(u, m.Treatments.TryGetValue(u.SurfaceId, out var g) ? g : Ground.Native, choice => SetTreatment(u.SurfaceId, choice)));
        HasUnknown = UnknownRows.Count > 0;
        RenderReadBackMap();
        RefreshAssumptions();
        UpdateContext();
    }

    void SetTreatment(int id, Ground? g)
    {
        _treatments[id] = g;
        Rebuild(resetReadBack: true);
    }

    void UpdateContext()
    {
        if (_model is null) return;
        ContextText = $"Hole {_model.Number} · {_model.TeeLabel} · pin {_pin} · {(_player == "power" ? "Power Drive" : "Normal")} · no wind · seed {RunSettingsNow.Seed} · "
            + (Mode switch { TerrainMode.On => "terrain on", TerrainMode.Override => "terrain UNVERIFIED", _ => "flat ground" });
        UpdateRunState();
    }

    [ObservableProperty] private string _pickSummary = "";

    // ───────── read back ─────────
    public ObservableCollection<SourcedRowViewModel> ReadBackRows { get; } = [];
    public ObservableCollection<UnknownSurfaceViewModel> UnknownRows { get; } = [];
    [ObservableProperty] private bool _hasUnknown;
    [ObservableProperty] private Bitmap? _holeMap;
    [ObservableProperty] private bool _readBackAccepted;
    [ObservableProperty] private bool _readBackDiffers;
    partial void OnReadBackAcceptedChanged(bool value) { UpdateRunState(); OnPropertyChanged(nameof(ReadBackStatus)); }
    public string ReadBackStatus => ReadBackAccepted ? "Matches the game (confirmed this session)." : ReadBackDiffers
        ? "Marked as different. Fix the hole in the designer and save (it reloads here), or accept it as is." : "Not confirmed yet.";

    [RelayCommand] private void AcceptReadBack() { ReadBackAccepted = true; ReadBackDiffers = false; Step = AssumptionsStep; }
    [RelayCommand] private void MarkDiffers() { ReadBackAccepted = false; ReadBackDiffers = true; OnPropertyChanged(nameof(ReadBackStatus)); }

    void RenderReadBackMap()
    {
        if (_model is null) return;
        var png = HoleMapRenderer.RenderPng(_model, new MapView { PixelsPerYard = 1.6, SelectedPin = _pin });
        HoleMap = new Bitmap(new MemoryStream(png));
    }

    // ───────── assumptions ─────────
    public ObservableCollection<TabViewModel> GroupButtons { get; } = [];
    public ObservableCollection<AssumptionRowViewModel> AssumptionRows { get; } = [];
    [ObservableProperty] private string _group = "Player";
    [ObservableProperty] private string _assumptionProgress = "";
    partial void OnGroupChanged(string value) => RefreshAssumptions();

    void RefreshAssumptions()
    {
        GroupButtons.Clear();
        foreach (var g in _sheet.Groups)
        {
            var rows = _sheet.Rows.Where(r => r.Group == g).ToList();
            string label = $"{g}  {rows.Count(r => r.Confirmed)}/{rows.Count}";
            string group = g;
            GroupButtons.Add(new TabViewModel(label, g == Group, () => Group = group));
        }
        AssumptionRows.Clear();
        foreach (var r in _sheet.Rows.Where(r => r.Group == Group)) AssumptionRows.Add(new AssumptionRowViewModel(r, RowChanged));
        int ph = _sheet.Rows.Count(r => r.IsPlaceholder && r.Confirmed);
        AssumptionProgress = $"{_sheet.ConfirmedCount} of {_sheet.Rows.Count} confirmed" + (ph > 0 ? $" · {ph} placeholders accepted as they are" : "")
            + (_sheet.AllConfirmed ? "." : ". The run stays locked until every row is confirmed.");
        UpdateRunState();
    }

    void RowChanged() => RefreshAssumptions();

    [RelayCommand] private void ConfirmGroup()
    {
        _sheet.ConfirmGroup(Group, DateTime.Now);
        var groups = _sheet.Groups.ToList();
        int i = groups.IndexOf(Group);
        if (i >= 0 && i < groups.Count - 1) Group = groups[i + 1]; else RefreshAssumptions();
    }

    // ───────── run ─────────
    [ObservableProperty] private bool _canRun;
    [ObservableProperty] private string _runBlockers = "";
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private Bitmap? _patternMap;
    [ObservableProperty] private string _resultTitle = "";
    [ObservableProperty] private string _resultBadge = "";
    [ObservableProperty] private string _bestText = "";
    [ObservableProperty] private string _dependsText = "";
    [ObservableProperty] private string _previousResultText = "";
    public ObservableCollection<AimRowViewModel> AimRows { get; } = [];
    public ObservableCollection<string> RunNotes { get; } = [];
    public ObservableCollection<LegendItemViewModel> PatternLegend { get; } = [];

    RunSettings RunSettingsNow => new();

    void UpdateRunState()
    {
        var blockers = new List<string>();
        if (_model is null) blockers.Add("the hole is still loading");
        if (!ReadBackAccepted) blockers.Add("confirm the read-back (step 2)");
        if (!_sheet.AllConfirmed) blockers.Add($"confirm the assumptions (step 3, {_sheet.Rows.Count - _sheet.ConfirmedCount} left)");
        if (HasNewSave) blockers.Add("reload or set aside the new save");
        if (_model?.Par == 3) blockers.Add("this is a par 3: the driver sweep doesn't apply, and par 3 tee shots arrive with the approach engine (Stage 4c). The Green read works now");
        CanRun = blockers.Count == 0 && !Busy;
        RunBlockers = blockers.Count == 0 ? "" : "Before running: " + string.Join("; ", blockers) + ".";
    }

    partial void OnBusyChanged(bool value) => UpdateRunState();
    partial void OnHasNewSaveChanged(bool value) => UpdateRunState();

    void ClearResults()
    {
        _lastRun = null;
        HasResults = false; PatternMap = null; AimRows.Clear(); RunNotes.Clear(); PatternLegend.Clear(); BestText = ""; ResultTitle = ""; DependsText = "";
    }

    [RelayCommand] private void Run()
    {
        if (!CanRun || _model is null) return;
        _sheet.SavePriors(_priorsPath);
        Busy = true;
        Show("Running…");
        var m = _model; var sheet = _sheet; int pin = _pin; string player = _player; var settings = RunSettingsNow; var terrain = CurrentTerrain(); double greenFriction = GreenFriction;
        Task.Run(() =>
        {
            try
            {
                var baseline = TableBaseline.LoadShipped(sheet.BaselineId);
                var profile = sheet.Profile(player);
                var run = GeometryRun.Run(m, profile, pin, sheet.GroundRules(greenFriction), sheet.Lies(), baseline, settings, terrain: terrain);
                var best = run.Best;
                var colours = new[] { SKColor.Parse("#E06A3A"), SKColor.Parse("#1F1D1A"), SKColor.Parse("#3A6EA5") };
                var picks = new[] { best.Aim - 20, best.Aim, best.Aim + 20 }
                    .Select((x, j) => (Aim: run.Aims.FirstOrDefault(a => Math.Abs(a.Aim - x) < 1e-6), Colour: colours[j])).Where(t => t.Aim is not null).ToList();
                var dir = m.AimFrame * (1 / Math.Max(m.AimFrame.Length, 1e-6)); var right = new FramePoint(dir.Along, -dir.Lat);
                var overlay = new MapOverlay(
                    picks.SelectMany(t => t.Aim!.Samples.Select(s => (s.Finish, t.Colour))).ToList(),
                    picks.Select(t => (dir * profile.Carry + right * t.Aim!.Aim, t.Colour)).ToList());
                double along = (dir * profile.Carry).Along;
                var png = HoleMapRenderer.RenderPng(m, new MapView
                {
                    PixelsPerYard = 2.4, SelectedPin = pin, Overlay = overlay,
                    AlongFrom = Math.Max(-10, along - 110), AlongTo = Math.Min(m.GreenFrame.Along + 35, along + 90),
                });
                Dispatcher.UIThread.Post(() =>
                {
                    Busy = false; Show("");
                    ShowResults(run, png, picks.Select(t => (t.Aim!.Aim, t.Colour)).ToList());
                });
            }
            catch (Exception ex) { Dispatcher.UIThread.Post(() => { Busy = false; Show($"The run failed: {ex.Message}", error: true); }); }
        });
    }

    void ShowResults(GeometryRunResult run, byte[] png, IReadOnlyList<(double Aim, SKColor Colour)> shown)
    {
        var m = _model!;
        _lastRun = run;
        PatternMap = new Bitmap(new MemoryStream(png));
        PatternLegend.Clear();
        foreach (var (aim, c) in shown)
            PatternLegend.Add(new LegendItemViewModel($"aim {aim:+0;-0;0}{(aim == run.Best.Aim ? " (best)" : "")}", new SolidColorBrush(Color.FromRgb(c.Red, c.Green, c.Blue))));
        AimRows.Clear();
        foreach (var a in run.Aims) AimRows.Add(new AimRowViewModel(a, a == run.Best));
        ResultTitle = $"{run.Player.Name} · {m.TeeLabel} · pin {run.Pin} · {run.Settings.SamplesPerAim:#,0} drives per aim · seed {run.Settings.Seed} · {_stamp!.ModifiedUtc.ToLocalTime():d MMM HH:mm} save";
        ResultBadge = run.Terrain is null ? "Geometry only · placeholder physics and a sparse secondary baseline"
            : run.Terrain.Label == "terrain unverified" ? "TERRAIN UNVERIFIED (override) · placeholder physics and a sparse secondary baseline"
            : $"Terrain {run.Terrain.Label} · placeholder physics and a sparse secondary baseline";
        var b = run.Best;
        BestText = $"Best line {b.Aim:+0;-0;0} yds {(b.Aim >= 0 ? "right" : "left")} of the aim marker: {b.Share(Ground.Fairway):P0} fairway, {b.MeanYardsLeft:0} yds left on average, "
            + $"{b.MeanSg:+0.000;-0.000} strokes gained off the tee (± {b.SgCi95:0.000}), {b.ExpectedStrokes:0.00} expected strokes for the hole.";
        RunNotes.Clear();
        foreach (var n in run.Notes) RunNotes.Add(n);
        var ph = _sheet.PlaceholdersFor(run.Player.Id, run.Terrain is not null).Select(r => r.Label).ToList();
        DependsText = ph.Count == 0 ? "" : $"Depends on {ph.Count} placeholder values: {string.Join("; ", ph)}.";
        HasResults = true;
    }

    void Show(string text, bool error = false) { Message = text; MessageBrush = error ? Warn : Ok; }
}

/// <summary>A labelled value with its source tag (FILE, ALEX, PLACEHOLDER…).</summary>
public sealed class SourcedRowViewModel(string item, string value, SourceTag source, string note)
{
    public string Item { get; } = item;
    public string Value { get; } = value;
    public string Note { get; } = note;
    public bool HasNote => Note.Length > 0;
    public string Tag { get; } = AssumptionRow.Tag(source);
    public IBrush TagInk { get; } = TagBrushes.Ink(source);
    public IBrush TagFill { get; } = TagBrushes.Fill(source);
    public IBrush ValueInk { get; } = source == SourceTag.Placeholder ? TagBrushes.Ink(source) : Brush.Parse("#18211C");
}

static class TagBrushes
{
    public static IBrush Ink(SourceTag s) => Brush.Parse(s switch
    {
        SourceTag.File => "#18211C", SourceTag.Measured => "#1F6E55", SourceTag.Prior => "#5B52A8", SourceTag.Alex => "#7A6640", _ => "#B4471F",
    });
    public static IBrush Fill(SourceTag s) => Brush.Parse(s switch
    {
        SourceTag.File => "#EEF0EA", SourceTag.Measured => "#E1F2EB", SourceTag.Prior => "#ECEAF8", SourceTag.Alex => "#F3EDE0", _ => "#FBE6DC",
    });
}

public sealed partial class UnknownSurfaceViewModel : ObservableObject
{
    readonly Action<Ground?> _set;
    public static IReadOnlyList<string> Choices { get; } = ["Like native", "Like heavy rough", "Like rough", "Like a bunker", "Like scrub", "Ignore"];

    public UnknownSurfaceViewModel(UnknownSurface u, Ground? current, Action<Ground?> set)
    {
        _set = set;
        Text = u.Text;
        _choice = current switch
        {
            Ground.HeavyRough => Choices[1], Ground.Rough => Choices[2], Ground.Bunker => Choices[3], Ground.Scrub => Choices[4], null => Choices[5], _ => Choices[0],
        };
    }

    public string Text { get; }
    [ObservableProperty] private string _choice;
    partial void OnChoiceChanged(string value) => _set(Array.IndexOf(Choices.ToArray(), value) switch
    {
        1 => Ground.HeavyRough, 2 => Ground.Rough, 3 => Ground.Bunker, 4 => Ground.Scrub, 5 => null, _ => Ground.Native,
    });
}

public sealed partial class AssumptionRowViewModel : ObservableObject
{
    readonly AssumptionRow _row;
    readonly Action _changed;

    public AssumptionRowViewModel(AssumptionRow row, Action changed)
    {
        _row = row; _changed = changed;
        _editText = row.Value;
        _choice = row.Kind == RowKind.Choice ? row.Value : null;
    }

    public string Label => _row.Label;
    public string Value => _row.Value + (_row.Unit.Length > 0 && _row.Kind != RowKind.Choice ? $"  {_row.Unit}" : "");
    public string Note => _row.Note + (_row.PriorConfirmed is { } d && _row.Source != SourceTag.Prior ? $" Last confirmed {d:d MMM yyyy}." : "");
    public bool HasNote => Note.Trim().Length > 0;
    public string Tag => AssumptionRow.Tag(_row.Source);
    public IBrush TagInk => TagBrushes.Ink(_row.Source);
    public IBrush TagFill => TagBrushes.Fill(_row.Source);
    public IBrush ValueInk => _row.IsPlaceholder ? TagBrushes.Ink(SourceTag.Placeholder) : Brush.Parse("#18211C");
    public bool Confirmed => _row.Confirmed;
    public bool NotConfirmed => !_row.Confirmed;
    public bool CanEdit => _row.Editable && _row.Kind != RowKind.Choice;
    public bool IsChoice => _row.Kind == RowKind.Choice && _row.Options.Count > 1;
    public IReadOnlyList<string> Options => _row.Options;

    [ObservableProperty] private bool _editing;
    [ObservableProperty] private string _editText;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string? _choice;

    partial void OnChoiceChanged(string? value)
    {
        if (value is null || value == _row.Value) return;
        Error = _row.TrySet(value, DateTime.Now) ?? "";
        if (Error == "") _changed();
    }

    [RelayCommand] private void Confirm() { _row.ConfirmedAt ??= DateTime.Now; _changed(); }
    [RelayCommand] private void Edit() { EditText = _row.Value; Editing = true; Error = ""; }
    [RelayCommand] private void CancelEdit() { Editing = false; Error = ""; }
    [RelayCommand] private void ApplyEdit()
    {
        Error = _row.TrySet(EditText, DateTime.Now) ?? "";
        if (Error != "") return;
        Editing = false;
        _changed();
    }
}

public sealed class AimRowViewModel(AimResult a, bool best)
{
    public string Aim { get; } = a.Aim.ToString("+0;-0;0");
    public string Fairway { get; } = a.Share(Ground.Fairway).ToString("P0");
    public string Rough { get; } = (a.Share(Ground.Rough) + a.Share(Ground.HeavyRough)).ToString("P0");
    public string Native { get; } = a.Share(Ground.Native).ToString("P0");
    public string Bunker { get; } = a.Share(Ground.Bunker).ToString("P0");
    public string Scrub { get; } = (a.Share(Ground.Scrub) + a.Share(Ground.Water)).ToString("P0");
    public string YardsLeft { get; } = a.MeanYardsLeft.ToString("0");
    public string Sg { get; } = $"{a.MeanSg:+0.000;-0.000} ± {a.SgCi95:0.000}";
    public string Expected { get; } = a.ExpectedStrokes.ToString("0.00");
    public string Outside { get; } = a.OutsideTableShare > 0 ? a.OutsideTableShare.ToString("P0") : "";
    public bool IsBest { get; } = best;
    public FontWeight Weight { get; } = best ? FontWeight.SemiBold : FontWeight.Normal;
    public IBrush Fill { get; } = best ? Brush.Parse("#E4EFE6") : Brushes.Transparent;
}

public sealed class LegendItemViewModel(string label, IBrush swatch)
{
    public string Label { get; } = label;
    public IBrush Swatch { get; } = swatch;
}
