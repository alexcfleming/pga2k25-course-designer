using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Pga2K25.Studio.ViewModels;
using Pga2K25.TerrainMap;

namespace Pga2K25.Studio.Views;

/// <summary>
/// Ties the terrain map to the workspace: the map image, sketch layers and note pins; hover, clicks and pin drags;
/// the hole-sheet view and zoom / turn controls; and the file pickers.
/// </summary>
public partial class WorkspaceView : UserControl
{
    readonly MapSurface _map;
    readonly TextBlock _zoomText;
    readonly Panel _northArrow;
    WorkspaceViewModel? _vm;

    public WorkspaceView()
    {
        AvaloniaXamlLoader.Load(this);
        _map = this.FindControl<MapSurface>("MapView")!;
        _zoomText = this.FindControl<TextBlock>("ZoomText")!;
        _northArrow = this.FindControl<Panel>("NorthArrow")!;
        _northArrow.RenderTransform = new RotateTransform();
        _map.MapPointerMoved += p => _vm?.Hover(p);
        _map.MapClicked += p => _vm?.MapClicked(p);
        _map.PinClicked += id => _vm?.PinClicked(id);
        _map.PinMoved += (id, p) => _vm?.PinMoved(id, p);
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
        }
        _vm = DataContext as WorkspaceViewModel;
        if (_vm is null) return;
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
    }

    void PullLayers()
    {
        if (_vm is null) return;
        _map.Sketches = _vm.CurrentSketchLayers();
        _map.Lines = _vm.CurrentLineLayers();
        _map.Pins = _vm.CurrentPins();
        _map.SelectedPin = _vm.SelectedPinId;
    }

    void CentreOn(Point px) => _map.CentreOn(px);
    void ShowFrame(IReadOnlyList<Point> corners, Vector up) => _map.ShowFrame(corners, up);
    void Fit() => _map.Fit();

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

    async void BrowseWorkbook_Click(object? sender, RoutedEventArgs e)
    {
        if (await PickFile("Choose the hole-data workbook", "Workbook", ["*.xlsx", "*.csv"]) is { } path) _vm?.SetWorkbook(path);
    }

    async void BrowseCardConfig_Click(object? sender, RoutedEventArgs e)
    {
        if (await PickFile("Choose course card settings", "Card settings", ["*.json"]) is { } path) _vm?.SetCardConfig(path);
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
