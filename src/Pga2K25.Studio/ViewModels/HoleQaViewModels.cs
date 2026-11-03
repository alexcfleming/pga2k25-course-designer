using System.Globalization;
using Avalonia.Collections;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.Readiness;

namespace Pga2K25.Studio.ViewModels;

/// <summary>A group's columns over the Hole QA grid: the check labels, turned on end, with a dot under the TGC minimums.</summary>
public sealed class HoleGroupHeaderViewModel(HoleGroup group, IReadOnlyList<HoleCheck> checks, int index)
{
    public string Name { get; } = group.Name;
    /// <summary>Shared with the rows' squares, so a group name wider than its columns widens both alike.</summary>
    public string SizeGroup { get; } = $"g{index}";
    public IReadOnlyList<HoleColumnViewModel> Columns { get; } = checks.Select(c => new HoleColumnViewModel(c)).ToList();
}

public sealed class HoleColumnViewModel(HoleCheck c)
{
    public string Label { get; } = c.Label;
    public string Tip { get; } = c.Minimum ? $"{c.Text} (TGC minimum)" : c.Note ? $"{c.Text} (a note on the hole's card)" : c.Text;
    public bool Minimum { get; } = c.Minimum;
}

/// <summary>
/// A short note beside a hole from its course-file measures, shown only when one is outside a limit. Provisional
/// figures are outlined with a dashed line: warnings only. A figure marked deliberate on the hole shows blue.
/// </summary>
public sealed class FlagChipViewModel
{
    public string Text { get; }
    public IBrush Fill { get; }
    public IBrush Ink { get; }
    public IBrush Edge { get; }
    public AvaloniaList<double>? Dashes { get; }

    public enum Kind { Flag, Worse, Provisional }

    public FlagChipViewModel(string text, Kind kind, bool deliberate)
    {
        Text = text;
        (Fill, Ink, Edge) = deliberate ? (Brush.Parse("#E3EDF5"), Brush.Parse("#1E4A70"), Brush.Parse("#E3EDF5"))
            : kind switch
            {
                Kind.Worse => (Brush.Parse("#F6E0DA"), Brush.Parse("#8E2F1E"), Brush.Parse("#F6E0DA")),
                Kind.Provisional => (Brushes.White, Brush.Parse("#7A4A00"), Brush.Parse("#B07A1E")),
                _ => (Brush.Parse("#FBEFD9"), Brush.Parse("#7A4A00"), Brush.Parse("#FBEFD9")),
            };
        Dashes = kind == Kind.Provisional && !deliberate ? [3, 2] : null;
    }

