using Pga2K25.CourseFormat;

namespace Pga2K25.Studio.Services;

/// <summary>
/// Studio's only network call: ten years of hourly 10 m wind for a real place from the free Open-Meteo archive
/// (ERA5 reanalysis, CC BY 4.0), binned into a <see cref="WindRose"/>. Runs only when the user presses
/// "Fetch wind data" with "Allow online wind data" switched on in Settings; never automatically.
/// </summary>
public static class WindDataFetcher
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    public static async Task<WindRose> FetchAsync(double latitude, double longitude, CancellationToken cancel = default)
    {
        // The ten most recent whole years.
        int last = DateTime.UtcNow.Year - 1, first = last - 9;
        string url = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"https://archive-api.open-meteo.com/v1/archive?latitude={latitude:0.####}&longitude={longitude:0.####}&start_date={first}-01-01&end_date={last}-12-31&hourly=wind_speed_10m,wind_direction_10m&wind_speed_unit=mph&models=era5");
        using var response = await Http.GetAsync(url, cancel);
        string json = await response.Content.ReadAsStringAsync(cancel);
        if (!response.IsSuccessStatusCode)
        {
            WindRose.FromOpenMeteo(json);   // throws with the service's reason when it gives one
            response.EnsureSuccessStatusCode();
        }
        var rose = WindRose.FromOpenMeteo(json);
        rose.Latitude = latitude;
        rose.Longitude = longitude;
        rose.Fetched = DateTime.UtcNow;
        return rose;
    }
}
