using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.Studio.Services;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// The workspace's left menu: the terrain map on its own at the top, then folding groups (Design and check, Products,
/// Course setup, Recent), each item with an icon. Products carry a dot for built, older than the course file's last
/// save, or not built. The menu folds to an icon rail (the terrain map gets the width), where Products, Course setup and
/// Recent open as pop-out lists. Which groups are folded, and the rail, are remembered in the app settings.
/// </summary>
public partial class WorkspaceViewModel
{
    public const string DesignGroup = "Design and check", ProductsGroup = "Products", SetupGroup = "Course setup", RecentGroup = "Recent";

    /// <summary>Glyphs in Segoe Fluent Icons (Windows 11), which keeps Segoe MDL2 Assets' code points (Windows 10).</summary>
    public static class Glyph
    {
        public const string Back = "", Terrain = "", Simulator = "", Measure = "", Readiness = "",
            Sheets = "", Cards = "", Greens = "", Yardage = "", Illustrations = "",
            Estimate = "", Scorecard = "", Products = "", Settings = "", EstimateInputs = "",
            Recent = "", Collapse = "", Expand = "", Open = "", Shut = "";
    }

    static readonly Dictionary<string, string> Icons = new()
    {
        [TerrainSection] = Glyph.Terrain, [HoleSimSection] = Glyph.Simulator, [MeasurementsSection] = Glyph.Measure,
        [ReadinessSection] = Glyph.Readiness, [SettingsSection] = Glyph.Settings, [EstimateInputsSection] = Glyph.EstimateInputs,
        ["Hole sheets"] = Glyph.Sheets, ["Course cards"] = Glyph.Cards, ["Greens book"] = Glyph.Greens, ["Yardage book"] = Glyph.Yardage,
        ["Hole illustrations"] = Glyph.Illustrations, [EstimateProduct] = Glyph.Estimate, ["Scorecard map"] = Glyph.Scorecard,
    };

    public string BackGlyph => Glyph.Back;

    public static string IconFor(string label) => Icons.TryGetValue(label, out var g) ? g : Glyph.Sheets;

    public NavItemViewModel TerrainNav { get; private set; } = null!;
    public NavGroupViewModel Design { get; private set; } = null!;
    public NavGroupViewModel ProductNav { get; private set; } = null!;
    public NavGroupViewModel Setup { get; private set; } = null!;
    public NavGroupViewModel RecentNav { get; private set; } = null!;

    IEnumerable<NavItemViewModel> AllNav => new[] { TerrainNav }.Concat(Design.Items).Concat(ProductNav.Items).Concat(Setup.Items);
    NavItemViewModel? Nav(string label) => AllNav.FirstOrDefault(n => n.Label == label);

    [ObservableProperty] private bool _menuCollapsed;
    public double MenuWidth => MenuCollapsed ? 60 : 248;
    public string MenuToggleGlyph => MenuCollapsed ? Glyph.Expand : Glyph.Collapse;
    public string MenuToggleText => MenuCollapsed ? "Show the menu" : "Fold the menu";

    partial void OnMenuCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(MenuWidth));
        OnPropertyChanged(nameof(MenuToggleGlyph));
        OnPropertyChanged(nameof(MenuToggleText));
        if (Settings.MenuCollapsed == value) return;
        Settings.MenuCollapsed = value;
        Settings.Save();
    }

    [RelayCommand] private void ToggleMenu() => MenuCollapsed = !MenuCollapsed;

    void InitMenu()
    {
        NavItemViewModel Item(string label) => new(label, label == Section, () => Section = label) { Icon = IconFor(label) };
        NavGroupViewModel Group(string title, string icon, IEnumerable<NavItemViewModel> items) =>
            new(title, icon, items, !Settings.ClosedMenuGroups.Contains(title), open =>
            {
                bool changed = open ? Settings.ClosedMenuGroups.Remove(title) : !Settings.ClosedMenuGroups.Contains(title);
                if (!open && changed) Settings.ClosedMenuGroups.Add(title);
                if (changed) Settings.Save();
            });

        TerrainNav = Item(TerrainSection);
        Design = Group(DesignGroup, Glyph.Readiness, new[] { HoleSimSection, MeasurementsSection, ReadinessSection }.Select(Item));
        ProductNav = Group(ProductsGroup, Glyph.Products, Products.All.Where(p => p.Name != TerrainSection).Select(p => Item(p.Name)));
        Setup = Group(SetupGroup, Glyph.Settings, new[] { SettingsSection, EstimateInputsSection }.Select(Item));
        RecentNav = Group(RecentGroup, Glyph.Recent, []);
        _menuCollapsed = Settings.MenuCollapsed;
        RefreshProductSections();
    }

    /// <summary>Each product's dot and page count, and the Products heading's "n of m built"; the open viewer reloads after a build.</summary>
    void RefreshProductSections()
    {
        var saved = File.Exists(Entry.Path) ? File.GetLastWriteTime(Entry.Path) : DateTime.MinValue;
        foreach (var nav in ProductNav.Items)
        {
            var p = Products.All.First(x => x.Name == nav.Label);
            int pages = Products.PagesOf(p, _place).Count;
            nav.Detail = pages == 0 ? "" : $"{pages} page{(pages == 1 ? "" : "s")}";
            nav.State = Products.StateOf(p, _place, saved);
        }
        int built = ProductNav.Items.Count(n => n.State != ProductState.NotBuilt);
        int stale = ProductNav.Items.Count(n => n.State == ProductState.OlderThanSave);
        ProductNav.Summary = $"{built} of {ProductNav.Items.Count} built";
        ProductNav.Tip = (stale == 0 ? ProductNav.Summary : $"{ProductNav.Summary}, {stale} older than the course file's last save")
            + ". Dots: green built, amber built before the course file's last save, hollow not built.";
        if (ProductViewer is { } v)
            ProductViewer = NewViewer(v.Product);
    }

    /// <summary>Publish readiness's open checks as a badge on its menu item, once the page has run its checks.</summary>
    void WatchReadiness(ReadinessViewModel readiness)
    {
        void Update()
        {
            if (Nav(ReadinessSection) is not { } nav) return;
            nav.Badge = readiness.OpenChecks > 0 ? readiness.OpenChecks.ToString() : "";
            nav.Detail = readiness.OpenChecks > 0 ? $"{readiness.OpenChecks} to fix or mark deliberate" : "";
        }
        readiness.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ReadinessViewModel.OpenChecks)) Update(); };
        Update();
    }
}

