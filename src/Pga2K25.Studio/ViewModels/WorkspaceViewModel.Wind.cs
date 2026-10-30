using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pga2K25.CourseFormat;
using Pga2K25.Maps;
using Pga2K25.Studio.Services;
using SkiaSharp;

namespace Pga2K25.Studio.ViewModels;

/// <summary>The wind block of the Place card: no rose, the climate model, or a real place's fetched record.</summary>
public partial class WorkspaceViewModel
{
    const string NoRose = "None", ModelRose = "Climate model (invented place)", DataRose = "Real place (wind record)";
    static readonly (string Label, double Value)[] Strengths = [("Light", 0.7), ("Moderate", 1), ("Windy", 1.35)];

    public IReadOnlyList<string> WindSourceChoices { get; } = [NoRose, ModelRose, DataRose];
    public IReadOnlyList<string> ClimateChoices { get; } = WindClimate.Presets.Select(WindClimate.Label).ToList();
    public IReadOnlyList<string> StrengthChoices { get; } = Strengths.Select(s => s.Label).ToList();

    [ObservableProperty] private string _windSourceChoice = NoRose;
    [ObservableProperty] private string _climateChoice = WindClimate.Label("sea");
    [ObservableProperty] private string _strengthChoice = "Moderate";
    [ObservableProperty] private double _windSteadiness = 1;
    [ObservableProperty] private string _windSeedText = "1";
    [ObservableProperty] private string _latitudeText = "";
    [ObservableProperty] private string _longitudeText = "";
    [ObservableProperty] private string _windStatus = "";
    [ObservableProperty] private Bitmap? _windPreview;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchWindCommand))]
    private bool _fetchingWind;

    public bool IsModelWind => WindSourceChoice == ModelRose;
    public bool IsDataWind => WindSourceChoice == DataRose;
    public bool HasWind => WindSourceChoice != NoRose;

    /// <summary>The Settings switch; the fetch button only shows when it's on.</summary>
    public bool AllowOnlineWind => Settings.AllowOnlineWind;

    int _windVersion;

    void InitWind(CoursePlace place)
    {
        WindSourceChoice = place.WindSource.ToLowerInvariant() switch { "model" => ModelRose, "data" => DataRose, _ => NoRose };
        ClimateChoice = WindClimate.Label(WindClimate.Presets.Contains(place.Climate) ? place.Climate : "sea");
        StrengthChoice = Strengths.MinBy(s => Math.Abs(s.Value - place.WindStrength)).Label;
        WindSteadiness = Math.Clamp(place.WindSteadiness, 0.4, 2.2);
        WindSeedText = place.WindSeed.ToString(CultureInfo.InvariantCulture);
        LatitudeText = place.Latitude?.ToString("0.####", CultureInfo.InvariantCulture) ?? "";
        LongitudeText = place.Longitude?.ToString("0.####", CultureInfo.InvariantCulture) ?? "";
        WindStatus = DataStatus(place.WindData);
    }

    string DataStatus(WindRose? data) => data is null
        ? AllowOnlineWind ? "No wind record yet; the climate model stands in until you fetch one."
                          : "No wind record yet; the climate model stands in. Fetching needs Allow online wind data in Settings."
        : $"Saved: {data.Source}" + (data.Hours is int n ? $", {n:N0} hours" : "") + (data.Fetched is DateTime f ? $", fetched {f.ToLocalTime():d MMM yyyy}" : "") +
          (AllowOnlineWind ? "" : ". Online wind data is off in Settings, so it can't be refreshed, but it keeps working.");

    partial void OnWindSourceChoiceChanged(string value)
    {
        OnPropertyChanged(nameof(IsModelWind)); OnPropertyChanged(nameof(IsDataWind)); OnPropertyChanged(nameof(HasWind));
        SavePlace(); RefreshWindPreview();
    }
    partial void OnClimateChoiceChanged(string value) { SavePlace(); RefreshWindPreview(); }
    partial void OnStrengthChoiceChanged(string value) { SavePlace(); RefreshWindPreview(); }
    partial void OnWindSteadinessChanged(double value) { SavePlace(); RefreshWindPreview(); }
    partial void OnWindSeedTextChanged(string value) { SavePlace(); RefreshWindPreview(); }
    partial void OnLatitudeTextChanged(string value) { SavePlace(); FetchWindCommand.NotifyCanExecuteChanged(); }
    partial void OnLongitudeTextChanged(string value) { SavePlace(); FetchWindCommand.NotifyCanExecuteChanged(); }

    [RelayCommand] private void RerollWind() => WindSeedText = Random.Shared.Next(1000, 10000).ToString(CultureInfo.InvariantCulture);

    static double? ParseDegrees(string text, double limit) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && Math.Abs(v) <= limit ? v : null;

    bool CanFetchWind() => AllowOnlineWind && !FetchingWind && ParseDegrees(LatitudeText, 90) is not null && ParseDegrees(LongitudeText, 180) is not null;

    /// <summary>The one network call in Studio, made only when the button is pressed with the setting on.</summary>
    [RelayCommand(CanExecute = nameof(CanFetchWind))]
    private async Task FetchWind()
    {
        if (!CanFetchWind()) return;
        double lat = ParseDegrees(LatitudeText, 90)!.Value, lon = ParseDegrees(LongitudeText, 180)!.Value;
        FetchingWind = true;
        WindStatus = "Fetching ten years of hourly wind from Open-Meteo…";
        try
        {
            var rose = await WindDataFetcher.FetchAsync(lat, lon);
            var place = CardPlace(CoursePlaces.Load(OutputFolder));
            place.WindData = rose;
            place.WindSource = "data";
            CoursePlaces.Save(OutputFolder, place);
            WindStatus = DataStatus(rose);
            if (WindSourceChoice != DataRose) WindSourceChoice = DataRose; else RefreshWindPreview();
        }
        catch (Exception ex)
        {
            WindStatus = $"Fetch failed: {ex.Message}";
        }
        finally { FetchingWind = false; }
    }

    void CardWind(CoursePlace saved)
    {
        saved.WindSource = WindSourceChoice switch { ModelRose => "model", DataRose => "data", _ => "none" };
        int climate = ClimateChoices.ToList().IndexOf(ClimateChoice);
        saved.Climate = WindClimate.Presets[Math.Max(0, climate)];
        saved.WindStrength = Strengths.FirstOrDefault(s => s.Label == StrengthChoice, Strengths[1]).Value;
        saved.WindSteadiness = Math.Round(WindSteadiness, 2);
        if (int.TryParse(WindSeedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed)) saved.WindSeed = seed;
        saved.Latitude = ParseDegrees(LatitudeText, 90);
        saved.Longitude = ParseDegrees(LongitudeText, 180);
    }

    /// <summary>Renders the rose the products will draw, off the UI thread; a newer request drops an older one.</summary>
    void RefreshWindPreview()
    {
        if (_placeLoading) return;
        if (!HasWind) { WindPreview = null; return; }
        var place = CardPlace(CoursePlaces.Load(OutputFolder));
        int version = ++_windVersion;
        string path = Entry.Path;
        Task.Run(() =>
        {
            try
            {
                var course = _placeCourse ??= CourseFile.Load(path).ReadModel();
                var rose = place.Rose(OceanFinder.SeaBearing(place, course));
                if (rose is null) return;
                const int w = 420, h = 440;
                var paper = SKColor.Parse("#fbf8f1");
                using var surface = SKSurface.Create(new SKImageInfo(w, h));
                surface.Canvas.Clear(paper);
                using var face = SKTypeface.FromFamilyName("Georgia");
                WindRoseDrawing.Draw(surface.Canvas, new SKRect(6, 6, w - 6, h - 6), rose, face, SKColor.Parse("#1f1d1a"), paper, 2f);
                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                var bitmap = new Bitmap(new MemoryStream(data.ToArray()));
                string summary = $"Prevailing {WindRose.SectorNames[WindRose.SectorOf(rose.PrevailingFrom)]}  ·  calm {rose.Calm:P0}  ·  breezy (8 mph +) {rose.Breezy:P0}" +
                                 (course.Holes.Count > 0 ? $"\n{WindExposure.Summary(WindExposure.For(course, rose))}" : "");
                Dispatcher.UIThread.Post(() => { if (version == _windVersion) { WindPreview = bitmap; WindSummary = summary; } });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => Status = $"Wind preview failed: {ex.Message}");
            }
        });
    }

    [ObservableProperty] private string _windSummary = "";
}
