using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.CourseFormat;
using Pga2K25.Studio.Services;
using Pga2K25.Studio.Views;
using Pga2K25.TerrainMap;
using SkiaSharp;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// The live map's planning layers: scanned hole sketches (imported from the printed hole sheets and tied to the
/// ground), the hole-sheet view (the map turned and zoomed to a sheet's own frame), and notes pinned to the ground.
/// Sketches live in "&lt;course&gt; - Sketches/" and notes in Notes.json, both in the course's output folder.
/// </summary>
public partial class WorkspaceViewModel
{
    /// <summary>One colour per hole, dark enough to read over the relief shading and distinct side by side.</summary>
    static readonly string[] HolePalette =
    [
        "#C62828", "#1565C0", "#2E7D32", "#6A1B9A", "#E65100", "#00838F", "#AD1457", "#4E342E", "#283593",
        "#827717", "#D84315", "#00695C", "#8E24AA", "#0277BD", "#558B2F", "#C2185B", "#5D4037", "#37474F",
    ];
    static readonly SKColor Graphite = new(0x26, 0x26, 0x26);
    const double NoteHoleReachMetres = 120;

    public static Color HoleColour(int hole) => Color.Parse(HolePalette[(hole - 1 + HolePalette.Length) % HolePalette.Length]);

    public IReadOnlyList<string> SketchModes { get; } = [TerrainSettings.SketchesOff, TerrainSettings.SketchesThisHole, TerrainSettings.SketchesAll];
    public IReadOnlyList<string> NoteKinds => CourseNotes.Kinds;
    public ObservableCollection<string> NoteHoleChoices { get; } = [NoteViewModel.CourseChoice];

    [ObservableProperty] private string _sketchMode = TerrainSettings.SketchesAll;
    [ObservableProperty] private bool _sketchColours = true;
    [ObservableProperty] private double _sketchOpacity = 0.9;
    [ObservableProperty] private bool _sketchScan;
    [ObservableProperty] private bool _sketchConstruction;
    [ObservableProperty] private bool _sketchRawPencil;
    [ObservableProperty] private bool _hasTracedLines;
    [ObservableProperty] private bool _showNotes = true;
    [ObservableProperty] private bool _placingNote;
    [ObservableProperty] private bool _holeView;
    [ObservableProperty] private string _viewText = "";
    [ObservableProperty] private bool _hasSketches;
    [ObservableProperty] private string _sketchSummary = "No sketches yet. Scan your marked-up hole sheets (PDF or photos) and import them.";
    [ObservableProperty] private bool _hasImportResults;
    [ObservableProperty] private NoteViewModel? _selectedNote;
    [ObservableProperty] private string _notesSummary = "";

    public ObservableCollection<SketchRowViewModel> SketchRows { get; } = [];
    public ObservableCollection<string> ImportResults { get; } = [];
    public ObservableCollection<NoteViewModel> Notes { get; } = [];
    public bool HasSelectedNote => SelectedNote is not null;
    public bool HasNotes => Notes.Count > 0;

    /// <summary>The sketch layers or pins changed; the view asks for them again.</summary>
    public event Action? LayersChanged;
    /// <summary>Turn the view to a hole sheet's frame (map pixels) with the given direction up.</summary>
    public event Action<IReadOnlyList<Point>, Vector>? ShowFrameRequested;
    public event Action? FitRequested;
    public event Action? NoteEditorFocusRequested;

    Dictionary<int, HoleSheet.Layout> _sheetLayouts = [];
    List<LoadedSketch> _sketches = [];
    (int Hole, Bitmap Image)? _scan;
    readonly List<CourseNote> _notes = [];
    DispatcherTimer? _notesSave;
    string _lastKind = CourseNotes.Build;
    bool _loadingLayerSettings;

    sealed class LoadedSketch(HoleSketch meta, byte[] alpha, int width, int height, SketchLines? traced)
    {
        public HoleSketch Meta { get; } = meta;
        /// <summary>The traced lines in world X/Z; null for sketches imported before tracing (shown as pencil).</summary>
        public SketchLines? Traced { get; } = traced;
        /// <summary>The traced lines as map-pixel geometry for one projection (edges, construction).</summary>
        public (object Projection, Avalonia.Media.Geometry Edges, Avalonia.Media.Geometry Construction)? Geometry { get; set; }
        public byte[] Alpha { get; } = alpha;
        public int Width { get; } = width;
        public int Height { get; } = height;
        public (SKColor Colour, Bitmap Image)? Tinted { get; set; }
    }

