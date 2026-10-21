namespace Pga2K25.Readiness;

/// <summary>Where a checklist item comes from, as the HTML checklist labelled it.</summary>
public enum ItemSource { YourNotes, OtherPublisher, DesignPrinciples, TourFeedback }

/// <summary>
/// One judgement-call item. <paramref name="Key"/> is the HTML checklist's own key (<c>pre.score</c>, <c>b.fringe</c>),
/// so a backup imports straight across. <paramref name="Checks"/> are course-file checks that answer it: when all of
/// them pass (or are marked deliberate) the item counts as done until the designer says otherwise.
/// </summary>
public sealed record ChecklistItem(string Key, string Text, ItemSource Source, IReadOnlyList<string> Checks)
{
    public ChecklistItem(string key, string text, ItemSource source, params string[] checks) : this(key, text, source, (IReadOnlyList<string>)checks) { }
}

/// <summary>A course-level stage: a list of items, done once for the course.</summary>
public sealed record ChecklistStage(string Id, string Title, string Lede, string Prefix, IReadOnlyList<ChecklistItem> Items);

/// <summary>A per-hole stage: the same columns ticked on every hole (key <c>{Prefix}.{hole}.{column}</c>).</summary>
public sealed record HoleStage(string Id, string Title, string Lede, string Prefix, IReadOnlyList<(string Id, string Label, ItemSource Source)> Columns)
{
    public string Key(int hole, string column) => $"{Prefix}.{hole}.{column}";
}

/// <summary>
/// The course QA and publish checklist (Alex's <c>course_qa_checklist.html</c>), with the items the course file
/// can answer linked to their checks.
/// </summary>
public static class Checklist
{
    const ItemSource N = ItemSource.YourNotes, P = ItemSource.OtherPublisher, D = ItemSource.DesignPrinciples, F = ItemSource.TourFeedback;

    /// <summary>Linked to every course-file check: done when none is left open.</summary>
    public const string AllChecks = "*";

    public static readonly ChecklistStage PreBuild = new("pre", "Pre-build gate", "Done once per course, before any hole is shaped.", "pre",
    [
        new("pre.score", "Scorecard locked: par and yardage for every tee set, totals match the in-game screen", D, "par", "tee-totals"),
        new("pre.sec7", "Same-par run check (Section 7 bar chart) run on all 18, every flag resolved or accepted", D, "same-par"),
        new("pre.anom", "Anomalies (very long or short holes, identical tee distances, odd par splits) confirmed as intentional and written down", D),
        new("pre.bear", "Tee and approach compass bearings locked for every hole", D),
        new("pre.order", "Distance, then outlines, then scale, then lines, walked in that order before shaping", D),
        new("pre.dist", "Distance bar charts produced before laying out each hole", D),
        new("pre.marker", "Marker pathways designed for each hole (more than one per hole)", D),
        new("pre.measure", "Every capture includes a MEASURE line of known yardage", D),
        new("pre.sctest", "Scorecard tests run against current plots as holes come online", D),
        new("pre.drive", "Test drive distance measured with the power profile and entered in the limits (sets the par 4 and par 5 limits)", F, "drive-entered"),
        new("pre.restraint", "Per-hole concept notes say what to leave out as well as what to build (for example \"consider zero bunkers\"), and the build brief carries them", F),
    ]);

    public static readonly ChecklistStage TourCourse = new("tourc", "Tour readiness", "Course-level checks from Section 11. The per-hole checks are on the hole table.", "tcl",
    [
        new("tcl.scale", "Scale audit: green size and fairway width numbers reviewed; every outlier is deliberate", F, "green-scale", "landing-width"),
        new("tcl.variety", "Variety audit: drivable and near-drivable par 4s within the per-nine limit; every par 5 passes the test-drive check", F, "drivable-front", "drivable-back", "par5"),
        new("tcl.drift", "Concept-to-build drift check: every built hazard and feature compared with its concept entry; anything bigger than the concept is justified or scaled back", F),
        new("tcl.corr", "Corridor check: no overlapping landing zones, and no hole plays over another hole's green", F, "corridor", "aim-spacing"),
        new("tcl.wps", "Waypoints reviewed on all 18 holes from every tee set", F),
        new("tcl.real", "Realism pass: rock, cliff and structure features compared with a named real-world example", F),
        new("tcl.cam", "Camera flyover: transitions between all 18 holes smooth, no jitter or harsh cuts", F),
        new("tcl.opts", "Every hole with a split fairway or alternate routes played both ways; neither route dominates", F),
        new("tcl.keep", "Detail work that sets the course apart (for example waste detail, planting, fog) still intact after fixes", F),
        new("tcl.past", "Reviewer feedback from earlier courses re-read before submitting (repeat issues: width and scale)", F),
    ]);

