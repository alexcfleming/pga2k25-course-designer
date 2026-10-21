using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.CourseFormat;
using Pga2K25.Readiness;
using Pga2K25.Studio.Services;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// Publish readiness: the checks the course file answers, the judgement calls the designer ticks, pins, playtest
/// coverage and the sign-off, saved as Readiness.json beside the products. Every change saves at once. The checks
/// re-run when the section opens and on Re-read.
/// </summary>
public partial class ReadinessViewModel : ObservableObject
{
    public const string OverviewTab = "Overview", HolesTab = "By hole", ChecklistsTab = "Checklists", PinsTab = "Pins and playtest", GateTab = "Publish gate";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    readonly string _coursePath, _outputDir, _courseName;
    readonly Action<int> _showHole;
    ReadinessState _state;
    ReadinessReport? _report;
    string? _stamp;

    public string StatePath { get; }
    public IReadOnlyList<string> Tabs { get; } = [OverviewTab, HolesTab, ChecklistsTab, PinsTab, GateTab];
    public ObservableCollection<TabViewModel> TabButtons { get; } = [];

    [ObservableProperty] private string _tab = OverviewTab;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private IBrush _messageBrush = Brush.Parse("#1F4D30");
    [ObservableProperty] private bool _hasReport;

    public bool IsOverview => Tab == OverviewTab;
    public bool IsHoles => Tab == HolesTab;
    public bool IsChecklists => Tab == ChecklistsTab;
    public bool IsPins => Tab == PinsTab;
    public bool IsGate => Tab == GateTab;

    // Overview.
    public ObservableCollection<FindingViewModel> Findings { get; } = [];
    public ObservableCollection<string> Passes { get; } = [];
    public ObservableCollection<StageRowViewModel> Stages { get; } = [];
    public ObservableCollection<LimitRowViewModel> Limits { get; } = [];
    [ObservableProperty] private string _gateTitle = "";
    [ObservableProperty] private string _gateDetail = "";
    [ObservableProperty] private IBrush _gateBrush = Brush.Parse("#B4471F");
    [ObservableProperty] private double _overallWidth;
    [ObservableProperty] private string _overallText = "";
    [ObservableProperty] private string _sourceText = "";
    [ObservableProperty] private string _passCount = "";
    [ObservableProperty] private string _openCount = "";

    // By hole.
    public IReadOnlyList<string> HoleStageNames { get; } = Checklist.HoleStages.Select(s => s.Title).ToList();
    [ObservableProperty] private string _holeStageName = Checklist.TourHoles.Title;
    [ObservableProperty] private string _holeStageLede = Checklist.TourHoles.Lede;
    public ObservableCollection<string> HoleColumns { get; } = [];
    public ObservableCollection<HoleRowViewModel> HoleRows { get; } = [];

    // Checklists.
    public ObservableCollection<StageNavViewModel> StageNav { get; } = [];
    public ObservableCollection<ChecklistItemViewModel> Items { get; } = [];
    [ObservableProperty] private string _stageId = Checklist.PreBuild.Id;
    [ObservableProperty] private string _stageTitle = Checklist.PreBuild.Title;
    [ObservableProperty] private string _stageLede = Checklist.PreBuild.Lede;
    [ObservableProperty] private string _newItemText = "";

    // Pins and playtest.
    public ObservableCollection<PinRowViewModel> PinRows { get; } = [];
    public ObservableCollection<ProfileViewModel> Profiles { get; } = [];
    [ObservableProperty] private string _pinTally = "";

    // Publish gate.
    public ObservableCollection<ChecklistItemViewModel> GateItems { get; } = [];
    public ObservableCollection<string> Blockers { get; } = [];
    [ObservableProperty] private string _signName = "";
    [ObservableProperty] private string _signDate = "";
    [ObservableProperty] private string _signNotes = "";
    [ObservableProperty] private string _designer = "";
    [ObservableProperty] private string _version = "";
    [ObservableProperty] private string _teeNames = "";
    [ObservableProperty] private string _lightingText = "";
    [ObservableProperty] private string _signedStampText = "";

    // Import.
    public ObservableCollection<string> ImportChoices { get; } = [];
    [ObservableProperty] private string? _importChoice;
    [ObservableProperty] private bool _choosingImport;
    IReadOnlyList<BackupCourse> _pendingImport = [];

    public ReadinessViewModel(string coursePath, string outputDir, string courseName, Action<int> showHole)
    {
        _coursePath = coursePath;
        _outputDir = outputDir;
        _courseName = courseName;
        _showHole = showHole;
        StatePath = ReadinessStore.PathFor(outputDir);
        _state = ReadinessStore.Load(StatePath);
        foreach (var t in Tabs) TabButtons.Add(new TabViewModel(t, t == Tab, () => Tab = t));
        foreach (var s in Checklist.Stages) StageNav.Add(new StageNavViewModel(s.Id, s.Title, s.Id == StageId, () => PickStage(s.Id)));
        LoadTexts();
        LoadLimits();
        Rerun();
    }

