using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Pga2K25.Studio.ViewModels;
using Pga2K25.TerrainMap;

namespace Pga2K25.Studio.Views;

/// <summary>
/// Ties the terrain map to the workspace: the map image, sketch layers, note pins and routing-test arrows; hover,
/// clicks, pin and arrow drags; the hole-sheet view and zoom / turn controls; and the file pickers.
/// </summary>
public partial class WorkspaceView : UserControl
{
    readonly MapSurface _map;
    readonly TextBlock _zoomText;
    readonly Panel _northArrow;
    readonly Panel _view3DHost;
    NativeWebView? _web;
    Uri? _webPage;
    bool _webStarted;
    WorkspaceViewModel? _vm;

    public WorkspaceView()
    {
        AvaloniaXamlLoader.Load(this);
        _map = this.FindControl<MapSurface>("MapView")!;
        _zoomText = this.FindControl<TextBlock>("ZoomText")!;
        _northArrow = this.FindControl<Panel>("NorthArrow")!;
        _northArrow.RenderTransform = new RotateTransform();
        _view3DHost = this.FindControl<Panel>("View3DHost")!;
        _map.MapPointerMoved += p => _vm?.Hover(p);
        _map.MapClicked += p => _vm?.MapClicked(p);
        _map.PinClicked += id => _vm?.PinClicked(id);
        _map.PinMoved += (id, p) => _vm?.PinMoved(id, p);
        _map.ArrowPlaced += (p, d) => _vm?.ArrowPlaced(p, d);
        _map.ArrowClicked += id => _vm?.ArrowClicked(id);
        _map.ArrowMoved += (id, p) => _vm?.ArrowMoved(id, p);
        _map.ArrowAimed += (id, leg, d) => _vm?.ArrowAimed(id, leg, d);
        _map.ViewChanged += UpdateViewReadout;
        UpdateViewReadout();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.CentreOn -= CentreOn;
            _vm.LayersChanged -= PullLayers;
            _vm.ShowFrameRequested -= ShowFrame;
            _vm.FitRequested -= Fit;
            _vm.NoteEditorFocusRequested -= FocusNoteEditor;
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.View3DHoleRequested -= ShowHoleIn3D;
        }
        _vm = DataContext as WorkspaceViewModel;
        UpdateView3D();
        if (_vm is null) return;
        _vm.View3DHoleRequested += ShowHoleIn3D;
        _vm.CentreOn += CentreOn;
        _vm.LayersChanged += PullLayers;
        _vm.ShowFrameRequested += ShowFrame;
        _vm.FitRequested += Fit;
        _vm.NoteEditorFocusRequested += FocusNoteEditor;
        _vm.PropertyChanged += OnVmPropertyChanged;
        _map.Source = _vm.MapImage;
        _map.Placing = _vm.PlacingNote;
        PullLayers();
    }

    void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null) return;
        if (e.PropertyName == nameof(WorkspaceViewModel.MapImage)) _map.Source = _vm.MapImage;
        else if (e.PropertyName == nameof(WorkspaceViewModel.PlacingNote)) _map.Placing = _vm.PlacingNote;
        else if (e.PropertyName is nameof(WorkspaceViewModel.View3D) or nameof(WorkspaceViewModel.View3DPage)) UpdateView3D();
        else if (e.PropertyName == nameof(WorkspaceViewModel.Section))
            // A page picked from one of the icon rail's pop-out lists closes the list.
            foreach (var name in new[] { "ProductsRail", "SetupRail", "RecentRail" })
                this.FindControl<Button>(name)?.Flyout?.Hide();
    }

    void PullLayers()
    {
        if (_vm is null) return;
        _map.Sketches = _vm.CurrentSketchLayers();
        _map.Lines = _vm.CurrentLineLayers();
        _map.Pins = _vm.CurrentPins();
        _map.SelectedPin = _vm.SelectedPinId;
        _map.Arrows = _vm.CurrentArrows();
        _map.Walks = _vm.CurrentWalks();
        _map.SelectedArrow = _vm.SelectedArrowId;
        _map.ShowRanges = _vm.ShowRanges;
        _map.ShowZones = _vm.ShowZones;
        _map.ShowCorridors = _vm.ShowCorridors;
        _map.Conflicts = _vm.CurrentConflicts();
        _map.ArrowTemplate = _vm.CurrentArrowTemplate();
    }

    /// <summary>
    /// Shows the built 3D page in a web view over the map, or hides it. The web view is a native window made the
    /// first time it is wanted (so a session that never opens the 3D view never starts one), and it is shown only
    /// while there is a page: nothing of Studio's can draw on top of it, so the "building" note sits beneath.
    /// </summary>
    void UpdateView3D()
    {
        bool show = _vm is { View3D: true, View3DPage: not null };
        if (show && _web is null)
        {
            try
            {
                _web = new NativeWebView();
                _web.EnvironmentRequested += (_, e) =>
                {
                    // Its browser profile goes with Studio's other files, not beside the program.
                    if (e is WindowsWebView2EnvironmentRequestedEventArgs windows)
                        windows.UserDataFolder = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CourseDesignerStudio", "WebView2");
                };
                _web.AdapterCreated += (_, _) => _webStarted = true;
                _view3DHost.Children.Add(_web);
                // A web view that can't start (no WebView2 runtime, say) fails later and off this call, so it is
                // checked after a wait: if its browser never came up, say so in its place.
                DispatcherTimer.RunOnce(() =>
                {
                    if (_webStarted || _web is null) return;
                    _view3DHost.Children.Remove(_web);
                    _web = null;
                    _webPage = null;
                    _vm?.View3DFailed("its built-in browser (Microsoft Edge WebView2) did not start.");
                }, TimeSpan.FromSeconds(12));
            }
            catch (Exception ex)
            {
                Program.LogCrash(ex);
                _web = null;
                _vm!.View3DFailed(ex.Message);
                return;
            }
        }
        if (_web is null) return;
        _web.IsVisible = show;
        if (show && _vm!.View3DPage != _webPage) _web.Source = _webPage = _vm.View3DPage!;
    }

    async void ShowHoleIn3D(int number)
    {
        if (_web is not { IsVisible: true } web) return;
        try { await web.InvokeScript($"window.view3d && view3d.showHole({number})"); }
        catch (Exception ex) { Program.LogCrash(ex); }
    }

    void CentreOn(Point px) => _map.CentreOn(px);
    void ShowFrame(IReadOnlyList<Point> corners, Vector up) => _map.ShowFrame(corners, up);
    void Fit() => _map.Fit(baseDegrees: _vm?.GameView == true ? 180 : 0);

    void FocusNoteEditor() =>
        Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("NoteText")?.Focus(), DispatcherPriority.Background);

    void UpdateViewReadout()
    {
        _zoomText.Text = $"{_map.Zoom * 100:0} %";
        ((RotateTransform)_northArrow.RenderTransform!).Angle = _map.RotationDegrees;
    }

    void ZoomIn_Click(object? sender, RoutedEventArgs e) => _map.ZoomBy(1.25);
    void ZoomOut_Click(object? sender, RoutedEventArgs e) => _map.ZoomBy(1 / 1.25);
    void RotateLeft_Click(object? sender, RoutedEventArgs e) => _map.RotateBy(-15);
    void RotateRight_Click(object? sender, RoutedEventArgs e) => _map.RotateBy(15);
    void NorthUp_Pressed(object? sender, PointerPressedEventArgs e) => _map.NorthUp();

    async void ImportScans_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || TopLevel.GetTopLevel(this) is not { } top) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose scanned hole sheets",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Scans (PDF or images)") { Patterns = ScanPages.Patterns }],
        });
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0) await _vm.ImportScansAsync(paths);
    }

    async void BrowseCardConfig_Click(object? sender, RoutedEventArgs e)
    {
        if (await PickFile("Choose course card settings", "Card settings", ["*.json"]) is { } path) _vm?.SetCardConfig(path);
    }

    async void ChooseLogo_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not LogoSlotViewModel slot) return;
        if (await PickFile($"Choose the {slot.Tip.ToLowerInvariant()} logo", "Images", ["*.png", "*.jpg", "*.jpeg", "*.webp"]) is { } path)
            _vm?.ImportLogo(slot, path);
    }

    async void ImportReadiness_Click(object? sender, RoutedEventArgs e)
    {
        if (await PickFile("Choose the course QA checklist backup", "Checklist backup", ["*.json"]) is { } path) _vm?.Readiness?.ImportBackup(path);
    }

    async Task<string?> PickFile(string title, string kind, IReadOnlyList<string> patterns)
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(kind) { Patterns = patterns }],
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
}
