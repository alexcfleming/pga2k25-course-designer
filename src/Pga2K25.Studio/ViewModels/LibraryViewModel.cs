using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.Studio.Services;

namespace Pga2K25.Studio.ViewModels;

/// <summary>Every course in the watched folder as a card, with a status filter and a name search.</summary>
public partial class LibraryViewModel : ObservableObject
{
    readonly MainViewModel _main;
    List<CourseCardViewModel> _all = [];

    public ObservableCollection<CourseCardViewModel> Courses { get; } = [];
    public ObservableCollection<FilterViewModel> Filters { get; } = [];

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _status = "Reading courses…";
    [ObservableProperty] private bool _busy;

    string _filter = "All";

    public string FolderText => _main.Settings.CoursesFolder;

    public LibraryViewModel(MainViewModel main) => _main = main;

    partial void OnSearchChanged(string value) => Apply();

    public void Refresh()
    {
        Busy = true;
        Task.Run(() =>
        {
            var entries = CourseLibrary.Scan(_main.Settings);
            var cards = entries.Select(e => new CourseCardViewModel(e, _main)).ToList();
            Dispatcher.UIThread.Post(() =>
            {
                _all = cards;
                Busy = false;
                Status = cards.Count == 0 ? "No courses found. Check the courses folder." : $"{cards.Count} course(s)";
                BuildFilters();
                Apply();
            });
        });
    }

    [RelayCommand] private void Reload() => Refresh();

    [RelayCommand] private void OpenSettings() => _main.ShowSettings();

    public void AddCourse(string path)
    {
        if (!_main.Settings.ExtraCourses.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            _main.Settings.ExtraCourses.Add(path);
            _main.Settings.Save();
        }
        Refresh();
    }

    void BuildFilters()
    {
        Filters.Clear();
        foreach (var (label, kind) in new (string, CourseKind?)[] { ("All", null), ("Routed", CourseKind.Routed), ("In progress", CourseKind.InProgress), ("Blank", CourseKind.Blank) })
        {
            int count = kind is null ? _all.Count : _all.Count(c => c.Entry.Kind == kind);
            Filters.Add(new FilterViewModel($"{label} · {count}", label == _filter, () => { _filter = label; BuildFilters(); Apply(); }));
        }
    }

    void Apply()
    {
        Courses.Clear();
        foreach (var c in _all)
        {
            bool kindOk = _filter == "All" || c.KindLabel == _filter;
            bool textOk = string.IsNullOrWhiteSpace(Search) || c.Entry.Name.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase);
            if (kindOk && textOk) Courses.Add(c);
        }
    }
}

public partial class FilterViewModel(string label, bool selected, Action select) : ObservableObject
{
    public string Label { get; } = label;
    public bool IsSelected { get; } = selected;

    [RelayCommand] private void Select() => select();
}

/// <summary>One course card: in-game thumbnail, status, size and the products already built.</summary>
public partial class CourseCardViewModel : ObservableObject
{
    readonly MainViewModel _main;
    public CourseEntry Entry { get; }
    public Bitmap? Thumbnail { get; }
    public bool HasThumbnail => Thumbnail is not null;
    public bool NoThumbnail => Thumbnail is null;

    public string KindLabel => Entry.Kind switch { CourseKind.InProgress => "In progress", CourseKind.Blank => "Blank", _ => "Routed" };
    public IBrush ChipBackground => Entry.Kind switch
    {
        CourseKind.InProgress => Brush.Parse("#E3EDF5"), CourseKind.Blank => Brush.Parse("#EEF0EA"), _ => Brush.Parse("#E4EFE6"),
    };
    public IBrush ChipForeground => Entry.Kind switch
    {
        CourseKind.InProgress => Brush.Parse("#1E4A70"), CourseKind.Blank => Brush.Parse("#3F4A43"), _ => Brush.Parse("#1F4D30"),
    };
    public string Meta => Entry.Error is not null ? "Could not read this file"
        : Entry.Holes == 0 ? "Blank plot · no holes yet"
        : $"{Entry.Holes} holes · par {Entry.Par} · {Entry.Yards:N0} yds";
    public string Saved => Entry.Saved.ToString("d MMM");
    public IReadOnlyList<string> Built { get; }
    public bool HasNote => Entry.Error is not null;
    public string Note => Entry.Error ?? "";

    public CourseCardViewModel(CourseEntry entry, MainViewModel main)
    {
        Entry = entry;
        _main = main;
        if (entry.Thumbnail is { } bytes)
        {
            try { Thumbnail = Bitmap.DecodeToWidth(new MemoryStream(bytes), 480); } catch (Exception) { Thumbnail = null; }
        }
        Built = Products.All.Where(p => File.Exists(Products.FileFor(p, main.Settings, entry.Name))).Select(p => p.Name).ToList();
    }

    [RelayCommand] private void Open()
    {
        if (Entry.Error is null) _main.OpenCourse(Entry);
    }
}
