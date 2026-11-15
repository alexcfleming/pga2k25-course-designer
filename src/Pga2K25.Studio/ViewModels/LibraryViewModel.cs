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

    /// <summary>The green notice after an archive or restore, with Undo.</summary>
    [ObservableProperty] private string _notice = "";
    [ObservableProperty] private bool _hasNotice;
    (CourseCardViewModel Card, bool Restored, ArchivedCourse? Was)? _last;

    public const string ArchivedFilter = "Archived";
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
            var folders = CourseFolders.Read(_main.Settings.OutputRoot);
            var cards = entries.Select(e => new CourseCardViewModel(e, _main, folders)).ToList();
            Dispatcher.UIThread.Post(() =>
            {
                _all = cards;
                Busy = false;
                Status = cards.Count == 0 ? "No courses found. Check the courses folder." : "";   // the All chip has the count
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
        var live = _all.Where(c => !c.IsArchived).ToList();
        int archived = _all.Count - live.Count;
        if (archived == 0 && _filter == ArchivedFilter) _filter = "All";
        foreach (var (label, kind) in new (string, CourseKind?)[] { ("All", null), ("Routed", CourseKind.Routed), ("In progress", CourseKind.InProgress), ("Blank", CourseKind.Blank) })
        {
            int count = kind is null ? live.Count : live.Count(c => c.Entry.Kind == kind);
            Filters.Add(new FilterViewModel($"{label} · {count}", label == _filter, () => { _filter = label; BuildFilters(); Apply(); }));
        }
        // Archived courses have a view of their own, shown once there are any.
        if (archived > 0)
            Filters.Add(new FilterViewModel($"{ArchivedFilter} · {archived}", _filter == ArchivedFilter, () => { _filter = ArchivedFilter; BuildFilters(); Apply(); }, archive: true));
    }

    void Apply()
    {
        Courses.Clear();
        foreach (var c in _all)
            if (Shows(_filter, Search, c.KindLabel, c.Entry.Name, c.IsArchived)) Courses.Add(c);
    }

    /// <summary>Whether a card shows under a filter and search: archived courses only in the Archived view, the rest by kind.</summary>
    public static bool Shows(string filter, string search, string kind, string name, bool archived)
    {
        bool viewOk = filter == ArchivedFilter ? archived : !archived && (filter == "All" || kind == filter);
        return viewOk && (string.IsNullOrWhiteSpace(search) || name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Archive or restore a card's course (Studio only: nothing on disk changes), with a notice to undo it.</summary>
    public void ToggleArchive(CourseCardViewModel card)
    {
        var settings = _main.Settings;
        var was = settings.ArchiveOf(card.Entry);
        if (was is null)
        {
            settings.Archive(card.Entry);
            Notice = $"Archived {card.Entry.Name}. It's hidden here; the course file and its products are untouched.";
        }
        else
        {
            settings.Restore(card.Entry);
            Notice = $"Restored {card.Entry.Name}. It's back with your courses.";
        }
        settings.Save();
        _last = (card, was is not null, was);
        HasNotice = true;
        card.Changed();
        BuildFilters();
        Apply();
    }

    [RelayCommand] private void Undo()
    {
        if (_last is not { } last) return;
        var settings = _main.Settings;
        if (last.Restored && last.Was is { } a) settings.Archived.Add(a);
        else settings.Restore(last.Card.Entry);
        settings.Save();
        last.Card.Changed();
        CloseNotice();
        BuildFilters();
        Apply();
    }

    [RelayCommand] private void CloseNotice()
    {
        _last = null;
        HasNotice = false;
        Notice = "";
    }
}

public partial class FilterViewModel(string label, bool selected, Action select, bool archive = false) : ObservableObject
{
    public string Label { get; } = label;
    public bool IsSelected { get; } = selected;
    /// <summary>The Archived view's chip, set apart from the kinds.</summary>
    public bool IsArchive { get; } = archive;

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
    /// <summary>"Renamed from …" when the designer renamed the course since its products were built.</summary>
    public string RenamedFrom { get; } = "";
    public bool IsRenamed => RenamedFrom.Length > 0;
    public string Note => Entry.Error ?? "";

    /// <summary>Hidden from the library by Archive (the file and its products are untouched).</summary>
    public bool IsArchived => _main.Settings.IsArchived(Entry);
    public string ArchiveLabel => IsArchived ? "Restore" : "Archive";
    /// <summary>Segoe Fluent / MDL2: archive box, or undo arrow to restore.</summary>
    public string ArchiveGlyph => IsArchived ? "\uE7A7" : "\uE7B8";
    public string ArchiveTip => IsArchived ? "Put this course back with your courses" : "Hide this course from the library. Nothing is deleted or moved.";
    public double CardOpacity => IsArchived ? 0.62 : 1;
    /// <summary>"Archived 14 Nov · saved since you archived it" on an archived card.</summary>
    public string ArchivedNote => _main.Settings.ArchiveOf(Entry) is { } a
        ? $"Archived {a.Archived:d MMM}" + (Entry.Saved > a.Archived ? " · saved since you archived it" : " · the file is still in its folder")
        : "";

    public void Changed()
    {
        foreach (var p in new[] { nameof(IsArchived), nameof(ArchiveLabel), nameof(ArchiveGlyph), nameof(ArchiveTip), nameof(CardOpacity), nameof(ArchivedNote) })
            OnPropertyChanged(p);
    }

    [RelayCommand] private void ToggleArchive() => _main.Library.ToggleArchive(this);

    public CourseCardViewModel(CourseEntry entry, MainViewModel main, IReadOnlyList<StampedFolder>? folders = null)
    {
        Entry = entry;
        _main = main;
        if (entry.Thumbnail is { } bytes)
        {
            try { Thumbnail = Bitmap.DecodeToWidth(new MemoryStream(bytes), 480); } catch (Exception) { Thumbnail = null; }
        }
        // A course renamed in the designer still finds its products, under the name they were built with.
        folders ??= CourseFolders.Read(main.Settings.OutputRoot);
        var place = CourseFolders.PlaceOf(main.Settings, entry, folders);
        Built = Products.All.Where(p => Products.BuiltFile(p, place) is not null).Select(p => p.Name).ToList();
        RenamedFrom = CourseFolders.RenamedFrom(main.Settings, entry, folders) is { } old ? $"Renamed from {old}: products follow when you open it" : "";
    }

    [RelayCommand] private void Open()
    {
        if (Entry.Error is null) _main.OpenCourse(Entry);
    }
}
