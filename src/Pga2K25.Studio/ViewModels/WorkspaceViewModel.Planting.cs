using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Pga2K25.CourseFormat;
using Pga2K25.Studio.Services;
using Pga2K25.TerrainMap;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// Planting: what the designer placed (trees, bushes, grass, flowers, walls), drawn into the live map by the same
/// renderer as the printed sheets. The toggles are saved with the course's terrain options as they change; the
/// display ones redraw. Vegetation the game generates by itself isn't in the file and isn't shown.
/// </summary>
public partial class WorkspaceViewModel
{
    static readonly IBrush TreeBrush = Brush.Parse("#4F7F3A"), ConiferBrush = Brush.Parse("#25523F"), BroadleafBrush = Brush.Parse("#7FA65E"),
        BushBrush = Brush.Parse("#5B7A30"), GrassBrush = Brush.Parse("#A88B45"), FlowerBrush = Brush.Parse("#C0477A"), WallBrush = Brush.Parse("#4A4F4B");

    public ObservableCollection<PlantingRowViewModel> PlantingRows { get; } = [];
    public ObservableCollection<PlantingRowViewModel> TreeSpeciesRows { get; } = [];

    [ObservableProperty] private bool _hasPlanting;
    [ObservableProperty] private string _plantingTrees = "";
    [ObservableProperty] private string _plantingTreesSplit = "";
    [ObservableProperty] private string _plantingSpeciesCount = "";

    public bool ShowPlanting { get => Terrain.ShowPlanting; set => SetPlanting(t => t.ShowPlanting = value, redraw: true); }
    public bool PlantTrees { get => Terrain.PlantTrees; set => SetPlanting(t => t.PlantTrees = value, redraw: Terrain.ShowPlanting); }
    public bool PlantBushes { get => Terrain.PlantBushes; set => SetPlanting(t => t.PlantBushes = value, redraw: Terrain.ShowPlanting); }
    public bool PlantGrass { get => Terrain.PlantGrass; set => SetPlanting(t => t.PlantGrass = value, redraw: Terrain.ShowPlanting); }
    public bool PlantFlowers { get => Terrain.PlantFlowers; set => SetPlanting(t => t.PlantFlowers = value, redraw: Terrain.ShowPlanting); }
    public bool PlantWalls { get => Terrain.PlantWalls; set => SetPlanting(t => t.PlantWalls = value, redraw: Terrain.ShowPlanting); }
    public bool TreesByType { get => Terrain.TreesByType; set => SetPlanting(t => t.TreesByType = value, redraw: Terrain.ShowPlanting); }
    public bool PrintPlantingOnMap { get => Terrain.PrintPlantingOnMap; set => SetPlanting(t => t.PrintPlantingOnMap = value, redraw: false); }
    public bool PrintPlantingOnSheets { get => Terrain.PrintPlantingOnSheets; set => SetPlanting(t => t.PrintPlantingOnSheets = value, redraw: false); }

    static readonly string[] PlantingProperties =
    [
        nameof(ShowPlanting), nameof(PlantTrees), nameof(PlantBushes), nameof(PlantGrass), nameof(PlantFlowers), nameof(PlantWalls),
        nameof(TreesByType), nameof(PrintPlantingOnMap), nameof(PrintPlantingOnSheets),
    ];

    void SetPlanting(Action<TerrainSettings> change, bool redraw)
    {
        change(Terrain);
        Settings.Save();
        RaisePlanting();
        if (redraw) Render();
    }

    /// <summary>The toggles after the options were loaded or reset.</summary>
    void RaisePlanting()
    {
        foreach (var name in PlantingProperties) OnPropertyChanged(name);
        if (_survey is not null) RebuildPlanting();
    }

    /// <summary>The Planting card: counts per layer and the tree species, from the course file.</summary>
    void RebuildPlanting()
    {
        PlantingRows.Clear();
        TreeSpeciesRows.Clear();
        if (_survey is null) return;
        var p = _survey.Planting;
        HasPlanting = !p.IsEmpty;

        static string Count(int placed, int scattered) => scattered == 0 ? placed.ToString("N0") : $"≈ {placed + scattered:N0}";
        string How(PlantKind k, int placed)
        {
            int groups = p.Groups.Count(g => g.Species.Kind == k), areas = p.Areas.Count(a => a.Species.Kind == k);
            var parts = new List<string> { $"{placed:N0} placed" };
            if (groups > 0) parts.Add($"{groups:N0} group{(groups == 1 ? "" : "s")}");
            if (areas > 0) parts.Add($"{areas:N0} area{(areas == 1 ? "" : "s")}");
            return string.Join(" · ", parts);
        }
        foreach (var (kind, label, brush) in new[]
                 {
                     (PlantKind.Tree, "Trees", TreeBrush), (PlantKind.Bush, "Bushes", BushBrush),
                     (PlantKind.Grass, "Grass & fescue", GrassBrush), (PlantKind.Flower, "Flowers", FlowerBrush),
                 })
        {
            var (placed, scattered) = p.Count(kind);
            PlantingRows.Add(new PlantingRowViewModel(brush, label, How(kind, placed), Count(placed, scattered)));
        }
        PlantingRows.Add(new PlantingRowViewModel(WallBrush, "Walls & fences", "lines", p.Walls.Count.ToString("N0")));

        var (trees, scatteredTrees) = p.Count(PlantKind.Tree);
        PlantingTrees = Count(trees, scatteredTrees);
        PlantingTreesSplit = scatteredTrees == 0 ? "all placed one by one" : $"{trees:N0} placed, ≈ {scatteredTrees:N0} scattered";
        var species = p.TreeSpecies();
        PlantingSpeciesCount = species.Count.ToString();
        foreach (var (sp, count, estimated) in species)
            TreeSpeciesRows.Add(new PlantingRowViewModel(
                !Terrain.TreesByType ? TreeBrush : sp.Form == TreeForm.Conifer ? ConiferBrush : BroadleafBrush,
                sp.Name, sp.Form == TreeForm.Conifer ? "Conifer" : "Broadleaf", (estimated ? "≈ " : "") + count.ToString("N0")));
    }
}

public sealed record PlantingRowViewModel(IBrush Dot, string Label, string How, string Count);