    /// <summary>Every chip for a hole, from the same limits the course-file checks use.</summary>
    public static List<FlagChipViewModel> For(HoleMeasures h, ReadinessReport r, ReadinessState s)
    {
        var L = r.Limits;
        bool Delib(string check) => s.Deliberate.ContainsKey(check) || s.Deliberate.ContainsKey($"{check}@{h.Number}");
        var chips = new List<FlagChipViewModel>();
        void Add(string text, Kind kind, string check) => chips.Add(new FlagChipViewModel(text, kind, Delib(check)));

        var widths = h.Stations.Select(x => x.WidthYards).ToList();
        if (widths.Where(w => w > L.WidthHighYards).Max() is double hi) Add($"Width {hi:0}", Kind.Provisional, "landing-widths");
        if (h.Stations.FirstOrDefault(x => x.WidthYards is null) is { } none) Add($"No fairway at {none.FromTeeYards}", Kind.Provisional, "landing-widths");
        else if (widths.Where(w => w < L.WidthLowYards).Min() is double lo) Add($"Width {lo:0}", Kind.Provisional, "landing-widths");
        if (h.Par == 5 && h.Yards < L.ShortestPar5MinYards) Add($"Par 5 {h.Yards}", Kind.Provisional, "shortest-par5");
        if (h.Par == 4 && h.Yards < L.ShortPar4Yards && r.Check("par4-under400")?.Severity == Severity.Warning)
            Add($"Under {L.ShortPar4Yards:0}", Kind.Provisional, "par4-under400");
        if (h.WidthSteadiness >= L.WidthSteadinessMax) Add($"Swing {h.WidthSteadiness:0.0}×", Kind.Provisional, "width-steadiness");
        if (h.LayupWidthYards is double lay && h.LandingWidthYards > lay * 1.1)
            Add($"Wider at {L.DesignDriveMinYards:0}–{L.DesignDriveMaxYards:0}", Kind.Provisional, "width-pattern");
        if (h.ChangeFeet > L.MaxClimbFeet || -h.ChangeFeet > L.MaxDropFeet) Add($"{h.ChangeFeet:+0;-0} ft", Kind.Flag, "elevation");
        if (h.GreenReliefFeet is double rf && (Math.Round(rf, 1) < L.GreenReliefMinFeet || Math.Round(rf, 1) > L.GreenReliefMaxFeet)) Add($"Relief {rf:0.0} ft", Kind.Flag, "green-relief");
        if (h.NearGreenYards is double ny && Math.Round(ny) < L.CorridorYards)
            Add(ny < 0.5 ? $"Crosses {Whose(h.NearGreenOf)}" : $"{ny:0} yds to {Whose(h.NearGreenOf)}", ny < 0.5 ? Kind.Worse : Kind.Flag, "corridor");
        var steep = h.Pins.Where(p => p.SlopePct >= L.PinSteepPct).ToList();
        var edge = h.Pins.Where(p => p.EdgeYards < L.PinEdgeYards).ToList();
        if (steep.Count > 0) Add(steep.Count == 1 ? $"Pin {steep[0].Number} steep" : $"{steep.Count} pins steep", Kind.Flag, "pins");
        if (edge.Count > 0) Add(edge.Count == 1 ? $"Pin {edge[0].Number} near edge" : $"{edge.Count} pins near edge", Kind.Flag, "pins");
        return chips;

        static string Whose(int? of) => of == 0 ? "practice green" : $"{of} green";
    }
}

/// <summary>
/// One square of the Hole QA grid: a check on a hole, clicked through Open → Done → Deliberate → N/A. A note square is
/// dashed until the note is written on the hole's card, and clicking it opens the card. A check the hole isn't asked
/// (a par 5 note on a par 4, say) keeps its place but shows nothing.
/// </summary>
public partial class QaCellViewModel : ObservableObject
{
    static readonly IBrush OpenFill = Brushes.White, OpenEdge = Brush.Parse("#8C958E"), DoneFill = Brush.Parse("#2D6A42"),
        DelibFill = Brush.Parse("#3D6E9A"), NaFill = Brush.Parse("#E6E8E3");
    static readonly AvaloniaList<double> Dashed = [2, 1.5];

    readonly HoleCheck _check;
    readonly int _hole;
    readonly ReadinessState _state;
    readonly Action<int> _changed, _select;

    public bool Asked { get; }

    [ObservableProperty] private IBrush _fill = OpenFill;
    [ObservableProperty] private IBrush _edge = OpenEdge;
    [ObservableProperty] private AvaloniaList<double>? _dashes;
    [ObservableProperty] private string _tip = "";

    public QaCellViewModel(HoleCheck check, HoleMeasures h, ReadinessLimits limits, ReadinessState state, Action<int> changed, Action<int> select)
    {
        _check = check;
        _hole = h.Number;
        _state = state;
        _changed = changed;
        _select = select;
        Asked = check.Applies(h, limits);
        Refresh();
    }

    public ItemStatus Status => Checklist.StatusOf(_check, _hole, _state);

    public void Refresh()
    {
        if (!Asked) { (Fill, Edge, Dashes, Tip) = (Brushes.Transparent, Brushes.Transparent, null, ""); return; }
        var s = Status;
        if (_check.Note)
        {
            bool written = s == ItemStatus.Done;
            (Fill, Edge, Dashes) = written ? (DoneFill, DoneFill, null) : (OpenFill, DoneFill, Dashed);
            Tip = $"Hole {_hole}, {_check.Text}: {(written ? "written" : "to write on the hole's card")}";
            return;
        }
        (Fill, Edge, Dashes) = s switch
        {
            ItemStatus.Done => (DoneFill, DoneFill, null),
            ItemStatus.Deliberate => (DelibFill, DelibFill, null),
            ItemStatus.NotApplicable => (NaFill, OpenEdge, Dashed),
            _ => (OpenFill, OpenEdge, (AvaloniaList<double>?)null),
        };
        Tip = $"Hole {_hole}, {_check.Text}: {Look.Label(s)}";
    }

