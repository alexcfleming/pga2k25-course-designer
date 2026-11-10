using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// The order and the one open card of the terrain map's right-hand panel. Cards sit in their home order (hole,
/// sketches, notes, map options). A tool that is switched on (routing test, planting, a selected note) is "worked on":
/// its card jumps to the top and opens, the most recent first. When it is switched off its card leaves, and the first
/// card still showing opens. Clicking a folded card opens it in place. Every other card is a one-line summary.
/// </summary>
public sealed class PanelStack(IReadOnlyList<string> home, Func<string, bool> available)
{
    readonly List<string> _working = [];
    /// <summary>The user picked the open card; until then it follows the first card showing (the hole, once one is selected).</summary>
    bool _chosen;
    public string? Open { get; private set; } = home.FirstOrDefault();

    /// <summary>Worked-on cards (most recent first), then the rest in home order.</summary>
    public IReadOnlyList<string> Order => _working.Concat(home.Where(k => !_working.Contains(k))).ToList();
    public bool IsWorking(string key) => _working.Contains(key);

    /// <summary>A tool card switched on: to the top, open.</summary>
    public void Work(string key)
    {
        _working.Remove(key);
        _working.Insert(0, key);
        Open = key;
        _chosen = false;
    }

    /// <summary>A tool card switched off: it leaves the top, and if it was open the first card showing opens.</summary>
    public void Stop(string key)
    {
        _working.Remove(key);
        if (Open == key) { Open = null; _chosen = false; }
        Settle();
    }

    /// <summary>A folded card clicked: it opens where it is (or folds, when it was the open one).</summary>
    public void Toggle(string key) { Open = Open == key ? null : key; _chosen = true; }

    /// <summary>Opens <paramref name="key"/> without moving it (an import started, a note being placed).</summary>
    public void Show(string key) { Open = key; _chosen = true; }

    /// <summary>When the open card has gone (no hole selected, say), the first card showing opens.</summary>
    public void Settle()
    {
        var first = Order.FirstOrDefault(available);
        if (Open is not null && available(Open) && (_chosen || Open == first)) return;
        if (Open is null && _chosen) return;   // the user folded every card
        Open = first;
    }
}

/// <summary>One card of the right-hand panel: open (its full card) or folded to a one-line summary.</summary>
public partial class PanelCardViewModel(string key, string title, string icon, Action<string> toggle) : ObservableObject
{
    public string Key { get; } = key;
    public string Title { get; } = title;
    public string Icon { get; } = icon;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _isWorking;
    [ObservableProperty] private int _row;
    public bool ShowsFull => IsAvailable && IsOpen;
    public bool ShowsFolded => IsAvailable && !IsOpen;
    partial void OnIsAvailableChanged(bool value) { OnPropertyChanged(nameof(ShowsFull)); OnPropertyChanged(nameof(ShowsFolded)); }
    partial void OnIsOpenChanged(bool value) { OnPropertyChanged(nameof(ShowsFull)); OnPropertyChanged(nameof(ShowsFolded)); }
    [RelayCommand] private void Toggle() => toggle(Key);
}

public partial class WorkspaceViewModel
{
    public const string HoleCardKey = "hole", RoutingCardKey = "routing", PlantingCardKey = "planting", NoteCardKey = "note",
        SketchesCardKey = "sketches", NotesCardKey = "notes", MapCardKey = "map";

    PanelStack _panel = null!;
    public PanelCardViewModel HoleCard { get; private set; } = null!;
    public PanelCardViewModel RoutingCard { get; private set; } = null!;
    public PanelCardViewModel PlantingCard { get; private set; } = null!;
    public PanelCardViewModel NoteCard { get; private set; } = null!;
    public PanelCardViewModel SketchesCard { get; private set; } = null!;
    public PanelCardViewModel NotesCard { get; private set; } = null!;
    public PanelCardViewModel MapCard { get; private set; } = null!;
    IEnumerable<PanelCardViewModel> Cards => [HoleCard, RoutingCard, PlantingCard, NoteCard, SketchesCard, NotesCard, MapCard];

