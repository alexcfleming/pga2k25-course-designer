using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.Studio.Services;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// Following a rename made in the designer (<see cref="CourseFolders"/>): on open, the course's product folder and
/// its products take the new name, with a notice and Undo; when that can't happen (the name is taken, a file is
/// open) the notice says why and offers the way on. Also "Find earlier products" on the Files card, for a course
/// renamed before Studio stamped its folder.
/// </summary>
public partial class WorkspaceViewModel
{
    ProductPlace _place = null!;
    RenameResult? _rename;

    /// <summary>The course's product folder.</summary>
    public string OutputFolder => _place.Dir;

    /// <summary>The name its products' files start with (the course's name, once a rename has been followed).</summary>
    string ProductStem => _place.Stem;

    [ObservableProperty] private bool _hasRenameNotice;
    [ObservableProperty] private bool _renameBlocked;
    public IBrush RenameBackground => Brush.Parse(RenameBlocked ? "#FAEEDA" : "#E4EFE6");
    public IBrush RenameForeground => Brush.Parse(RenameBlocked ? "#7A4A0E" : "#1F4D30");
    partial void OnRenameBlockedChanged(bool value) { OnPropertyChanged(nameof(RenameBackground)); OnPropertyChanged(nameof(RenameForeground)); }
    [ObservableProperty] private string _renameTitle = "";
    [ObservableProperty] private string _renameDetail = "";
    [ObservableProperty] private bool _canUndoRename;
    [ObservableProperty] private bool _canRetryRename;
    [ObservableProperty] private string _suffixFolderLabel = "";
    [ObservableProperty] private bool _hasSuffixFolder;

    /// <summary>Runs first on open: follows a rename, then stamps the folder so the next one is followed too.</summary>
    void InitProductFolder()
    {
        _rename = null;
        try { _rename = CourseFolders.Follow(Settings, Entry); }
        catch (Exception ex) { Status = $"Could not follow the rename: {ex.Message}"; }
        if (_rename is { Done: true }) Settings.Save();
        try { CourseFolders.Stamp(Settings, Entry); } catch (Exception) { /* unstamped still works */ }
        _place = CourseFolders.PlaceOf(Settings, Entry);
        ShowRename(_rename);
    }

    void ShowRename(RenameResult? r)
    {
        HasRenameNotice = r is not null;
        if (r is null) return;
        RenameBlocked = !r.Done;
        CanUndoRename = r.Done && r.OldName != r.NewName;
        CanRetryRename = !r.Done && r.InUse.Count > 0;
        HasSuffixFolder = !r.Done && r.SuggestedFolder is not null;
        SuffixFolderLabel = r.SuggestedFolder is { } s ? $"Use “{s}”" : "";
        string folder = Path.GetFileName(r.ToDir);
        if (r.Done)
        {
            RenameTitle = r.OldName == r.NewName ? $"Products moved to “{folder}”."
                : $"Renamed in the game: “{r.OldName}” is now “{r.NewName}”.";
            RenameDetail = $"Your products, notes, measurements and settings came across. The folder is now “{folder}”"
                + (r.Renamed > 0 ? $" and {r.Renamed} file{(r.Renamed == 1 ? " was" : "s were")} renamed." : ".")
                + (r.OldName != r.NewName && r.Renamed > 0 ? $" Built pages still print “{r.OldName}” until you rebuild them." : "");
        }
        else if (r.SuggestedFolder is not null)
        {
            RenameTitle = $"Renamed in the game to “{r.NewName}”, but a folder of that name already holds other products.";
            RenameDetail = (string.IsNullOrEmpty(r.TakenBy) ? $"“{folder}” isn't this course's."
                    : $"“{folder}” belongs to {Path.GetFileName(r.TakenBy)}.")
                + $" Your products stay in “{Path.GetFileName(r.FromDir)}” until you choose.";
        }
        else if (r.InUse.Count > 0)
        {
            RenameTitle = $"Renamed in the game to “{r.NewName}”, but {r.InUse.Count} file{(r.InUse.Count == 1 ? " is" : "s are")} open in another app.";
            RenameDetail = string.Join(", ", r.InUse.Take(4).Select(Path.GetFileName)) + (r.InUse.Count > 4 ? $" and {r.InUse.Count - 4} more" : "")
                + $". Nothing was changed: your products are still in “{Path.GetFileName(r.FromDir)}”. Close them and try again.";
        }
        else
        {
            RenameTitle = $"Renamed in the game to “{r.NewName}”, but the products couldn't be renamed.";
            RenameDetail = $"{r.Error} Nothing was changed: your products are still in “{Path.GetFileName(r.FromDir)}”.";
            CanRetryRename = true;
        }
    }

