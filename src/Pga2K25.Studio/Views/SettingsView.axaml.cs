using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Pga2K25.Studio.ViewModels;

namespace Pga2K25.Studio.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => AvaloniaXamlLoader.Load(this);

    async void BrowseCourses_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && await PickFolder("Courses folder") is { } path) vm.CoursesFolder = path;
    }

    async void BrowseOutput_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && await PickFolder("Output folder") is { } path) vm.OutputRoot = path;
    }

    async Task<string?> PickFolder(string title)
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return null;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
}
