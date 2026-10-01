using System.Text.Json.Nodes;
using Pga2K25.Finisher.Support;

namespace Pga2K25.Finisher;

/// <summary>
/// The <c>malificaCourseFinisher</c> record the Finisher keeps inside CourseDescription. Generated
/// edits are appended as array tails and described here (start, count, signature), so a re-run can
/// remove exactly its own prior output and leave anything the designer touched alone.
/// The key is shared with the Python tool on purpose: both read and honour each other's records.
/// </summary>
public static class Provenance
{
    public const string FinisherKey = "malificaCourseFinisher";
    public const int FinisherVersion = 10;

    public static JsonObject Meta(JsonObject course) => Json.EnsureObject(course, FinisherKey);

    public static JsonObject? Existing(JsonObject course, string feature) =>
        (course[FinisherKey] as JsonObject)?[feature] as JsonObject;

    /// <summary><c>meta['version'] = max(int(meta.get('version', 0) or 0), FINISHER_VERSION)</c>.</summary>
    public static void BumpVersion(JsonObject meta) =>
        meta["version"] = Json.I(Math.Max(Json.Int(meta["version"], 0), FinisherVersion));

    /// <summary>Some passes (cart paths, water) set the version outright rather than taking the max.</summary>
    public static void SetVersion(JsonObject meta) => meta["version"] = Json.I(FinisherVersion);

    public static void Remove(JsonObject course, string feature) => (course[FinisherKey] as JsonObject)?.Remove(feature);

    /// <summary>
    /// <c>_surface_brush_tail_signature</c>: SHA-256 of the items as sorted-key compact JSON with
    /// every float rounded to 3 decimals.
    /// </summary>
    public static string TailSignature(IEnumerable<JsonNode?> items) =>
        Py.Sha256Hex(Py.Dumps(Json.Array(items.Select(i => i?.DeepClone())), sortKeys: true, roundFloats: 3));

    /// <summary>
    /// <c>_prior_tee_flatten_tail</c>: the Finisher's tee-levelling tail, but only while it is still
    /// the exact end of <c>height[]</c>.
    /// </summary>
    public static (int Start, int Count)? PriorTeeFlattenTail(JsonObject course)
    {
        var info = Existing(course, "teeFlatten");
        if (info is null) return null;
        int start = Json.Int(info["startIndex"], -1);
        int count = Json.Int(info["stamps"], 0);
        int length = Json.Arr(course, "height").Count;
        if (start < 0 || count <= 0 || start + count != length) return null;
        return (start, count);
    }

    /// <summary>
    /// Length of <c>height[]</c> that belongs to the source terrain: everything before the Finisher's
    /// own tee-levelling tail (which every terrain sampler ignores).
    /// </summary>
    public static int SourceHeightLength(JsonObject course) =>
        PriorTeeFlattenTail(course)?.Start ?? Json.Arr(course, "height").Count;
}
