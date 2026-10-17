using Avalonia;

namespace Pga2K25.Studio;

static class Program
{
    /// <summary>Unhandled errors are appended here, so a crash on a tester's machine can be sent back.</summary>
    public static string CrashLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CourseDesignerStudio", "crash.log");

    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => LogCrash(e.Exception);
        try
        {
            AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            throw;
        }
    }

    public static void LogCrash(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath,
                $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} · version {typeof(Program).Assembly.GetName().Version}{Environment.NewLine}{ex}{Environment.NewLine}");
        }
        catch { /* never let logging hide the original error */ }
    }
}