/// <summary>A folding group in the workspace menu; <paramref name="opened"/> remembers the fold.</summary>
public partial class NavGroupViewModel : ObservableObject
{
    readonly Action<bool> _opened;
    public string Title { get; }
    public string Heading => Title.ToUpperInvariant();
    public string Icon { get; }
    public ObservableCollection<NavItemViewModel> Items { get; }
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _tip = "";
    public string Chevron => IsOpen ? WorkspaceViewModel.Glyph.Open : WorkspaceViewModel.Glyph.Shut;
    /// <summary>Lit (in the rail) when the selected page is in this group.</summary>
    public bool HasSelection => Items.Any(i => i.IsSelected);

    public NavGroupViewModel(string title, string icon, IEnumerable<NavItemViewModel> items, bool open, Action<bool> opened)
    {
        Title = title; Icon = icon; _isOpen = open; _opened = opened;
        Items = new(items);
        foreach (var i in Items) i.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(NavItemViewModel.IsSelected)) OnPropertyChanged(nameof(HasSelection)); };
    }

    partial void OnIsOpenChanged(bool value) { OnPropertyChanged(nameof(Chevron)); _opened(value); }
    [RelayCommand] private void Toggle() => IsOpen = !IsOpen;
}

public partial class NavItemViewModel(string label, bool selected, Action select) : ObservableObject
{
    static readonly IBrush BuiltBrush = Brush.Parse("#3B8B54"), StaleBrush = Brush.Parse("#D9A033"), NotBuiltBrush = Brush.Parse("#9AA39D");

    public string Label { get; } = label;
    public string Icon { get; init; } = "";
    [ObservableProperty] private bool _isSelected = selected;
    /// <summary>A quiet count on the right (measurements); for products, the page count in the tooltip.</summary>
    [ObservableProperty] private string _detail = "";
    /// <summary>A highlighted count on the right (readiness checks open); empty for none.</summary>
    [ObservableProperty] private string _badge = "";
    /// <summary>Products only: built, older than the last save, or not built (null for other pages).</summary>
    [ObservableProperty] private ProductState? _state;

    public bool HasBadge => Badge.Length > 0;
    public bool HasState => State is not null;
    public bool ShowsDetail => State is null && Badge.Length == 0 && Detail.Length > 0;
    public IBrush StateFill => State switch { ProductState.Built => BuiltBrush, ProductState.OlderThanSave => StaleBrush, _ => Brushes.Transparent };
    public IBrush StateEdge => State == ProductState.NotBuilt ? NotBuiltBrush : Brushes.Transparent;
    public string Tip => State switch
    {
        ProductState.Built => $"{Label}: built" + (Detail.Length > 0 ? $", {Detail}" : ""),
        ProductState.OlderThanSave => $"{Label}: built before the course file was last saved" + (Detail.Length > 0 ? $", {Detail}" : ""),
        ProductState.NotBuilt => $"{Label}: not built yet",
        _ => Badge.Length > 0 ? $"{Label}: {Detail}" : Detail.Length > 0 ? $"{Label} ({Detail})" : Label,
    };

    partial void OnDetailChanged(string value) { OnPropertyChanged(nameof(ShowsDetail)); OnPropertyChanged(nameof(Tip)); }
    partial void OnBadgeChanged(string value) { OnPropertyChanged(nameof(HasBadge)); OnPropertyChanged(nameof(ShowsDetail)); OnPropertyChanged(nameof(Tip)); }
    partial void OnStateChanged(ProductState? value)
    {
        OnPropertyChanged(nameof(HasState)); OnPropertyChanged(nameof(ShowsDetail)); OnPropertyChanged(nameof(StateFill));
        OnPropertyChanged(nameof(StateEdge)); OnPropertyChanged(nameof(Tip));
    }
    [RelayCommand] private void Select() => select();
}