    /// <summary>A rename that is still to happen holds builds back, so new files don't land under the old name.</summary>
    bool RenamePending => _rename is { Done: false };

    [RelayCommand] private void DismissRename() => HasRenameNotice = false;

    [RelayCommand] private void RetryRename() => Reopen(() => CourseFolders.Follow(Settings, Entry));

    [RelayCommand] private void UseSuffixFolder()
    {
        if (_rename?.SuggestedFolder is { } name) Reopen(() => CourseFolders.Follow(Settings, Entry, name));
    }

    [RelayCommand] private void UndoRename()
    {
        if (_rename is not { Done: true } done) return;
        var back = CourseFolders.Undo(Settings, Entry, done);
        Settings.Save();
        if (!back.Done) { Status = back.InUse.Count > 0 ? "Close the open product files to undo the rename" : $"Could not undo: {back.Error}"; return; }
        _main.OpenCourse(Entry);
        if (_main.Current is WorkspaceViewModel w) w.Status = $"Products are back under “{done.OldName}”; “{done.NewName}” starts a folder of its own.";
    }

    /// <summary>Re-runs a rename and opens the course again so every page reads from the folder it ends in.</summary>
    void Reopen(Func<RenameResult?> act)
    {
        RenameResult? r;
        try { r = act(); } catch (Exception ex) { Status = ex.Message; return; }
        Settings.Save();
        if (r is { Done: true })
        {
            _main.OpenCourse(Entry);
            if (_main.Current is WorkspaceViewModel w) { w._rename = r; w.ShowRename(r); }
        }
        else { _rename = r; ShowRename(r); }
    }

    // ───────────────────────── find earlier products ─────────────────────────

    public ObservableCollection<EarlierFolderViewModel> EarlierFolders { get; } = [];
    [ObservableProperty] private bool _earlierSearched;
    [ObservableProperty] private string _earlierSummary = "";

    [RelayCommand] private void FindEarlierProducts()
    {
        EarlierFolders.Clear();
        try
        {
            var library = CourseLibrary.Scan(Settings);
            if (!library.Any(c => string.Equals(c.Path, Entry.Path, StringComparison.OrdinalIgnoreCase))) library.Add(Entry);
            foreach (var f in CourseFolders.Unclaimed(Settings, library))
                EarlierFolders.Add(new EarlierFolderViewModel(f, Adopt));
            EarlierSummary = EarlierFolders.Count == 0 ? "Every product folder belongs to a course in the library."
                : "Product folders no course in the library claims, newest first.";
        }
        catch (Exception ex) { EarlierSummary = $"Could not look: {ex.Message}"; }
        EarlierSearched = true;
    }

    void Adopt(EarlierFolder f)
    {
        RenameResult r;
        try { r = CourseFolders.Adopt(Settings, Entry, f); }
        catch (Exception ex) { EarlierSummary = ex.Message; return; }
        if (!r.Done)
        {
            EarlierSummary = r.Error ?? (r.InUse.Count > 0 ? $"{r.InUse.Count} file(s) in “{Path.GetFileName(f.Dir)}” are open in another app. Close them and try again."
                : $"“{Path.GetFileName(r.ToDir)}” already holds other products.");
            return;
        }
        Settings.Save();
        _main.OpenCourse(Entry);
        if (_main.Current is WorkspaceViewModel w)
        {
            w._rename = r;
            w.ShowRename(r with { OldName = f.Name });
            w.CanUndoRename = false;
        }
    }
}

public partial class EarlierFolderViewModel(EarlierFolder folder, Action<EarlierFolder> adopt) : ObservableObject
{
    public string Name => Path.GetFileName(folder.Dir);
    public string Detail => $"{folder.Files} file{(folder.Files == 1 ? "" : "s")} · last built {folder.LastWrite:d MMM}";
    [RelayCommand] private void Use() => adopt(folder);
}
