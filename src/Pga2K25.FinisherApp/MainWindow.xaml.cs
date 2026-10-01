using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Pga2K25.Finisher;
using Pga2K25.Finisher.Desktop;

namespace Pga2K25.FinisherApp;

/// <summary>Standalone Course Finisher window: input course, the finisher form, and the log.</summary>
public partial class MainWindow : Window, IFinisherHost
{
    string? _lastOutput;

    public MainWindow()
    {
        InitializeComponent();
        Finisher.Host = this;
        Log($"Course Finisher (C# port of Mal's TGC 2K25 Course Finisher {CourseFinisher.ReferenceVersion}) ready.");
    }

    public string? CoursePath
    {
        get
        {
            string course = CourseBox.Text.Trim();
            if (File.Exists(course)) return course;
            MessageBox.Show(this, "Select an input .course file first.", "Course Finisher");
            return null;
        }
    }

    /// <summary>Default output goes next to the input.</summary>
    public string? OutputFolder => null;

    void BrowseCourse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select course", Filter = "Golf Course (*.course)|*.course|All files|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        CourseBox.Text = dialog.FileName;
        Finisher.ResetOutput();
        string path = dialog.FileName;
        _ = Run((Button)sender, "COURSE ANALYSIS", progress =>
        {
            progress.Report(CourseFinisher.Analyze(path).Summary());
            return null;
        });
    }

    public async Task Run(Button button, string title, Func<IProgress<string>, string?> work)
    {
        var progress = new Progress<string>(Log);   // marshals back to the UI thread
        button.IsEnabled = false;
        Log("");
        Log($"=== {title} ===");
        try
        {
            _lastOutput = await Task.Run(() => work(progress)) ?? _lastOutput;
            Log("Done.");
        }
        catch (Exception ex)
        {
            Log($"Failed: {ex.Message}");
        }
        finally
        {
            button.IsEnabled = true;
            OpenButton.IsEnabled = _lastOutput is not null;
        }
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_lastOutput is not null && File.Exists(_lastOutput))
            Process.Start(new ProcessStartInfo(_lastOutput) { UseShellExecute = true });
    }

    void Clear_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

    void Log(string line)
    {
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
    }
}
