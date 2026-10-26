using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.CourseFormat;
using Pga2K25.Studio.Views;
using Pga2K25.TerrainMap;
using SkiaSharp;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// Routing test: par 3, 4 and 5 arrows of fixed placeholder length dropped and aimed on the map, adding up to a par
/// and approximate yards. Par 4s and 5s bend at fixed points along their length and show landing zones; corridors
/// along every hole flag pairs that crowd each other. One per course, saved as RoutingTest.json in the output folder
/// on every change; clearing deletes it. Holes number in the order they are dropped and can be moved up or down the table.
/// </summary>
public partial class WorkspaceViewModel
{
    const string ArrowId = "route:";

    static readonly Dictionary<int, Color> ParColours = new()
    {
        [3] = Color.Parse("#2B6CA3"),
        [4] = Color.Parse("#C2641C"),
        [5] = Color.Parse("#6A3D8F"),
    };

    List<RoutingArrow> _routing = [];
    RoutingArrow? _selectedArrow;
    int _routingTool;

    static readonly IBrush OverlapBrush = Brush.Parse("#B3261E"), TightBrush = Brush.Parse("#A65F00"), ClearBrush = Brush.Parse("#2F6B3F");

    List<RoutingConflict> _conflicts = [];

    public ObservableCollection<RoutingRowViewModel> RoutingRows { get; } = [];
    public ObservableCollection<RoutingCorridorViewModel> RoutingCorridorRows { get; } = [];
    public ObservableCollection<RoutingDetailViewModel> SelectedHoleLines { get; } = [];
    public ObservableCollection<RoutingCorridorViewModel> SelectedHoleCorridors { get; } = [];
    public IReadOnlyList<RoutingLegendViewModel> RoutingLegend { get; } =
        RoutingPar.All.Select(p => new RoutingLegendViewModel(new SolidColorBrush(ParColours[p.Par]),
            $"Par {p.Par}: {p.Yards} yd, {BendText(p)}. Usual {p.MinYards}–{p.MaxYards}")).ToList();

    public string RoutingZonesText { get; } =
        $"Landing zones are {RoutingZone.WidthYards} yd wide along the line of play: drive 250–300 yd from the tee, par 5 layup 420–470 yd. "
        + $"Corridors are {RoutingCorridors.WidthYards} yd wide, and holes closer than {RoutingCorridors.WidthYards + RoutingCorridors.ClearanceYards} yd, "
        + $"centre line to centre line, are flagged. Back-to-back holes skip the last and first {RoutingCorridors.BackToBackYards} yd.";

    static string BendText(RoutingPar p) => p.BendYards.Count == 0 ? "straight" : $"bends at {string.Join(" and ", p.BendYards)} yd";

    [ObservableProperty] private bool _routingTestOn;
    [ObservableProperty] private bool _showWalks = true;
    [ObservableProperty] private bool _showRanges;
    [ObservableProperty] private bool _showZones = true;
    [ObservableProperty] private bool _showCorridors = true;
    [ObservableProperty] private bool _hasBentArrowSelected;
    [ObservableProperty] private bool _corridorsClear = true;
    [ObservableProperty] private string _selectedHoleTitle = "";
    [ObservableProperty] private string _selectedHoleShape = "";
    [ObservableProperty] private bool _hasRouting;
    [ObservableProperty] private bool _hasArrowSelected;
    [ObservableProperty] private bool _confirmClearRouting;
    [ObservableProperty] private string _routingCount = "";
    [ObservableProperty] private string _routingTotalPar = "0";
    [ObservableProperty] private string _routingTotalYards = "0";
    [ObservableProperty] private string _routingMix = "";
    [ObservableProperty] private string _routingNines = "";
    [ObservableProperty] private string _routingWalks = "";

    public bool PlacingPar3 { get => _routingTool == 3; set => PickRoutingTool(3, value); }
    public bool PlacingPar4 { get => _routingTool == 4; set => PickRoutingTool(4, value); }
    public bool PlacingPar5 { get => _routingTool == 5; set => PickRoutingTool(5, value); }

    void PickRoutingTool(int par, bool on)
    {
        if (on) SetRoutingTool(par);
        else if (_routingTool == par) SetRoutingTool(0);
    }

    void SetRoutingTool(int par)
    {
        _routingTool = par;
        OnPropertyChanged(nameof(PlacingPar3));
        OnPropertyChanged(nameof(PlacingPar4));
        OnPropertyChanged(nameof(PlacingPar5));
        if (par > 0)
        {
            PlacingNote = false;
            MarkingNotWater = false;
            RoutingTestOn = true;
            HoverText = $"Par {par}: press where the tee goes and drag toward the green";
        }
        LayersChanged?.Invoke();
    }