    /// <summary>Workspace properties that change a card's summary or whether it shows.</summary>
    static readonly HashSet<string> PanelInputs =
    [
        nameof(HasHole), nameof(HoleTitle), nameof(HoleParYards), nameof(TeeFeet), nameof(GreenFeet),
        nameof(RoutingCount), nameof(RoutingTotalPar), nameof(RoutingTotalYards), nameof(PlantingTrees), nameof(HasPlanting),
        nameof(SelectedNote), nameof(SketchSummary), nameof(NotesSummary), nameof(OptSummary),
    ];

    void InitPanel()
    {
        void Toggle(string key) { _panel.Toggle(key); SyncPanel(); }
        HoleCard = new(HoleCardKey, "Hole", "", Toggle);
        RoutingCard = new(RoutingCardKey, "Routing test", "", Toggle);
        PlantingCard = new(PlantingCardKey, "Planting", "", Toggle);
        NoteCard = new(NoteCardKey, "Note", "", Toggle);
        SketchesCard = new(SketchesCardKey, "Hole sketches", "", Toggle);
        NotesCard = new(NotesCardKey, "Notes", "", Toggle);
        MapCard = new(MapCardKey, "Map options", "", Toggle);
        _panel = new PanelStack([HoleCardKey, SketchesCardKey, NotesCardKey, MapCardKey], CardShows);
        if (RoutingTestOn) _panel.Work(RoutingCardKey);
        if (ShowPlanting) _panel.Work(PlantingCardKey);
        PropertyChanged += OnPanelInput;
        SyncPanel();
    }

    bool CardShows(string key) => key switch
    {
        HoleCardKey => HasHole,
        RoutingCardKey => RoutingTestOn,
        PlantingCardKey => ShowPlanting,
        NoteCardKey => HasSelectedNote,
        _ => true,
    };

    void OnPanelInput(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RoutingTestOn): Work(RoutingCardKey, RoutingTestOn); return;
            case nameof(ShowPlanting): Work(PlantingCardKey, ShowPlanting); return;
            case nameof(SelectedNote): Work(NoteCardKey, HasSelectedNote); return;
            case nameof(PlacingNote): if (PlacingNote) { _panel.Show(NotesCardKey); SyncPanel(); } return;
        }
        if (e.PropertyName is { } name && PanelInputs.Contains(name)) SyncPanel();
    }

    void Work(string key, bool on)
    {
        if (on) _panel.Work(key); else _panel.Stop(key);
        SyncPanel();
    }

    /// <summary>Opens the sketches card where it is (an import has started).</summary>
    void ShowSketchesCard() { _panel.Show(SketchesCardKey); SyncPanel(); }

    void SyncPanel()
    {
        _panel.Settle();
        var order = _panel.Order.ToList();
        foreach (var c in Cards)
        {
            c.IsAvailable = CardShows(c.Key);
            c.IsOpen = _panel.Open == c.Key;
            c.IsWorking = _panel.IsWorking(c.Key);
            int i = order.IndexOf(c.Key);
            c.Row = i >= 0 ? i : order.Count;   // a tool card that's off sits below the rest, hidden
        }
        HoleCard.Summary = HasHole ? $"{HoleTitle} · {HoleParYards} · tee {TeeFeet} → green {GreenFeet}" : "";
        RoutingCard.Summary = $"{RoutingCount} · par {RoutingTotalPar} · ≈ {RoutingTotalYards} yds";
        PlantingCard.Summary = HasPlanting ? PlantingTrees : "Nothing planted yet";
        NoteCard.Summary = SelectedNote is { } n ? (n.Text.Length > 60 ? n.Text[..57] + "…" : n.Text.Length > 0 ? n.Text : "New note") : "";
        SketchesCard.Summary = SketchSummary;
        NotesCard.Summary = NotesSummary;
        MapCard.Summary = OptSummary;
    }
}