    public static readonly ChecklistStage Build = new("build", "Build checks", "Course-level work that is not tied to a single hole.", "b",
    [
        new("b.autogen", "Auto-generated clutter cleaned; no trees in the play area unless a deliberate penalty", N),
        new("b.edge", "Terrain edge roughing done", N),
        new("b.rough", "Roughing checks done", N),
        new("b.equip", "Equipment populated", N),
        new("b.bath", "Bathrooms obscured from sightlines", N),
        new("b.setups", "Setups for holes 1, 9, 10 and 18 done", N),
        new("b.ppt", "Scorecard map generated by the Studio and checked against the in-game scorecard", N),
        new("b.club", "Clubhouse area, road, range and other non-play areas dressed", P),
        new("b.speeds", "Green speeds set to a normal Stimp reading (8 to 13)", P, "green-speed"),
        new("b.bgplant", "Background planting done", P),
        new("b.fringe", "No green fringe", D, "fringe"),
        new("b.trans", "Fairway-to-rough transition visibly distinct (colour or texture); fairway-to-sand edges blended, not stark", D),
        new("b.elev", "Elevation: no hole drops more than 50 ft or climbs more than 30 ft; no unintentional blindness; total plot range 50 to 70 ft; rough within 15 to 20 ft of fairway", D, "elevation", "plot-range"),
        new("b.flat", "Greens not flat: 1.5 to 3 ft internal variation; slope colouring reads correctly (white under 3.3%, orange just over, red will not hold)", D, "green-relief"),
        new("b.bdepth", "Bunker depths within range: greenside 2 to 4 ft (a ceiling, not a target), fairway 1 to 3 ft", D),
        new("b.pal", "Planting palette small: one or two highlight colours", D),
    ]);

    public static readonly ChecklistStage Publish = new("pub", "Publish gate", "Nothing goes out until every box here is done, the numbers are clear, and the sign-off is filled in.", "pg",
    [
        new("pg.all18", "I played all 18 holes myself before calling it done", D),
        new("pg.hz", "Every hazard on every hole played out of", N),
        new("pg.wind", "Every wind direction played at high intensity (H) at least once with each player profile (compass grids complete)", P, "playtest-winds"),
        new("pg.tees", "Every tee set and all four pin positions played with both player profiles", N, "playtest-tees"),
        new("pg.par", "Par check: hole pars add up to the course par, OUT and IN totals match the in-game scorecard", P, "par"),
        new("pg.names", "Hole names, yardages and pars match across the workbook, plates and in-game scorecard", D),
        new("pg.proof", "All narrative text proofread", D),
        new("pg.atm", "Lighting, cloud and fog settings in the game match the values recorded from the file", N),
        new("pg.shots", "Hero shots and plate captures retaken after the last terrain or planting change", D),
        new("pg.tour", "Tour-readiness section complete: every flag cleared or marked deliberate", F, AllChecks),
        new("pg.outside", "Someone other than me, or me with fresh eyes, played the whole course from default settings", F),
        new("pg.files", "Final files saved and uploaded to the Project", D),
    ]);

    public static readonly IReadOnlyList<ChecklistStage> Stages = [PreBuild, TourCourse, Build, Publish];

    public static readonly HoleStage TourHoles = new("tour-holes", "Tour readiness", "Judgement calls from Section 11 on every hole.", "tr",
    [
        ("wp", "Waypoints and default club checked from every tee", F),
        ("aim", "Aim line at this hole's own green; no shot over another green", F),
        ("opt", "Every route viable and readable from the tee", F),
        ("sand", "Bunker sand sculpting even and in the course's style", F),
        ("blend", "Fairway-to-sand and bunker surfaces blended, not stark", F),
        ("real", "Rocks, cliffs and props plausible, not fantasy scale", F),
        ("cam", "Hole camera and transition into the hole smooth", F),
        ("concept", "Built hazards match the concept doc's restraint", F),
    ]);

    public static readonly HoleStage HoleChecks = new("hole-checks", "Per-hole checks", "Tick each check as it's finished on that hole.", "hc",
    [
        ("sightTee", "Sightline from tee", P), ("vApp", "Approach visibility views", N), ("vBail", "Bail-out visibility views", N),
        ("gcont", "Green contour study", N), ("fcont", "Fairway contour check (height)", N), ("hazard", "Played out of all hazards", N),
        ("img", "Hole image test and draw", N), ("back", "Looking back from green and at tee", P), ("trans", "Transitions", P),
        ("bunk", "Bunkers", P), ("grass", "Highlight grasses", P), ("teeflat", "Tees flat", P), ("multi", "Multiple tees", P),
    ]);

    public static readonly HoleStage Planting = new("planting", "Planting passes", "Each pass done on each hole.", "pl",
    [
        ("waste", "Open waste", N), ("cliff", "Cliff edge / hole edge", N), ("g360", "Green 360", N), ("t360", "Tee 360", N),
        ("hwaste", "Hole wastes", N), ("hill", "Open hill light", N), ("bunk", "Around bunkers", N),
    ]);

    public static readonly IReadOnlyList<HoleStage> HoleStages = [TourHoles, HoleChecks, Planting];

    /// <summary>Wind directions in the in-game setting's sense: N blows from the south towards the north.</summary>
    public static readonly IReadOnlyList<string> WindDirections = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];

    public static string SourceLabel(ItemSource s) => s switch
    {
        ItemSource.YourNotes => "your notes",
        ItemSource.OtherPublisher => "other publisher",
        ItemSource.DesignPrinciples => "design principles",
        _ => "tour feedback",
    };
}
