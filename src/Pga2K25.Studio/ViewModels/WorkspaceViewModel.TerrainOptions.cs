using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.Studio.Services;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// The terrain map's options panel. The fields are edited freely and take effect on Apply, which saves them with
/// the course and redraws the live map (re-surveying when the area, contours or water change). Builds of the
/// terrain map and hole sheets use the saved values.
/// </summary>
public partial class WorkspaceViewModel
{
    TerrainSettings Terrain => Settings.ExtrasFor(Entry.Path).Terrain;

    public IReadOnlyList<string> Areas { get; } = [TerrainSettings.Routing, TerrainSettings.WholePlot, TerrainSettings.Buildable];

    [ObservableProperty] private string _optArea = TerrainSettings.Routing;
    [ObservableProperty] private string _optMargin = "";
    [ObservableProperty] private string _optInterval = "";
    [ObservableProperty] private string _optIndex = "";
    [ObservableProperty] private string _optGrid = "";
    [ObservableProperty] private bool _optWater = true;
    [ObservableProperty] private bool _optWaterFootprints;
    [ObservableProperty] private bool _optDrawnGreenSites;
    [ObservableProperty] private string _optPrintPpm = "";
    [ObservableProperty] private string _optError = "";
    [ObservableProperty] private string _optSummary = "";

    public bool OptMarginEnabled => OptArea == TerrainSettings.Routing;
    partial void OnOptAreaChanged(string value) => OnPropertyChanged(nameof(OptMarginEnabled));

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Toolbar state and panel fields from the course's saved options (no redraw).</summary>
    void LoadTerrainOptions()
    {
        var t = Terrain;
        _shading = t.Shading;
        _showSurfaces = t.ShowSurfaces;
        _showGrid = t.ShowGrid;
        _showHilltops = t.ShowHilltops;
        OnPropertyChanged(nameof(Shading));
        OnPropertyChanged(nameof(ShowSurfaces));
        OnPropertyChanged(nameof(ShowGrid));
        OnPropertyChanged(nameof(ShowHilltops));
        OptArea = t.Area;
        OptMargin = t.MarginMetres.ToString("0.##", Inv);
        OptInterval = t.IntervalFeet is double ft ? ft.ToString("0.##", Inv) : "";
        OptIndex = t.IndexEvery.ToString(Inv);
        OptGrid = t.GridYards.ToString("0.##", Inv);
        OptWater = t.ShowWater;
        OptWaterFootprints = t.ShowWaterFootprints;
        OptDrawnGreenSites = t.ShowDrawnGreenSites;
        OptPrintPpm = t.PrintPixelsPerMetre.ToString("0.##", Inv);
        OptError = "";
        UpdateOptSummary();
    }

    void UpdateOptSummary()
    {
        var t = Terrain;
        string area = t.Area == TerrainSettings.Routing ? $"routing + {t.MarginMetres:0} m" : t.Area.ToLowerInvariant();
        string contours = t.IntervalFeet is double ft ? $"{ft:0.##} ft contours" : "contours auto";
        OptSummary = $"{area} · {contours} · grid {(t.ShowGrid ? $"{t.GridYards:0} yd" : "off")} · prints at {t.PrintPixelsPerMetre:0.#} px/m";
    }

    /// <summary>The toolbar's shading and layers are saved as they change, and only redraw.</summary>
    void SaveToolbar()
    {
        var t = Terrain;
        t.Shading = Shading;
        t.ShowSurfaces = ShowSurfaces;
        t.ShowGrid = ShowGrid;
        t.ShowHilltops = ShowHilltops;
        Settings.Save();
        UpdateOptSummary();
    }

    [RelayCommand] private void ApplyOptions()
    {
        OptError = "";
        if (!TryNumber(OptMargin, 0, 2000, out double margin)) { OptError = "Margin should be 0–2000 m."; return; }
        double? interval = null;
        if (!string.IsNullOrWhiteSpace(OptInterval) && !OptInterval.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryNumber(OptInterval, 0.1, 500, out double ft)) { OptError = "Contour interval should be Auto (blank) or 0.1–500 ft."; return; }
            interval = ft;
        }
        if (!int.TryParse(OptIndex, NumberStyles.Integer, Inv, out int index) || index is < 1 or > 20) { OptError = "Index every should be 1–20."; return; }
        if (!TryNumber(OptGrid, 10, 2000, out double grid)) { OptError = "Grid square should be 10–2000 yd."; return; }
        if (!TryNumber(OptPrintPpm, 0.5, 10, out double ppm)) { OptError = "Printed resolution should be 0.5–10 px per metre."; return; }

        var t = Terrain;
        var before = t.SurveyKey;
        t.Area = OptArea;
        t.MarginMetres = margin;
        t.IntervalFeet = interval;
        t.IndexEvery = index;
        t.GridYards = grid;
        t.ShowWater = OptWater;
        t.ShowWaterFootprints = OptWaterFootprints;   // drawing only: a redraw, not a resurvey
        t.ShowDrawnGreenSites = OptDrawnGreenSites;
        t.PrintPixelsPerMetre = ppm;
        Settings.Save();
        UpdateOptSummary();
        if (t.SurveyKey != before) Resurvey();
        else Render();
    }

    [RelayCommand] private void ResetOptions()
    {
        var before = Terrain.SurveyKey;
        Settings.ExtrasFor(Entry.Path).Terrain = new TerrainSettings();
        Settings.Save();
        LoadTerrainOptions();
        if (Terrain.SurveyKey != before) Resurvey();
        else Render();
    }

    void Resurvey()
    {
        Loading = true;
        Status = "Re-surveying the terrain with the new options…";
        Task.Run(LoadSurvey);
    }

    static bool TryNumber(string text, double min, double max, out double value) =>
        double.TryParse(text?.Trim(), NumberStyles.Float, Inv, out value) && value >= min && value <= max;
}