    string SketchFolder => HoleSketches.FolderFor(Products.OutputDir(Settings, Entry.Name), CourseLibrary.FileStem(Entry.Name));
    string NotesPath => CourseNotes.PathFor(Settings, Entry.Name);
    public string SketchFolderText => SketchFolder;

    // ───────────────────────────── settings ─────────────────────────────

    void LoadLayerSettings()
    {
        _loadingLayerSettings = true;
        var t = Terrain;
        SketchMode = SketchModes.Contains(t.Sketches) ? t.Sketches : TerrainSettings.SketchesAll;
        SketchColours = t.SketchColours;
        SketchOpacity = Math.Clamp(t.SketchOpacity, 0.1, 1);
        SketchScan = t.SketchScan;
        SketchConstruction = t.SketchConstruction;
        SketchRawPencil = t.SketchRawPencil;
        ShowNotes = t.ShowNotes;
        _loadingLayerSettings = false;
    }

    void SaveLayerSettings()
    {
        if (_loadingLayerSettings) return;
        var t = Terrain;
        t.Sketches = SketchMode;
        t.SketchColours = SketchColours;
        t.SketchOpacity = Math.Round(SketchOpacity, 2);
        t.SketchScan = SketchScan;
        t.SketchConstruction = SketchConstruction;
        t.SketchRawPencil = SketchRawPencil;
        t.ShowNotes = ShowNotes;
        Settings.Save();
        LayersChanged?.Invoke();
    }

    partial void OnSketchModeChanged(string value) => SaveLayerSettings();
    partial void OnSketchColoursChanged(bool value) => SaveLayerSettings();
    partial void OnSketchOpacityChanged(double value) => SaveLayerSettings();
    partial void OnSketchScanChanged(bool value) => SaveLayerSettings();
    partial void OnSketchConstructionChanged(bool value) => SaveLayerSettings();
    partial void OnSketchRawPencilChanged(bool value) => SaveLayerSettings();
    partial void OnShowNotesChanged(bool value) => SaveLayerSettings();

    partial void OnPlacingNoteChanged(bool value)
    {
        if (value) { MarkingNotWater = false; ShowNotes = true; HoverText = "Click the map where the note belongs"; }
    }

    partial void OnSelectedNoteChanged(NoteViewModel? oldValue, NoteViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
        OnPropertyChanged(nameof(HasSelectedNote));
        LayersChanged?.Invoke();
    }

    /// <summary>Hole sheet frames for the hole view, from the survey's routing (cheap: no terrain).</summary>
    void SetSheetLayouts(TerrainSurvey survey)
    {
        var options = new HoleSheetOptions();
        _sheetLayouts = survey.Course.Holes.ToDictionary(h => h.Number, h => HoleSheet.LayoutFor(survey.Layers, h, options));
        NoteHoleChoices.Clear();
        NoteHoleChoices.Add(NoteViewModel.CourseChoice);
        foreach (var h in survey.Holes) NoteHoleChoices.Add(h.Hole.Number.ToString());
        foreach (var n in Notes) n.Refresh();
    }

    // ───────────────────────────── layers for the view ─────────────────────────────

    Point MapPx(Vec2 w)
    {
        var p = _projection!.Map(w);
        return new Point(p.X, p.Y);
    }

    /// <summary>Takes a straightened sheet image (its corners on the frame's corners) to map pixels.</summary>
    Matrix SketchMatrix(HoleSketch meta, int width, int height)
    {
        var c = meta.FrameCorners();
        Point p0 = MapPx(c[0]), p1 = MapPx(c[1]), p3 = MapPx(c[3]);
        return new Matrix((p1.X - p0.X) / width, (p1.Y - p0.Y) / width, (p3.X - p0.X) / height, (p3.Y - p0.Y) / height, p0.X, p0.Y);
    }

