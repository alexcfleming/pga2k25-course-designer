using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.Maps;
using Pga2K25.Studio.Services;

namespace Pga2K25.Studio.ViewModels;

/// <summary>The workspace's section menu, recent courses, measurements page and per-course settings.</summary>
public partial class WorkspaceViewModel
{
    public const string TerrainSection = "Terrain map", MeasurementsSection = "Measurements", SettingsSection = "Course settings",
        EstimateInputsSection = "Estimate inputs", EstimateProduct = "Construction estimate", ReadinessSection = "Publish readiness",
        HoleSimSection = "Hole design simulator";

    [ObservableProperty] private string _section = TerrainSection;
    public bool IsTerrain => Section == TerrainSection;
    public bool IsMeasurements => Section == MeasurementsSection;
    public bool IsCourseSettings => Section == SettingsSection;
    public bool IsEstimateInputs => Section == EstimateInputsSection;
    public bool IsReadiness => Section == ReadinessSection;
    public bool IsHoleSim => Section == HoleSimSection;
    /// <summary>The height strip (the sea datum) beside the page title: only where heights are read, the terrain map and measurements.</summary>
    public bool ShowsHeightStrip => IsTerrain || IsMeasurements;
    public bool ShowsModelCheck => ShowsHeightStrip && HasModelCheck;
    partial void OnHasModelCheckChanged(bool value) => OnPropertyChanged(nameof(ShowsModelCheck));

    /// <summary>The hole design simulator, made when the section first opens; its save watcher runs only while the section is open.</summary>
    [ObservableProperty] private HoleSimViewModel? _holeSim;

    /// <summary>Publish readiness, made when the section first opens and kept (its checks re-run on Re-read).</summary>
    [ObservableProperty] private ReadinessViewModel? _readiness;

    /// <summary>The estimate's inputs, read fresh from Estimate.json and the price overrides each time the section opens.</summary>
    [ObservableProperty] private EstimateInputsViewModel? _estimateInputs;

    [ObservableProperty] private ProductViewerViewModel? _productViewer;
    public bool IsProductView => ProductViewer is not null;
    partial void OnProductViewerChanged(ProductViewerViewModel? value) => OnPropertyChanged(nameof(IsProductView));
    public ObservableCollection<RecentCourseViewModel> Recent { get; } = [];
    public Bitmap? CourseThumbnail { get; private set; }
    public string CourseKindText => Entry.Kind switch { CourseKind.InProgress => "In progress", CourseKind.Blank => "Blank plot", _ => "Routed" };

