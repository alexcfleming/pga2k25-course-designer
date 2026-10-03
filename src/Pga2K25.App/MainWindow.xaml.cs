using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Pga2K25.CourseCard;
using Pga2K25.CourseFormat;
using Pga2K25.Finisher.Desktop;
using Pga2K25.GreensBook;
using Pga2K25.Maps;
using Pga2K25.TerrainMap;
using Pga2K25.YardageBook;

namespace Pga2K25.App;

public partial class MainWindow : Window, IFinisherHost
{
    string? _lastOutput;

    public MainWindow()
    {
        InitializeComponent();
        Finisher.Host = this;
    }

    string? IFinisherHost.CoursePath => TryInputs(out var course, out _) ? course : null;
    string? IFinisherHost.OutputFolder => OutputBox.Text.Trim() is { Length: > 0 } f ? f : null;
    Task IFinisherHost.Run(Button button, string title, Func<IProgress<string>, string?> work) => Run(button, title, work);

    void BrowseCourse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select course", Filter = "Golf Course (*.course)|*.course|All files|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        CourseBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(OutputBox.Text))
            OutputBox.Text = Path.GetDirectoryName(dialog.FileName);
        Finisher.ResetOutput();
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

    async void Yardage_Click(object sender, RoutedEventArgs e)
    {
        if (!TryInputs(out var course, out var output)) return;
        if (!TryParseHoles(YardageHolesBox.Text, out var holes))
        {
            MessageBox.Show(this, "Holes should be All, or a list such as 1-9 or 1, 4, 11-14.", "Yardage Book");
            return;
        }
        var outputs = new YardageBookOutputs { Pdf = YardagePdfBox.IsChecked == true, Pngs = YardagePngBox.IsChecked == true };
        if (!outputs.Pdf && !outputs.Pngs) { MessageBox.Show(this, "Pick the PDF workbook, PNGs, or both.", "Yardage Book"); return; }
        var options = new YardageOptions { Credit = CreditBox.Text.Trim() };
        if (DataBox.Text.Trim() is { Length: > 0 } dataPath)
        {
            // Names only: every yardage and elevation comes from the course file.
            try { options = options with { TeeNames = HoleData.Load(dataPath).TeeNames }; }
            catch (Exception ex) { MessageBox.Show(this, $"Could not read hole data: {ex.Message}", "Yardage Book"); return; }
        }
        bool verbose = YardageVerboseBox.IsChecked == true;
        await Run(YardageButton, "YARDAGE BOOK", progress =>
        {
            var result = YardageBookBuilder.Build(course, output, options, outputs, holes, progress, verbose);
            return result.PdfPath ?? result.PngPaths.FirstOrDefault();
        });
    }

    async void Terrain_Click(object sender, RoutedEventArgs e)
    {
        if (!TryInputs(out var course, out var output) || TerrainOptions() is not { } options) return;
        await Run(TerrainButton, "TERRAIN MAP", progress => TerrainMapBuilder.Build(course, output, options, progress).PngPath);
    }

    async void HoleSheets_Click(object sender, RoutedEventArgs e)
    {
        if (!TryInputs(out var course, out var output) || TerrainOptions() is not { } options) return;
        if (!TryParseHoles(SheetHolesBox.Text, out var holes))
        {
            MessageBox.Show(this, "Holes should be All, or a list such as 1-9 or 2, 7, 16.", "Hole Sheets");
            return;
        }
        // Shading, interval, surfaces and credit carry over; the sheet sets its own resolution and area.
        var sheet = new HoleSheetOptions { Map = options };
        await Run(HoleSheetsButton, "HOLE SHEETS", progress =>
        {
            var result = HoleSheetBuilder.Build(course, output, sheet, holes, progress);
            return result.PdfPath ?? result.PngPaths.FirstOrDefault();
        });
    }

    /// <summary>Terrain options from the tab, or null (after a message) when a field is invalid.</summary>
    TerrainMapOptions? TerrainOptions()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        double? interval = null;
        string intervalText = TerrainIntervalBox.Text.Trim();
        if (intervalText.Length > 0 && !intervalText.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            if (!double.TryParse(intervalText, System.Globalization.NumberStyles.Float, inv, out var ft) || ft <= 0)
            { MessageBox.Show(this, "Contour interval should be Auto or a number of feet.", "Terrain Map"); return null; }
            interval = ft;
        }
        if (!double.TryParse(TerrainPpmBox.Text, System.Globalization.NumberStyles.Float, inv, out var ppm) || ppm < 0.5 || ppm > 10)
        { MessageBox.Show(this, "Pixels per metre should be between 0.5 and 10.", "Terrain Map"); return null; }
        return new TerrainMapOptions
        {
            Extent = TerrainExtentBox.SelectedIndex switch { 1 => MapExtent.FullPlot, 2 => MapExtent.Buildable, _ => MapExtent.Routing },
            Shading = (ReliefShading)TerrainShadingBox.SelectedIndex,
            ContourIntervalFeet = interval,
            PixelsPerMetre = ppm,
            ShowFeatures = TerrainFeaturesBox.IsChecked == true,
            Credit = CreditBox.Text.Trim(),
        };
    }

    void BrowseCardConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select card settings", Filter = "Card settings (*.json)|*.json|All files|*.*" };
        if (dialog.ShowDialog(this) == true) CardConfigBox.Text = dialog.FileName;
    }

    async void Cards_Click(object sender, RoutedEventArgs e)
    {
        if (!TryInputs(out var course, out var output)) return;
        CourseCardConfig config;
        try
        {
            config = CardConfigBox.Text.Trim() is { Length: > 0 } path ? CourseCardConfig.Load(path) : new CourseCardConfig();
        }
        catch (Exception ex) { MessageBox.Show(this, $"Could not read card settings: {ex.Message}", "Course Cards"); return; }

        if (CardWindBox.Text.Trim() is { Length: > 0 } wind)
        {
            if (!int.TryParse(wind, out int deg) || deg is < 0 or > 359)
            { MessageBox.Show(this, "Wind should be a bearing from 0 to 359, or blank.", "Course Cards"); return; }
            config = config with { WindFromDegrees = deg };
        }
        // 0 = from settings, 1 = detect from the surf props, 2 = none, then the eight sides.
        config = CardOceanBox.SelectedIndex switch
        {
            0 => config,
            1 => config with { OceanSide = null },
            2 => config with { OceanSide = "none" },
            _ => config with { OceanSide = ((ComboBoxItem)CardOceanBox.SelectedItem).Content.ToString() },
        };
        var formats = new List<string>();
        if (CardPngBox.IsChecked == true) formats.Add("png");
        if (CardPdfBox.IsChecked == true) formats.Add("pdf");
        if (CardSvgBox.IsChecked == true) formats.Add("svg");
        if (formats.Count == 0) { MessageBox.Show(this, "Pick at least one format.", "Course Cards"); return; }
        config = config with { Formats = formats };

        string? workbook = DataBox.Text.Trim() is { Length: > 0 } data ? data : null;
        bool force = CardForceBox.IsChecked == true;
        await Run(CardsButton, "COURSE CARDS", progress =>
            CourseCardBuilder.Build(course, output, config, workbook, progress, force).Files
                .FirstOrDefault(f => f.EndsWith("Course Data Card.png", StringComparison.OrdinalIgnoreCase)) ?? output);
    }

    /// <summary>"All" (or blank) → null; otherwise a list of numbers and ranges, e.g. "1-9, 12".</summary>
    internal static bool TryParseHoles(string text, out List<int>? holes)
    {
        holes = null;
        text = text.Trim();
        if (text.Length == 0 || text.Equals("all", StringComparison.OrdinalIgnoreCase)) return true;
        var list = new List<int>();
        foreach (var part in text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var ends = part.Split('-');
            if (ends.Length > 2 || !int.TryParse(ends[0], out int a) || !int.TryParse(ends[^1], out int b) || a < 1 || b < a) return false;
            for (int i = a; i <= b; i++) if (!list.Contains(i)) list.Add(i);
        }
        holes = list;
        return list.Count > 0;
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
