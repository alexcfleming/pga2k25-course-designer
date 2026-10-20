using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.CourseFormat;
using Pga2K25.Studio.Services;
using Pga2K25.TerrainMap;
using SkiaSharp;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// One course: a live terrain map (rendered by the same code as the printed map), the selected hole's
/// figures against any in-game measurements, and the products with build / open actions.
/// </summary>
public partial class WorkspaceViewModel : ObservableObject
{
    /// <summary>Viewer resolution. The survey's contour thinning depends on it, so survey and render share it.</summary>
    const double ViewerPixelsPerMetre = 2;

    readonly MainViewModel _main;
    public CourseEntry Entry { get; }
    StudioSettings Settings => _main.Settings;

    TerrainSurvey? _survey;
    TerrainMapRenderer? _renderer;
    SKRect _mapRect;
    Pga2K25.Maps.MapProjection? _projection;
    List<MeasuredLeg> _measured = [];
    int _selectedHole;

    [ObservableProperty] private WriteableBitmap? _mapImage;
    [ObservableProperty] private bool _loading = true;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = "Rebuilding the terrain from the course's brushes…";
    [ObservableProperty] private string _hoverText = "Hover over the map for ground height and slope";
    [ObservableProperty] private string _shading = "Relief";
    [ObservableProperty] private bool _showSurfaces = true;
    [ObservableProperty] private bool _showGrid = true;
    [ObservableProperty] private bool _showHilltops = true;
    [ObservableProperty] private string _modelCheck = "";
    [ObservableProperty] private bool _hasModelCheck;
    [ObservableProperty] private string _seaText = "";

    // Selected hole.
    [ObservableProperty] private bool _hasHole;
    [ObservableProperty] private string _holeTitle = "";
    [ObservableProperty] private string _holeParYards = "";
    [ObservableProperty] private string _teeFeet = "";
    [ObservableProperty] private string _greenFeet = "";
    [ObservableProperty] private string _changeFeet = "";
    [ObservableProperty] private string _siteText = "";
    [ObservableProperty] private double _tiltBarWidth;
    [ObservableProperty] private IBrush _tiltBarBrush = Brush.Parse("#2D6A42");
    [ObservableProperty] private bool _hasLegs;

    public ObservableCollection<HoleButtonViewModel> Holes { get; } = [];
    public ObservableCollection<LegRowViewModel> Legs { get; } = [];

    /// <summary>Ponds and creeks the selected hole plays over or beside.</summary>
    public ObservableCollection<WaterRowViewModel> WaterRows { get; } = [];
    [ObservableProperty] private bool _hasWater;
    public ObservableCollection<ProductRowViewModel> ProductRows { get; } = [];
    public ObservableCollection<string> Log { get; } = [];
    public IReadOnlyList<string> Shadings { get; } = ["Relief", "Slope", "Plain"];

    public string CourseName => Entry.Name;
    public string CourseMeta => Entry.Holes == 0 ? "Blank plot · no holes yet" : $"{Entry.Holes} holes · par {Entry.Par} · {Entry.Yards:N0} yds";

    /// <summary>Raised to centre the viewer on a page point (bitmap pixels).</summary>
    public event Action<Point>? CentreOn;

    public WorkspaceViewModel(MainViewModel main, CourseEntry entry)
    {
        _main = main;
        Entry = entry;
        RefreshProducts();
        InitSections();
        LoadTerrainOptions();
        LoadLayerSettings();
        LoadNotes();
        LoadSketches();
        Task.Run(LoadSurvey);
    }

    /// <summary>The course's saved terrain options at the viewer's resolution.</summary>
    TerrainMapOptions ViewerOptions() => Terrain.ToOptions(Settings.Credit, ViewerPixelsPerMetre);

