using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.TerrainMap;
using Pga2K25.View3D;

namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// The terrain map's 3D view: the live map's own survey (same area, ground, sea and water) built into one web page,
/// which the workspace shows in a web view in place of the map. The page is rebuilt whenever the map is surveyed
/// again while the view is on. Studio only for now: no product is written.
/// </summary>
public partial class WorkspaceViewModel
{
    /// <summary>Where built pages are kept: scratch files, one per course, replaced on each build.</summary>
    static readonly string View3DFolder = Path.Combine(Path.GetTempPath(), "CourseDesignerStudio", "3D view");

    [ObservableProperty] private bool _view3D;
    /// <summary>What to say in place of the model while there isn't one: building, or why it failed.</summary>
    [ObservableProperty] private string _view3DStatus = "";
    [ObservableProperty] private bool _view3DBuilding;
    /// <summary>The built page as a file address for the web view; null until it is built.</summary>
    [ObservableProperty] private Uri? _view3DPage;

    TerrainSurvey? _view3DSurvey;   // the survey the page was built from
    string? _view3DFile;
    int _view3DBuild;

    /// <summary>Raised when a hole is picked while the 3D view is on, so the page can show it from the tee; 0 asks for the whole course.</summary>
    public event Action<int>? View3DHoleRequested;

    partial void OnView3DChanged(bool value)
    {
        if (value) RefreshView3D();
    }

    /// <summary>Builds the page for the current survey if the 3D view is on and shows an older one (or none).</summary>
    void RefreshView3D()
    {
        if (!View3D) return;
        if (_survey is not { } survey)
        {
            View3DStatus = "Waiting for the terrain…";
            View3DBuilding = true;
            return;
        }
        if (ReferenceEquals(survey, _view3DSurvey)) return;
        _view3DSurvey = survey;
        int build = ++_view3DBuild;
        string name = Entry.Name;
        View3DPage = null;
        View3DStatus = "Building the 3D model…";
        View3DBuilding = true;
        Task.Run(() =>
        {
            try
            {
                var options = new View3DOptions();
                string html = View3DBuilder.Html(Scene3D.From(survey, options), name, options);
                Directory.CreateDirectory(View3DFolder);
                string file = Path.Combine(View3DFolder, string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim() + ".html");
                File.WriteAllText(file, html, new UTF8Encoding(false));
                Dispatcher.UIThread.Post(() =>
                {
                    if (build != _view3DBuild) return;   // a newer survey is already being built
                    _view3DFile = file;
                    // The build number makes each page a new address, so the web view loads it afresh.
                    View3DPage = new UriBuilder(new Uri(file)) { Query = $"host=studio&build={build}" }.Uri;
                    View3DStatus = "";
                    View3DBuilding = false;
                });
            }
            catch (Exception ex)
            {
                Program.LogCrash(ex);
                Dispatcher.UIThread.Post(() =>
                {
                    if (build != _view3DBuild) return;
                    _view3DSurvey = null;
                    View3DStatus = $"The 3D model could not be built: {ex.Message}";
                    View3DBuilding = false;
                });
            }
        });
    }

    /// <summary>The web view could not start (no WebView2 runtime, say): say so, and the page can still go to a browser.</summary>
    public void View3DFailed(string why)
    {
        View3DStatus = $"The 3D view could not be shown here: {why} Use “Open in browser” instead.";
        View3DBuilding = false;
    }

    [RelayCommand]
    private void OpenView3DInBrowser()
    {
        if (_view3DFile is { } file) OpenPath(file);
    }
}
