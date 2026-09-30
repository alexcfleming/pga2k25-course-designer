using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;
using Pga2K25.Maps;

namespace Pga2K25.App;

public partial class MainWindow : Window
{
    string? _lastOutput;

    public MainWindow() => InitializeComponent();

    void BrowseCourse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select course", Filter = "Golf Course (*.course)|*.course|All files|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        CourseBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(OutputBox.Text))
            OutputBox.Text = Path.GetDirectoryName(dialog.FileName);
        Summarize(dialog.FileName);
    }

    void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Output folder" };
        if (dialog.ShowDialog(this) == true) OutputBox.Text = dialog.FolderName;
    }

    void BrowseData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select hole data", Filter = "Hole data (*.xlsx;*.csv)|*.xlsx;*.csv|All files|*.*" };
        if (dialog.ShowDialog(this) == true) DataBox.Text = dialog.FileName;
    }

    void Summarize(string path)
    {
        try
        {
            var model = CourseFile.Load(path).ReadModel();
            int splineGreens = model.Holes.Count(h => model.SurfaceSplines.Any(s =>
                s.Surface == Surfaces.Green && s.Closed && Geometry.Contains(Geometry.SampleSpline(s), h.GreenAnchor)));
            Log($"{model.Name}: {model.Holes.Count} holes, par {model.Holes.Sum(h => h.Par)}, " +
                $"{model.HeightStamps.Count} terrain stamps, {splineGreens} hole(s) with a drawn green outline.");
        }
        catch (Exception ex)
        {
            Log($"Could not read course: {ex.Message}");
        }
    }

    bool TryInputs(out string course, out string output)
    {
        course = CourseBox.Text.Trim();
        output = OutputBox.Text.Trim();
        if (!File.Exists(course)) { MessageBox.Show(this, "Select a .course file first.", "Course Designer"); return false; }
        if (output.Length == 0) output = Path.GetDirectoryName(course)!;
        return true;
    }

    async void Greens_Click(object sender, RoutedEventArgs e)
    {
        if (!TryInputs(out var course, out var output)) return;
        var options = new GreensBookOptions
        {
            Credit = CreditBox.Text.Trim(),
            ColourSlopes = ColourBox.IsChecked == true,
            Terrain = new TerrainModelOptions { Hardness = HardnessSlider.Value },
        };
        var outputs = new GreensBookOutputs { GreenStudies = StudiesBox.IsChecked == true, Pdf = PdfBox.IsChecked == true };
        await Run(GreensButton, "GREENS BOOK", progress =>
        {
            var result = GreensBookBuilder.Build(course, output, options, outputs, log: progress);
            int flagged = result.Greens.Count(g => g.Warnings.Any(w => w.StartsWith("Implausibly")));
            if (flagged > 0) progress.Report($"{flagged} green(s) look implausibly steep; the terrain model is probably wrong for those holes.");
            return result.SheetPath ?? result.PdfPath;
        });
    }

    async void Map_Click(object sender, RoutedEventArgs e)
    {
        if (!TryInputs(out var course, out var output)) return;
        var themes = new List<MapThemeKind>();
        if (VintageBox.IsChecked == true) themes.Add(MapThemeKind.Vintage);
        if (ModernBox.IsChecked == true) themes.Add(MapThemeKind.Modern);
        if (themes.Count == 0) { MessageBox.Show(this, "Pick at least one theme.", "Scorecard Map"); return; }
        HoleData? data = null;
        if (DataBox.Text.Trim() is { Length: > 0 } dataPath)
        {
            try { data = HoleData.Load(dataPath); }
            catch (Exception ex) { MessageBox.Show(this, $"Could not read hole data: {ex.Message}", "Scorecard Map"); return; }
        }
        var options = new ScorecardMapOptions
        {
            Data = data,
            ShowWaste = WasteBox.IsChecked == true,
            MergeBunkers = MergeBox.IsChecked == true,
            BunkerMergeMetres = double.TryParse(MergeMetresBox.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var mm) && mm > 0 ? mm : 10,
            Credit = CreditBox.Text.Trim(),
            Subtitle = SubtitleBox.Text.Trim(),
            AutoRotate = RotateBox.IsChecked == true,
            ShowPaths = PathsBox.IsChecked == true,
            Orientation = (PageOrientation)OrientationBox.SelectedIndex,
        };
        bool pdf = MapPdfBox.IsChecked == true;
        await Run(MapButton, "SCORECARD MAP", progress =>
            ScorecardMapBuilder.Build(course, output, options, themes, pdf, log: progress).Files.FirstOrDefault());
    }

    async Task Run(Button button, string title, Func<IProgress<string>, string?> work)
    {
        var progress = new Progress<string>(Log);   // marshals back to the UI thread
        button.IsEnabled = false;
        OpenButton.IsEnabled = false;
        Log("");
        Log($"=== {title} ===");
        try
        {
            _lastOutput = await Task.Run(() => work(progress));
            OpenButton.IsEnabled = _lastOutput is not null;
            Log("Done.");
        }
        catch (Exception ex)
        {
            Log($"Failed: {ex.Message}");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_lastOutput is not null && File.Exists(_lastOutput))
            Process.Start(new ProcessStartInfo(_lastOutput) { UseShellExecute = true });
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        string folder = OutputBox.Text.Trim();
        if (Directory.Exists(folder))
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    void Log(string line)
    {
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
    }
}