    partial void OnTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsOverview));
        OnPropertyChanged(nameof(IsHoles));
        OnPropertyChanged(nameof(IsChecklists));
        OnPropertyChanged(nameof(IsPins));
        OnPropertyChanged(nameof(IsGate));
        foreach (var t in TabButtons) t.IsSelected = t.Label == value;
    }

    void Show(string text, bool error = false)
    {
        Message = text;
        MessageBrush = Brush.Parse(error ? "#A32D2D" : "#1F4D30");
    }

    void Save()
    {
        try { ReadinessStore.Save(StatePath, _state); }
        catch (Exception ex) { Show($"Not saved: {ex.Message}", error: true); }
    }

    // ───────────────────────── running the checks ─────────────────────────

    [RelayCommand] private void Rerun()
    {
        if (Busy) return;
        Busy = true;
        Show("Checking the course file…");
        var limits = _state.Limits;
        Task.Run(() =>
        {
            try
            {
                var course = CourseFile.Load(_coursePath).ReadModel();
                var report = ReadinessChecker.Run(course, limits);
                string stamp = ReadinessStore.Stamp(_coursePath);
                Dispatcher.UIThread.Post(() =>
                {
                    _report = report;
                    _stamp = stamp;
                    HasReport = true;
                    Busy = false;
                    SourceText = $"Checked against {Path.GetFileName(_coursePath)} · read {DateTime.Now:d MMM, HH:mm} · back tees" +
                                 (limits.TestDriveYards is double d ? $" · {d:0}-yd test drive" : " · no test drive entered");
                    Show($"{report.Checks.Count(c => c.Passed)} of {report.Checks.Count} course-file checks pass.");
                    RebuildAll();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => { Busy = false; Show($"Could not check the course: {ex.Message}", error: true); });
            }
        });
    }

    void RebuildAll()
    {
        RebuildFindings();
        RebuildHoleTable();
        RebuildItems();
        RebuildPins();
        RebuildProfiles();
        RebuildGateItems();
        RefreshSummary();
    }

    /// <summary>Progress, the gate and the stage counts: cheap, so redone after every change.</summary>
    void RefreshSummary()
    {
        if (_report is null) return;
        var progress = ReadinessEvaluator.Progress(_report, _state);
        Stages.Clear();
        foreach (var p in progress) Stages.Add(new StageRowViewModel(p));
        int done = progress.Sum(p => p.Done), total = progress.Sum(p => p.Total);
        OverallWidth = total == 0 ? 0 : 320.0 * done / total;
        OverallText = total == 0 ? "" : $"{100.0 * done / total:0} %";
        var blockers = ReadinessEvaluator.GateBlockers(_report, _state, _stamp);
        Blockers.Clear();
        foreach (var b in blockers) Blockers.Add(b);
        bool ready = blockers.Count == 0;
        GateTitle = ready ? "Ready to publish and submit for tour review" : "Not ready to submit for tour review";
        GateDetail = ready ? "Every check is cleared and the sign-off is complete." : string.Join(" · ", blockers);
        GateBrush = Brush.Parse(ready ? "#2D6A42" : "#B4471F");
        int cleared = _report.Checks.Count(c => ReadinessEvaluator.IsCleared(c, _state));
        PassCount = $"{cleared} file checks cleared";
        OpenCount = $"{_report.Checks.Count - cleared} to fix or mark deliberate";
        foreach (var nav in StageNav)
        {
            var p = progress.FirstOrDefault(x => x.Id == nav.Id);
            nav.Detail = p is null ? "" : $"{p.Done}/{p.Total}";
        }
        SignedStampText = _state.SignOff.CourseStamp is null ? "Not signed off yet."
            : _state.SignOff.CourseStamp == _stamp ? "Signed off against this version of the course file."
            : "The course file has changed since the sign-off: sign off again once it's checked.";
    }

    // ───────────────────────── overview ─────────────────────────

    void RebuildFindings()
    {
        Findings.Clear();
        Passes.Clear();
        if (_report is null) return;
        foreach (var c in _report.Checks.OrderByDescending(c => c.Severity).ThenBy(c => c.Title))
        {
            if (c.Severity == Severity.Pass) { Passes.Add($"{c.Title}. {c.Detail}"); continue; }
            Findings.Add(new FindingViewModel(c, _state, _showHole, changed => { Save(); RefreshSummary(); RebuildItems(); RebuildGateItems(); }));
        }
    }

    void LoadLimits()
    {
        var L = _state.Limits;
        Limits.Clear();
        void Add(string key, string label, double? v) => Limits.Add(new LimitRowViewModel(key, label, v is double x ? x.ToString("0.##", Inv) : ""));
        Add("drive", "Test drive (yds)", L.TestDriveYards);
        Add("par5", "Par 5 left after it (yds)", L.Par5MinLeftYards);
        Add("drivable", "Drivable within (yds)", L.DrivableWithinYards);
        Add("near", "Near-drivable within (yds)", L.NearDrivableWithinYards);
        Add("perNine", "Drivable per nine, at most", L.MaxDrivablePerNine);
        Add("scaleLow", "Scale low (× median)", L.ScaleLow);
        Add("scaleHigh", "Scale high (× median)", L.ScaleHigh);
        Add("corridor", "Corridor (yds)", L.CorridorYards);
        Add("aim", "Aim points apart (yds)", L.AimSpacingYards);
        Add("drop", "Largest drop (ft)", L.MaxDropFeet);
        Add("climb", "Largest climb (ft)", L.MaxClimbFeet);
        Add("rangeMin", "Plot range from (ft)", L.PlotRangeMinFeet);
        Add("rangeMax", "Plot range to (ft)", L.PlotRangeMaxFeet);
        Add("reliefMin", "Green relief from (ft)", L.GreenReliefMinFeet);
        Add("reliefMax", "Green relief to (ft)", L.GreenReliefMaxFeet);
        Add("teeMin", "Tee set total from (yds)", L.TeeTotalMinYards);
        Add("teeMax", "Tee set total to (yds)", L.TeeTotalMaxYards);
    }

    [RelayCommand] private void ApplyLimits()
    {
        var L = _state.Limits;
        var bad = new List<string>();
        double? Get(string key, bool optional = false)
        {
            var row = Limits.First(r => r.Key == key);
            if (!EstimateInputsViewModel.TryNumber(row.Value, out var v) || (v is null && !optional) || v < 0) { bad.Add(row.Label); return null; }
            return v;
        }
        var next = L with
        {
            TestDriveYards = Get("drive", optional: true),
            Par5MinLeftYards = Get("par5") ?? L.Par5MinLeftYards,
            DrivableWithinYards = Get("drivable") ?? L.DrivableWithinYards,
            NearDrivableWithinYards = Get("near") ?? L.NearDrivableWithinYards,
            MaxDrivablePerNine = (int)(Get("perNine") ?? L.MaxDrivablePerNine),
            ScaleLow = Get("scaleLow") ?? L.ScaleLow,
            ScaleHigh = Get("scaleHigh") ?? L.ScaleHigh,
            CorridorYards = Get("corridor") ?? L.CorridorYards,
            AimSpacingYards = Get("aim") ?? L.AimSpacingYards,
            MaxDropFeet = Get("drop") ?? L.MaxDropFeet,
            MaxClimbFeet = Get("climb") ?? L.MaxClimbFeet,
            PlotRangeMinFeet = Get("rangeMin") ?? L.PlotRangeMinFeet,
            PlotRangeMaxFeet = Get("rangeMax") ?? L.PlotRangeMaxFeet,
            GreenReliefMinFeet = Get("reliefMin") ?? L.GreenReliefMinFeet,
            GreenReliefMaxFeet = Get("reliefMax") ?? L.GreenReliefMaxFeet,
            TeeTotalMinYards = Get("teeMin") ?? L.TeeTotalMinYards,
            TeeTotalMaxYards = Get("teeMax") ?? L.TeeTotalMaxYards,
        };
        if (bad.Count > 0) { Show($"Not applied. Check: {string.Join(", ", bad)}.", error: true); return; }
        _state.Limits = next;
        Save();
        Rerun();
    }

    [RelayCommand] private void ResetLimits()
    {
        _state.Limits = new ReadinessLimits { TestDriveYards = _state.Limits.TestDriveYards };
        Save();
        LoadLimits();
        Rerun();
    }

    // ───────────────────────── by hole ─────────────────────────

    partial void OnHoleStageNameChanged(string value)
    {
        HoleStageLede = Checklist.HoleStages.First(s => s.Title == value).Lede;
        RebuildHoleTable();
    }

    void RebuildHoleTable()
    {
        HoleRows.Clear();
        HoleColumns.Clear();
        if (_report is null) return;
        var stage = Checklist.HoleStages.First(s => s.Title == HoleStageName);
        foreach (var c in stage.Columns) HoleColumns.Add(c.Label);
        foreach (var h in _report.Holes)
            HoleRows.Add(new HoleRowViewModel(h, _report, stage, _state, () => { Save(); RefreshSummary(); RebuildFindings(); }));
    }

    // ───────────────────────── checklists ─────────────────────────

    void PickStage(string id)
    {
        StageId = id;
        var s = Checklist.Stages.First(x => x.Id == id);
        StageTitle = s.Title;
        StageLede = s.Lede;
        foreach (var n in StageNav) n.IsSelected = n.Id == id;
        RebuildItems();
    }

    ChecklistItemViewModel ItemVm(ChecklistItem item, bool custom) =>
        new(item, _report!, _state, custom, () => { Save(); RefreshSummary(); RebuildItemStates(); }, custom ? () => RemoveCustom(item.Key) : null);

    void RebuildItems()
    {
        Items.Clear();
        if (_report is null) return;
        var stage = Checklist.Stages.First(x => x.Id == StageId);
        foreach (var item in stage.Items) Items.Add(ItemVm(item, custom: false));
        foreach (var item in ReadinessEvaluator.CustomItems(stage, _state)) Items.Add(ItemVm(item, custom: true));
    }

    /// <summary>Re-reads every visible item's status (an item answered by the file can change with another tick).</summary>
    void RebuildItemStates()
    {
        foreach (var i in Items.Concat(GateItems)) i.Refresh();
    }

    [RelayCommand] private void AddItem()
    {
        string text = NewItemText.Trim();
        if (text.Length == 0) return;
        var stage = Checklist.Stages.First(x => x.Id == StageId);
        _state.Custom.Add(new CustomItem { Stage = stage.Id, Key = $"{stage.Prefix}.x{DateTime.Now.Ticks:x}", Text = text });
        NewItemText = "";
        Save();
        RebuildItems();
        RebuildGateItems();
        RefreshSummary();
    }

    void RemoveCustom(string key)
    {
        _state.Custom.RemoveAll(c => c.Key == key);
        _state.Items.Remove(key);
        Save();
        RebuildItems();
        RebuildGateItems();
        RefreshSummary();
    }

    // ───────────────────────── pins and playtest ─────────────────────────

    void RebuildPins()
    {
        PinRows.Clear();
        if (_report is null) return;
        foreach (var h in _report.Holes) PinRows.Add(new PinRowViewModel(h, _state, () => { Save(); RefreshSummary(); UpdatePinTally(); }));
        UpdatePinTally();
    }

    void UpdatePinTally()
    {
        if (_report is null) return;
        var pins = _report.Holes.SelectMany(h => h.Pins).ToList();
        if (pins.Count == 0) { PinTally = "No pins in the file."; return; }
        string Count(Func<PinPlacement, bool> f) => pins.Count(f).ToString(Inv);
        var diff = _state.Pins.Values.Select(p => p.Difficulty).Where(d => d is { Length: > 0 }).ToList();
        PinTally = $"Front {Count(p => p.Depth == 'F')} · centre {Count(p => p.Depth == 'C')} · back {Count(p => p.Depth == 'B')}   " +
                   $"Left {Count(p => p.Side == 'L')} · centre {Count(p => p.Side == 'C')} · right {Count(p => p.Side == 'R')}   " +
                   $"Hard {diff.Count(d => d == "R")} · medium {diff.Count(d => d == "A")} · easy {diff.Count(d => d == "G")}";
    }

    void RebuildProfiles()
    {
        Profiles.Clear();
        if (_report is null) return;
        int tees = Math.Max(1, _report.TeeSets.Count);
        for (int i = 0; i < 2; i++)
            Profiles.Add(new ProfileViewModel(i, _state.Profile(i), tees, _state.Info.TeeNames, _state.Info.PrevailingWind, () => { Save(); RefreshSummary(); RebuildItemStates(); }));
    }

    // ───────────────────────── publish gate ─────────────────────────

    void RebuildGateItems()
    {
        GateItems.Clear();
        if (_report is null) return;
        foreach (var item in Checklist.Publish.Items) GateItems.Add(ItemVm(item, custom: false));
        foreach (var item in ReadinessEvaluator.CustomItems(Checklist.Publish, _state)) GateItems.Add(ItemVm(item, custom: true));
        var a = _report.Atmosphere;
        LightingText = a is null ? "No lighting settings in the file."
            : $"Time {a.Clock} · sun {a.SunOrientation:0.#}° at {a.SunInclination:0.#}° · clouds {Cloud(a.Cloud1)} / {Cloud(a.Cloud2)} · " +
              $"cloud height {a.CloudHeight:0.##} · fog ceiling {a.FogCeiling:0.##}, density {a.FogDensity:0.##}{(a.Haze > 0 ? $" · haze {a.Haze:0.##}" : "")}{(a.Rain ? " · rain" : "")}";
        static string Cloud(CloudLayer? c) => c is null ? "none" : $"{c.Type} {c.Cloudiness:0.##}";
    }

    void LoadTexts()
    {
        _signName = _state.SignOff.Name;
        _signDate = _state.SignOff.Date;
        _signNotes = _state.SignOff.Notes;
        _designer = _state.Info.Designer;
        _version = _state.Info.Version;
        _teeNames = string.Join(", ", _state.Info.TeeNames);
        foreach (var p in new[] { nameof(SignName), nameof(SignDate), nameof(SignNotes), nameof(Designer), nameof(Version), nameof(TeeNames) }) OnPropertyChanged(p);
    }

    partial void OnSignNameChanged(string value) { _state.SignOff.Name = value; Save(); RefreshSummary(); }
    partial void OnSignDateChanged(string value) { _state.SignOff.Date = value; Save(); RefreshSummary(); }
    partial void OnSignNotesChanged(string value) { _state.SignOff.Notes = value; Save(); }
    partial void OnDesignerChanged(string value) { _state.Info.Designer = value; Save(); }
    partial void OnVersionChanged(string value) { _state.Info.Version = value; Save(); }
    partial void OnTeeNamesChanged(string value)
    {
        _state.Info.TeeNames = value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        Save();
    }

    /// <summary>Ties the sign-off to this version of the course file; a later change re-opens the gate.</summary>
    [RelayCommand] private void SignOff()
    {
        if (_stamp is null) return;
        if (string.IsNullOrWhiteSpace(SignName)) { Show("Type who is signing off first.", error: true); return; }
        if (string.IsNullOrWhiteSpace(SignDate)) SignDate = DateTime.Now.ToString("yyyy-MM-dd", Inv);
        _state.SignOff.CourseStamp = _stamp;
        Save();
        RefreshSummary();
        Show("Signed off against this version of the course file.");
    }

    [RelayCommand] private void ExportRecord()
    {
        if (_report is null) return;
        try
        {
            var files = ReadinessRecord.Write(_outputDir, _coursePath, _report, _state);
            Show($"Record written: {Path.GetFileName(files.Html)} and its JSON, in the output folder.");
            if (ShellOpen.Open(files.Html) is { } why) Show(why, error: true);
        }
        catch (Exception ex) { Show($"Record not written: {ex.Message}", error: true); }
    }

    [RelayCommand] private void OpenStateFile()
    {
        if (File.Exists(StatePath) && ShellOpen.Reveal(StatePath) is { } why) Show(why, error: true);
    }

    // ───────────────────────── import ─────────────────────────

    /// <summary>Reads an HTML checklist backup: the course of the same name goes straight in, otherwise pick one.</summary>
    public void ImportBackup(string path)
    {
        try
        {
            var courses = ChecklistImport.ReadBackup(File.ReadAllText(path));
            if (courses.Count == 0) { Show("That backup has no courses in it.", error: true); return; }
            if (ChecklistImport.Match(courses, _courseName) is { } match) { DoImport(match); return; }
            _pendingImport = courses;
            ImportChoices.Clear();
            foreach (var c in courses) ImportChoices.Add(c.Name);
            ImportChoice = ImportChoices[0];
            ChoosingImport = true;
            Show($"No course called {_courseName} in that backup. Pick which one to bring in.");
        }
        catch (Exception ex) { Show($"Not imported: {ex.Message}", error: true); }
    }

    [RelayCommand] private void ImportChosen()
    {
        if (_pendingImport.FirstOrDefault(c => c.Name == ImportChoice) is { } c) DoImport(c);
        ChoosingImport = false;
    }

    [RelayCommand] private void CancelImport() { ChoosingImport = false; Show(""); }

    void DoImport(BackupCourse course)
    {
        var summary = ChecklistImport.Import(course, _state);
        Save();
        LoadTexts();
        LoadLimits();
        Show($"Imported from “{course.Name}”: {summary}." + (summary.Notes.Count > 0 ? " " + string.Join(" ", summary.Notes) : ""));
        Rerun();
    }
}

public partial class TabViewModel(string label, bool selected, Action select) : ObservableObject
{
    public string Label { get; } = label;
    [ObservableProperty] private bool _isSelected = selected;
    [RelayCommand] private void Select() => select();
}

public partial class StageNavViewModel(string id, string title, bool selected, Action select) : ObservableObject
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    [ObservableProperty] private bool _isSelected = selected;
    [ObservableProperty] private string _detail = "";
    [RelayCommand] private void Select() => select();
}