    public IReadOnlyList<SketchLayer> CurrentSketchLayers()
    {
        if (_projection is null || SketchMode == TerrainSettings.SketchesOff) return [];
        var layers = new List<SketchLayer>();
        // The selected hole last, so it sits on top where holes overlap.
        foreach (var s in _sketches.OrderBy(s => s.Meta.Hole == _selectedHole))
        {
            bool selected = s.Meta.Hole == _selectedHole;
            if (SketchMode == TerrainSettings.SketchesThisHole && !selected) continue;
            if (selected && SketchScan && ScanFor(s.Meta) is { } scan)
            {
                layers.Add(new SketchLayer(scan, SketchMatrix(s.Meta, scan.PixelSize.Width, scan.PixelSize.Height), SketchOpacity * 0.8));
                continue;
            }
            // Traced lines replace the pencil image unless the raw pencil is asked for.
            if (s.Traced is not null && !SketchRawPencil) continue;
            var colour = SketchColours ? ToSk(HoleColour(s.Meta.Hole)) : Graphite;
            if (s.Tinted is not { } t || t.Colour != colour) s.Tinted = (colour, Tint(s, colour));
            layers.Add(new SketchLayer(s.Tinted.Value.Image, SketchMatrix(s.Meta, s.Width, s.Height), SketchOpacity));
        }
        return layers;
    }

    /// <summary>The traced lines to draw: edges always, construction (shot lines, aim points) when asked for.</summary>
    public IReadOnlyList<LineLayer> CurrentLineLayers()
    {
        if (_projection is null || SketchMode == TerrainSettings.SketchesOff || SketchRawPencil) return [];
        var layers = new List<LineLayer>();
        foreach (var s in _sketches.OrderBy(s => s.Meta.Hole == _selectedHole))
        {
            bool selected = s.Meta.Hole == _selectedHole;
            if (s.Traced is null || (SketchMode == TerrainSettings.SketchesThisHole && !selected)) continue;
            if (selected && SketchScan) continue;   // the scan itself is showing
            if (s.Geometry is not { } g || !ReferenceEquals(g.Projection, _projection))
            {
                s.Geometry = (_projection, LineGeometry(s.Traced, construction: false), LineGeometry(s.Traced, construction: true));
                g = s.Geometry.Value;
            }
            var colour = SketchColours ? HoleColour(s.Meta.Hole) : Color.FromRgb(Graphite.Red, Graphite.Green, Graphite.Blue);
            if (SketchConstruction) layers.Add(new LineLayer(g.Construction, colour, SketchOpacity * 0.8, Construction: true));
            layers.Add(new LineLayer(g.Edges, colour, SketchOpacity, Construction: false));
        }
        return layers;
    }

