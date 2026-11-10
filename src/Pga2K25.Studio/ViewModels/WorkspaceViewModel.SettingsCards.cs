namespace Pga2K25.Studio.ViewModels;

/// <summary>
/// Course settings as a stack: Place, Holes, Logo, Scorecard maps, Course card settings and Files, one open at a time,
/// the rest folded to a summary line. Place starts open. Summaries refresh when the page opens and as cards open.
/// </summary>
public partial class WorkspaceViewModel
{
    public PanelCardViewModel SetPlace { get; private set; } = null!;
    public PanelCardViewModel SetHoles { get; private set; } = null!;
    public PanelCardViewModel SetLogo { get; private set; } = null!;
    public PanelCardViewModel SetMaps { get; private set; } = null!;
    public PanelCardViewModel SetCards { get; private set; } = null!;
    public PanelCardViewModel SetFiles { get; private set; } = null!;
    IEnumerable<PanelCardViewModel> SettingsCards => [SetPlace, SetHoles, SetLogo, SetMaps, SetCards, SetFiles];

    void InitSettingsCards()
    {
        void Toggle(string key)
        {
            foreach (var c in SettingsCards) c.IsOpen = c.Key == key && !c.IsOpen;
            RefreshSettingsSummaries();
        }
        SetPlace = new("place", "Place", "", Toggle);
        SetHoles = new("holes", "Holes", "", Toggle);
        SetLogo = new("logo", "Logo", "", Toggle);
        SetMaps = new("maps", "Scorecard maps", "", Toggle);
        SetCards = new("cards", "Course card settings", "", Toggle);
        SetFiles = new("files", "Files", "", Toggle);
        foreach (var c in SettingsCards) c.IsAvailable = true;
        SetPlace.IsOpen = true;
    }

    void RefreshSettingsSummaries()
    {
        SetPlace.Summary = PlaceSummary([PlaceLine1, PlaceLine2, PlaceLine3],
            LocatorChoice == NoLocator ? null : LocatorChoice == RegionLocatorChoice ? "generated region" : "site locator", WindSourceChoice != NoRose);
        SetHoles.Summary = HolesSummary(TeeNamesText, HoleEntries.Count(h => !string.IsNullOrWhiteSpace(h.Name)));
        SetLogo.Summary = LogoSummary(LogoSlots.Count(s => s.HasImage), LogoSlots.Count);
        SetMaps.Summary = $"Bunker merge {BunkerMerge}";
        SetCards.Summary = string.IsNullOrWhiteSpace(CardConfigPath) ? "No card settings file" : Path.GetFileName(CardConfigPath);
        SetFiles.Summary = Path.GetFileName(CoursePath);
    }

    /// <summary>"Olkhon Island, Lake Baikal · generated region · wind rose", or "No place set".</summary>
    public static string PlaceSummary(IEnumerable<string> lines, string? locator, bool windRose)
    {
        var place = lines.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
        var parts = new List<string> { place.Count > 0 ? string.Join(", ", place) : "No place set" };
        if (locator is not null) parts.Add(locator);
        if (windRose) parts.Add("wind rose");
        return string.Join(" · ", parts);
    }

    /// <summary>"Tees: Red, White · 3 holes named", or "Tee names not set".</summary>
    public static string HolesSummary(string teeNames, int named) =>
        (string.IsNullOrWhiteSpace(teeNames) ? "Tee names not set" : $"Tees: {teeNames.Trim()}")
        + (named > 0 ? $" · {named} hole{(named == 1 ? "" : "s")} named" : "");

    public static string LogoSummary(int have, int of) => have == 0 ? "No logo" : $"{have} of {of} versions";
}
