using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Pga2K25.CourseFormat;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// The Holes card in Course settings: tee set names and, per hole, a name and any hazards or green notes written by
/// hand. Saved to Holes.json as it's typed; the yardage book, scorecard maps and course cards read it.
/// </summary>
public partial class WorkspaceViewModel
{
    public ObservableCollection<HoleEntryViewModel> HoleEntries { get; } = [];

    [ObservableProperty] private string _teeNamesText = "";
    [ObservableProperty] private string _teeSetsHint = "";

    bool _holesLoading;

    void InitHoles()
    {
        _holesLoading = true;
        var saved = CourseHolesFile.Load(OutputFolder);
        // Tee names typed on the Publish readiness page before this card existed carry over.
        var tees = saved.Tees.Count > 0 ? saved.Tees : ReadinessTeeNames();
        TeeNamesText = string.Join(", ", tees);
        _holesLoading = false;

        string path = Entry.Path;
        Task.Run(() =>
        {
            CourseModel course;
            try { course = _placeCourse ??= CourseFile.Load(path).ReadModel(); }
            catch (Exception) { return; }
            int sets = course.Holes.Count == 0 ? 1 : course.Holes.Max(h => Math.Max(1, h.Tees.Count));
            Dispatcher.UIThread.Post(() =>
            {
                TeeSetsHint = $"The course file has {sets} tee set{(sets == 1 ? "" : "s")}, shortest first.";
                _holesLoading = true;
                HoleEntries.Clear();
                foreach (var h in course.Holes.OrderBy(h => h.Number))
                    HoleEntries.Add(new HoleEntryViewModel(h.Number, saved.For(h.Number), SaveHoles));
                _holesLoading = false;
                ShowCourseFileNotes();
            });
        });
    }

    IReadOnlyList<CourseCard.HoleNote>? _courseFileNotes;
    bool _courseFileNotesStarted;

    /// <summary>
    /// Reads what the hole data card writes from the course file, to show faintly where nothing is written by hand.
    /// It takes about as long as the cards' own pass, so it starts only when Course settings is first opened.
    /// </summary>
    void LoadCourseFileNotes()
    {
        if (_courseFileNotesStarted) return;
        _courseFileNotesStarted = true;
        string path = Entry.Path;
        Task.Run(() =>
        {
            IReadOnlyList<CourseCard.HoleNote> notes;
            try { notes = CourseCard.HoleNotes.For(_placeCourse ??= CourseFile.Load(path).ReadModel()); }
            catch (Exception) { return; }
            Dispatcher.UIThread.Post(() =>
            {
                _courseFileNotes = notes;
                ShowCourseFileNotes();
            });
        });
    }

    void ShowCourseFileNotes()
    {
        if (_courseFileNotes is not { } notes) return;
        foreach (var row in HoleEntries)
            if (notes.FirstOrDefault(n => n.Hole == row.Hole) is { } n) row.SetCourseFileNotes(n.Hazards, n.GreenNotes);
    }

    List<string> ReadinessTeeNames()
    {
        try { return Pga2K25.Readiness.ReadinessStore.Load(Pga2K25.Readiness.ReadinessStore.PathFor(OutputFolder)).Info.TeeNames; }
        catch (Exception) { return []; }
    }

    partial void OnTeeNamesTextChanged(string value)
    {
        SaveHoles();
        if (!_holesLoading && Readiness is { } r) r.TeeNames = value;
    }

    void SaveHoles()
    {
        if (_holesLoading) return;
        var holes = CourseHolesFile.Load(OutputFolder);
        holes.TeeNames = TeeNamesText.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        foreach (var row in HoleEntries) row.WriteTo(holes.Entry(row.Hole));
        holes.Holes.RemoveAll(e => e.Name.Length == 0 && e.Hazards.Count == 0 && e.GreenNotes.Length == 0);
        try { CourseHolesFile.Save(OutputFolder, holes); }
        catch (Exception ex) { Status = $"Holes could not be saved: {ex.Message}"; }
    }
}

/// <summary>One hole's row on the Holes card.</summary>
public partial class HoleEntryViewModel : ObservableObject
{
    readonly Action _changed;
    bool _loading = true;

    public HoleEntryViewModel(int hole, HoleEntry? saved, Action changed)
    {
        Hole = hole;
        _changed = changed;
        Name = saved?.Name ?? "";
        Hazards = string.Join(" · ", saved?.Hazards ?? []);
        GreenNotes = saved?.GreenNotes ?? "";
        _loading = false;
    }

    public int Hole { get; }
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _hazards = "";
    [ObservableProperty] private string _greenNotes = "";
    [ObservableProperty] private string _hazardsPlaceholder = "From the course file (reading…)";
    [ObservableProperty] private string _greenNotesPlaceholder = "From the course file (reading…)";

    partial void OnNameChanged(string value) { if (!_loading) _changed(); }
    partial void OnHazardsChanged(string value) { if (!_loading) _changed(); }
    partial void OnGreenNotesChanged(string value) { if (!_loading) _changed(); }

    public void SetCourseFileNotes(IReadOnlyList<string> hazards, string green)
    {
        HazardsPlaceholder = hazards.Count > 0 ? string.Join(" · ", hazards) : "None found in the course file";
        GreenNotesPlaceholder = green.Length > 0 ? green : "None found in the course file";
    }

    /// <summary>Hazards are typed separated by " · " or semicolons (commas belong inside a tag, as in "FW bunkers L 230, R 250").</summary>
    public void WriteTo(HoleEntry entry)
    {
        entry.Name = Name.Trim();
        entry.Hazards = Hazards.Split(['·', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        entry.GreenNotes = GreenNotes.Trim();
    }
}