    partial void OnRoutingTestOnChanged(bool value)
    {
        if (!value) { SetRoutingTool(0); SelectArrow(null); ConfirmClearRouting = false; }
        LayersChanged?.Invoke();
    }

    partial void OnShowWalksChanged(bool value) => LayersChanged?.Invoke();
    partial void OnShowRangesChanged(bool value) => LayersChanged?.Invoke();
    partial void OnShowZonesChanged(bool value) => LayersChanged?.Invoke();
    partial void OnShowCorridorsChanged(bool value) => LayersChanged?.Invoke();

    void LoadRoutingTest()
    {
        _routing = RoutingTest.Load(OutputFolder);
        RoutingTestOn = _routing.Count > 0;
        RebuildRoutingRows();
    }

    void SaveRouting()
    {
        try { RoutingTest.Save(OutputFolder, _routing); }
        catch (Exception ex) { Status = $"Routing test not saved: {ex.Message}"; }
        RebuildRoutingRows();
    }

    RoutingTally Tally() => RoutingTally.Of(_routing, _survey is { } s ? s.Feet : null);

    static string Feet(double? f) => f is not { } v ? "—" : Math.Abs(v) < 0.5 ? "level" : $"{(v > 0 ? "+" : "−")}{Math.Abs(v):0} ft";

    void RebuildRoutingRows()
    {
        var tally = Tally();
        _conflicts = RoutingCorridors.Conflicts(_routing);
        RoutingRows.Clear();
        foreach (var h in tally.Holes)
        {
            var arrow = h.Arrow;
            RoutingRows.Add(new RoutingRowViewModel(h, new SolidColorBrush(ParColours[arrow.Par]), arrow == _selectedArrow, h.Number == 1, h.Number == tally.Holes.Count,
                select: () => SelectArrow(arrow), move: d => MoveHole(arrow, d), remove: () => RemoveArrow(arrow)));
        }
        HasRouting = _routing.Count > 0;
        RoutingCount = $"{_routing.Count} of 18 holes";
        RoutingTotalPar = tally.Par.ToString();
        RoutingTotalYards = tally.Yards.ToString("N0");
        RoutingMix = string.Join("   ", RoutingPars.Select(p => $"{tally.Count(p)} × par {p}"));
        RoutingNines = tally.Holes.Count > 9 ? $"Out: par {tally.OutPar}, {tally.OutYards:N0} yd   ·   In: par {tally.InPar}, {tally.InYards:N0} yd" : "";
        RoutingWalks = tally.Holes.Count > 1
            ? $"Green-to-tee walks: {tally.WalkYards:N0} yd in all, longest {tally.LongestWalkYards} yd"
            : "Walks show once two holes are down.";
        HasArrowSelected = _selectedArrow is not null;
        HasBentArrowSelected = _selectedArrow is { IsStraight: false };

        RoutingCorridorRows.Clear();
        foreach (var c in _conflicts)
        {
            var later = _routing[c.HoleB - 1];
            RoutingCorridorRows.Add(new RoutingCorridorViewModel(c.Overlap ? OverlapBrush : TightBrush, c.Overlap
                ? $"Holes {c.HoleA} and {c.HoleB} overlap: {c.Yards} yd apart"
                : $"Holes {c.HoleA} and {c.HoleB} are tight: {c.Yards} yd apart, {c.ClearYards} yd between corridors", () => SelectArrow(later)));
        }
        CorridorsClear = _conflicts.Count == 0;

        SelectedHoleLines.Clear();
        SelectedHoleCorridors.Clear();
        if (tally.Holes.FirstOrDefault(h => h.Arrow == _selectedArrow) is { } sel)
        {
            SelectedHoleTitle = $"Hole {sel.Number} · Par {sel.Arrow.Par} · {sel.Yards} yd";
            SelectedHoleShape = sel.Arrow.ShapeLong;
            foreach (var z in sel.Zones)
                SelectedHoleLines.Add(new RoutingDetailViewModel($"{z.Zone.Name} {z.Zone.FromYards}–{z.Zone.ToYards} yd", Feet(z.RiseFeet)));
            SelectedHoleLines.Add(new RoutingDetailViewModel("Green", Feet(sel.RiseFeet)));
            foreach (var c in _conflicts.Where(c => c.HoleA == sel.Number || c.HoleB == sel.Number))
            {
                var other = _routing[(c.HoleA == sel.Number ? c.HoleB : c.HoleA) - 1];
                int number = c.HoleA == sel.Number ? c.HoleB : c.HoleA;
                SelectedHoleCorridors.Add(new RoutingCorridorViewModel(c.Overlap ? OverlapBrush : TightBrush, c.Overlap
                    ? $"Hole {number}: corridors overlap, {c.Yards} yd apart"
                    : $"Hole {number}: tight, {c.Yards} yd apart", () => SelectArrow(other)));
            }
            if (SelectedHoleCorridors.Count == 0)
                SelectedHoleCorridors.Add(new RoutingCorridorViewModel(ClearBrush, "Corridor clear of other holes", () => { }));
        }
        LayersChanged?.Invoke();
    }