/// <summary>The look of a status or severity: label, fill and text colour.</summary>
static class Look
{
    public static (IBrush Fill, IBrush Ink) Of(Severity s) => s switch
    {
        Severity.High => (Brush.Parse("#F6E0DA"), Brush.Parse("#8E2F1E")),
        Severity.Flag => (Brush.Parse("#FBEFD9"), Brush.Parse("#7A4A00")),
        Severity.Pass => (Brush.Parse("#E4EFE6"), Brush.Parse("#1F4D30")),
        _ => (Brush.Parse("#E3EDF5"), Brush.Parse("#1E4A70")),
    };

    public static (IBrush Fill, IBrush Ink, IBrush Edge) Of(ItemStatus s) => s switch
    {
        ItemStatus.Done => (Brush.Parse("#E4EFE6"), Brush.Parse("#1F4D30"), Brush.Parse("#9CC3A6")),
        ItemStatus.Deliberate => (Brush.Parse("#E3EDF5"), Brush.Parse("#1E4A70"), Brush.Parse("#A9C3DA")),
        ItemStatus.NotApplicable => (Brush.Parse("#F3F4F0"), Brush.Parse("#56605A"), Brush.Parse("#C9CFC6")),
        _ => (Brush.Parse("#FFFFFF"), Brush.Parse("#3F4A43"), Brush.Parse("#C9CFC6")),
    };

