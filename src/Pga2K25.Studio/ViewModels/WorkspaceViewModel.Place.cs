using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using SkiaSharp;

namespace Pga2K25.Studio.ViewModels;

/// <summary>The Place card in Course settings: where the course is, saved to Place.json as it's typed.</summary>
public partial class WorkspaceViewModel
{
    const string DetectSea = "Detect from the plot", NoSea = "None (inland)", NoWind = "Not set";
    const string NoLocator = "None", SiteLocator = "The site: plot, sea and course", RegionLocatorChoice = "Generated region";
    static readonly string[] Compass = ["North", "North-east", "East", "South-east", "South", "South-west", "West", "North-west"];
    static readonly string[] Shapes = ["Island", "Peninsula", "Bay", "Lake shore", "Inland"];
    static readonly (string Label, double Km)[] Sizes = [("Small (8 km)", 8), ("Medium (25 km)", 25), ("Large (80 km)", 80)];

    public IReadOnlyList<string> SeaSideChoices { get; } = [DetectSea, NoSea, .. Compass];
    public IReadOnlyList<string> WindChoices { get; } = [NoWind, .. Compass];
    public IReadOnlyList<string> LocatorChoices { get; } = [NoLocator, SiteLocator, RegionLocatorChoice];
    public IReadOnlyList<string> ShapeChoices { get; } = Shapes;
    public IReadOnlyList<string> SizeChoices { get; } = Sizes.Select(s => s.Label).ToList();

    [ObservableProperty] private string _placeLine1 = "";
    [ObservableProperty] private string _placeLine2 = "";
    [ObservableProperty] private string _placeLine3 = "";
    [ObservableProperty] private string _placeDescription = "";
    [ObservableProperty] private string _seaSideChoice = DetectSea;
    [ObservableProperty] private string _windChoice = NoWind;
    [ObservableProperty] private string _locatorChoice = NoLocator;
    [ObservableProperty] private string _shapeChoice = Shapes[0];
    [ObservableProperty] private string _seedText = "1";
    [ObservableProperty] private string _sizeChoice = Sizes[1].Label;
    [ObservableProperty] private bool _placeWatermark = true;
    [ObservableProperty] private string _detectedSea = "";
    [ObservableProperty] private Bitmap? _regionPreview;

    public bool IsRegionLocator => LocatorChoice == RegionLocatorChoice;

    bool _placeLoading;
    CourseModel? _placeCourse;
    SiteInset? _placeSite;
    int _previewVersion;

    void InitPlace()
    {
        _placeLoading = true;
        var place = CoursePlaces.Load(OutputFolder);
        PlaceLine1 = place.Lines.ElementAtOrDefault(0) ?? "";
        PlaceLine2 = place.Lines.ElementAtOrDefault(1) ?? "";
        PlaceLine3 = place.Lines.ElementAtOrDefault(2) ?? "";
        PlaceDescription = place.Description;
        SeaSideChoice = place.SeaSide switch
        {
            null => DetectSea,
            var s when s.Equals("none", StringComparison.OrdinalIgnoreCase) => NoSea,
            var s => Compass.FirstOrDefault(c => c.Equals(s, StringComparison.OrdinalIgnoreCase)) ?? DetectSea,
        };
        WindChoice = place.WindFromDegrees is int w ? Compass[(int)Math.Round((w % 360 + 360) % 360 / 45.0) % 8] : NoWind;
        LocatorChoice = place.ShowsRegion ? RegionLocatorChoice : place.ShowsSite ? SiteLocator : NoLocator;
        ShapeChoice = Shapes[(int)RegionGenerator.Parse(place.Shape)];
        SeedText = place.Seed.ToString();
        SizeChoice = Sizes.MinBy(s => Math.Abs(s.Km - place.SizeKm)).Label;
        PlaceWatermark = place.Watermark;
        InitWind(place);
        _placeLoading = false;

        // The surf props say which way the sea is; read the course off the UI thread.
        string path = Entry.Path;
        Task.Run(() =>
        {
            string text;
            try
            {
                var course = _placeCourse ??= CourseFile.Load(path).ReadModel();
                text = OceanFinder.SeaBearing(course) is int b ? $"Detected: {Compass[(int)Math.Round(b / 45.0) % 8]}, from the surf props" : "No sea found on the plot";
            }
            catch (Exception) { text = ""; }
            Dispatcher.UIThread.Post(() => DetectedSea = text);
        });
        RefreshRegionPreview();
        RefreshWindPreview();
    }