    static readonly int[] RoutingPars = [3, 4, 5];

    void SelectArrow(RoutingArrow? arrow)
    {
        _selectedArrow = arrow;
        RebuildRoutingRows();
    }

    RoutingArrow? ArrowFor(string id) =>
        id.StartsWith(ArrowId, StringComparison.Ordinal) && int.TryParse(id.AsSpan(ArrowId.Length), out int i) && i >= 0 && i < _routing.Count
            ? _routing[i] : null;

    // ───────────────────── map events (bitmap pixels) ─────────────────────

    Vec2 World(Point px) => _projection!.Unmap(new SKPoint((float)px.X, (float)px.Y));

    double BearingAt(Point px, Vector direction) => RoutingArrow.BearingOf(World(px + direction * 50) - World(px));

    public void ArrowPlaced(Point tee, Vector? direction)
    {
        if (_projection is null || _routingTool == 0) return;
        var w = World(tee);
        var arrow = new RoutingArrow
        {
            Par = _routingTool,
            X = Math.Round(w.X, 2),
            Z = Math.Round(w.Z, 2),
            Bearing = Math.Round(direction is { } d ? BearingAt(tee, d) : 0, 1),
        };
        _routing.Add(arrow);
        _selectedArrow = arrow;
        SaveRouting();
    }

    public void ArrowClicked(string id) => SelectArrow(ArrowFor(id));

    public void ArrowMoved(string id, Point tee)
    {
        if (_projection is null || ArrowFor(id) is not { } arrow) return;
        var w = World(tee);
        (arrow.X, arrow.Z) = (Math.Round(w.X, 2), Math.Round(w.Z, 2));
        _selectedArrow = arrow;
        SaveRouting();
    }

    /// <summary>Leg 0 aims the whole arrow; a later leg sets the turn at the bend before it.</summary>
    public void ArrowAimed(string id, int leg, Vector direction)
    {
        if (_projection is null || ArrowFor(id) is not { } arrow || leg < 0 || leg >= arrow.LegCount) return;
        double bearing = BearingAt(Px(arrow.Points[leg]), direction);
        if (leg == 0) arrow.Bearing = Math.Round(bearing, 1);
        else arrow.SetTurn(leg - 1, bearing - arrow.LegBearing(leg - 1));
        _selectedArrow = arrow;
        SaveRouting();
    }

    /// <summary>Clears the arrow selection on a plain click away from the arrows.</summary>
    void DeselectArrow()
    {
        if (_selectedArrow is not null) SelectArrow(null);
    }

    // ───────────────────── what the map draws ─────────────────────

    Point Px(Vec2 w) { var p = _projection!.Map(w); return new Point(p.X, p.Y); }

    /// <summary>An arrow in map pixels, its turns measured on the bitmap; given its hole, the zones carry their rise.</summary>
    MapArrow ToMapArrow(RoutingArrow a, int index, RoutingHole? hole = null)
    {
        var pts = a.Points.Select(Px).ToList();
        var legs = new List<double>();
        var heads = new List<Vector>();
        for (int k = 0; k + 1 < pts.Count; k++)
        {
            Vector v = pts[k + 1] - pts[k];
            legs.Add(v.Length);
            heads.Add(v.Length > 1e-9 ? v / v.Length : new Vector(0, -1));
        }
        var turns = Enumerable.Range(1, heads.Count - 1).Select(k => MapArrow.TurnBetween(heads[k - 1], heads[k])).ToList();
        var p = a.Placeholder;
        double perYard = Math.Max(legs.Sum(), 1e-6) / p.Yards;
        var zones = p.Zones.Select((z, i) => new MapZone(z.FromYards * perYard, z.ToYards * perYard,
            hole?.Zones[i].RiseFeet is { } f ? Feet(f) : "")).ToList();
        return new MapArrow(ArrowId + index, pts[0], heads[0], legs, turns, p.MinYards * perYard, p.MaxYards * perYard,
            ParColours[a.Par], (index + 1).ToString(), zones, RoutingCorridors.WidthYards * perYard, RoutingZone.WidthYards * perYard);
    }

    public IReadOnlyList<MapArrow> CurrentArrows()
    {
        if (!RoutingTestOn || _projection is null) return [];
        var tally = Tally();
        return _routing.Select((a, i) => ToMapArrow(a, i, tally.Holes[i])).ToList();
    }

    public IReadOnlyList<MapConflict> CurrentConflicts() =>
        !RoutingTestOn || _projection is null ? [] : _conflicts.Select(c => new MapConflict(Px(c.A), Px(c.B), c.Overlap, $"{c.Yards} yd")).ToList();