    public static string Label(ItemStatus s) => s switch
    {
        ItemStatus.Done => "Done", ItemStatus.Deliberate => "Deliberate", ItemStatus.NotApplicable => "N/A", _ => "Open",
    };

    /// <summary>Open → Done → Deliberate → N/A → Open.</summary>
    public static ItemStatus Next(ItemStatus s) => (ItemStatus)(((int)s + 1) % 4);
}

/// <summary>A course-file check that isn't a plain pass: fix it, or mark it deliberate with the reason.</summary>
public partial class FindingViewModel : ObservableObject
{
    readonly ReadinessCheck _check;
    readonly ReadinessState _state;
    readonly Action<int> _showHole;
    readonly Action<bool> _changed;

    public string Title => _check.Title;
    public string Detail => _check.Detail;
    public string Rule => _check.Rule;
    public bool HasHole => _check.Holes.Count > 0;
    public string ShowLabel => HasHole ? $"Show hole {_check.Holes[0]}" : "";
    public bool CanMark => _check.Severity is Severity.Flag or Severity.High;

    [ObservableProperty] private string _level = "";
    [ObservableProperty] private IBrush _fill = Brushes.White;
    [ObservableProperty] private IBrush _ink = Brushes.Black;
    [ObservableProperty] private bool _isDeliberate;
    [ObservableProperty] private string _reason = "";
    [ObservableProperty] private string _markLabel = "";

