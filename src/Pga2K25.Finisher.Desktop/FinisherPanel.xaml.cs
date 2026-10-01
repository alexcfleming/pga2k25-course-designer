using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Pga2K25.Finisher.Greens;

namespace Pga2K25.Finisher.Desktop;

/// <summary>What the Finisher form needs from the window hosting it.</summary>
public interface IFinisherHost
{
    /// <summary>The course to finish, or null (after telling the user) when none is selected.</summary>
    string? CoursePath { get; }

    /// <summary>Folder for the default "&lt;course&gt; - Finished.course", or null for next to the input.</summary>
    string? OutputFolder { get; }

    /// <summary>Runs <paramref name="work"/> off the UI thread, streaming its log; returns when done.</summary>
    Task Run(Button button, string title, Func<IProgress<string>, string?> work);
}

/// <summary>The Course Finisher form (the reference tool's four option groups plus Analyze / Create).</summary>
public partial class FinisherPanel : UserControl
{
    public FinisherPanel() => InitializeComponent();

    public IFinisherHost? Host { get; set; }

    /// <summary>Clears a chosen output path (call when the input course changes).</summary>
    public void ResetOutput() => FinishedBox.Text = "";

    void BrowseFinished_Click(object sender, RoutedEventArgs e)
    {
        string course = Host?.CoursePath ?? "";
        var dialog = new SaveFileDialog
        {
            Title = "Save finished course as",
            Filter = "Golf Course (*.course)|*.course|All files|*.*",
            DefaultExt = ".course",
            FileName = course.Length > 0 ? Path.GetFileName(FinishedPath(course)) : "",
            InitialDirectory = Host?.OutputFolder is { } dir && Directory.Exists(dir) ? dir : "",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) FinishedBox.Text = dialog.FileName;
    }

    /// <summary>The finished-course path: the box, else "&lt;course&gt; - Finished.course" in the output folder.</summary>
    string FinishedPath(string course)
    {
        if (FinishedBox.Text.Trim() is { Length: > 0 } chosen) return chosen;
        string folder = Host?.OutputFolder ?? "";
        string name = Path.GetFileName(CourseFinisher.DefaultOutputPath(course));
        return Path.Combine(folder.Length > 0 ? folder : Path.GetDirectoryName(Path.GetFullPath(course))!, name);
    }

    async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (Host?.CoursePath is not string course) return;
        await Host!.Run(AnalyzeButton, "COURSE ANALYSIS", progress =>
        {
            progress.Report(CourseFinisher.Analyze(course).Summary());
            return null;
        });
    }

    static List<string> Checked(Panel panel) =>
        panel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();

    async void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (Host?.CoursePath is not string course) return;
        static double? Metres(string text) =>
            double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
        var options = new FinisherOptions
        {
            DesignerSmooth = Checked(SmoothPanel),
            Optimize = Checked(OptimizePanel),
            CompleteTees = CompleteTeesBox.IsChecked == true,
            TargetTeesPerHole = TargetTeesBox.SelectedIndex + 1,
            FlattenTees = LevelTeesBox.IsChecked == true,
            HeavyRough = HeavyRoughBox.IsChecked == true,
            SmartPins = PinsBox.IsChecked == true,
            PinMode = PinModeBox.SelectedIndex == 1 ? SmartPinMode.Rebuild : SmartPinMode.Repair,
            UltraSafeGreens = UltraSafeBox.IsChecked == true,
            UltraSafeSeverity = (GreenSafety)Math.Max(0, SeverityBox.SelectedIndex),
            TopographicWeathering = WeatheringBox.IsChecked == true,
            ExportGreensBook = FinisherBookBox.IsChecked == true,
            FlattenCartPaths = CartPathsBox.IsChecked == true,
            ReplaceLegacyCartPaths = ReplaceLegacyBox.IsChecked == true,
            PreserveManualCartPathEdits = PreserveManualBox.IsChecked == true,
            GenerateWater = WaterBox.IsChecked == true,
            ReplaceWater = ReplaceWaterBox.IsChecked == true,
            WaterClearance = Metres(ClearanceBox.Text) ?? double.NaN,
            WaterMergeSpread = Metres(MergeSpreadBox.Text) ?? double.NaN,
        };
        if (options.Validate() is string problem) { MessageBox.Show(Window.GetWindow(this)!, problem, "Course Finisher"); return; }
        string finished = FinishedPath(course);
        if (string.Equals(Path.GetFullPath(finished), Path.GetFullPath(course), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(Window.GetWindow(this)!, "Output must be a new file; the finisher will not overwrite the source course.", "Course Finisher");
            return;
        }
        await Host!.Run(FinishButton, "FINISHING COURSE", progress =>
        {
            var report = CourseFinisher.Finish(course, finished, options, progress);
            progress.Report($"Saved: {report.OutputPath}");
            if (report.GreensBook is { } book) progress.Report($"Greens Book: {book.Pdf}");
            if (report.After is { } after) progress.Report(after.Summary());
            return report.GreensBook?.Pdf;   // "Open Last Output" opens the book; the .course itself has no viewer
        });
    }

}
