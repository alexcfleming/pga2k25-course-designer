using Pga2K25.CourseFormat;

namespace Pga2K25.Readiness;

/// <summary>How a check came out, mildest first.</summary>
public enum Severity
{
    Pass,
    /// <summary>Recorded for the file, nothing to judge (lighting, the green speed as Stimp).</summary>
    Info,
    /// <summary>
    /// A provisional check outside its band: worth a look, never a pass or a fail, and it doesn't hold the gate.
    /// These limits come from a small sample of reviewed courses, so they stay warnings until the evidence firms up.
    /// </summary>
    Warning,
    /// <summary>Needs a reading or a figure the file can't give (no test drive entered, an unmapped setting).</summary>
    Confirm,
    /// <summary>Outside a limit: fix it or mark it deliberate.</summary>
    Flag,
    /// <summary>What reviewers weighed most: hole design and variety.</summary>
    High,
}

/// <summary>
/// One course-file check. <paramref name="Id"/> is stable across runs, so a deliberate mark or a note can stay
/// with it; <paramref name="Holes"/> are the holes it names (empty for course-wide checks).
/// </summary>
public sealed record ReadinessCheck(string Id, string Title, Severity Severity, string Detail, string Rule,
    IReadOnlyList<int> Holes)
{
    public bool Passed => Severity is Severity.Pass or Severity.Info or Severity.Warning;
}

/// <summary>
/// Where a pin sits on its green, seen along the approach: F / C / B, and L / C / R. <see cref="EdgeYards"/> is how far
/// it is from the green's edge; <see cref="SlopePct"/> the steepest slope within a cup's grid square of it (terrain
/// model), null where the green is flat there or no green is drawn.
/// </summary>
public sealed record PinPlacement(int Number, char Depth, char Side)
{
    public double? EdgeYards { get; init; }
    public double? SlopePct { get; init; }
}

/// <summary>What the designer says about a hole that changes its measures: a fairway start of their own, a shared fairway.</summary>
public sealed record HoleOverride(double? FairwayStartYards, bool SharedFairway);

/// <summary>Everything measured on one hole. Yards are from the back tee; null means not measurable on this hole.</summary>
public sealed record HoleMeasures
{
    public required int Number { get; init; }
    public required int Par { get; init; }
    public required int Yards { get; init; }

    /// <summary>"Drivable", "Near-drivable", or null (par 4s only, with a test drive).</summary>
    public string? DriveClass { get; init; }
    /// <summary>Par 5s: yards left after the test drive.</summary>
    public int? LeftAfterDrive { get; init; }

    public double GreenSquareFeet { get; init; }
    /// <summary>True when the green isn't drawn yet (the game's own estimate): left out of the medians.</summary>
    public bool GreenEstimated { get; init; }
    public double? GreenRatio { get; init; }

    /// <summary>Contiguous fairway width across the line where the tee shot lands (par 4s and 5s).</summary>
    public double? LandingWidthYards { get; init; }
    public bool LandingOffFairway { get; init; }
    public double? WidthRatio { get; init; }

    /// <summary>
    /// Par 4s and 5s: how far from the back tee the main fairway starts, from the file (null when no fairway holds the
    /// width along the line). 60 means 60 yds or less.
    /// </summary>
    public double? FairwayStartFileYards { get; init; }
    /// <summary>The designer's own fairway start, when typed over the file's.</summary>
    public double? FairwayStartOverrideYards { get; init; }
    public double? FairwayStartYards => FairwayStartOverrideYards ?? FairwayStartFileYards;
    /// <summary>Shares its fairway with other holes: left out of the width figures and the fairway-start median.</summary>
    public bool SharedFairway { get; init; }
    /// <summary>The narrowest the line's fairway gets from where it starts to short of the green (yds).</summary>
    public double? NarrowestFairwayYards { get; init; }
    /// <summary>Past 200 yds, how many times the fairway's wide stretches are its narrow ones (90th over 10th percentile).</summary>
    public double? WidthSteadiness { get; init; }

    /// <summary>Green site height minus back tee height (ft), from the terrain model.</summary>
    public double ChangeFeet { get; init; }

    /// <summary>Highest minus lowest point inside a built green (ft), from the terrain model.</summary>
    public double? GreenReliefFeet { get; init; }
    public double? GreenMedianSlopePct { get; init; }

    /// <summary>The nearest other green to this hole's line of play: whose (0 for a practice green) and how far.</summary>
    public int? NearGreenOf { get; init; }
    public double? NearGreenYards { get; init; }

    public bool EndsOnOwnGreen { get; init; }
    public IReadOnlyList<PinPlacement> Pins { get; init; } = [];
}

public sealed record TeeSetTotal(int Index, int Out, int In)
{
    public int Total => Out + In;
}

/// <summary>Section 7: consecutive holes sharing a par.</summary>
public sealed record SameParRun(int FirstHole, int LastHole, int Par, IReadOnlyList<int> Yards, string Finding, Severity Severity)
{
    public int Spread => Yards.Max() - Yards.Min();
}

public sealed record ReadinessReport
{
    public required string CourseName { get; init; }
    public required ReadinessLimits Limits { get; init; }
    public required IReadOnlyList<HoleMeasures> Holes { get; init; }
    public required IReadOnlyList<TeeSetTotal> TeeSets { get; init; }
    public required IReadOnlyList<SameParRun> Runs { get; init; }
    public required IReadOnlyList<ReadinessCheck> Checks { get; init; }
    public Atmosphere? Atmosphere { get; init; }
    public double? Stimp { get; init; }
    public double MedianGreenSquareFeet { get; init; }
    public double MedianLandingYards { get; init; }
    /// <summary>Median main-fairway start over the par 4s and 5s not marked shared (yds; 0 when none).</summary>
    public double MedianFairwayStartYards { get; init; }
    public double PlotRangeFeet { get; init; }

    public IEnumerable<ReadinessCheck> Open => Checks.Where(c => !c.Passed);
    public ReadinessCheck? Check(string id) => Checks.FirstOrDefault(c => c.Id == id);
}