    partial void OnSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsTerrain));
        OnPropertyChanged(nameof(IsMeasurements));
        OnPropertyChanged(nameof(IsCourseSettings));
        OnPropertyChanged(nameof(IsEstimateInputs));
        OnPropertyChanged(nameof(IsReadiness));
        OnPropertyChanged(nameof(IsHoleSim));
        OnPropertyChanged(nameof(ShowsHeightStrip));
        OnPropertyChanged(nameof(ShowsModelCheck));
        if (!Busy) Status = "";   // the last page's status line doesn't carry over
        if (value == HoleSimSection) HoleSim ??= new HoleSimViewModel(Entry.Path, Entry.Name, OutputFolder);
        HoleSim?.SetActive(value == HoleSimSection);
        if (value == EstimateInputsSection) OpenEstimateInputs();
        if (value == SettingsSection) { LoadCourseFileNotes(); RefreshSettingsSummaries(); }
        if (value == ReadinessSection && Readiness is null)
        {
            Readiness = new ReadinessViewModel(Entry.Path, OutputFolder, Entry.Name, hole =>
            {
                Section = TerrainSection;
                SelectHole(hole, centre: true);
            });
            WatchReadiness(Readiness);
        }
        foreach (var s in AllNav) s.IsSelected = s.Label == value;
        ProductViewer = Products.All.FirstOrDefault(p => p.Name == value && p.Name != TerrainSection) is { } product
            ? NewViewer(product)
            : null;
    }

    ProductViewerViewModel NewViewer(ProductDefinition product) =>
        new(product, Settings, Entry.Name, () => BuildAsync([product]), product.Name == ScorecardProduct ? ScorecardStyles() : null);

    const string ScorecardProduct = "Scorecard map";

    /// <summary>A tick per scorecard map style, saved for this course; Build and Build all make only the ticked ones.</summary>
    List<StyleChoiceViewModel> ScorecardStyles()
    {
        var picked = Settings.ExtrasFor(Entry.Path).ScorecardThemeKinds();
        return Enum.GetValues<MapThemeKind>().Select(kind => new StyleChoiceViewModel(MapTheme.For(kind).Name, picked.Contains(kind), on =>
        {
            if (!Settings.ExtrasFor(Entry.Path).SetScorecardTheme(kind, on))
            {
                Status = "At least one scorecard map style stays ticked.";
                return false;
            }
            Settings.Save();
            return true;
        })).ToList();
    }

    void InitSections()
    {
        if (Entry.Thumbnail is { } bytes)
        {
            try { CourseThumbnail = Bitmap.DecodeToWidth(new MemoryStream(bytes), 96); } catch (Exception) { CourseThumbnail = null; }
        }
        InitMenu();
        InitPanel();
        InitSettingsCards();
        var extras = Settings.ExtrasFor(Entry.Path);
        CardConfigPath = extras.CardConfig ?? "";
        _bunkerMerge = BunkerMergeLabel(extras.BunkerMergeMetres);
        InitPlace();
        InitHoles();
        InitLogos();

        var recent = Settings.Recent.Where(p => !string.Equals(p, Entry.Path, StringComparison.OrdinalIgnoreCase) && File.Exists(p)).Take(5).ToList();
        Task.Run(() =>
        {
            var entries = recent.Select(CourseLibrary.Read).Where(e => e.Error is null).ToList();
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var e in entries) Recent.Add(new RecentCourseViewModel(e, () => _main.OpenCourse(e)));
            });
        });
    }

    // ───────────────────────── measurements ─────────────────────────

    public ObservableCollection<MeasurementRowViewModel> MeasurementRows { get; } = [];
    public ObservableCollection<int> HoleNumbers { get; } = [];
    public IReadOnlyList<string> LegChoices { get; } = ["Tee → aim", "Aim → green"];
    public string MeasurementsFile => Measurements.PathFor(Settings, Entry.Name);

    [ObservableProperty] private int? _newHole;
    [ObservableProperty] private string _newLeg = "Tee → aim";
    [ObservableProperty] private string _newYards = "";
    [ObservableProperty] private string _newFeet = "";
    [ObservableProperty] private string _measureError = "";
    [ObservableProperty] private string _measurementSummary = "No measurements yet";

    partial void OnNewHoleChanged(int? value) => PrefillYards();
    partial void OnNewLegChanged(string value) => PrefillYards();

    /// <summary>The file's own leg length, so the yards you type can be checked against where you clicked.</summary>
    void PrefillYards()
    {
        if (_survey?.Holes.FirstOrDefault(h => h.Hole.Number == NewHole) is not { } h) return;
        var r = h.Hole.Route;
        var (a, b) = NewLeg == LegChoices[0] ? (r[0], r[1]) : (r[^2], r[^1]);
        NewYards = Math.Round(Pga2K25.CourseFormat.Vec2.Distance(a, b) * CourseLibrary.YardsPerMetre).ToString("0");
    }

    [RelayCommand] private void AddMeasurement()
    {
        MeasureError = "";
        if (NewHole is not int hole) { MeasureError = "Pick a hole."; return; }
        if (!int.TryParse(NewYards, out int yards) || yards <= 0) { MeasureError = "Yards should be a whole number."; return; }
        if (!Measurements.TryParseFeet(NewFeet, out double feet)) { MeasureError = "Height should look like +13'6\", -0'7 or -5.3."; return; }
        _measured.Add(new MeasuredLeg(hole, NewLeg == LegChoices[0], yards, feet));
        Measurements.Save(MeasurementsFile, _measured);
        NewFeet = "";
        MeasurementsChanged();
    }

    void RemoveMeasurement(MeasuredLeg m)
    {
        _measured.Remove(m);
        Measurements.Save(MeasurementsFile, _measured);
        MeasurementsChanged();
    }

    [RelayCommand] private void OpenMeasurementsFile()
    {
        if (File.Exists(MeasurementsFile)) OpenPath(MeasurementsFile);
    }

    void MeasurementsChanged()
    {
        UpdateModelCheck();
        if (_selectedHole > 0) SelectHole(_selectedHole, centre: false);
        RebuildMeasurementRows();
    }

    void RebuildMeasurementRows()
    {
        MeasurementRows.Clear();
        var all = AllMeasured;
        foreach (var m in all.OrderBy(m => m.Hole).ThenBy(m => m.TeeToAim ? 0 : 1))
        {
            double? model = null;
            if (_survey?.Holes.FirstOrDefault(h => h.Hole.Number == m.Hole) is { } h)
            {
                var r = h.Hole.Route;
                var (a, b) = m.TeeToAim ? (r[0], r[1]) : (r[^2], r[^1]);
                model = _survey.Feet(b) - _survey.Feet(a);
            }
            MeasurementRows.Add(new MeasurementRowViewModel(m, model, () => RemoveMeasurement(m)));
        }
        foreach (var l in _fileLines.OrderBy(l => l.Hole == 0 ? int.MaxValue : l.Hole))
            MeasurementRows.Add(new MeasurementRowViewModel(l, _survey is null ? null : _survey.Feet(l.To) - _survey.Feet(l.From)));
        int replaced = _measured.Count - all.Count(m => !m.FromCourseFile);
        int fromFile = _fileLegs.Count + _fileLines.Count;
        string source = fromFile == 0 ? ""
            : $" · {fromFile} from the course file's measure lines" + (replaced > 0 ? $" (replacing {replaced} typed)" : "");
        int total = all.Count + _fileLines.Count;
        MeasurementSummary = total == 0 ? "No measurements yet" : (HasModelCheck ? ModelCheck : $"{total} measurement(s)") + source;
        if (Nav(MeasurementsSection) is { } nav) nav.Detail = total > 0 ? total.ToString() : "";
    }

    // ───────────────────────── course settings ─────────────────────────

    [ObservableProperty] private string _cardConfigPath = "";
    public string OutputFolder => Products.OutputDir(Settings, Entry.Name);
    public string CoursePath => Entry.Path;

    public void SetCardConfig(string? path)
    {
        Settings.ExtrasFor(Entry.Path).CardConfig = string.IsNullOrWhiteSpace(path) ? null : path;
        Settings.Save();
        CardConfigPath = path ?? "";
    }

    void OpenEstimateInputs()
    {
        try
        {
            var holes = HoleNumbers.Count > 0 ? HoleNumbers.ToList()
                : Pga2K25.CourseFormat.CourseFile.Load(Entry.Path).ReadModel().Holes.Select(h => h.Number).ToList();
            var product = Products.All.First(p => p.Name == EstimateProduct);
            EstimateInputs = new EstimateInputsViewModel(Pga2K25.Estimate.EstimateBuilder.InputPath(OutputFolder), holes, async () =>
            {
                await BuildAsync([product]);
                Section = EstimateProduct;   // show the rebuilt report
            });
        }
        catch (Exception ex)
        {
            EstimateInputs = null;
            Status = $"Estimate inputs could not be read: {ex.Message}";
        }
    }

    [RelayCommand] private void ClearCardConfig() => SetCardConfig(null);

    public IReadOnlyList<string> BunkerMerges { get; } = CourseExtras.BunkerMergeChoices.Select(BunkerMergeLabel).ToList();
    [ObservableProperty] private string _bunkerMerge = BunkerMergeLabel(CourseExtras.DefaultBunkerMerge);

    static string BunkerMergeLabel(double metres) =>
        metres == CourseExtras.DefaultBunkerMerge ? $"{metres:0} m (default)" : $"{metres:0.#} m";

    partial void OnBunkerMergeChanged(string value)
    {
        double metres = CourseExtras.BunkerMergeChoices.FirstOrDefault(m => BunkerMergeLabel(m) == value, CourseExtras.DefaultBunkerMerge);
        var extras = Settings.ExtrasFor(Entry.Path);
        if (extras.BunkerMergeMetres == metres) return;
        extras.BunkerMergeMetres = metres;
        Settings.Save();
    }
}

