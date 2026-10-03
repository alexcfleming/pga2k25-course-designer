using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Pga2K25.Studio.ViewModels;

namespace Pga2K25.Studio.Views;

public partial class LibraryView : UserControl
{
    public LibraryView() => AvaloniaXamlLoader.Load(this);

    async void AddCourse_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LibraryViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add a course file",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Golf course") { Patterns = ["*.course"] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) vm.AddCourse(path);
    }
}