    partial void OnPlaceLine1Changed(string value) { SavePlace(); RefreshRegionPreview(); }
    partial void OnPlaceLine2Changed(string value) => SavePlace();
    partial void OnPlaceLine3Changed(string value) => SavePlace();
    partial void OnPlaceDescriptionChanged(string value) => SavePlace();
    partial void OnSeaSideChoiceChanged(string value) { SavePlace(); RefreshRegionPreview(); RefreshWindPreview(); }
    partial void OnWindChoiceChanged(string value) { SavePlace(); RefreshWindPreview(); }
    partial void OnLocatorChoiceChanged(string value) { OnPropertyChanged(nameof(IsRegionLocator)); SavePlace(); RefreshRegionPreview(); }
    partial void OnShapeChoiceChanged(string value) { SavePlace(); RefreshRegionPreview(); }
    partial void OnSeedTextChanged(string value) { SavePlace(); RefreshRegionPreview(); }
    partial void OnSizeChoiceChanged(string value) { SavePlace(); RefreshRegionPreview(); }
    partial void OnPlaceWatermarkChanged(bool value) => SavePlace();

    [RelayCommand] private void RerollRegion() => SeedText = Random.Shared.Next(1000, 10000).ToString();

    /// <summary>The card written onto <paramref name="saved"/>, which keeps any fields the card doesn't edit.</summary>
    CoursePlace CardPlace(CoursePlace saved)
    {
        saved.Lines = new[] { PlaceLine1, PlaceLine2, PlaceLine3 }.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        saved.Description = PlaceDescription.Trim();
        saved.SeaSide = SeaSideChoice switch
        {
            DetectSea => null,
            NoSea => "none",
            var c => c.ToUpperInvariant(),
        };
        int wind = Array.IndexOf(Compass, WindChoice);
        saved.WindFromDegrees = wind < 0 ? null : wind * 45;
        saved.Coastline = IsRegionLocator ? "region" : LocatorChoice == SiteLocator ? "site" : "none";
        saved.Shape = ((RegionShape)Math.Max(0, Array.IndexOf(Shapes, ShapeChoice))).ToString().ToLowerInvariant();
        if (int.TryParse(SeedText, out int seed)) saved.Seed = seed;
        saved.SizeKm = Sizes.FirstOrDefault(s => s.Label == SizeChoice, Sizes[1]).Km;
        saved.Watermark = PlaceWatermark;
        CardWind(saved);
        return saved;
    }

    void SavePlace()
    {
        if (_placeLoading) return;
        var place = CardPlace(CoursePlaces.Load(OutputFolder));
        try { CoursePlaces.Save(OutputFolder, place); }
        catch (Exception ex) { Status = $"Place could not be saved: {ex.Message}"; }
    }

    /// <summary>Renders the locator the terrain map will show, off the UI thread; a newer request drops an older one.</summary>
    void RefreshRegionPreview()
    {
        if (_placeLoading) return;
        if (!IsRegionLocator) { RegionPreview = null; return; }
        var place = CardPlace(new CoursePlace());
        int version = ++_previewVersion;
        string path = Entry.Path;
        Task.Run(() =>
        {
            try
            {
                var course = _placeCourse ??= CourseFile.Load(path).ReadModel();
                var site = _placeSite ??= SiteInset.Build(course, CourseLayers.From(course));
                var region = RegionGenerator.Generate(RegionGenerator.Parse(place.Shape), place.Seed, OceanFinder.SeaBearing(place, course), place.SizeKm);
                const int w = 640, h = 400;
                var paper = SKColor.Parse("#fbf8f1");
                using var surface = SKSurface.Create(new SKImageInfo(w, h));
                surface.Canvas.Clear(paper);
                using var face = SKTypeface.FromFamilyName("Georgia");
                RegionLocator.DrawLocator(surface.Canvas, new SKRect(2, 2, w - 2, h - 2), region, place.PlaceLines.FirstOrDefault(), face,
                    SKColor.Parse("#1f1d1a"), paper, SKColor.Parse("#c0392b"), 2.2f, site, SKColor.Parse("#3f7fae").WithAlpha(170));
                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                var bitmap = new Bitmap(new MemoryStream(data.ToArray()));
                Dispatcher.UIThread.Post(() => { if (version == _previewVersion) RegionPreview = bitmap; });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => Status = $"Region preview failed: {ex.Message}");
            }
        });
    }
}
