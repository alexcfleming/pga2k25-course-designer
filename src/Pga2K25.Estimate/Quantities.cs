namespace Pga2K25.Estimate;

/// <summary>One quantity, tagged with where it came from.</summary>
/// <param name="Basis">Short note on how the number was got, e.g. "delta vs moderate_earthmoving baseline, g=0.5".</param>
public sealed record QuantityRow(int? Hole, string Item, double Qty, string Unit, Provenance Provenance, Confidence? Confidence, string Basis);

/// <summary>A priced quantity. <see cref="Cost"/> is null when the item has no price yet.</summary>
public sealed record LineItem(QuantityRow Quantity, string CostKey, double? UnitPrice, double? Cost, string PriceStatus, string PriceSource)
{
    public bool IsPlaceholder => PriceStatus.Equals("placeholder", StringComparison.OrdinalIgnoreCase);

    public static LineItem Price(QuantityRow q, string key, UnitCost c, double priceFactor = 1)
    {
        double? unit = c.Price * priceFactor;
        return new LineItem(q, key, unit, unit * q.Qty, c.Status, c.Source);
    }
}

/// <summary>An entry in the baseline assumption register: what was assumed about the land, by whom, how sure.</summary>
public sealed record Assumption(string Scope, string Statement, string SetBy, Confidence Confidence);