    public FindingViewModel(ReadinessCheck check, ReadinessState state, Action<int> showHole, Action<bool> changed)
    {
        _check = check;
        _state = state;
        _showHole = showHole;
        _changed = changed;
        _reason = state.Deliberate.TryGetValue(check.Id, out var m) ? m.Reason : "";
        Refresh();
    }

    void Refresh()
    {
        IsDeliberate = _state.Deliberate.ContainsKey(_check.Id);
        bool someHoles = !IsDeliberate && _check.Holes.Any(h => _state.Deliberate.ContainsKey($"{_check.Id}@{h}"));
        var cleared = ReadinessEvaluator.IsCleared(_check, _state);
        Level = cleared && !_check.Passed ? "Deliberate" : _check.Severity.ToString();
        (Fill, Ink) = cleared && !_check.Passed ? (Brush.Parse("#E3EDF5"), Brush.Parse("#1E4A70")) : Look.Of(_check.Severity);
        MarkLabel = IsDeliberate ? "Reopen" : someHoles ? "Mark the rest deliberate" : "Mark deliberate";
    }

    [RelayCommand] private void Mark()
    {
        if (IsDeliberate) _state.Deliberate.Remove(_check.Id);
        else _state.Deliberate[_check.Id] = new DeliberateMark { Reason = Reason.Trim().Length > 0 ? Reason.Trim() : "Marked deliberate" };
        Refresh();
        _changed(true);
    }