    public IReadOnlyList<MapWalk> CurrentWalks()
    {
        if (!RoutingTestOn || !ShowWalks || _projection is null) return [];
        var tally = RoutingTally.Of(_routing);
        return tally.Holes.Where(h => h.WalkYards is not null)
            .Select(h => new MapWalk(Px(h.Arrow.Green), Px(_routing[h.Number].Tee), $"{h.WalkYards} yd")).ToList();
    }

    public string? SelectedArrowId => _selectedArrow is { } a && RoutingTestOn ? ArrowId + _routing.IndexOf(a) : null;

    /// <summary>The par being placed, as an arrow of the right length and colour at the map's scale.</summary>
    public MapArrow? CurrentArrowTemplate() =>
        _routingTool == 0 || _projection is null ? null : ToMapArrow(new RoutingArrow { Par = _routingTool }, _routing.Count);

    // ───────────────────── commands ─────────────────────

    [RelayCommand] private void TurnArrowLeft() => TurnArrow(-15);
    [RelayCommand] private void TurnArrowRight() => TurnArrow(15);

    void TurnArrow(double degrees)
    {
        if (_selectedArrow is not { } a) return;
        a.Bearing = ((a.Bearing + degrees) % 360 + 360) % 360;
        SaveRouting();
    }

    [RelayCommand] private void StraightenArrow()
    {
        if (_selectedArrow is not { } a) return;
        a.Straighten();
        SaveRouting();
    }

    [RelayCommand] private void DeleteArrow()
    {
        if (_selectedArrow is { } a) RemoveArrow(a);
    }

    void RemoveArrow(RoutingArrow arrow)
    {
        _routing.Remove(arrow);
        if (_selectedArrow == arrow) _selectedArrow = null;
        SaveRouting();
    }

    /// <summary>Swaps a hole with the one before (−1) or after (+1): the arrows stay put, the numbers change.</summary>
    void MoveHole(RoutingArrow arrow, int step)
    {
        int i = _routing.IndexOf(arrow), j = i + step;
        if (i < 0 || j < 0 || j >= _routing.Count) return;
        (_routing[i], _routing[j]) = (_routing[j], _routing[i]);
        SaveRouting();
    }

    [RelayCommand] private void AskClearRouting() => ConfirmClearRouting = true;
    [RelayCommand] private void CancelClearRouting() => ConfirmClearRouting = false;

    [RelayCommand] private void ClearRouting()
    {
        _routing.Clear();
        _selectedArrow = null;
        ConfirmClearRouting = false;
        SetRoutingTool(0);
        SaveRouting();
        Status = "Routing test cleared";
    }
}

public sealed partial class RoutingRowViewModel(RoutingHole h, IBrush dot, bool selected, bool first, bool last,
    Action select, Action<int> move, Action remove) : ObservableObject
{
    public string Number { get; } = h.Number.ToString();
    public IBrush Dot { get; } = dot;
    public string Par { get; } = h.Arrow.Par.ToString();
    public string Yards { get; } = h.Yards.ToString();
    public string Shape { get; } = h.Arrow.ShapeShort;
    public bool IsBent { get; } = !h.Arrow.IsStraight;
    public string Rise { get; } = h.RiseFeet is not { } f ? "—" : Math.Abs(f) < 0.5 ? "level" : $"{(f > 0 ? "+" : "−")}{Math.Abs(f):0} ft";
    public string Walk { get; } = h.WalkYards is { } w ? $"{w} yd" : "—";
    public bool IsSelected { get; } = selected;
    public IBrush Background { get; } = selected ? Brush.Parse("#E3EDF5") : Brushes.Transparent;
    public bool CanMoveUp { get; } = !first;
    public bool CanMoveDown { get; } = !last;
    public string UpName { get; } = $"Make hole {h.Number} hole {h.Number - 1}";
    public string DownName { get; } = $"Make hole {h.Number} hole {h.Number + 1}";
    public string RemoveName { get; } = $"Remove hole {h.Number}";
    [RelayCommand] private void Select() => select();
    [RelayCommand] private void MoveUp() => move(-1);
    [RelayCommand] private void MoveDown() => move(1);
    [RelayCommand] private void Remove() => remove();
}

public sealed record RoutingLegendViewModel(IBrush Dot, string Text);

/// <summary>A label and value on the selected hole's card: a landing zone or the green, with its rise.</summary>
public sealed record RoutingDetailViewModel(string Label, string Value);

/// <summary>A corridor warning, or the selected hole's all-clear; clicking selects the hole it names.</summary>
public sealed partial class RoutingCorridorViewModel(IBrush dot, string text, Action select) : ObservableObject
{
    public IBrush Dot { get; } = dot;
    public string Text { get; } = text;
    [RelayCommand] private void Select() => select();
}
