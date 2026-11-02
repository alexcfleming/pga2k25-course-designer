namespace Pga2K25.Readiness;

/// <summary>Where a checklist item comes from, as the HTML checklist labelled it.</summary>
public enum ItemSource { YourNotes, OtherPublisher, DesignPrinciples, TourFeedback, TgcMinimums }

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

/// <summary>
/// One Hole QA check, ticked on every hole. Its key on a hole is <c>{Prefix}.{hole}.{Id}</c>; the prefixes are the HTML
/// checklist's own (<c>hc</c>, <c>tr</c>, <c>pl</c>) or <c>hq</c> for checks added since. <paramref name="Also"/> is a
/// second key read as the same check (two of the HTML checklist's checks merged into one).
/// </summary>
public sealed record HoleCheck(string Prefix, string Id, string Label, string Text, ItemSource Source, bool Minimum = false,
    (string Prefix, string Id)? Also = null)
{
    public string Key(int hole) => $"{Prefix}.{hole}.{Id}";
    public string? AlsoKey(int hole) => Also is { } a ? $"{a.Prefix}.{hole}.{a.Id}" : null;
}

/// <summary>A group of Hole QA checks.</summary>
public sealed record HoleGroup(string Name, IReadOnlyList<HoleCheck> Checks);

/// <summary>
/// The course QA and publish checklist (Alex's <c>course_qa_checklist.html</c>), with the items the course file
/// can answer linked to their checks.
/// </summary>
public static class Checklist
{
    const ItemSource N = ItemSource.YourNotes, P = ItemSource.OtherPublisher, D = ItemSource.DesignPrinciples, F = ItemSource.TourFeedback, M = ItemSource.TgcMinimums;

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

    public static readonly ChecklistStage Minimums = new("tgcm", "TGC minimums", "The course-level minimum approval requirements TGC Tours publishes. The per-hole ones are in Hole QA.", "tgm",
    [
        new("tgm.par", "Total par 69 to 74; every hole a par 3, 4 or 5; 18 holes", M, "par-range"),
        new("tgm.range", "Holes of the same par have a range of yardages", M, "same-par-range"),
        new("tgm.width", "Fairways at least 25 yds wide (recommended, not required)", M, "narrow-fairway"),
        new("tgm.pins", "Pins clear of the green's edge and off red slope at the cup", M, "pins"),
        new("tgm.real", "A real-course recreation is very accurate (N/A for an original course)", M),
    ]);