    [RelayCommand] private void Cycle()
    {
        if (!Asked) return;
        if (_check.Note) { _select(_hole); return; }
        Checklist.Set(_check, _hole, Look.Next(Status), _state);
        _changed(_hole);
    }
}

public sealed class QaCellGroupViewModel(IReadOnlyList<QaCellViewModel> cells, int index)
{
    public string SizeGroup { get; } = $"g{index}";
    public IReadOnlyList<QaCellViewModel> Cells { get; } = cells;
}

/// <summary>A hole's row in the Hole QA grid: its figures outside a limit, then every check as a square.</summary>
public partial class HoleQaRowViewModel : ObservableObject
{
    readonly Action<int> _select;

    public int Hole { get; }
    public string ParYards { get; }
    public IReadOnlyList<FlagChipViewModel> Flags { get; }
    public bool Clean => Flags.Count == 0;
    public IReadOnlyList<QaCellGroupViewModel> Groups { get; }
    public string SelectTip => $"Show hole {Hole}'s card";

    [ObservableProperty] private string _count = "";
    [ObservableProperty] private bool _isSelected;

    public HoleQaRowViewModel(HoleMeasures h, ReadinessReport report, ReadinessState state, IReadOnlyList<IReadOnlyList<HoleCheck>> groups,
        Action<int> changed, Action<int> select)
    {
        _select = select;
        Hole = h.Number;
        ParYards = $"{h.Par} · {h.Yards}";
        Flags = FlagChipViewModel.For(h, report, state);
        Groups = groups.Select((g, i) => new QaCellGroupViewModel(g.Select(c => new QaCellViewModel(c, h, report.Limits, state, changed, select)).ToList(), i)).ToList();
        Refresh();
    }

    public IEnumerable<QaCellViewModel> Cells => Groups.SelectMany(g => g.Cells).Where(c => c.Asked);
    public bool AllClosed => Cells.All(c => c.Status != ItemStatus.Open);

    public void Refresh()
    {
        foreach (var c in Groups.SelectMany(g => g.Cells)) c.Refresh();
        Count = $"{Cells.Count(c => c.Status != ItemStatus.Open)}/{Cells.Count()}";
    }

    [RelayCommand] private void Select() => _select(Hole);
}

/// <summary>One figure on the hole card, shaded when outside a limit (dashed when the limit is provisional, green on target).</summary>
public sealed class MeasureBoxViewModel
{
    static readonly IBrush Plain = Brush.Parse("#F7F8F5"), Shade = Brush.Parse("#FBEFD9"), Amber = Brush.Parse("#B07A1E"), Target = Brush.Parse("#E4EFE6");

    public string Label { get; }
    public string Value { get; }
    public IBrush Fill { get; }
    public IBrush Edge { get; }
    public AvaloniaList<double>? Dashes { get; }

    public MeasureBoxViewModel(string label, string value, bool outside, bool provisional = false, bool onTarget = false)
    {
        Label = label;
        Value = value;
        (Fill, Edge) = !outside ? (onTarget ? (Target, Target) : (Plain, Plain)) : provisional ? (Brushes.White, Amber) : (Shade, Shade);
        Dashes = outside && provisional ? [3, 2] : null;
    }
}

public sealed class PinLineViewModel(PinPlacement p, ReadinessLimits L)
{
    static readonly IBrush Shade = Brush.Parse("#FBEFD9"), None = Brushes.Transparent;
    public string Number { get; } = p.Number.ToString(CultureInfo.InvariantCulture);
    public string Where { get; } = $"{p.Depth}{p.Side}";
    public string Edge { get; } = p.EdgeYards is double e ? $"{e:0.0} yds" : "—";
    public IBrush EdgeFill { get; } = p.EdgeYards < L.PinEdgeYards ? Shade : None;
    public string Slope { get; } = p.EdgeYards is null ? "—" : p.SlopePct is double s ? $"{s:0.0}%" : "flat";
    public IBrush SlopeFill { get; } = p.SlopePct >= L.PinSteepPct ? Shade : None;
}

