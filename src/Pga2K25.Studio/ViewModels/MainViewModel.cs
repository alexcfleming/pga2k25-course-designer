using CommunityToolkit.Mvvm.ComponentModel;
using Pga2K25.Studio.Services;

namespace Pga2K25.Studio.ViewModels;

/// <summary>The window: the library, or one course's workspace.</summary>
public partial class MainViewModel : ObservableObject
{
    public StudioSettings Settings { get; } = StudioSettings.Load();
    public LibraryViewModel Library { get; }

    [ObservableProperty] private ObservableObject _current;

    public MainViewModel()
    {
        Library = new LibraryViewModel(this);
        _current = Library;
        Library.Refresh();
    }

    public void OpenCourse(CourseEntry entry) => Current = new WorkspaceViewModel(this, entry);

    public void ShowLibrary()
    {
        Current = Library;
        Library.Refresh();
    }
}