    partial void OnReasonChanged(string value)
    {
        if (_state.Deliberate.TryGetValue(_check.Id, out var m)) { m.Reason = value; _changed(false); }
    }

    [RelayCommand] private void ShowHole() { if (HasHole) _showHole(_check.Holes[0]); }
}

public sealed class StageRowViewModel(StageProgress p)
{
    public string Title { get; } = p.Title;
    public string Count { get; } = $"{p.Done} / {p.Total}";
    public string Note { get; } = p.Note;
    public double BarWidth { get; } = p.Total == 0 ? 0 : 260.0 * p.Done / p.Total;
    public IBrush BarBrush { get; } = Brush.Parse(p.Complete ? "#2D6A42" : "#7FA88A");
}

public partial class LimitRowViewModel(string key, string label, string value) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    [ObservableProperty] private string _value = value;
}

/// <summary>One cell of the hole table: a judgement call on one hole, clicked through Open → Done → Deliberate → N/A.</summary>
public partial class StatusCellViewModel : ObservableObject
{
    readonly string _key;
    readonly ReadinessState _state;
    readonly Action _changed;
    public string Tip { get; }

    [ObservableProperty] private string _label = "";
    [ObservableProperty] private IBrush _fill = Brushes.White;
    [ObservableProperty] private IBrush _ink = Brushes.Black;
    [ObservableProperty] private IBrush _edge = Brushes.Gray;

    public StatusCellViewModel(string key, string tip, ReadinessState state, Action changed)
    {
        _key = key;
        _state = state;
        _changed = changed;
        Tip = tip;
        Refresh();
    }

    void Refresh()
    {
        var s = _state.StatusOf(_key);
        Label = Look.Label(s);
        (Fill, Ink, Edge) = Look.Of(s);
    }

    [RelayCommand] private void Cycle()
    {
        _state.Set(_key, Look.Next(_state.StatusOf(_key)));
        Refresh();
        _changed();
    }
}

/// <summary>A hole's row: what the file measured (flagged cells shaded) and the stage's judgement calls.</summary>
public partial class HoleRowViewModel : ObservableObject
{
    static readonly IBrush Plain = Brush.Parse("#F7F8F5"), Flagged = Brush.Parse("#FBEFD9"), Worse = Brush.Parse("#F6E0DA");
    readonly int _hole;
    readonly ReadinessState _state;
    readonly Action _changed;

    public int Hole { get; }
    public string ParYards { get; }
    public string Drive { get; }
    public IBrush DriveFill { get; }
    public string Green { get; }
    public IBrush GreenFill { get; }
    public string Width { get; }
    public IBrush WidthFill { get; }
    public string Change { get; }
    public IBrush ChangeFill { get; }
    public string Relief { get; }
    public IBrush ReliefFill { get; }
    public string Near { get; }
    public IBrush NearFill { get; }
    public ObservableCollection<StatusCellViewModel> Cells { get; } = [];

    [ObservableProperty] private bool _sizeDeliberate;

