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
    public const string OverviewTab = "Overview", HolesTab = "Hole QA", ChecklistsTab = "Checklists", PinsTab = "Pins and playtest", GateTab = "Publish gate";
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
    /// <summary>File checks still to fix or mark deliberate (the menu's badge).</summary>
    [ObservableProperty] private int _openChecks;

    // Hole QA.
    public ObservableCollection<HoleGroupHeaderViewModel> HoleQaHeader { get; } = [];
    public ObservableCollection<HoleQaRowViewModel> HoleQaRows { get; } = [];
    [ObservableProperty] private HoleCardViewModel? _card;
    [ObservableProperty] private bool _minimumsOnly;
    [ObservableProperty] private bool _openHolesOnly;
    [ObservableProperty] private string _holeQaTotals = "";
    [ObservableProperty] private string _holeQaLede = "";
    int _selectedHole = 1;

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
        var overrides = _state.Overrides();
        Task.Run(() =>
        {
            try
            {
                var course = CourseFile.Load(_coursePath).ReadModel();
                var report = ReadinessChecker.Run(course, limits, overrides: overrides);
                string stamp = ReadinessStore.Stamp(_coursePath);
                Dispatcher.UIThread.Post(() =>
                {
                    _report = report;
                    _stamp = stamp;
                    HasReport = true;
                    Busy = false;
                    SourceText = $"Checked against {Path.GetFileName(_coursePath)} · read {DateTime.Now:d MMM, HH:mm} · back tees" +
                                 $" · {limits.DesignDriveMinYards:0}–{limits.DesignDriveMaxYards:0}-yd design tee shot" + (limits.TestDriveYards is double d ? $", {d:0}-yd test drive" : "");
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
        OpenChecks = _report.Checks.Count - cleared;
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
        Add("teeMin", "Design tee shot from (yds)", L.DesignDriveMinYards);
        Add("teeMax", "Design tee shot to (yds)", L.DesignDriveMaxYards);
        Add("drive", "Test drive, recorded (yds)", L.TestDriveYards);
        Add("medianFour", "Median par 4 at least (yds)", L.MedianPar4MinYards);
        Add("shortPar4", "Par 4s under (yds)", L.ShortPar4Yards);
        Add("maxShort", "Par 4s under it, at most", L.MaxShortPar4s);
        Add("shortFive", "Shortest par 5 at least (yds)", L.ShortestPar5MinYards);
        Add("backMin", "Back tee range from (yds, shown only)", L.BackTeeMinYards);
        Add("backMax", "Back tee range to (yds, shown only)", L.BackTeeMaxYards);
        Add("targetMin", "Landing width target from (yds)", L.LandingTargetMinYards);
        Add("targetMax", "Landing width target to (yds)", L.LandingTargetMaxYards);
        Add("reason", "Reason needed over (yds)", L.WidthReasonYards);
        Add("widthHigh", "Width warning over (yds)", L.WidthHighYards);
        Add("widthLow", "Width warning under (yds)", L.WidthLowYards);
        Add("steady", "Width swing past 200 (×)", L.WidthSteadinessMax);
        Add("hazReach", "Landing hazards within (yds of the line)", L.LandingHazardReachYards);
        Add("hazFrom", "Landing hazards from (yds)", L.LandingHazardFromYards);
        Add("hazTo", "Landing hazards to (yds)", L.LandingHazardToYards);
        Add("straight", "Card and straight shown apart from (yds)", L.StraightDiffYards);
        Add("scaleLow", "Green prompt under (× median)", L.ScaleLow);
        Add("scaleHigh", "Green prompt over (× median)", L.ScaleHigh);
        Add("startMin", "Fairway start note from (yds)", L.FairwayStartMinYards);
        Add("startMax", "Fairway start note to (yds)", L.FairwayStartMaxYards);
        Add("startWidth", "Fairway start: width (yds)", L.FairwayStartWidthYards);
        Add("startHold", "Fairway start: held for (yds)", L.FairwayStartHoldYards);
        Add("corridor", "Corridor (yds)", L.CorridorYards);
        Add("aim", "Aim points apart (yds)", L.AimSpacingYards);
        Add("drop", "Largest drop (ft)", L.MaxDropFeet);
        Add("climb", "Largest climb (ft)", L.MaxClimbFeet);
        Add("rangeMin", "Plot range from (ft)", L.PlotRangeMinFeet);
        Add("rangeMax", "Plot range to (ft)", L.PlotRangeMaxFeet);
        Add("reliefMin", "Green relief from (ft)", L.GreenReliefMinFeet);
        Add("reliefMax", "Green relief to (ft)", L.GreenReliefMaxFeet);
        Add("parMin", "Total par from (TGC)", L.ParMin);
        Add("parMax", "Total par to (TGC)", L.ParMax);
        Add("sameParSpread", "Same-par spread at least (yds)", L.SameParSpreadYards);
        Add("pinEdge", "Pin from edge at least (yds; 3 grid squares)", L.PinEdgeYards);
        Add("pinSteep", "Pin slope at the cup under (%)", L.PinSteepPct);
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
            DesignDriveMinYards = Get("teeMin") ?? L.DesignDriveMinYards,
            DesignDriveMaxYards = Get("teeMax") ?? L.DesignDriveMaxYards,
            TestDriveYards = Get("drive", optional: true),
            MedianPar4MinYards = Get("medianFour") ?? L.MedianPar4MinYards,
            ShortPar4Yards = Get("shortPar4") ?? L.ShortPar4Yards,
            MaxShortPar4s = (int)(Get("maxShort") ?? L.MaxShortPar4s),
            ShortestPar5MinYards = Get("shortFive") ?? L.ShortestPar5MinYards,
            BackTeeMinYards = Get("backMin") ?? L.BackTeeMinYards,
            BackTeeMaxYards = Get("backMax") ?? L.BackTeeMaxYards,
            LandingTargetMinYards = Get("targetMin") ?? L.LandingTargetMinYards,
            LandingTargetMaxYards = Get("targetMax") ?? L.LandingTargetMaxYards,
            WidthReasonYards = Get("reason") ?? L.WidthReasonYards,
            WidthHighYards = Get("widthHigh") ?? L.WidthHighYards,
            WidthLowYards = Get("widthLow") ?? L.WidthLowYards,
            WidthSteadinessMax = Get("steady") ?? L.WidthSteadinessMax,
            LandingHazardReachYards = Get("hazReach") ?? L.LandingHazardReachYards,
            LandingHazardFromYards = Get("hazFrom") ?? L.LandingHazardFromYards,
            LandingHazardToYards = Get("hazTo") ?? L.LandingHazardToYards,
            StraightDiffYards = Get("straight") ?? L.StraightDiffYards,
            ScaleLow = Get("scaleLow") ?? L.ScaleLow,
            ScaleHigh = Get("scaleHigh") ?? L.ScaleHigh,
            FairwayStartMinYards = Get("startMin") ?? L.FairwayStartMinYards,
            FairwayStartMaxYards = Get("startMax") ?? L.FairwayStartMaxYards,
            FairwayStartWidthYards = Get("startWidth") ?? L.FairwayStartWidthYards,
            FairwayStartHoldYards = Get("startHold") ?? L.FairwayStartHoldYards,
            CorridorYards = Get("corridor") ?? L.CorridorYards,
            AimSpacingYards = Get("aim") ?? L.AimSpacingYards,
            MaxDropFeet = Get("drop") ?? L.MaxDropFeet,
            MaxClimbFeet = Get("climb") ?? L.MaxClimbFeet,
            PlotRangeMinFeet = Get("rangeMin") ?? L.PlotRangeMinFeet,
            PlotRangeMaxFeet = Get("rangeMax") ?? L.PlotRangeMaxFeet,
            GreenReliefMinFeet = Get("reliefMin") ?? L.GreenReliefMinFeet,
            GreenReliefMaxFeet = Get("reliefMax") ?? L.GreenReliefMaxFeet,
            ParMin = (int)(Get("parMin") ?? L.ParMin),
            ParMax = (int)(Get("parMax") ?? L.ParMax),
            SameParSpreadYards = Get("sameParSpread") ?? L.SameParSpreadYards,
            PinEdgeYards = Get("pinEdge") ?? L.PinEdgeYards,
            PinSteepPct = Get("pinSteep") ?? L.PinSteepPct,
        };
        if (bad.Count > 0) { Show($"Not applied. Check: {string.Join(", ", bad)}.", error: true); return; }
        _state.Limits = next;
        Save();
        Rerun();
    }

    [RelayCommand] private void ResetLimits()
    {
        _state.Limits = new ReadinessLimits
        {
            TestDriveYards = _state.Limits.TestDriveYards,
            DesignDriveMinYards = _state.Limits.DesignDriveMinYards,
            DesignDriveMaxYards = _state.Limits.DesignDriveMaxYards,
        };
        Save();
        LoadLimits();
        Rerun();
    }

    // ───────────────────────── hole QA ─────────────────────────

    partial void OnMinimumsOnlyChanged(bool value) => RebuildHoleTable();
    partial void OnOpenHolesOnlyChanged(bool value) => RebuildHoleTable();

    [RelayCommand] private void ShowAllChecks() => MinimumsOnly = false;
    [RelayCommand] private void ShowMinimums() => MinimumsOnly = true;
    [RelayCommand] private void ShowAllHoles() => OpenHolesOnly = false;
    [RelayCommand] private void ShowOpenHoles() => OpenHolesOnly = true;

    /// <summary>The groups and checks on show: every check, or only TGC's minimums.</summary>
    List<(string Name, IReadOnlyList<HoleCheck> Checks)> VisibleGroups() =>
        Checklist.HoleQa.Select(g => (g.Name, (IReadOnlyList<HoleCheck>)g.Checks.Where(c => !MinimumsOnly || c.Minimum).ToList()))
            .Where(g => g.Item2.Count > 0).ToList();

    void RebuildHoleTable()
    {
        HoleQaHeader.Clear();
        HoleQaRows.Clear();
        if (_report is null) { Card = null; return; }
        var groups = VisibleGroups();
        for (int i = 0; i < groups.Count; i++) HoleQaHeader.Add(new HoleGroupHeaderViewModel(Checklist.HoleQa.First(x => x.Name == groups[i].Name), groups[i].Checks, i));
        int perHole = groups.Sum(g => g.Checks.Count);
        HoleQaLede = MinimumsOnly
            ? $"The {perHole} TGC published minimums on every hole. Click a square to cycle Open, Done, Deliberate, N/A; click a hole for its card."
            : $"Up to {perHole} checks a hole; a dot marks a TGC published minimum. Dashed green squares are hole design notes: write them on the hole's card. Click a square to cycle Open, Done, Deliberate, N/A; click a hole for its card.";
        foreach (var h in _report.Holes)
        {
            var row = new HoleQaRowViewModel(h, _report, _state, groups.Select(g => g.Checks).ToList(), HoleQaChanged, SelectHole);
            if (OpenHolesOnly && row.AllClosed) continue;
            row.IsSelected = h.Number == _selectedHole;
            HoleQaRows.Add(row);
        }
        BuildCard();
        UpdateHoleQaTotals();
    }

    void SelectHole(int hole)
    {
        _selectedHole = hole;
        foreach (var r in HoleQaRows) r.IsSelected = r.Hole == hole;
        BuildCard();
    }

    void BuildCard()
    {
        var h = _report?.Holes.FirstOrDefault(x => x.Number == _selectedHole) ?? _report?.Holes.FirstOrDefault();
        Card = h is null || _report is null ? null : new HoleCardViewModel(h, _report, _state, VisibleGroups(), HoleQaChanged, Save, Rerun);
    }

    /// <summary>A check changed on a hole: save, and redraw that hole's row and card and the counts.</summary>
    void HoleQaChanged(int hole)
    {
        Save();
        foreach (var r in HoleQaRows.Where(r => r.Hole == hole)) r.Refresh();
        if (Card?.Hole == hole) Card.Refresh();
        UpdateHoleQaTotals();
        RefreshSummary();
        RebuildItemStates();
    }

    void UpdateHoleQaTotals()
    {
        if (_report is null) return;
        var checks = VisibleGroups().SelectMany(g => g.Checks).ToList();
        var statuses = _report.Holes.SelectMany(h => checks.Where(c => c.Applies(h, _report.Limits)).Select(c => Checklist.StatusOf(c, h.Number, _state))).ToList();
        int done = statuses.Count(x => x == ItemStatus.Done), closed = statuses.Count(x => x != ItemStatus.Open);
        HoleQaTotals = $"{done} of {statuses.Count} done · {closed - done} deliberate or N/A";
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
        // The Holes card in Course settings is where the products read tee names; this box shares them.
        if (CourseHolesFile.Load(_outputDir).Tees is { Count: > 0 } tees) _state.Info.TeeNames = [.. tees];
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
        try
        {
            var holes = CourseHolesFile.Load(_outputDir);
            if (!holes.Tees.SequenceEqual(_state.Info.TeeNames))
            {
                holes.TeeNames = [.. _state.Info.TeeNames];
                CourseHolesFile.Save(_outputDir, holes);
            }
        }
        catch (Exception ex) { Show($"Tee names not saved to {CourseHolesFile.FileName}: {ex.Message}", error: true); }
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
        Severity.Warning => (Brushes.White, Brush.Parse("#7A4A00")),
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
