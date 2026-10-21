namespace Pga2K25.Readiness;

/// <summary>
/// The numbers the course-file checks judge against. They come from the design principles (§3, §7, §9) and the
/// proposed tour-readiness limits (§11.4); none of them is a rule, so every one can be changed per course.
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
}
