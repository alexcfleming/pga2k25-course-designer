using Pga2K25.CourseFormat;

namespace Pga2K25.HoleIllustrations;

/// <summary>How the hole plates are drawn. Every number on a plate still comes from the course file.</summary>
public sealed record IllustrationOptions
{
    /// <summary>The designer credit ("DESIGNED BY … · year"); blank prints no designer line.</summary>
    public string Credit { get; init; } = "";

    public int Year { get; init; } = DateTime.Now.Year;

    /// <summary>The Studio's hole entries (Holes.json) for tee and hole names; null reads them from the output folder.</summary>
    public CourseHoles? Holes { get; init; }

    /// <summary>The ruler's top: back-tee yards plus this margin, rounded up to the next step (locked 7 Nov 2026).</summary>
    public int RulerMarginYards { get; init; } = 30;
    public int RulerStepYards { get; init; } = 50;

    /// <summary>How far the rough wash reaches past this hole's corridor (yds, before its ragged noise).</summary>
    public double RoughReachYards { get; init; } = 32;

    /// <summary>The line of play is buffered by this much (yds) into the corridor the rough wraps.</summary>
    public double RouteBufferYards { get; init; } = 28;

    /// <summary>Radius of the scallops on a canopy edge (yds).</summary>
    public double CanopyBumpYards { get; init; } = 3.4;

    /// <summary>A merged bunker bigger than this (sq yd) is drawn as waste: a peach wash, no hachures (locked 7 Nov 2026).</summary>
    public double WasteThresholdSqYd { get; init; } = 1500;

    /// <summary>A bunker this close to the green centre (yds) has its face toward the green, else toward the tee.</summary>
    public double GreensideBunkerYards { get; init; } = 45;

    public int MowBandYards { get; init; } = 12;
    public bool ShowTeeStrip { get; init; } = true;
    public bool ShowLineOfPlay { get; init; } = true;

    /// <summary>Also draw surface-15 waste areas, ribbons and brushes through the waste style.</summary>
    public bool Waste { get; init; }

    public IllustrationStyle Style { get; init; } = IllustrationStyle.Default;
}
