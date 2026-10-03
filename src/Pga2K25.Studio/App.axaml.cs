using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Pga2K25.Studio.ViewModels;
using Pga2K25.Studio.Views;

namespace Pga2K25.Studio;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var main = new MainViewModel();
            // A course path on the command line opens straight into that course.
            if (desktop.Args?.FirstOrDefault(a => a.EndsWith(".course", StringComparison.OrdinalIgnoreCase) && File.Exists(a)) is { } path)
                main.OpenCourse(Services.CourseLibrary.Read(Path.GetFullPath(path)));
            desktop.MainWindow = new MainWindow { DataContext = main };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