public partial class RecentCourseViewModel : ObservableObject
{
    readonly Action _open;
    public string Name { get; }
    public Bitmap? Thumbnail { get; }

    public RecentCourseViewModel(CourseEntry entry, Action open)
    {
        Name = entry.Name;
        _open = open;
        if (entry.Thumbnail is { } bytes)
        {
            try { Thumbnail = Bitmap.DecodeToWidth(new MemoryStream(bytes), 64); } catch (Exception) { Thumbnail = null; }
        }
    }

    [RelayCommand] private void Open() => _open();
}

public partial class MeasurementRowViewModel : ObservableObject
{
    readonly Action? _remove;

    MeasurementRowViewModel(string hole, string leg, int yards, double feet, double? model, bool fromFile, Action? remove)
    {
        _remove = remove;
        Hole = hole; Leg = leg; Yards = $"{yards} yd"; Game = Signed(feet) + " ft";
        CanRemove = !fromFile; Source = fromFile ? "course file" : "typed";
        Model = model is double v ? Signed(v) + " ft" : "—";
        Diff = model is double d ? Signed(d - feet) + " ft" : "";
        DiffBrush = Brush.Parse(model is double e && Math.Abs(e - feet) > 4 ? "#8A5300" : "#1F4D30");
    }

    public MeasurementRowViewModel(MeasuredLeg m, double? model, Action remove)
        : this(m.Hole.ToString(), m.TeeToAim ? "Tee → aim" : "Aim → green", m.Yards, m.Feet, model, m.FromCourseFile, remove) { }

    /// <summary>Another leg of a measure line in the course file.</summary>
    public MeasurementRowViewModel(MeasuredLine l, double? model)
        : this(l.Hole > 0 ? l.Hole.ToString() : "—", "Measure line", l.Yards, l.Feet, model, true, null) { }

    static string Signed(double v) => (v > 0.05 ? "+" : v < -0.05 ? "−" : "") + Math.Abs(v).ToString("0.0");
    public string Hole { get; }
    public string Leg { get; }
    public string Yards { get; }
    public string Game { get; }
    /// <summary>Typed readings can be removed here; a course-file one goes when its measure line is deleted in the game.</summary>
    public bool CanRemove { get; }
    public string Source { get; }
    public string Model { get; }
    public string Diff { get; }
    public IBrush DiffBrush { get; }
    [RelayCommand] private void Remove() => _remove?.Invoke();
}
