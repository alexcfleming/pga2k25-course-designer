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
    public string Tip { get; } = c.Minimum ? $"{c.Text} (TGC minimum)" : c.Text;
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

    /// <summary>Every chip for a hole, worst first, from the same limits the course-file checks use.</summary>
    public static List<FlagChipViewModel> For(HoleMeasures h, ReadinessLimits L, ReadinessState s)
    {
        bool Delib(string check) => s.Deliberate.ContainsKey(check) || s.Deliberate.ContainsKey($"{check}@{h.Number}");
        var chips = new List<FlagChipViewModel>();
        void Add(string text, Kind kind, string check) => chips.Add(new FlagChipViewModel(text, kind, Delib(check)));

        if (h.DriveClass == "Drivable") Add("Drivable", Kind.Worse, h.Number <= 9 ? "drivable-front" : "drivable-back");
        else if (h.DriveClass == "Near-drivable") Add("Near-drivable", Kind.Flag, h.Number <= 9 ? "drivable-front" : "drivable-back");
        if (h.LeftAfterDrive is int left && left < L.Par5MinLeftYards) Add($"{left} left", Kind.Worse, "par5");
        if (h.Par >= 4 && !h.SharedFairway)
        {
            if (h.FairwayStartYards is double fs && (fs < L.FairwayStartMinYards || fs > L.FairwayStartMaxYards))
                Add(fs <= 60 ? "Start ≤60" : $"Start {fs:0}", Kind.Provisional, "fairway-start");
            else if (h.FairwayStartYards is null) Add("No main fairway", Kind.Provisional, "fairway-start");
        }
        if (!h.GreenEstimated && h.GreenSquareFeet < L.SmallGreenSquareFeet) Add($"Green {h.GreenSquareFeet.ToString("N0", CultureInfo.InvariantCulture)} sq ft", Kind.Provisional, "small-greens");
        else if (h.GreenRatio is double g && (g < L.ScaleLow || g > L.ScaleHigh)) Add($"Green {g:0.00}×", Kind.Provisional, "green-scale");
        if (!h.SharedFairway)
        {
            if (h.LandingOffFairway) Add("Lands off fairway", Kind.Flag, "landing-width");
            else if (h.WidthRatio is double w && (w < L.ScaleLow || w > L.ScaleHigh)) Add($"Landing {w:0.00}×", Kind.Flag, "landing-width");
            if (h.WidthSteadiness is double sd && sd > L.WidthSteadinessMax) Add($"Width {sd:0.0}×", Kind.Provisional, "width-steadiness");
        }
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

/// <summary>One square of the Hole QA grid: a check on a hole, clicked through Open → Done → Deliberate → N/A.</summary>
public partial class QaCellViewModel : ObservableObject
{
    static readonly IBrush OpenFill = Brushes.White, OpenEdge = Brush.Parse("#8C958E"), DoneFill = Brush.Parse("#2D6A42"),
        DelibFill = Brush.Parse("#3D6E9A"), NaFill = Brush.Parse("#E6E8E3");

    readonly HoleCheck _check;
    readonly int _hole;
    readonly ReadinessState _state;
    readonly Action<int> _changed;

    [ObservableProperty] private IBrush _fill = OpenFill;
    [ObservableProperty] private IBrush _edge = OpenEdge;
    [ObservableProperty] private string _tip = "";

    public QaCellViewModel(HoleCheck check, int hole, ReadinessState state, Action<int> changed)
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
        (Fill, Edge) = s switch
        {
            ItemStatus.Done => (DoneFill, DoneFill),
            ItemStatus.Deliberate => (DelibFill, DelibFill),
            ItemStatus.NotApplicable => (NaFill, OpenEdge),
            _ => (OpenFill, OpenEdge),
        };
        Tip = $"Hole {_hole}, {_check.Text}: {Look.Label(s)}";
    }

    [RelayCommand] private void Cycle()
    {
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

    public HoleQaRowViewModel(HoleMeasures h, ReadinessLimits limits, ReadinessState state, IReadOnlyList<IReadOnlyList<HoleCheck>> groups,
        Action<int> changed, Action<int> select)
    {
        _select = select;
        Hole = h.Number;
        ParYards = $"{h.Par} · {h.Yards}";
        Flags = FlagChipViewModel.For(h, limits, state);
        Groups = groups.Select((g, i) => new QaCellGroupViewModel(g.Select(c => new QaCellViewModel(c, h.Number, state, changed)).ToList(), i)).ToList();
        Refresh();
    }

    public IEnumerable<QaCellViewModel> Cells => Groups.SelectMany(g => g.Cells);
    public bool AllClosed => Cells.All(c => c.Status != ItemStatus.Open);

    public void Refresh()
    {
        foreach (var c in Cells) c.Refresh();
        Count = $"{Cells.Count(c => c.Status != ItemStatus.Open)}/{Cells.Count()}";
    }

    [RelayCommand] private void Select() => _select(Hole);
}

/// <summary>One figure on the hole card, shaded when outside a limit (dashed when the limit is provisional).</summary>
public sealed class MeasureBoxViewModel
{
    public string Label { get; }
    public string Value { get; }
    public IBrush Fill { get; }
    public IBrush Edge { get; }
    public AvaloniaList<double>? Dashes { get; }

    public MeasureBoxViewModel(string label, string value, bool outside, bool provisional = false)
    {
        Label = label;
        Value = value;
        (Fill, Edge) = !outside ? (Brush.Parse("#F7F8F5"), Brush.Parse("#F7F8F5"))
            : provisional ? (Brushes.White, Brush.Parse("#B07A1E")) : (Brush.Parse("#FBEFD9"), Brush.Parse("#FBEFD9"));
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
/// One hole on its own: every figure, the main fairway start (with the designer's override and shared-fairway
/// mark), the pins, every check written out in full by group, and a note.
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
    public string StartFromFile { get; }
    public string StartBand { get; }
    public IReadOnlyList<PinLineViewModel> Pins { get; }
    public bool HasPins => Pins.Count > 0;
    public IReadOnlyList<CardGroupViewModel> Groups { get; }

    [ObservableProperty] private string _overrideText;
    [ObservableProperty] private bool _shared;
    [ObservableProperty] private string _sharedWith;
    [ObservableProperty] private string _note;
    [ObservableProperty] private bool _sizeDeliberate;

    public HoleCardViewModel(HoleMeasures h, ReadinessReport r, ReadinessState state, IReadOnlyList<(string Name, IReadOnlyList<HoleCheck> Checks)> groups,
        Action<int> changed, Action save, Action recheck)
    {
        _h = h;
        _state = state;
        _save = save;
        _recheck = recheck;
        var L = r.Limits;
        string N0(double v) => v.ToString("N0", CultureInfo.InvariantCulture);
        var m = new List<MeasureBoxViewModel>
        {
            new("Test drive", h.DriveClass ?? (h.LeftAfterDrive is int left ? $"{left} left" : h.Par == 3 ? "par 3" : L.TestDriveYards is null ? "no drive entered" : "fine"),
                h.DriveClass is not null || h.LeftAfterDrive < L.Par5MinLeftYards),
            new("Green (provisional)", h.GreenEstimated ? "not drawn" : $"{N0(h.GreenSquareFeet)} sq ft · {h.GreenRatio:0.00}×",
                !h.GreenEstimated && (h.GreenSquareFeet < L.SmallGreenSquareFeet || h.GreenRatio < L.ScaleLow || h.GreenRatio > L.ScaleHigh), provisional: true),
            new("Landing width", h.LandingWidthYards is double w ? $"{w:0}{(h.LandingOffFairway ? " off fairway" : "")} yds · {h.WidthRatio:0.00}×" : h.Par == 3 ? "par 3" : "—",
                !h.SharedFairway && (h.LandingOffFairway || h.WidthRatio < L.ScaleLow || h.WidthRatio > L.ScaleHigh)),
            new("Tee to green", $"{h.ChangeFeet:+0;-0;0} ft", h.ChangeFeet > L.MaxClimbFeet || -h.ChangeFeet > L.MaxDropFeet),
            new("Green relief", h.GreenReliefFeet is double rf ? $"{rf:0.0} ft" : "—",
                h.GreenReliefFeet is double rr && (Math.Round(rr, 1) < L.GreenReliefMinFeet || Math.Round(rr, 1) > L.GreenReliefMaxFeet)),
            new("Nearest other green", h.NearGreenYards is double ny ? $"{ny:0} yds · {(h.NearGreenOf == 0 ? "practice" : $"hole {h.NearGreenOf}")}" : "—",
                h.NearGreenYards is double n2 && Math.Round(n2) < L.CorridorYards),
        };
        if (IsLong)
        {
            m.Add(new("Narrowest fairway (note)", h.NarrowestFairwayYards is double nw ? $"{nw:0} yds" : "—", h.NarrowestFairwayYards < L.FairwayMinWidthYards, provisional: true));
            m.Add(new("Wide ÷ narrow past 200 (provisional)", h.WidthSteadiness is double sd ? $"{sd:0.0}×" : "—", !h.SharedFairway && h.WidthSteadiness > L.WidthSteadinessMax, provisional: true));
        }
        Measures = m;
        StartFromFile = h.FairwayStartFileYards is double fs ? (fs <= 60 ? "60 yds or less from the file" : $"{fs:0} yds from the file") : "No main fairway found along the line";
        StartBand = $"provisional · band {L.FairwayStartMinYards:0}–{L.FairwayStartMaxYards:0} yds";
        Pins = h.Pins.Select(p => new PinLineViewModel(p, L)).ToList();
        Groups = groups.Select(g => new CardGroupViewModel(g.Name, g.Checks.Select(c => new CardItemViewModel(c, h.Number, state, changed)).ToList(), h.Number, changed)).ToList();

        var hs = state.Holes.TryGetValue(h.Number, out var own) ? own : new HoleState();
        _overrideText = hs.FairwayStartYards is double o ? o.ToString("0", CultureInfo.InvariantCulture) : "";
        _shared = hs.SharedFairway;
        _sharedWith = hs.SharedWith;
        _note = hs.Note;
        _sizeDeliberate = state.Deliberate.ContainsKey($"green-scale@{h.Number}");
    }

    public void Refresh() { foreach (var g in Groups) g.Refresh(); }

    partial void OnNoteChanged(string value) { _state.Hole(Hole).Note = value; _save(); }
    partial void OnSharedWithChanged(string value) { _state.Hole(Hole).SharedWith = value; _save(); }

    /// <summary>A shared fairway changes the medians, so the checks run again.</summary>
    partial void OnSharedChanged(bool value) { _state.Hole(Hole).SharedFairway = value; _save(); _recheck(); }

    /// <summary>"Size is deliberate" on this hole, as in the HTML checklist: covers green size and landing width here.</summary>
    partial void OnSizeDeliberateChanged(bool value)
    {
        foreach (var check in new[] { "green-scale", "landing-width" })
        {
            string key = $"{check}@{Hole}";
            if (value) _state.Deliberate.TryAdd(key, new DeliberateMark { Reason = "Size is deliberate on this hole" });
            else _state.Deliberate.Remove(key);
        }
        _save();
        _recheck();
    }

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