/// <summary>A hole design note on the card: required while the hole is asked it, closed once written.</summary>
public partial class NoteBoxViewModel : ObservableObject
{
    readonly HoleCheck _check;
    readonly int _hole;
    readonly ReadinessState _state;
    readonly Action<int> _changed;

    public string Label => _check.Text;
    public string Hint { get; }

    [ObservableProperty] private string _text;

    public NoteBoxViewModel(HoleCheck check, int hole, string hint, ReadinessState state, Action<int> changed)
    {
        _check = check;
        _hole = hole;
        _state = state;
        _changed = changed;
        Hint = hint;
        _text = state.Holes.TryGetValue(hole, out var h) && h.Notes.TryGetValue(check.Id, out var t) ? t : "";
    }

    partial void OnTextChanged(string value)
    {
        var notes = _state.Hole(_hole).Notes;
        if (string.IsNullOrWhiteSpace(value)) notes.Remove(_check.Id);
        else notes[_check.Id] = value;
        _changed(_hole);
    }
}

/// <summary>A check on the hole card: its full wording and a status pill.</summary>
public partial class CardItemViewModel : ObservableObject
{
    readonly HoleCheck _check;
    readonly int _hole;
    readonly ReadinessState _state;
    readonly Action<int> _changed;

    public string Text => _check.Text;
    public bool Minimum => _check.Minimum;
    public string Tip => _check.Minimum ? "A TGC published minimum" : Checklist.SourceLabel(_check.Source);

    [ObservableProperty] private string _label = "";
    [ObservableProperty] private IBrush _fill = Brushes.White;
    [ObservableProperty] private IBrush _ink = Brushes.Black;
    [ObservableProperty] private IBrush _edge = Brushes.Gray;

    public CardItemViewModel(HoleCheck check, int hole, ReadinessState state, Action<int> changed)
    {
        _check = check;
        _hole = hole;
        _state = state;
        _changed = changed;
        Refresh();
    }

    public ItemStatus Status => Checklist.StatusOf(_check, _hole, _state);

    public void Refresh()
    {
        var s = Status;
        Label = Look.Label(s);
        (Fill, Ink, Edge) = Look.Of(s);
    }

    public void Close() { if (Status == ItemStatus.Open) Checklist.Set(_check, _hole, ItemStatus.Done, _state); }

    [RelayCommand] private void Cycle()
    {
        Checklist.Set(_check, _hole, Look.Next(Status), _state);
        _changed(_hole);
    }
}

public partial class CardGroupViewModel : ObservableObject
{
    readonly int _hole;
    readonly Action<int> _changed;

    public string Name { get; }
    public IReadOnlyList<CardItemViewModel> Items { get; }
    [ObservableProperty] private string _count = "";

    public CardGroupViewModel(string name, IReadOnlyList<CardItemViewModel> items, int hole, Action<int> changed)
    {
        Name = name;
        Items = items;
        _hole = hole;
        _changed = changed;
        Refresh();
    }

    public void Refresh()
    {
        foreach (var i in Items) i.Refresh();
        Count = $"{Items.Count(i => i.Status != ItemStatus.Open)} / {Items.Count}";
    }

    [RelayCommand] private void Finish()
    {
        foreach (var i in Items) i.Close();
        _changed(_hole);
    }
}

/// <summary>
/// One hole on its own: its figures (card and straight-line yards, yards left after the design tee shot, the green
/// against its approach), the landing zone (widths at the design tee shot, lay-up against landing, fairway start,
/// hazards by the line, the designer's fairway start and shared-fairway mark), the hole design notes, the pins, every
/// check written out by group, and a note.
/// </summary>
public partial class HoleCardViewModel : ObservableObject
{
    readonly HoleMeasures _h;
    readonly ReadinessState _state;
    readonly Action _save, _recheck;

    public int Hole => _h.Number;
    public string Title => $"Hole {_h.Number}";
    public string Sub => $"Par {_h.Par} · {_h.Yards} yds";
    public IReadOnlyList<MeasureBoxViewModel> Measures { get; }
    public bool IsLong => _h.Par >= 4;
    public IReadOnlyList<MeasureBoxViewModel> Stations { get; }
    public string WidthLimits { get; }
    public string Pattern { get; }
    public string StartFromFile { get; }
    public IReadOnlyList<string> Hazards { get; }
    public bool NoHazards => Hazards.Count == 0;
    public string HazardsLabel { get; }
    public IReadOnlyList<NoteBoxViewModel> Notes { get; }
    public IReadOnlyList<PinLineViewModel> Pins { get; }
    public bool HasPins => Pins.Count > 0;
    public IReadOnlyList<CardGroupViewModel> Groups { get; }