    public static readonly ChecklistStage TourCourse = new("tourc", "Course review", "Course-level checks from Section 11. The per-hole checks are in Hole QA.", "tcl",
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
        new("pg.names", "Hole names, yardages and pars match across the Holes card, plates and in-game scorecard", D),
        new("pg.proof", "All narrative text proofread", D),
        new("pg.atm", "Lighting, cloud and fog settings in the game match the values recorded from the file", N),
        new("pg.shots", "Hero shots and plate captures retaken after the last terrain or planting change", D),
        new("pg.tour", "Course review complete: every course-file flag cleared or marked deliberate", F, AllChecks),
        new("pg.outside", "Someone other than me, or me with fresh eyes, played the whole course from default settings", F),
        new("pg.files", "Final files saved and uploaded to the Project", D),
    ]);

    public static readonly IReadOnlyList<ChecklistStage> Stages = [PreBuild, Minimums, TourCourse, Build, Publish];

    /// <summary>
    /// Hole QA: every check done on every hole, in six groups. The HTML checklist's three per-hole stages (per-hole
    /// checks, tour readiness, planting passes) keep their keys, so a backup or saved progress carries straight over;
    /// its bunker and transition checks each merge with their tour-readiness twin. The minimums are TGC Tours' own.
    /// </summary>
    public static readonly IReadOnlyList<HoleGroup> HoleQa =
    [
        new("Lines and views",
        [
            new("hc", "sightTee", "Tee view", "Sightline from the tee", P),
            new("hc", "vApp", "Approach", "Approach views", N),
            new("hc", "vBail", "Bail-out", "Bail-out views", N),
            new("hc", "back", "Look back", "Looking back from green and at tee", P),
            new("tr", "aim", "Aim line", "Aim line at its own green; no shot over another green", F),
            new("tr", "wp", "Waypoints", "Waypoints and default club checked from every tee", F),
            new("hq", "block", "No blocker", "No unavoidable obstruction on the tee shot or approach", M, true),
        ]),
        new("Shaping",
        [
            new("hc", "gcont", "Green", "Green contour study", N),
            new("hc", "fcont", "Fairway", "Fairway contour heights", N),
            new("hc", "teeflat", "Tees flat", "Tee boxes flat", P, true),
            new("hc", "multi", "Multi tees", "Multiple tees", P),
            new("hc", "bunk", "Bunkers", "Bunkers: sand even and in the course's style", P, Also: ("tr", "sand")),
            new("hc", "trans", "Edges", "Edges blended: fairway, sand and rough, not stark", P, Also: ("tr", "blend")),
            new("hq", "lrough", "Light rough", "Light rough mostly symmetrical with the fairway and green", M, true),
            new("hq", "bcut", "Bunker cuts", "Bunkers don't cut straight into several layers of rough or fairway (recommended)", M, true),
        ]),
        new("Play",
        [
            new("hc", "hazard", "Hazards", "Played out of every hazard", N),
            new("tr", "opt", "Routes", "Every route works and reads from the tee", F),
            new("tr", "concept", "Concept", "Built hazards match the concept notes' restraint", F),
            new("hc", "img", "Image", "Hole image test and draw", N),
            new("hq", "wind", "Any wind", "Fairway reachable in any wind, from every tee", M, true),
            new("hq", "front", "Front holds", "A shot carried onto the front of the green doesn't roll off the back", M, true),
            new("hq", "bfair", "Bunkers fair", "Bunkers fair", M, true),
        ]),
        new("Pins",
        [
            new("hq", "pvary", "Varied", "Pin locations varied within each pin set", M, true),
            new("hq", "pclose", "Can get close", "Every pin gives a fair chance to hit an approach close: playtested", M, true),
            new("hq", "pslope", "Off big slopes", "No pin in the middle of a large yellow or red slope", M, true),
        ]),
        new("Look",
        [
            new("hc", "grass", "Grasses", "Highlight grasses", P),
            new("tr", "real", "Rocks", "Rocks, cliffs and props believable, not fantasy scale", F),
            new("tr", "cam", "Camera", "Hole camera and the transition into the hole smooth", F),
            new("hq", "noplant", "No plants", "No plants on the fairway, green, tees or cart path", M, true),
            new("hq", "stock", "Stock shapes", "Stock shapes blended and not overused", M, true),
            new("hq", "repeat", "Repeats", "The same tree or rock isn't repeated over and over", M, true),
            new("hq", "paths", "Paths, splines", "Cart paths smoothed; spline kinks cleaned up", M, true),
        ]),
        new("Planting",
        [
            new("pl", "waste", "Open waste", "Open waste", N),
            new("pl", "cliff", "Cliff edge", "Cliff edge and hole edge", N),
            new("pl", "g360", "Green 360", "Green 360", N),
            new("pl", "t360", "Tee 360", "Tee 360", N),
            new("pl", "hwaste", "Hole waste", "Hole wastes", N),
            new("pl", "hill", "Hill light", "Open hill light", N),
            new("pl", "bunk", "By bunkers", "Around bunkers", N),
        ]),
    ];

    public static IEnumerable<HoleCheck> HoleChecks => HoleQa.SelectMany(g => g.Checks);

    /// <summary>
    /// A Hole QA check's status on a hole. A merged check reads both of its keys; the more closed one wins
    /// (Done, then Deliberate, then N/A, then Open).
    /// </summary>
    public static ItemStatus StatusOf(HoleCheck c, int hole, ReadinessState s)
    {
        var a = s.StatusOf(c.Key(hole));
        if (c.AlsoKey(hole) is not { } also) return a;
        var b = s.StatusOf(also);
        static int Rank(ItemStatus x) => x switch { ItemStatus.Done => 3, ItemStatus.Deliberate => 2, ItemStatus.NotApplicable => 1, _ => 0 };
        return Rank(b) > Rank(a) ? b : a;
    }

    /// <summary>Sets a check on a hole. A merged check keeps its status on its own key and drops the twin's.</summary>
    public static void Set(HoleCheck c, int hole, ItemStatus status, ReadinessState s)
    {
        s.Set(c.Key(hole), status);
        if (c.AlsoKey(hole) is { } also) s.Items.Remove(also);
    }

    /// <summary>Wind directions in the in-game setting's sense: N blows from the south towards the north.</summary>
    public static readonly IReadOnlyList<string> WindDirections = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];

    public static string SourceLabel(ItemSource s) => s switch
    {
        ItemSource.YourNotes => "your notes",
        ItemSource.OtherPublisher => "other publisher",
        ItemSource.DesignPrinciples => "design principles",
        ItemSource.TgcMinimums => "TGC minimums",
        _ => "reviewer feedback",
    };
}