    Avalonia.Media.Geometry LineGeometry(SketchLines traced, bool construction)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        foreach (var line in traced.Lines)
        {
            if (line.Construction != construction || line.Xz.Length < 4) continue;
            ctx.BeginFigure(MapPx(new Vec2(line.Xz[0], line.Xz[1])), false);
            for (int i = 2; i + 1 < line.Xz.Length; i += 2) ctx.LineTo(MapPx(new Vec2(line.Xz[i], line.Xz[i + 1])));
            ctx.EndFigure(false);
        }
        return geometry;
    }

    public IReadOnlyList<MapPin> CurrentPins()
    {
        if (_projection is null) return [];
        var pins = !ShowNotes ? [] : Notes.Select(n => new MapPin(n.Id, MapPx(new Vec2(n.Model.X, n.Model.Z)), n.Number.ToString(),
            Color.Parse(CourseNotes.ColourOf(n.Kind)), n.Callout)).ToList();
        pins.AddRange(_notWater.Select((m, i) => new MapPin($"{NotWaterPin}{i}", MapPx(m.Point), "✕", Color.Parse("#1E4A70"), "Not water in game")));
        return pins;
    }

    public string? SelectedPinId => SelectedNote?.Id;

    static SKColor ToSk(Color c) => new(c.R, c.G, c.B);

    static Bitmap Tint(LoadedSketch s, SKColor c)
    {
        var bitmap = new WriteableBitmap(new PixelSize(s.Width, s.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = bitmap.Lock();
        var row = new byte[s.Width * 4];
        for (int y = 0; y < s.Height; y++)
        {
            for (int x = 0; x < s.Width; x++)
            {
                int a = s.Alpha[y * s.Width + x];
                row[4 * x] = (byte)(c.Blue * a / 255);
                row[4 * x + 1] = (byte)(c.Green * a / 255);
                row[4 * x + 2] = (byte)(c.Red * a / 255);
                row[4 * x + 3] = (byte)a;
            }
            Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
        }
        return bitmap;
    }

    Bitmap? ScanFor(HoleSketch meta)
    {
        if (_scan is { } s && s.Hole == meta.Hole) return s.Image;
        string path = Path.Combine(SketchFolder, meta.Scan);
        if (!File.Exists(path)) return null;
        try
        {
            using var f = File.OpenRead(path);
            var image = new Bitmap(f);
            _scan = (meta.Hole, image);
            return image;
        }
        catch (Exception) { return null; }
    }

    // ───────────────────────────── sketches ─────────────────────────────

    void LoadSketches()
    {
        string folder = SketchFolder;
        Task.Run(() =>
        {
            var set = HoleSketches.Load(folder);
            var loaded = new List<LoadedSketch>();
            foreach (var meta in set.Sheets)
            {
                string path = Path.Combine(folder, meta.Pencil);
                if (meta.Frame.Length != 4 || !File.Exists(path)) continue;
                using var decoded = SKBitmap.Decode(path);
                if (decoded is null) continue;
                using var alpha = decoded.Copy(SKColorType.Alpha8);
                if (alpha is null) continue;
                var traced = meta.Lines.Length > 0 ? HoleSketches.LoadLines(Path.Combine(folder, meta.Lines)) : null;
                loaded.Add(new LoadedSketch(meta, alpha.GetPixelSpan().ToArray(), alpha.Width, alpha.Height, traced));
            }
            Dispatcher.UIThread.Post(() =>
            {
                _sketches = loaded;
                _scan = null;
                SketchRows.Clear();
                foreach (var s in loaded.OrderBy(s => s.Meta.Hole))
                    SketchRows.Add(new SketchRowViewModel(s.Meta, HoleColour(s.Meta.Hole), RemoveSketch));
                HasSketches = loaded.Count > 0;
                HasTracedLines = loaded.Any(s => s.Traced is not null);
                var sources = loaded.Select(s => s.Meta.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                SketchSummary = loaded.Count == 0
                    ? "No sketches yet. Scan your marked-up hole sheets (PDF or photos) and import them."
                    : $"{loaded.Count} hole{(loaded.Count == 1 ? "" : "s")} sketched · from {string.Join(", ", sources.Take(2))}{(sources.Count > 2 ? $" and {sources.Count - 2} more" : "")}";
                LayersChanged?.Invoke();
            });
        });
    }

    /// <summary>Imports scanned hole sheets: each page is matched to its hole, so order and extra pages don't matter.</summary>
    public async Task ImportScansAsync(IReadOnlyList<string> files)
    {
        if (Busy || files.Count == 0) return;
        Busy = true;
        ImportResults.Clear();
        HasImportResults = false;
        Status = "Reading the scans…";
        var progress = new Progress<string>(line => { Log.Add(line); Status = line; });
        // The same sheet options as the Hole sheets build, so the fresh render matches what was printed.
        var options = new HoleSheetOptions { Map = Terrain.ToOptions(Settings.Credit) with { ContourIntervalFeet = null } };
        try
        {
            // The Hole sheets build's PNGs are usually what was printed; they still match where the ground has changed since.
            string built = Path.Combine(Products.OutputDir(Settings, Entry.Name), CourseLibrary.FileStem(Entry.Name) + " - Hole Sheets");
            var results = await Task.Run(() => HoleSketches.Import(Entry.Path, files, SketchFolder, options, progress,
                Directory.Exists(built) ? built : null));
            foreach (var r in results)
                ImportResults.Add($"{Path.GetFileName(r.Source)} p{r.Page}: {r.Message}");
            int saved = results.Count(r => r.Saved), skipped = results.Count - saved;
            Status = saved == 0 ? "No hole sheets found in the scans" :
                $"Imported sketches for {saved} hole{(saved == 1 ? "" : "s")}{(skipped > 0 ? $"; {skipped} page{(skipped == 1 ? "" : "s")} skipped" : "")}";
            if (saved > 0 && SketchMode == TerrainSettings.SketchesOff) SketchMode = TerrainSettings.SketchesAll;
        }
        catch (Exception ex)
        {
            Status = $"Import failed: {ex.Message}";
            ImportResults.Add(Status);
        }
        HasImportResults = ImportResults.Count > 0;
        Busy = false;
        LoadSketches();
    }

    void RemoveSketch(int hole)
    {
        try { HoleSketches.Remove(SketchFolder, hole); }
        catch (Exception ex) { Status = $"Could not remove hole {hole}'s sketch: {ex.Message}"; }
        LoadSketches();
    }

    [RelayCommand] private void OpenSketchFolder()
    {
        Directory.CreateDirectory(SketchFolder);
        OpenPath(SketchFolder);
    }

    // ───────────────────────────── hole view ─────────────────────────────

    [RelayCommand] private void ShowHoleView()
    {
        if (_selectedHole == 0) return;
        HoleView = true;
        RequestFrame(_selectedHole);
    }

    [RelayCommand] private void ShowWholeMap()
    {
        HoleView = false;
        ViewText = "";
        FitRequested?.Invoke();
    }

    void RequestFrame(int hole)
    {
        if (_projection is null || !_sheetLayouts.TryGetValue(hole, out var layout)) return;
        var corners = layout.FrameCorners().Select(MapPx).ToList();
        var up = MapPx(layout.BackTee + layout.Forward * 100) - MapPx(layout.BackTee);
        ViewText = $"Hole {hole} · as on its sheet";
        ShowFrameRequested?.Invoke(corners, up);
    }

    // ───────────────────────────── notes ─────────────────────────────

    void LoadNotes()
    {
        _notes.Clear();
        _notes.AddRange(CourseNotes.Load(NotesPath));
        RebuildNoteRows();
    }

    void RebuildNoteRows()
    {
        string? keep = SelectedNote?.Id;
        Notes.Clear();
        int number = 1;
        foreach (var n in _notes.OrderBy(n => n.Created))
            Notes.Add(new NoteViewModel(n, number++, NoteChanged, SelectNote));
        // The list reads by hole; numbers stay in the order the notes were made.
        var sorted = Notes.OrderBy(n => n.Model.Hole ?? int.MaxValue).ThenBy(n => n.Number).ToList();
        Notes.Clear();
        foreach (var n in sorted) Notes.Add(n);
        SelectedNote = keep is null ? null : Notes.FirstOrDefault(n => n.Id == keep);
        OnPropertyChanged(nameof(HasNotes));
        NotesSummary = Notes.Count == 0 ? "Pin notes on the ground: how to build it, planting, concepts."
            : $"{Notes.Count} note{(Notes.Count == 1 ? "" : "s")} · saved to Notes.json beside the products";
        LayersChanged?.Invoke();
    }

    void SelectNote(NoteViewModel note)
    {
        SelectedNote = note;
        if (_projection is not null) CentreOn?.Invoke(MapPx(new Vec2(note.Model.X, note.Model.Z)));
    }

    void NoteChanged(NoteViewModel note)
    {
        _lastKind = note.Kind;
        ScheduleNotesSave();
        LayersChanged?.Invoke();
    }

    void ScheduleNotesSave()
    {
        _notesSave ??= new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Background, (_, _) => SaveNotesNow());
        _notesSave.Stop();
        _notesSave.Start();
    }

    void SaveNotesNow()
    {
        _notesSave?.Stop();
        try { CourseNotes.Save(NotesPath, _notes); }
        catch (Exception ex) { Status = $"Could not save the notes: {ex.Message}"; }
    }

    /// <summary>
    /// The selected hole if its route is within reach (you are usually looking at it), otherwise the hole whose
    /// route passes nearest within reach; otherwise the course.
    /// </summary>
    int? NearestHole(Vec2 w)
    {
        if (_survey is null) return null;
        int? best = null;
        double bestD = NoteHoleReachMetres;
        foreach (var h in _survey.Holes.OrderByDescending(h => h.Hole.Number == _selectedHole))
        {
            var r = h.Route;
            for (int i = 0; i + 1 < r.Count; i++)
            {
                var ab = r[i + 1] - r[i];
                double len2 = ab.X * ab.X + ab.Z * ab.Z;
                double t = len2 < 1e-9 ? 0 : Math.Clamp(((w.X - r[i].X) * ab.X + (w.Z - r[i].Z) * ab.Z) / len2, 0, 1);
                double d = Vec2.Distance(w, r[i] + ab * t);
                if (d < bestD) { bestD = d; best = h.Hole.Number; }
            }
            if (best == _selectedHole && best is not null) return best;
        }
        return best;
    }

    /// <summary>A click on the map: drops a note while placing, otherwise clears the selection.</summary>
    public void MapClicked(Point px)
    {
        if (MarkingNotWater) { MarkNotWater(px); return; }
        if (!PlacingNote) { SelectedNote = null; return; }
        if (_projection is null || !_mapRect.Contains(new SKPoint((float)px.X, (float)px.Y))) return;
        var w = _projection.Unmap(new SKPoint((float)px.X, (float)px.Y));
        var note = new CourseNote { X = Math.Round(w.X, 2), Z = Math.Round(w.Z, 2), Kind = _lastKind, Hole = NearestHole(w) };
        _notes.Add(note);
        PlacingNote = false;
        HoverText = "Type the note in the panel on the right";
        SaveNotesNow();
        RebuildNoteRows();
        SelectedNote = Notes.FirstOrDefault(n => n.Id == note.Id);
        NoteEditorFocusRequested?.Invoke();
    }

    public void PinClicked(string id)
    {
        if (id.StartsWith(NotWaterPin, StringComparison.Ordinal)) return;
        SelectedNote = Notes.FirstOrDefault(n => n.Id == id);
    }

    public void PinMoved(string id, Point px)
    {
        if (_projection is null || Notes.FirstOrDefault(n => n.Id == id) is not { } note) return;
        var w = _projection.Unmap(new SKPoint((float)px.X, (float)px.Y));
        note.Model.X = Math.Round(w.X, 2);
        note.Model.Z = Math.Round(w.Z, 2);
        note.Model.Modified = DateTime.Now;
        SelectedNote = note;
        SaveNotesNow();
        LayersChanged?.Invoke();
    }

    [RelayCommand] private void DeleteNote()
    {
        if (SelectedNote is not { } note) return;
        _notes.Remove(note.Model);
        SelectedNote = null;
        SaveNotesNow();
        RebuildNoteRows();
    }

    [RelayCommand] private void DoneNote()
    {
        SaveNotesNow();
        SelectedNote = null;
        RebuildNoteRows();
    }

    [RelayCommand] private void OpenNotesFile()
    {
        if (!File.Exists(NotesPath)) SaveNotesNow();
        OpenPath(NotesPath);
    }
}

public sealed partial class SketchRowViewModel(HoleSketch meta, Color colour, Action<int> remove) : ObservableObject
{
    public int Hole { get; } = meta.Hole;
    public string Title { get; } = $"Hole {meta.Hole}";
    public string Detail { get; } = $"{meta.Source} p{meta.Page} · match {meta.Match:0.00} · {meta.Imported:d MMM}";
    public IBrush Swatch { get; } = new SolidColorBrush(colour);
    [RelayCommand] private void Remove() => remove(Hole);
}

/// <summary>A pinned note in the list and the editor; edits go straight to the note and are saved shortly after.</summary>
public sealed partial class NoteViewModel : ObservableObject
{
    public const string CourseChoice = "Course";
    readonly Action<NoteViewModel> _changed, _select;

    public CourseNote Model { get; }
    public string Id => Model.Id;
    public int Number { get; }

    [ObservableProperty] private string _text;
    [ObservableProperty] private string _kind;
    [ObservableProperty] private string _holeChoice;
    [ObservableProperty] private bool _isSelected;

    public NoteViewModel(CourseNote model, int number, Action<NoteViewModel> changed, Action<NoteViewModel> select)
    {
        Model = model;
        Number = number;
        _changed = changed;
        _select = select;
        _text = model.Text;
        _kind = CourseNotes.Kinds.Contains(model.Kind) ? model.Kind : CourseNotes.General;
        _holeChoice = model.Hole?.ToString() ?? CourseChoice;
    }

    public string Summary => string.IsNullOrWhiteSpace(Text) ? "(no text yet)" : Text.Trim().Split('\n')[0].Trim();
    public string HoleText => Model.Hole is int h ? $"Hole {h}" : "Course";
    public IBrush KindBrush => Brush.Parse(CourseNotes.ColourOf(Kind));
    public string Callout => $"{Kind} · {HoleText}\n{(string.IsNullOrWhiteSpace(Text) ? "(no text yet)" : Text.Trim())}";

    partial void OnTextChanged(string value)
    {
        Model.Text = value ?? "";
        Touch(nameof(Summary));
    }

    partial void OnKindChanged(string value)
    {
        if (value is null) return;   // a list being refilled
        Model.Kind = value;
        Touch(nameof(KindBrush));
    }

    partial void OnHoleChoiceChanged(string value)
    {
        if (value is null) return;   // the hole list being refilled
        Model.Hole = int.TryParse(value, out int h) ? h : null;
        Touch(nameof(HoleText));
    }

    void Touch(string property)
    {
        Model.Modified = DateTime.Now;
        OnPropertyChanged(property);
        OnPropertyChanged(nameof(Callout));
        _changed(this);
    }

    public void Refresh() => OnPropertyChanged(nameof(HoleText));

    [RelayCommand] private void Select() => _select(this);
}
