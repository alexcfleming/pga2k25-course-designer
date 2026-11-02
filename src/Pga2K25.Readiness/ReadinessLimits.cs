namespace Pga2K25.Readiness;

/// <summary>
/// The numbers the course-file checks judge against. They come from the design principles (§3, §7, §9), the proposed
/// review limits (§11.4), TGC Tours' published minimums and a few provisional bands; none of them is a rule, so every
/// one can be changed per course.
/// </summary>
public sealed record ReadinessLimits
{
    /// <summary>Test drive with the power profile (yds), measured per course. Null skips the drive checks.</summary>
    public double? TestDriveYards { get; init; }

    /// <summary>A par 5 must leave at least this many yards after the test drive.</summary>
    public double Par5MinLeftYards { get; init; } = 220;

    /// <summary>A par 4 within this many yards over the test drive is drivable.</summary>
    public double DrivableWithinYards { get; init; } = 20;

    /// <summary>A par 4 within this many yards over the test drive is near-drivable.</summary>
    public double NearDrivableWithinYards { get; init; } = 50;

    /// <summary>Drivable plus near-drivable par 4s allowed per nine.</summary>
    public int MaxDrivablePerNine { get; init; } = 2;

    /// <summary>Green area and landing width are flagged outside this many times the course median.</summary>
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

    /// <summary>Every tee set's total (yds).</summary>
    public double TeeTotalMinYards { get; init; } = 6500;
    public double TeeTotalMaxYards { get; init; } = 7500;

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

    /// <summary>Fairway width TGC recommends (yds); narrower is noted, not flagged.</summary>
    public double FairwayMinWidthYards { get; init; } = 25;

    /// <summary>A pin this close to the green's edge (yds) is flagged: three of the in-game grid squares.</summary>
    public double PinEdgeYards { get; init; } = 2;

    /// <summary>A pin with slope this steep (%) within its grid square is flagged: the greens book's red (3.3°).</summary>
    public double PinSteepPct { get; init; } = 5.8;

    // Provisional: warnings only, from a small sample of reviewed courses.

    /// <summary>Main fairway start from the back tee on par 4s and 5s (yds).</summary>
    public double FairwayStartMinYards { get; init; } = 150;
    public double FairwayStartMaxYards { get; init; } = 225;

    /// <summary>The main fairway starts where the strip by the line is this wide (yds) and stays so this far (yds).</summary>
    public double FairwayStartWidthYards { get; init; } = 25;
    public double FairwayStartHoldYards { get; init; } = 35;

    /// <summary>Par 4s this long or shorter (yds) are counted; more than <see cref="MaxShortPar4s"/> warns.</summary>
    public double ShortPar4Yards { get; init; } = 380;
    public int MaxShortPar4s { get; init; } = 2;

    /// <summary>Past 200 yds, wide stretches more than this many times the narrow ones warn.</summary>
    public double WidthSteadinessMax { get; init; } = 3;

    /// <summary>Greens smaller than this (sq ft) warn.</summary>
    public double SmallGreenSquareFeet { get; init; } = 3000;
}
