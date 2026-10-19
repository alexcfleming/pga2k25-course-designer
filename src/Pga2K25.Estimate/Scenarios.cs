using System.Text.Json.Serialization;

namespace Pga2K25.Estimate;

[JsonConverter(typeof(JsonStringEnumConverter<PriceCase>))]
public enum PriceCase { Low, Likely, High }

/// <summary>One of the low / likely / high cases (scenarios.json). All values are placeholders set by judgement.</summary>
public sealed record Scenario
{
    [JsonIgnore] public string Name { get; init; } = "likely";
    /// <summary>Added to every zone's natural fidelity on top of Estimate.json's own offset.</summary>
    public double FidelityOffset { get; init; }
    public PriceCase Prices { get; init; } = PriceCase.Likely;
    /// <summary>For unit costs with no low/high: price × (1 + spread).</summary>
    public double NoRangeSpread { get; init; }
    /// <summary>Scales hole quantities tagged assumed.</summary>
    public double AssumedQuantityFactor { get; init; } = 1;
    /// <summary>Replaces the project contingency when set.</summary>
    public double? Contingency { get; init; }

    public static Scenario Likely { get; } = new();

    /// <summary>The unit price this scenario uses: the item's low or high where it has one, else price × (1 + spread).</summary>
    public double? PriceOf(UnitCost c) => Prices switch
    {
        PriceCase.Low => c.Low ?? c.Price * (1 + NoRangeSpread),
        PriceCase.High => c.High ?? c.Price * (1 + NoRangeSpread),
        _ => c.Price,
    };

    public QuantityRow Scale(QuantityRow q) =>
        q.Provenance != Provenance.Assumed || AssumedQuantityFactor == 1 ? q
            : q with { Qty = q.Qty * AssumedQuantityFactor, Basis = FormattableString.Invariant($"{q.Basis}; × {AssumedQuantityFactor:0.##} ({Name} case)") };

    /// <summary>Project settings with this scenario's contingency, if it sets one.</summary>
    public Dictionary<string, Setting> Apply(IReadOnlyDictionary<string, Setting> project)
    {
        var p = new Dictionary<string, Setting>(project);
        if (Contingency is { } c)
            p["contingency"] = new Setting { Value = c, Status = "placeholder", Source = $"{Name} scenario (scenarios.json)" };
        return p;
    }
}

public sealed record ScenarioFile
{
    public string Note { get; init; } = "";
    public Dictionary<string, Scenario> Scenarios { get; init; } = new();
    public List<double> FidelitySweep { get; init; } = [];

    public Scenario Get(string name) => Scenarios.TryGetValue(name, out var s) ? s with { Name = name }
        : throw new KeyNotFoundException($"Unknown scenario '{name}'.");
}