    void LoadSurvey()
    {
        try
        {
            var course = CourseFile.Load(Entry.Path).ReadModel();
            // Hilltops are always surveyed; the layer toggle only decides whether they are drawn.
            var survey = TerrainSurvey.Build(course, ViewerOptions() with { ShowSummits = true });
            _measured = Measurements.Load(Measurements.PathFor(Settings, Entry.Name));
            Dispatcher.UIThread.Post(() =>
            {
                _survey = survey;
                SetSheetLayouts(survey);
                SeaText = survey.SeaLevelMetres is null ? "No sea on the sheet" : "Elevations above sea level";
                Holes.Clear();
                foreach (var h in survey.Holes)
                    Holes.Add(new HoleButtonViewModel(h.Hole.Number, n => SelectHole(n, centre: true)));
                UpdateModelCheck();
                HoleNumbers.Clear();
                foreach (var h in survey.Holes) HoleNumbers.Add(h.Hole.Number);
                NewHole = HoleNumbers.FirstOrDefault() is int first and > 0 ? first : null;
                RebuildMeasurementRows();
                Status = $"{survey.Contours.Count:N0} contour lines every {survey.IntervalFeet:0.##} ft";
                Render();
                int keep = Holes.Any(h => h.Number == _selectedHole) ? _selectedHole : Holes.FirstOrDefault()?.Number ?? 0;
                if (keep > 0) SelectHole(keep, centre: false);
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => { Loading = false; Status = $"Could not read the course: {ex.Message}"; });
        }
    }

    void Render()
    {
        if (_survey is null) return;
        var survey = _survey;
        var options = ViewerOptions();
        Loading = true;
        Task.Run(() =>
        {
            var renderer = new TerrainMapRenderer(options);
            var size = renderer.PageSize(survey);
            var info = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            renderer.Draw(surface.Canvas, survey);
            var bitmap = new WriteableBitmap(new PixelSize(size.Width, size.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var fb = bitmap.Lock())
            using (var pixmap = surface.PeekPixels())
                pixmap.ReadPixels(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul), fb.Address, fb.RowBytes, 0, 0);
            var (map, projection) = renderer.MapFrame(survey);
            Dispatcher.UIThread.Post(() =>
            {
                _renderer?.Dispose();
                _renderer = renderer;
                _mapRect = map;
                _projection = projection;
                MapImage = bitmap;
                Loading = false;
                LayersChanged?.Invoke();
            });
        });
    }


    partial void OnShadingChanged(string value) { SaveToolbar(); Render(); }
    partial void OnShowSurfacesChanged(bool value) { SaveToolbar(); Render(); }
    partial void OnShowGridChanged(bool value) { SaveToolbar(); Render(); }
    partial void OnShowHilltopsChanged(bool value) { SaveToolbar(); Render(); }

    /// <summary>Ground height, slope and grid square under a bitmap pixel.</summary>
    public void Hover(Point px)
    {
        if (_survey is null || _projection is null || _renderer is null) return;
        var p = new SKPoint((float)px.X, (float)px.Y);
        if (!_mapRect.Contains(p)) { HoverText = "Off the map"; return; }
        var w = _projection.Unmap(p);
        string grid = _renderer.GridReference(_survey, p) is { } g ? $" · {g}" : "";
        HoverText = (PlacingNote ? "Click to pin the note · " : "") + $"{_survey.Feet(w):0} ft · slope {_survey.SlopePct(w):0.0} %{grid}";
    }

    void SelectHole(int number, bool centre)
    {
        if (_survey?.Holes.FirstOrDefault(h => h.Hole.Number == number) is not { } h) return;
        _selectedHole = number;
        foreach (var b in Holes) b.IsSelected = b.Number == number;
        HasHole = true;
        HoleTitle = $"Hole {number}";
        HoleParYards = $"Par {h.Hole.Par} · {h.Yards} yds";
        TeeFeet = $"{h.TeeFeet:0} ft";
        GreenFeet = $"{h.GreenFeet:0} ft";
        ChangeFeet = Math.Abs(h.ChangeFeet) < 0.5 ? "level" : $"{(h.ChangeFeet > 0 ? "+" : "−")}{Math.Abs(h.ChangeFeet):0} ft";
        SiteText = $"{h.GreenSiteGradePct:0.0} % tilt · ±{h.GreenSiteReliefFeet:0.0} ft";
        TiltBarWidth = Math.Min(1, h.GreenSiteGradePct / 15) * 240;
        TiltBarBrush = Brush.Parse(h.GreenSiteGradePct > 8 ? "#B87514" : "#2D6A42");

        Legs.Clear();
        // The file's own route waypoints: readings are taken from the route's tee marker, not necessarily the back tee.
        var r = h.Hole.Route;
        foreach (var m in _measured.Where(m => m.Hole == number))
        {
            var (a, b) = m.TeeToAim ? (r[0], r[1]) : (r[^2], r[^1]);
            double model = _survey.Feet(b) - _survey.Feet(a);
            Legs.Add(new LegRowViewModel((m.TeeToAim ? "Tee → aim · " : "Aim → green · ") + $"{m.Yards} yd", m.Feet, model));
        }
        HasLegs = Legs.Count > 0;

        WaterRows.Clear();
        foreach (var w in HoleWater.Along(_survey, h)) WaterRows.Add(new WaterRowViewModel(w));
        HasWater = WaterRows.Count > 0;

        LayersChanged?.Invoke();
        if (centre && HoleView) RequestFrame(number);
        else if (centre && _projection is not null)
        {
            var p = _projection.Map(new Vec2((r[0].X + r[^1].X) / 2, (r[0].Z + r[^1].Z) / 2));
            CentreOn?.Invoke(new Point(p.X, p.Y));
        }
    }

    void UpdateModelCheck()
    {
        if (_survey is null || _measured.Count == 0) { HasModelCheck = false; return; }
        double sum = 0;
        int n = 0;
        foreach (var m in _measured)
        {
            if (_survey.Holes.FirstOrDefault(h => h.Hole.Number == m.Hole) is not { } h) continue;
            var r = h.Hole.Route;
            var (a, b) = m.TeeToAim ? (r[0], r[1]) : (r[^2], r[^1]);
            double e = _survey.Feet(b) - _survey.Feet(a) - m.Feet;
            sum += e * e;
            n++;
        }
        HasModelCheck = n > 0;
        ModelCheck = n > 0 ? $"Model check · {Math.Sqrt(sum / n):0.0} ft RMS over {n} measured legs" : "";
    }

    // ───────────────────────── products ─────────────────────────

    void RefreshProducts()
    {
        ProductRows.Clear();
        foreach (var p in Products.All)
        {
            string file = Products.FileFor(p, Settings, Entry.Name);
            ProductRows.Add(new ProductRowViewModel(p.Name, File.Exists(file) ? File.GetLastWriteTime(file) : null,
                view: () => Section = p.Name, build: () => BuildAsync([p])));
        }
    }

    [RelayCommand] private Task BuildAll() => BuildAsync(Products.All);

    async Task BuildAsync(IReadOnlyList<ProductDefinition> products)
    {
        if (Busy) return;
        Busy = true;
        string outDir = Products.OutputDir(Settings, Entry.Name);
        var progress = new Progress<string>(line => { Log.Add(line); Status = line; });
        foreach (var p in products)
        {
            Log.Add($"— {p.Name} —");
            var ctx = new BuildContext(Entry.Path, outDir, Settings, Settings.ExtrasFor(Entry.Path), progress);
            try { await Task.Run(() => p.Build(ctx)); }
            catch (Exception ex) { Log.Add($"{p.Name} failed: {ex.Message}"); Status = $"{p.Name} failed: {ex.Message}"; }
        }
        Busy = false;
        RefreshProducts();
        RefreshProductSections();
        Status = "Done";
    }

    [RelayCommand] private void OpenOutputFolder()
    {
        string dir = Products.OutputDir(Settings, Entry.Name);
        Directory.CreateDirectory(dir);
        OpenPath(dir);
    }

    [RelayCommand] private void BackToLibrary() => _main.ShowLibrary();

    void OpenPath(string path)
    {
        if (ShellOpen.Open(path) is { } problem) Status = problem;
    }
}

public partial class HoleButtonViewModel(int number, Action<int> select) : ObservableObject
{
    public int Number { get; } = number;
    [ObservableProperty] private bool _isSelected;
    [RelayCommand] private void Select() => select(Number);
}

/// <summary>A measured leg against the model: game, model and how far apart they are.</summary>
/// <summary>One pond or creek in the hole inspector: what the line of play does with it, and where.</summary>
public sealed class WaterRowViewModel(HoleWaterRow w)
{
    public string Label { get; } = (w.Crosses ? "Carry over water" : $"Water {w.Side}") + $" · {w.LevelFeet:0} ft";
    public string Detail { get; } = w.Crosses
        ? (w.CarryYards - w.FrontYards < 3 ? $"{w.FrontYards}" : $"{w.FrontYards}–{w.CarryYards}") + $" yd · {w.ToGreenYards} to green"
        : $"{w.OffsetYards} yd off at {w.FrontYards} yd";
}

public sealed class LegRowViewModel(string label, double game, double model)
{
    static string Signed(double v) => (v > 0.05 ? "+" : v < -0.05 ? "−" : "") + Math.Abs(v).ToString("0.0");
    public string Label { get; } = label;
    public string Game { get; } = Signed(game);
    public string Model { get; } = Signed(model);
    public string Diff { get; } = Signed(model - game) + " ft";
    public IBrush DiffBrush { get; } = Brush.Parse(Math.Abs(model - game) > 4 ? "#8A5300" : "#1F4D30");
}

public sealed partial class ProductRowViewModel(string name, DateTime? built, Action view, Func<Task> build) : ObservableObject
{
    public string Name { get; } = name;
    public string StatusText { get; } = built is { } t ? $"Built {t:d MMM HH:mm}" : "Not built";
    public IBrush StatusBrush { get; } = Brush.Parse(built is null ? "#56605A" : "#1F4D30");
    public bool CanOpen { get; } = built is not null;
    public string BuildLabel { get; } = built is null ? "Build" : "Rebuild";
    [RelayCommand] private void View() => view();
    [RelayCommand] private Task Build() => build();
}