    [ObservableProperty] private string _overrideText;
    [ObservableProperty] private bool _shared;
    [ObservableProperty] private string _sharedWith;
    [ObservableProperty] private string _note;

    public HoleCardViewModel(HoleMeasures h, ReadinessReport r, ReadinessState state, IReadOnlyList<(string Name, IReadOnlyList<HoleCheck> Checks)> groups,
        Action<int> changed, Action save, Action recheck)
    {
        _h = h;
        _state = state;
        _save = save;
        _recheck = recheck;
        var L = r.Limits;
        string N0(double v) => v.ToString("N0", CultureInfo.InvariantCulture);
        int Left(double drive) => Math.Max(0, h.LeftAfter(drive));
        bool bent = Math.Abs(h.Yards - h.StraightYards) >= L.StraightDiffYards;
        Measures =
        [
            new("Card · straight from the back tee", bent ? $"{h.Yards} · {h.StraightYards} straight" : $"{h.Yards} yds", false),
            IsLong
                ? new($"Yards left after {L.DesignDriveMinYards:0} / {L.DesignDriveMidYards:0} / {L.DesignDriveMaxYards:0}",
                    $"{Left(L.DesignDriveMinYards)} / {Left(L.DesignDriveMidYards)} / {Left(L.DesignDriveMaxYards)}",
                    h.Par == 5 && h.Yards < L.ShortestPar5MinYards, provisional: true)
                : new("Par 3", $"{h.Yards} yds", false),
            new("Green · against the approach", h.GreenEstimated ? "not drawn" : $"{N0(h.GreenSquareFeet)} sq ft" + (IsLong ? $" · {Left(L.DesignDriveMidYards)} left" : ""), false),
            new("Green relief", h.GreenReliefFeet is double rf ? $"{rf:0.0} ft" : "—",
                h.GreenReliefFeet is double rr && (Math.Round(rr, 1) < L.GreenReliefMinFeet || Math.Round(rr, 1) > L.GreenReliefMaxFeet)),
            new("Tee to green", $"{h.ChangeFeet:+0;-0;0} ft", h.ChangeFeet > L.MaxClimbFeet || -h.ChangeFeet > L.MaxDropFeet),
            new("Nearest other green", h.NearGreenYards is double ny ? $"{ny:0} yds · {(h.NearGreenOf == 0 ? "practice" : $"hole {h.NearGreenOf}")}" : "—",
                h.NearGreenYards is double n2 && Math.Round(n2) < L.CorridorYards),
        ];

        Stations = new[] { L.DesignDriveMinYards, L.DesignDriveMidYards, L.DesignDriveMaxYards }.Select(y =>
        {
            var st = h.Stations.FirstOrDefault(x => x.FromTeeYards == (int)Math.Round(y));
            if (st is null) return new MeasureBoxViewModel($"At {y:0}", "short of it", false);
            if (st.WidthYards is not double w) return new MeasureBoxViewModel($"At {y:0}", "no fairway", true, provisional: true);
            bool warn = w > L.WidthHighYards || w < L.WidthLowYards;
            string label = !warn && w > L.WidthReasonYards ? $"At {y:0} · needs a reason" : $"At {y:0}";
            return new MeasureBoxViewModel(label, $"{w:0} yds", warn, provisional: true, onTarget: w >= L.LandingTargetMinYards && w <= L.LandingTargetMaxYards);
        }).ToList();
        WidthLimits = $"Target {L.LandingTargetMinYards:0}–{L.LandingTargetMaxYards:0} yds · reason over {L.WidthReasonYards:0} · warn over {L.WidthHighYards:0} or under {L.WidthLowYards:0}";
        Pattern = h.LayupWidthYards is double lay && h.LandingWidthYards is double land
            ? $"Lay-up (250–290) {lay:0} yds, landing ({L.DesignDriveMinYards:0}–{L.DesignDriveMaxYards:0}) {land:0} yds: " +
              (land > lay * 1.1 ? "wider where it should be tighter (provisional warning)." : "roomy then tighter, as it should be.")
            : "";
        string start = h.FairwayStartFileYards is double fs ? (fs <= 60 ? "60 yds or less" : $"{fs:0} yds") : "";
        StartFromFile = (start.Length > 0 ? $"Main fairway starts {start} from the back tee (a note; band {L.FairwayStartMinYards:0}–{L.FairwayStartMaxYards:0})." : "No main fairway found along the line.") +
                        (h.ForwardCarryYards is double carry ? $" Longest forward-tee carry to it: {carry:0} yds." : "");
        Hazards = h.LandingHazards.Select(x => Capitalise(ReadinessChecker.Describe(x))).ToList();
        HazardsLabel = $"Hazards within {L.LandingHazardReachYards:0} yds of the line, {L.LandingHazardFromYards:0}–{L.LandingHazardToYards:0} yds (a prompt, not scored)";

        string Hint(HoleCheck c) => c.Id switch
        {
            "miss" => "Each side: rough, a hazard, a blocked angle or a harder recovery.",
            "hazards" => h.LandingHazards.Count > 0 ? $"{h.LandingHazards.Count} by the line in the landing zone. One line each; a centreline hazard needs a real answer." : "One line per hazard. A centreline hazard is fine only if the answer is real.",
            "second" => $"{Left(L.DesignDriveMaxYards)} yds left after {L.DesignDriveMaxYards:0}. What does the player weigh?",
            "width" => $"Over {L.WidthReasonYards:0} yds here: for example a central hazard that makes each side play differently.",
            _ => "",
        };
        var all = groups.SelectMany(g => g.Checks).ToList();
        Notes = all.Where(c => c.Note && c.Applies(h, L)).Select(c => new NoteBoxViewModel(c, h.Number, Hint(c), state, changed)).ToList();
        Pins = h.Pins.Select(p => new PinLineViewModel(p, L)).ToList();
        Groups = groups.Select(g => (g.Name, Checks: g.Checks.Where(c => !c.Note && c.Applies(h, L)).ToList()))
            .Where(g => g.Checks.Count > 0)
            .Select(g => new CardGroupViewModel(g.Name, g.Checks.Select(c => new CardItemViewModel(c, h.Number, state, changed)).ToList(), h.Number, changed)).ToList();

        var hs = state.Holes.TryGetValue(h.Number, out var own) ? own : new HoleState();
        _overrideText = hs.FairwayStartYards is double ov ? ov.ToString("0", CultureInfo.InvariantCulture) : "";
        _shared = hs.SharedFairway;
        _sharedWith = hs.SharedWith;
        _note = hs.Note;
    }