    public HoleRowViewModel(HoleMeasures h, ReadinessReport r, HoleStage stage, ReadinessState state, Action changed)
    {
        _hole = h.Number;
        _state = state;
        _changed = changed;
        var L = r.Limits;
        Hole = h.Number;
        ParYards = $"{h.Par} · {h.Yards}";
        Drive = h.DriveClass ?? (h.LeftAfterDrive is int left ? $"{left} left" : "—");
        DriveFill = h.DriveClass == "Drivable" || h.LeftAfterDrive < L.Par5MinLeftYards ? Worse : h.DriveClass is not null ? Flagged : Plain;
        Green = h.GreenEstimated ? "not drawn" : $"{h.GreenSquareFeet:N0} · {h.GreenRatio:0.00}×";
        GreenFill = h.GreenRatio is double g && (g < L.ScaleLow || g > L.ScaleHigh) ? Flagged : Plain;
        Width = h.LandingWidthYards is double w ? $"{w:0}{(h.LandingOffFairway ? "*" : "")} · {h.WidthRatio:0.00}×" : h.Par == 3 ? "par 3" : "—";
        WidthFill = h.LandingOffFairway || h.WidthRatio is double wr && (wr < L.ScaleLow || wr > L.ScaleHigh) ? Flagged : Plain;
        Change = $"{h.ChangeFeet:+0;-0;0} ft";
        ChangeFill = h.ChangeFeet > L.MaxClimbFeet || -h.ChangeFeet > L.MaxDropFeet ? Flagged : Plain;
        Relief = h.GreenReliefFeet is double rf ? $"{rf:0.0} ft" : "—";
        ReliefFill = h.GreenReliefFeet is double rr && (Math.Round(rr, 1) < L.GreenReliefMinFeet || Math.Round(rr, 1) > L.GreenReliefMaxFeet) ? Flagged : Plain;
        Near = h.NearGreenYards is double ny ? $"{ny:0} yds · {(h.NearGreenOf == 0 ? "practice" : $"hole {h.NearGreenOf}")}" : "—";
        NearFill = h.NearGreenYards is double n2 && Math.Round(n2) < L.CorridorYards ? (n2 < 0.5 ? Worse : Flagged) : Plain;
        foreach (var c in stage.Columns) Cells.Add(new StatusCellViewModel(stage.Key(h.Number, c.Id), c.Label, state, changed));
        _sizeDeliberate = state.Deliberate.ContainsKey($"green-scale@{h.Number}");
    }

    /// <summary>"Size is deliberate" on this hole, as in the HTML checklist: covers green size and landing width here.</summary>
    partial void OnSizeDeliberateChanged(bool value)
    {
        foreach (var check in new[] { "green-scale", "landing-width" })
        {
            string key = $"{check}@{_hole}";
            if (value) _state.Deliberate.TryAdd(key, new DeliberateMark { Reason = "Size is deliberate on this hole" });
            else _state.Deliberate.Remove(key);
        }
        _changed();
    }
}

/// <summary>A checklist item with its status, source, note and, when the file answers it, the evidence.</summary>
public partial class ChecklistItemViewModel : ObservableObject
{
    readonly ChecklistItem _item;
    readonly ReadinessReport _report;
    readonly ReadinessState _state;
    readonly Action _changed;
    readonly Action? _remove;

    public string Text => _item.Text;
    public string Source { get; }
    public bool IsCustom => _remove is not null;
    public bool HasEvidence => Evidence.Length > 0;
    public string Evidence { get; }

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private IBrush _fill = Brushes.White;
    [ObservableProperty] private IBrush _ink = Brushes.Black;
    [ObservableProperty] private IBrush _edge = Brushes.Gray;
    [ObservableProperty] private string _note;

    public ChecklistItemViewModel(ChecklistItem item, ReadinessReport report, ReadinessState state, bool custom, Action changed, Action? remove)
    {
        _item = item;
        _report = report;
        _state = state;
        _changed = changed;
        _remove = remove;
        Source = custom ? "added by you" : Checklist.SourceLabel(item.Source);
        _note = state.Items.TryGetValue(item.Key, out var s) ? s.Note : "";
        var checks = item.Checks.Select(id => report.Check(id)).OfType<ReadinessCheck>().ToList();
        Evidence = item.Checks.Contains(Checklist.AllChecks) ? $"{report.Checks.Count(c => ReadinessEvaluator.IsCleared(c, state))} of {report.Checks.Count} file checks cleared"
            : checks.Count > 0 ? string.Join(" · ", checks.Select(c => $"{c.Title}: {(ReadinessEvaluator.IsCleared(c, state) ? (c.Passed ? "passes" : "deliberate") : c.Severity.ToString().ToLowerInvariant())}"))
            : item.Checks.Contains("drive-entered") ? (state.Limits.TestDriveYards is double d ? $"test drive {d:0} yds in the limits" : "no test drive in the limits yet")
            : item.Checks.Count > 0 ? "done when the playtest coverage is complete" : "";
        Refresh();
    }

    public void Refresh()
    {
        var s = ReadinessEvaluator.Effective(_item, _report, _state, out bool fromFile);
        Status = Look.Label(s) + (fromFile ? " · file" : "");
        (Fill, Ink, Edge) = Look.Of(s);
    }

    [RelayCommand] private void Cycle()
    {
        var current = ReadinessEvaluator.Effective(_item, _report, _state, out _);
        _state.Set(_item.Key, Look.Next(current));
        Refresh();
        _changed();
    }

    partial void OnNoteChanged(string value)
    {
        if (!_state.Items.TryGetValue(_item.Key, out var s)) _state.Items[_item.Key] = s = new ItemState();
        s.Note = value;
        _changed();
    }

