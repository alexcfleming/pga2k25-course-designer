using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Pga2K25.CourseFormat;
using Pga2K25.GreensBook;

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

    async void Generate_Click(object sender, RoutedEventArgs e)
    {
        string course = CourseBox.Text.Trim(), output = OutputBox.Text.Trim();
        if (!File.Exists(course)) { MessageBox.Show(this, "Select a .course file first.", "Greens Book"); return; }
        if (output.Length == 0) output = Path.GetDirectoryName(course)!;

        var options = new GreensBookOptions
        {
            Credit = CreditBox.Text.Trim(),
            ColourSlopes = ColourBox.IsChecked == true,
            Terrain = new TerrainModelOptions { Hardness = HardnessSlider.Value },
        };
        var outputs = new GreensBookOutputs
        {
            GreenStudies = StudiesBox.IsChecked == true,
            Pdf = PdfBox.IsChecked == true,
        };
        var progress = new Progress<string>(Log);   // marshals back to the UI thread

        GenerateButton.IsEnabled = false;
        OpenButton.IsEnabled = false;
        Log("");
        Log("=== CREATING GREENS BOOK ===");
        try
        {
            var result = await Task.Run(() => GreensBookBuilder.Build(course, output, options, outputs, log: progress));
            _lastOutput = result.SheetPath ?? result.PdfPath;
            OpenButton.IsEnabled = true;
            int flagged = result.Greens.Count(g => g.Warnings.Any(w => w.StartsWith("Implausibly")));
            if (flagged > 0)
                Log($"{flagged} green(s) look implausibly steep; the terrain model is probably wrong for those holes.");
            Log("Done.");
        }
        catch (Exception ex)
        {
            Log($"Failed: {ex.Message}");
        }
        finally
        {
            GenerateButton.IsEnabled = true;
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