    static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public void Refresh() { foreach (var g in Groups) g.Refresh(); }

    partial void OnNoteChanged(string value) { _state.Hole(Hole).Note = value; _save(); }
    partial void OnSharedWithChanged(string value) { _state.Hole(Hole).SharedWith = value; _save(); }

    /// <summary>A shared fairway leaves the fairway-start median, so the checks run again.</summary>
    partial void OnSharedChanged(bool value) { _state.Hole(Hole).SharedFairway = value; _save(); _recheck(); }

    /// <summary>Sets the typed fairway start (blank clears it) and runs the checks again.</summary>
    [RelayCommand] private void ApplyOverride()
    {
        string t = OverrideText.Trim();
        double? v = double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && x >= 0 ? x : null;
        if (t.Length > 0 && v is null) return;
        _state.Hole(Hole).FairwayStartYards = v;
        _save();
        _recheck();
    }
}

/// <summary>Small converters for the Hole QA grid.</summary>
public static class HoleQaLook
{
    static readonly IBrush Selected = Brush.Parse("#E4EFE6");

    /// <summary>The selected hole's row is tinted.</summary>
    public static readonly FuncValueConverter<bool, IBrush> RowFill = new(on => on ? Selected : Brushes.Transparent);

    /// <summary>Shown or kept as a blank of the same size (so the checks' text lines up).</summary>
    public static readonly FuncValueConverter<bool, double> Shown = new(on => on ? 1 : 0);
}