    [RelayCommand] private void Remove() => _remove?.Invoke();
}

/// <summary>A hole's four pins: where the file puts each, and the difficulty the designer gives it.</summary>
public sealed class PinRowViewModel
{
    public int Hole { get; }
    public IReadOnlyList<PinCellViewModel> Pins { get; }

    public PinRowViewModel(HoleMeasures h, ReadinessState state, Action changed)
    {
        Hole = h.Number;
        Pins = Enumerable.Range(1, 4).Select(n =>
        {
            var p = h.Pins.FirstOrDefault(x => x.Number == n);
            return new PinCellViewModel($"{h.Number}.{n}", n, p is null ? "—" : $"{p.Depth}{p.Side}", state, changed);
        }).ToList();
    }
}

public partial class PinCellViewModel : ObservableObject
{
    static readonly string[] Order = ["", "R", "A", "G"];
    readonly string _key;
    readonly ReadinessState _state;
    readonly Action _changed;

    public string Position { get; }
    public string Tip { get; }

    [ObservableProperty] private string _difficulty = "";
    [ObservableProperty] private IBrush _fill = Brushes.White;
    [ObservableProperty] private IBrush _ink = Brushes.Black;

    public PinCellViewModel(string key, int number, string position, ReadinessState state, Action changed)
    {
        _key = key;
        _state = state;
        _changed = changed;
        Position = position;
        string noted = state.Pins.TryGetValue(key, out var ps) && (ps.NotedDepth ?? ps.NotedSide) is not null ? $" (checklist had {ps.NotedDepth}{ps.NotedSide})" : "";
        Tip = $"Pin {number}: {(position == "—" ? "not in the file" : position)} by thirds along the approach and across the green{noted}. Click for difficulty.";
        Refresh();
    }

    void Refresh()
    {
        string d = _state.Pins.TryGetValue(_key, out var ps) ? ps.Difficulty ?? "" : "";
        Difficulty = d switch { "R" => "Hard", "A" => "Medium", "G" => "Easy", _ => "Set" };
        (Fill, Ink) = d switch
        {
            "R" => (Brush.Parse("#F6E0DA"), Brush.Parse("#8E2F1E")),
            "A" => (Brush.Parse("#FBEFD9"), Brush.Parse("#7A4A00")),
            "G" => (Brush.Parse("#E4EFE6"), Brush.Parse("#1F4D30")),
            _ => (Brush.Parse("#FFFFFF"), Brush.Parse("#56605A")),
        };
    }

    [RelayCommand] private void Cycle()
    {
        if (!_state.Pins.TryGetValue(_key, out var ps)) _state.Pins[_key] = ps = new PinState();
        ps.Difficulty = Order[(Array.IndexOf(Order, ps.Difficulty ?? "") + 1) % Order.Length];
        if (ps.Difficulty == "") ps.Difficulty = null;
        Refresh();
        _changed();
    }
}

public partial class ToggleItemViewModel(string label, bool on, Action<bool> set) : ObservableObject
{
    public string Label { get; } = label;
    [ObservableProperty] private bool _isOn = on;
    partial void OnIsOnChanged(bool value) => set(value);
}

/// <summary>One playtest profile: who and how, and what's been played (winds at high intensity, tee sets, pins).</summary>
public partial class ProfileViewModel : ObservableObject
{
    readonly PlaytestProfile _p;
    readonly Action _changed;

    public string Title { get; }
    public string PrevailingText { get; }
    public IReadOnlyList<ToggleItemViewModel> Winds { get; }
    public IReadOnlyList<ToggleItemViewModel> Tees { get; }
    public IReadOnlyList<ToggleItemViewModel> PinsPlayed { get; }

    [ObservableProperty] private string _style;
    [ObservableProperty] private string _wind;
    [ObservableProperty] private string _player;

    public ProfileViewModel(int index, PlaytestProfile p, int teeSets, IReadOnlyList<string> teeNames, string prevailing, Action changed)
    {
        _p = p;
        _changed = changed;
        Title = $"Profile {index + 1}";
        PrevailingText = prevailing.Length > 0 ? $"Prevailing wind {prevailing}" : "";
        _style = p.Style;
        _wind = p.Wind;
        _player = p.Player;
        Winds = Checklist.WindDirections.Select(d => Toggle(d, p.Winds, d)).ToList();
        Tees = Enumerable.Range(1, teeSets).Select(t => Toggle(teeNames.Count >= t ? teeNames[t - 1] : $"Tee {t}", p.Tees, t)).ToList();
        PinsPlayed = Enumerable.Range(1, 4).Select(n => Toggle($"Pin {n}", p.Pins, n)).ToList();
    }

    ToggleItemViewModel Toggle<T>(string label, List<T> set, T value) => new(label, set.Contains(value), on =>
    {
        if (on && !set.Contains(value)) set.Add(value);
        if (!on) set.Remove(value);
        _changed();
    });

    partial void OnStyleChanged(string value) { _p.Style = value; _changed(); }
    partial void OnWindChanged(string value) { _p.Wind = value; _changed(); }
    partial void OnPlayerChanged(string value) { _p.Player = value; _changed(); }
}
