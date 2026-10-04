using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Pga2K25.Studio.ViewModels;

namespace Pga2K25.Studio.Views;

/// <summary>Ties the terrain viewer to the workspace: hover readout, centring on a hole, and the settings pickers.</summary>
public partial class WorkspaceView : UserControl
{
    readonly ZoomImage _map;
    WorkspaceViewModel? _vm;

    public WorkspaceView()
    {
        AvaloniaXamlLoader.Load(this);
        _map = this.FindControl<ZoomImage>("MapView")!;
        _map.ImagePointerMoved += p => _vm?.Hover(p);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.CentreOn -= CentreOn;
        _vm = DataContext as WorkspaceViewModel;
        if (_vm is not null) _vm.CentreOn += CentreOn;
    }

    void CentreOn(Point px) => _map.CentreOn(px);

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
