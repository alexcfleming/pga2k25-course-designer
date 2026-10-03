using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Pga2K25.Studio.ViewModels;

/// <summary>App-wide settings: where courses are read from, where products go, and the credit line.</summary>
public partial class SettingsViewModel : ObservableObject
{
    readonly MainViewModel _main;

    [ObservableProperty] private string _coursesFolder;
    [ObservableProperty] private string _outputRoot;
    [ObservableProperty] private string _credit;
    [ObservableProperty] private string _error = "";

    public string SettingsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CourseDesignerStudio", "settings.json");

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        _coursesFolder = main.Settings.CoursesFolder;
        _outputRoot = main.Settings.OutputRoot;
        _credit = main.Settings.Credit;
    }

    [RelayCommand] private void Save()
    {
        if (!Directory.Exists(CoursesFolder.Trim())) { Error = "The courses folder doesn't exist."; return; }
        if (string.IsNullOrWhiteSpace(OutputRoot)) { Error = "Choose an output folder."; return; }
        var s = _main.Settings;
        s.CoursesFolder = CoursesFolder.Trim();
        s.OutputRoot = OutputRoot.Trim();
        s.Credit = Credit.Trim();
        s.Save();
        _main.ShowLibrary();
    }

    [RelayCommand] private void Cancel() => _main.ShowLibrary();
}
