using System.Globalization;
using System.Text.Json.Nodes;
using Pga2K25.CourseFormat;

namespace Pga2K25.Finisher.Support;

/// <summary>
/// Small helpers for working on the mutable CourseDescription tree the way the Python tool works
/// on its dict: tolerant reads (<c>get(key, default) or default</c>), <c>setdefault</c>, and
/// number creation that keeps the game's float spelling (<c>1.0</c>, never <c>1</c>).
/// </summary>
public static class Json
{
    /// <summary>
    /// A JSON float written with Python <c>repr</c> digits. Non-finite values become null: Python's
    /// <c>json.dumps</c> would emit a bare <c>NaN</c>, which is not valid JSON.
    /// </summary>
    public static JsonNode? F(double v) => double.IsFinite(v) ? JsonNode.Parse(Py.Repr(v)) : null;

    public static JsonNode I(long v) => JsonValue.Create(v);

    public static JsonNode S(string v) => JsonValue.Create(v)!;

    /// <summary>The literal string <c>"-Infinity"</c> the game stores for "snap to terrain" Y.</summary>
    public static JsonNode NegInf() => JsonValue.Create("-Infinity")!;

    /// <summary><c>{"x": x, "y": y, "z": z}</c> with float values.</summary>
    public static JsonObject Xyz(double x, double y, double z) => new() { ["x"] = F(x), ["y"] = F(y), ["z"] = F(z) };

    /// <summary><c>{"x": x, "y": "-Infinity", "z": z}</c>.</summary>
    public static JsonObject XGroundZ(double x, double z) => new() { ["x"] = F(x), ["y"] = NegInf(), ["z"] = F(z) };

    /// <summary>Number or NaN when missing / not numeric (stands in for Python's KeyError/ValueError paths).</summary>
    public static double Num(JsonNode? n) => CourseModel.Num(n, double.NaN);

    public static double Num(JsonNode? n, double fallback) => CourseModel.Num(n, fallback);

    /// <summary>Python <c>int(x)</c>: truncates toward zero; <paramref name="fallback"/> when unreadable.</summary>
    public static int Int(JsonNode? n, int fallback)
    {
        double d = CourseModel.Num(n, double.NaN);
        return double.IsFinite(d) ? (int)Math.Truncate(d) : fallback;
    }

    public static string Str(JsonNode? n, string fallback = "")
    {
        if (n is not JsonValue v) return fallback;
        if (v.TryGetValue(out string? s)) return s;
        return v.ToJsonString();
    }

    /// <summary>Python truthiness of a JSON value.</summary>
    public static bool Truthy(JsonNode? n) => n switch
    {
        null => false,
        JsonArray a => a.Count > 0,
        JsonObject o => o.Count > 0,
        JsonValue v when v.TryGetValue(out bool b) => b,
        JsonValue v when v.TryGetValue(out string? s) => s.Length > 0,
        JsonValue v => CourseModel.Num(v, 0) != 0,
        _ => true,
    };

    /// <summary><c>o.get(key, []) or []</c> — never null; a fresh empty array when missing.</summary>
    public static JsonArray Arr(JsonObject? o, string key) => o?[key] as JsonArray ?? [];

    public static JsonObject Obj(JsonObject? o, string key) => o?[key] as JsonObject ?? [];

    /// <summary><c>o.setdefault(key, [])</c>.</summary>
    public static JsonArray EnsureArray(JsonObject o, string key)
    {
        if (o[key] is JsonArray a) return a;
        var created = new JsonArray();
        o[key] = created;
        return created;
    }

    /// <summary><c>o.setdefault(key, {})</c>.</summary>
    public static JsonObject EnsureObject(JsonObject o, string key)
    {
        if (o[key] is JsonObject a) return a;
        var created = new JsonObject();
        o[key] = created;
        return created;
    }

    /// <summary>X/Z of a <c>{"x":…, "z":…}</c> point (NaN when absent).</summary>
    public static (double X, double Z) XZ(JsonNode? p) => (Num(p?["x"]), Num(p?["z"]));

    public static bool Finite(double x, double z) => double.IsFinite(x) && double.IsFinite(z);

    public static JsonArray Array(IEnumerable<JsonNode?> items)
    {
        var a = new JsonArray();
        foreach (var item in items) a.Add(item);
        return a;
    }

    public static JsonArray Floats(IEnumerable<double> values) => Array(values.Select(v => (JsonNode?)F(v)));

    public static JsonArray Ints(IEnumerable<int> values) => Array(values.Select(v => (JsonNode?)I(v)));

    public static string Str(double v) => Py.Repr(v);

    public static string Str(int v) => v.ToString(CultureInfo.InvariantCulture);
}
