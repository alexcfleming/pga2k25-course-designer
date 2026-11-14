using System.Collections.ObjectModel;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.Studio.Services;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// The Surfaces card beside the terrain map: theme, materials, terrain painting and weathering from the course file,
/// with a note on any line (kept with the material, so it follows it to every course, or for this course only) and a
/// note per group on how its layers work together on this course.
/// </summary>
public partial class WorkspaceViewModel
{
    [ObservableProperty] private string _surfacesHeading = "";
    [ObservableProperty] private string _surfacesSummaryText = "Reading the course file…";
    public ObservableCollection<SurfaceGroupViewModel> SurfaceGroups { get; } = [];

    string _surfacesBase = "";
    MaterialNotes _materialNotes = new();
    CourseSurfaceNotes _courseSurfaceNotes = new();

    void ApplySurfaces((string Heading, string Summary, IReadOnlyList<SurfaceGroup> Groups) look)
    {
        SurfacesHeading = look.Heading;
        _surfacesBase = look.Summary;
        _materialNotes = MaterialNotes.Load(MaterialNotes.DefaultPath);
        _courseSurfaceNotes = CourseSurfaceNotes.Load(OutputFolder);
        // Each material note learns this course uses it, for its "On 2 courses" line.
        bool seen = false;
        foreach (var r in look.Groups.SelectMany(g => g.Rows).Where(r => r.Key.Length > 0)) seen |= _materialNotes.SeenOn(r.Key, Entry.Name);
        if (seen) SaveMaterialNotes();
        SurfaceGroups.Clear();
        foreach (var g in look.Groups)
            SurfaceGroups.Add(new SurfaceGroupViewModel(g, Entry.Name, this));
        RefreshSurfaceNotes();
    }

    internal string? MaterialNoteText(string key) => _materialNotes.Text(key);
    internal MaterialNote? MaterialNoteFor(string key) => _materialNotes.Notes.GetValueOrDefault(key);
    internal string? CourseLineNote(string key) => _courseSurfaceNotes.Lines.GetValueOrDefault(key);
    internal string? GroupNote(string title) => _courseSurfaceNotes.Groups.GetValueOrDefault(title);

    internal void SaveLineNote(SurfaceRow row, string text, bool everyCourse)
    {
        if (everyCourse)
        {
            _materialNotes.Set(row.Key, text, Entry.Name, DateTime.Now);
            SaveMaterialNotes();
        }
        else
        {
            CourseSurfaceNotes.Put(_courseSurfaceNotes.Lines, row.Key, text);
            SaveCourseSurfaceNotes();
        }
        RefreshSurfaceNotes();
    }

    internal void SaveGroupNote(string title, string text)
    {
        CourseSurfaceNotes.Put(_courseSurfaceNotes.Groups, title, text);
        SaveCourseSurfaceNotes();
        RefreshSurfaceNotes();
    }

    void SaveMaterialNotes()
    {
        try { _materialNotes.Save(MaterialNotes.DefaultPath); } catch (Exception ex) { Status = $"Surface notes not saved: {ex.Message}"; }
    }

    void SaveCourseSurfaceNotes()
    {
        try { _courseSurfaceNotes.Save(OutputFolder); } catch (Exception ex) { Status = $"Surface notes not saved: {ex.Message}"; }
    }

    void RefreshSurfaceNotes()
    {
        foreach (var g in SurfaceGroups) g.Refresh();
        int notes = SurfaceGroups.Sum(g => g.NoteCount);
        SurfacesSummaryText = _surfacesBase + (notes == 0 ? "" : $" · {notes} note{(notes == 1 ? "" : "s")}");
    }
}

/// <summary>One group of the Surfaces card (Create surfaces, Terrain painting, Surface weathering) and its combination note.</summary>
public partial class SurfaceGroupViewModel : ObservableObject
{
    readonly WorkspaceViewModel _ws;
    public string Title { get; }
    public IReadOnlyList<SurfaceRowViewModel> Rows { get; }
    public string NoteTitle { get; }

    [ObservableProperty] private string _note = "";
    [ObservableProperty] private bool _hasNote;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editText = "";

