using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// The simulator's navigation: a shared bar (hole, tee, pin, player) over three sections. Set up is done once per hole
/// (Hole, Assumptions, Live checks, Terrain) and shows how far along it is; Shots plays the shot types the hole's par
/// has (Drive, Second shot, Approach or Tee shot with its Play/Map switch, Green); Hole plays whole holes (Summary,
/// Experiments). Each section remembers the tab it was left on.
/// </summary>
public partial class HoleSimViewModel
{
    public const string SetupSection = "Set up", ShotsSection = "Shots", HoleSection = "Hole";

    readonly Dictionary<string, string> _lastStepIn = new() { [SetupSection] = ReadBackStep, [ShotsSection] = RunStep, [HoleSection] = SummaryStep };
    bool _updatingNav;

    public ObservableCollection<TabViewModel> SectionButtons { get; } = [];
    public ObservableCollection<TabViewModel> SubButtons { get; } = [];
    public ObservableCollection<TabViewModel> ApproachModeButtons { get; } = [];
    public ObservableCollection<int> HoleNumbers { get; } = [];

    [ObservableProperty] private string _setupProgress = "";
    [ObservableProperty] private string _sectionHint = "";
    [ObservableProperty] private int _selectedHoleNumber;

    public bool IsApproachGroup => IsApproach || IsApproachMap;
    public bool IsSummary => Step == SummaryStep;
    public bool IsExperiments => Step == ExperimentsStep;

    partial void OnSelectedHoleNumberChanged(int value)
    {
        if (_updatingNav || value <= 0 || value == _hole) return;
        SelectHole(value);
    }

    static string SectionOf(string step) => step switch
    {
        ReadBackStep or AssumptionsStep or LiveChecksStep or GateStep => SetupSection,
        SummaryStep or ExperimentsStep or RecordStep => HoleSection,
        _ => ShotsSection,
    };

    bool[] SetupDone() =>
    [
        ReadBackAccepted,
        _sheet.AllConfirmed,
        _live.Readings.Count > 0 || _live.Drives.Count > 0,
        Mode == TerrainMode.On,
    ];

    List<(string Step, string Label)> SubsOf(string section)
    {
        var done = SetupDone();
        string Tick(int i, string label) => done[i] ? "✓ " + label : label;
        int par = _model?.Par ?? 4;
        return section switch
        {
            SetupSection => [(ReadBackStep, Tick(0, "Hole")), (AssumptionsStep, Tick(1, "Assumptions")), (LiveChecksStep, Tick(2, "Live checks")), (GateStep, Tick(3, "Terrain"))],
            HoleSection => [(SummaryStep, "Summary"), (ExperimentsStep, "Experiments"), (RecordStep, OpenFindings > 0 ? $"Record · {OpenFindings} to check" : "Record")],
            _ => par == 3 ? [(ApproachStep, "Tee shot"), (GreenStep, "Green")]
                : par >= 5 ? [(RunStep, "Drive"), (SecondShotStep, "Second shot"), (ApproachStep, "Approach"), (GreenStep, "Green")]
                : [(RunStep, "Drive"), (ApproachStep, "Approach"), (GreenStep, "Green")],
        };
    }

    /// <summary>Rebuilds the section and tab buttons for the current step, hole and setup state.</summary>
    void UpdateNav()
    {
        if (_updatingNav) return;
        _updatingNav = true;
        try
        {
            string section = SectionOf(Step);
            var subs = SubsOf(section);
            // A step the hole doesn't have (Drive or Second shot on a par 3, say): go to the section's first tab.
            string current = Step == ApproachMapStep ? ApproachStep : Step;
            if (!subs.Any(s => s.Step == current)) { _updatingNav = false; Step = subs[0].Step; return; }
            _lastStepIn[section] = Step;
            int done = SetupDone().Count(d => d);
            SetupProgress = $"{done} of 4";
            SectionButtons.Clear();
            foreach (var s in new[] { SetupSection, ShotsSection, HoleSection })
            {
                string sec = s;
                string label = s == SetupSection ? $"Set up  ·  {done} of 4" : s;
                SectionButtons.Add(new TabViewModel(label, s == section, () => GoSection(sec)));
            }
            SubButtons.Clear();
            foreach (var (step, label) in subs) { string st = step; SubButtons.Add(new TabViewModel(label, step == current, () => Step = st)); }
            ApproachModeButtons.Clear();
            ApproachModeButtons.Add(new TabViewModel("Play a shot", Step == ApproachStep, () => Step = ApproachStep));
            ApproachModeButtons.Add(new TabViewModel("Map", Step == ApproachMapStep, () => Step = ApproachMapStep));
            SectionHint = section == SetupSection ? "Done once per hole. Shots and Hole stay open, but warn while anything here is unconfirmed."
                : done < 4 ? $"Set up is {done} of 4: {string.Join(", ", new[] { "Hole", "Assumptions", "Live checks", "Terrain" }.Where((_, i) => !SetupDone()[i]))} still to do, so results carry warnings."
                : "";
            if (HoleNumbers.Count > 0 && SelectedHoleNumber != _hole) SelectedHoleNumber = _hole;
        }
        finally { _updatingNav = false; }
    }

    void GoSection(string section)
    {
        var subs = SubsOf(section);
        string last = _lastStepIn.TryGetValue(section, out var l) ? l : subs[0].Step;
        string check = last == ApproachMapStep ? ApproachStep : last;
        Step = subs.Any(s => s.Step == check) ? last : subs[0].Step;
    }

    void BuildHoleNumbers()
    {
        _updatingNav = true;
        try
        {
            HoleNumbers.Clear();
            foreach (var h in _course!.Holes) HoleNumbers.Add(h.Number);
            SelectedHoleNumber = _hole;
        }
        finally { _updatingNav = false; }
    }
}
