using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Pga2K25.CourseCard;
using Pga2K25.CourseFormat;

namespace Pga2K25.Studio.ViewModels;

/// <summary>The Place card in Course settings: where the course is, saved to Place.json as it's typed.</summary>
public partial class WorkspaceViewModel
{
    const string DetectSea = "Detect from the plot", NoSea = "None (inland)", NoWind = "Not set";
    const string NoLocator = "None", SiteLocator = "The site: plot, sea and course";
    static readonly string[] Compass = ["North", "North-east", "East", "South-east", "South", "South-west", "West", "North-west"];

    public IReadOnlyList<string> SeaSideChoices { get; } = [DetectSea, NoSea, .. Compass];
    public IReadOnlyList<string> WindChoices { get; } = [NoWind, .. Compass];
    public IReadOnlyList<string> LocatorChoices { get; } = [NoLocator, SiteLocator];

    [ObservableProperty] private string _placeLine1 = "";
    [ObservableProperty] private string _placeLine2 = "";
    [ObservableProperty] private string _placeLine3 = "";
    [ObservableProperty] private string _placeDescription = "";
    [ObservableProperty] private string _seaSideChoice = DetectSea;
    [ObservableProperty] private string _windChoice = NoWind;
    [ObservableProperty] private string _locatorChoice = NoLocator;
    [ObservableProperty] private string _detectedSea = "";

    bool _placeLoading;

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
        LocatorChoice = place.ShowsSite ? SiteLocator : NoLocator;
        _placeLoading = false;

        // The surf props say which way the sea is; read the course off the UI thread.
        string path = Entry.Path;
        Task.Run(() =>
        {
            string text;
            try
            {
                var course = CourseFile.Load(path).ReadModel();
                text = OceanFinder.SeaBearing(course) is int b ? $"Detected: {Compass[(int)Math.Round(b / 45.0) % 8]}, from the surf props" : "No sea found on the plot";
            }
            catch (Exception) { text = ""; }
            Dispatcher.UIThread.Post(() => DetectedSea = text);
        });
    }

    partial void OnPlaceLine1Changed(string value) => SavePlace();
    partial void OnPlaceLine2Changed(string value) => SavePlace();
    partial void OnPlaceLine3Changed(string value) => SavePlace();
    partial void OnPlaceDescriptionChanged(string value) => SavePlace();
    partial void OnSeaSideChoiceChanged(string value) => SavePlace();
    partial void OnWindChoiceChanged(string value) => SavePlace();
    partial void OnLocatorChoiceChanged(string value) => SavePlace();

    void SavePlace()
    {
        if (_placeLoading) return;
        var place = CoursePlaces.Load(OutputFolder);   // keep fields this card doesn't edit
        place.Lines = new[] { PlaceLine1, PlaceLine2, PlaceLine3 }.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        place.Description = PlaceDescription.Trim();
        place.SeaSide = SeaSideChoice switch
        {
            DetectSea => null,
            NoSea => "none",
            var c => c.ToUpperInvariant(),
        };
        int wind = Array.IndexOf(Compass, WindChoice);
        place.WindFromDegrees = wind < 0 ? null : wind * 45;
        place.Coastline = LocatorChoice == SiteLocator ? "site" : "none";
        try { CoursePlaces.Save(OutputFolder, place); }
        catch (Exception ex) { Status = $"Place could not be saved: {ex.Message}"; }
    }
}