    public string AddNoteLabel => $"+ Note: {NoteTitle}";
    public bool CanAddNote => !HasNote && !IsEditing;
    partial void OnHasNoteChanged(bool value) => OnPropertyChanged(nameof(CanAddNote));
    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(CanAddNote));

    public SurfaceGroupViewModel(SurfaceGroup group, string course, WorkspaceViewModel ws)
    {
        _ws = ws;
        Title = group.Title;
        NoteTitle = group.Title switch
        {
            "Terrain painting" => $"These layers together on {course}",
            "Surface weathering" => $"Weathering on {course}",
            _ => $"These surfaces together on {course}",
        };
        Rows = group.Rows.Select(r => new SurfaceRowViewModel(r, course, ws)).ToList();
    }

    public int NoteCount => (HasNote ? 1 : 0) + Rows.Sum(r => (r.HasMaterialNote ? 1 : 0) + (r.HasCourseNote ? 1 : 0));

    public void Refresh()
    {
        Note = _ws.GroupNote(Title) ?? "";
        HasNote = Note.Length > 0;
        foreach (var r in Rows) r.Refresh();
    }

    [RelayCommand] private void Edit() { EditText = Note; IsEditing = true; }
    [RelayCommand] private void Cancel() => IsEditing = false;
    [RelayCommand] private void Save() { IsEditing = false; _ws.SaveGroupNote(Title, EditText); }
}

/// <summary>One line of the Surfaces card with its notes: the material's own (every course) and this course's.</summary>
public partial class SurfaceRowViewModel : ObservableObject
{
    readonly WorkspaceViewModel _ws;
    readonly string _course;
    public SurfaceRow Row { get; }
    public string Label => Row.Label;
    public string Value => Row.Value;
    public string Source => Row.Source;
    public string Detail => Row.Detail;
    public bool IsDefault => Row.IsDefault;
    public bool CanNote => Row.Key.Length > 0;

    /// <summary>"Splat 1 · TPC Sawgrass, on every course" or "Theme 2's Path ③ ④ ⑤, on every course".</summary>
    public string EveryCourseLabel { get; }
    public string CourseOnlyLabel => $"{_course} only";

    [ObservableProperty] private string _materialNote = "";
    [ObservableProperty] private string _materialMeta = "";
    [ObservableProperty] private bool _hasMaterialNote;
    [ObservableProperty] private string _courseNote = "";
    [ObservableProperty] private bool _hasCourseNote;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editText = "";
    [ObservableProperty] private bool _forEveryCourse = true;
    [ObservableProperty] private string _penText = "✎";

    public SurfaceRowViewModel(SurfaceRow row, string course, WorkspaceViewModel ws)
    {
        Row = row;
        _course = course;
        _ws = ws;
        EveryCourseLabel = EveryCourseLabelFor(row);
    }

    /// <summary>The "on every course" choice for a line's note; blank for a line that takes no note of its own (no key),
    /// such as Surface Weathering's "Painted · none" on a course with no weathering strokes.</summary>
    public static string EveryCourseLabelFor(SurfaceRow row)
    {
        if (row.Key.Length == 0) return "";
        string what = row.IsDefault
            ? $"T{row.Key.Split(" · ")[0][1..]}'s {row.Label}"
            : row.Source.Length > 0 ? $"{row.Value} · {row.Source}" : row.Value;
        return $"{what}, on every course";
    }

    public void Refresh()
    {
        if (!CanNote) return;
        var m = _ws.MaterialNoteFor(Row.Key);
        MaterialNote = m?.Text ?? "";
        HasMaterialNote = MaterialNote.Length > 0;
        MaterialMeta = m is null || !HasMaterialNote ? "" :
            (m.Courses.Count <= 1 ? "On 1 course" : $"On {m.Courses.Count} courses: {string.Join(", ", m.Courses)}") + $" · edited {m.Edited:d MMM}";
        CourseNote = _ws.CourseLineNote(Row.Key) ?? "";
        HasCourseNote = CourseNote.Length > 0;
        int n = (HasMaterialNote ? 1 : 0) + (HasCourseNote ? 1 : 0);
        PenText = n == 0 ? "✎" : $"✎ {n}";
    }

    /// <summary>Opens the material's note, or this course's when that's the only one.</summary>
    [RelayCommand] private void Edit()
    {
        ForEveryCourse = HasMaterialNote || !HasCourseNote;
        EditText = ForEveryCourse ? MaterialNote : CourseNote;
        IsEditing = true;
    }

    partial void OnForEveryCourseChanged(bool value)
    {
        if (IsEditing) EditText = value ? MaterialNote : CourseNote;
    }

    [RelayCommand] private void Cancel() => IsEditing = false;
    [RelayCommand] private void Save() { IsEditing = false; _ws.SaveLineNote(Row, EditText, ForEveryCourse); }
}

/// <summary>A "theme default" line is set faint and italic, so the chosen materials stand out.</summary>
public static class SurfaceStyles
{
    public static readonly IValueConverter Italic = new FuncValueConverter<bool, FontStyle>(d => d ? FontStyle.Italic : FontStyle.Normal);
    public static readonly IValueConverter ValueBrush = new FuncValueConverter<bool, IBrush>(d => Brush.Parse(d ? "#8A928C" : "#18211C"));
}
