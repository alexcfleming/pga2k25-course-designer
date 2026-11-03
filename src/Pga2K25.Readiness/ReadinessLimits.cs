namespace Pga2K25.Readiness;

/// <summary>
/// The numbers the course-file checks judge against. They come from the design principles (§3, §7, §12), TGC Tours'
/// published minimums and a few provisional bands; none of them is a rule, so every one can be changed per course.
/// The §12 length and width limits are provisional: chosen after seeing a handful of reviewed courses, so they warn.
/// </summary>
public sealed record ReadinessLimits
{
    /// <summary>Design tee shot from the back tee (yds): every landing-zone figure is measured from this range.</summary>
    public double DesignDriveMinYards { get; init; } = 300;
    public double DesignDriveMaxYards { get; init; } = 350;
    public double DesignDriveMidYards => (DesignDriveMinYards + DesignDriveMaxYards) / 2;

    /// <summary>Test drive with the power profile (yds), recorded to check the design tee shot against. Nothing is judged on it.</summary>
    public double? TestDriveYards { get; init; }

    /// <summary>Green area against the course median: outside this band is a prompt to look, not a limit.</summary>
    public double ScaleLow { get; init; } = 0.6;
    public double ScaleHigh { get; init; } = 1.6;

    /// <summary>A line of play closer than this (yds) to another hole's green is flagged; crossing it is worse.</summary>
    public double CorridorYards { get; init; } = 25;

    /// <summary>Aim points of two holes closer than this (yds) share a landing zone.</summary>
    public double AimSpacingYards { get; init; } = 35;

    /// <summary>Largest tee-to-green drop and climb (ft).</summary>
    public double MaxDropFeet { get; init; } = 50;
    public double MaxClimbFeet { get; init; } = 30;

    /// <summary>Ground range across the tees, aim points and greens (ft).</summary>
    public double PlotRangeMinFeet { get; init; } = 50;
    public double PlotRangeMaxFeet { get; init; } = 70;

    /// <summary>Internal height range of a built green (ft): flatter reads as under-built, more as extreme.</summary>
    public double GreenReliefMinFeet { get; init; } = 1.5;
    public double GreenReliefMaxFeet { get; init; } = 3;

    /// <summary>Normal green speed range (Stimp).</summary>
    public double StimpMin { get; init; } = 8;
    public double StimpMax { get; init; } = 13;

    /// <summary>Section 7: a two-hole same-par run is narrow under this spread and safe from this spread (yds).</summary>
    public double RunNarrowYards { get; init; } = 40;
    public double RunWideYards { get; init; } = 60;

    // TGC Tours' published minimums.

    /// <summary>Total par allowed.</summary>
    public int ParMin { get; init; } = 69;
    public int ParMax { get; init; } = 74;

    /// <summary>Holes of the same par should span at least this many yards.</summary>
    public double SameParSpreadYards { get; init; } = 30;

    /// <summary>A pin this close to the green's edge (yds) is flagged, to point at pins to count against the three-grid-square minimum in the designer.</summary>
    public double PinEdgeYards { get; init; } = 2;

    /// <summary>A pin with slope this steep (%) within its grid square is flagged: the greens book's red (3.3°).</summary>
    public double PinSteepPct { get; init; } = 5.8;

    // §12 length (card yards, back tee). Provisional.

    /// <summary>Back-tee total seen on passing courses: shown as a range, never judged.</summary>
    public double BackTeeMinYards { get; init; } = 7050;
    public double BackTeeMaxYards { get; init; } = 7600;

    /// <summary>The median par 4 should be at least this long (yds).</summary>
    public double MedianPar4MinYards { get; init; } = 430;

    /// <summary>Par 4s under this length (yds): more than <see cref="MaxShortPar4s"/> warns.</summary>
    public double ShortPar4Yards { get; init; } = 400;
    public int MaxShortPar4s { get; init; } = 3;

    /// <summary>The shortest par 5 should be at least this long (yds).</summary>
    public double ShortestPar5MinYards { get; init; } = 560;

    /// <summary>Card and straight-line yards this far apart (yds) are both shown.</summary>
    public double StraightDiffYards { get; init; } = 15;

    // §12 width, across the fairway at the design tee shot. Provisional.

    /// <summary>The default landing width (yds): shown as the target.</summary>
    public double LandingTargetMinYards { get; init; } = 35;
    public double LandingTargetMaxYards { get; init; } = 50;

    /// <summary>Wider than this at any station (yds) needs a written reason on the hole.</summary>
    public double WidthReasonYards { get; init; } = 60;

    /// <summary>Wider or narrower than these at any station (yds) warns; shared fairways count.</summary>
    public double WidthHighYards { get; init; } = 70;
    public double WidthLowYards { get; init; } = 25;

    /// <summary>Past 200 yds, wide stretches more than this many times the narrow ones warn.</summary>
    public double WidthSteadinessMax { get; init; } = 3;

    /// <summary>Hazards this close (yds) to the line between these distances from the back tee are listed (a prompt only).</summary>
    public double LandingHazardReachYards { get; init; } = 25;
    public double LandingHazardFromYards { get; init; } = 275;
    public double LandingHazardToYards { get; init; } = 360;

    // Notes only.

    /// <summary>Main fairway start from the back tee on par 4s and 5s (yds): shown, never warned.</summary>
    public double FairwayStartMinYards { get; init; } = 150;
    public double FairwayStartMaxYards { get; init; } = 225;

    /// <summary>The main fairway starts where the strip by the line is this wide (yds) and stays so this far (yds).</summary>
    public double FairwayStartWidthYards { get; init; } = 25;
    public double FairwayStartHoldYards { get; init; } = 35;
}
