using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pga2K25.Finisher.Support;

/// <summary>
/// Exact equivalents of the Python built-ins the reference Course Finisher relies on for anything
/// that is persisted or hashed: <c>round(x, n)</c>, <c>repr(float)</c>, <c>f"{x:.3f}"</c> and
/// <c>json.dumps(..., sort_keys=True, separators=(',', ':'), ensure_ascii=False)</c>.
/// Matching them byte for byte means provenance signatures written by the Python tool are
/// recognised here and vice versa, so either tool can safely re-run over the other's output.
/// </summary>
public static class Py
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Python <c>round(x, digits)</c>: round half to even on the exact binary value, then nearest double.</summary>
    public static double Round(double x, int digits)
    {
        if (!double.IsFinite(x) || x == 0) return x;
        var (negative, q) = ScaledInteger(x, digits);
        double r = double.Parse(InsertPoint(q.ToString(Inv), digits), Inv);
        return negative ? -r : r;
    }

    /// <summary>Python <c>f"{x:.{digits}f}"</c> (exact half-even rounding, keeps "-0.000").</summary>
    public static string Fixed(double x, int digits)
    {
        if (double.IsNaN(x)) return "nan";
        if (double.IsInfinity(x)) return x > 0 ? "inf" : "-inf";
        bool negative = x < 0 || (x == 0 && double.IsNegative(x));
        if (x == 0) return (negative ? "-" : "") + (digits > 0 ? "0." + new string('0', digits) : "0");
        var (_, q) = ScaledInteger(x, digits);
        return (negative ? "-" : "") + InsertPoint(q.ToString(Inv), digits);
    }

    /// <summary>|x| · 10^digits rounded half-even, computed exactly from the IEEE bits.</summary>
    static (bool Negative, BigInteger Value) ScaledInteger(double x, int digits)
    {
        long bits = BitConverter.DoubleToInt64Bits(x);
        bool negative = bits < 0;
        int exponent = (int)((bits >> 52) & 0x7FF);
        long mantissa = bits & 0xFFFFFFFFFFFFFL;
        if (exponent == 0) exponent++; else mantissa |= 1L << 52;
        exponent -= 1075;   // x = mantissa · 2^exponent
        var num = new BigInteger(mantissa) * BigInteger.Pow(10, digits);
        if (exponent >= 0) return (negative, num << exponent);
        var den = BigInteger.One << -exponent;
        var q = BigInteger.DivRem(num, den, out var rem);
        int cmp = (rem * 2).CompareTo(den);
        if (cmp > 0 || (cmp == 0 && !q.IsEven)) q += 1;
        return (negative, q);
    }

    static string InsertPoint(string digitsText, int digits)
    {
        if (digits <= 0) return digitsText;
        digitsText = digitsText.PadLeft(digits + 1, '0');
        return digitsText[..^digits] + "." + digitsText[^digits..];
    }

    /// <summary>Python <c>repr(float)</c>: shortest round-trip digits, fixed for 1e-4 ≤ |x| &lt; 1e16, else exponent form.</summary>
    public static string Repr(double x)
    {
        if (double.IsNaN(x)) return "NaN";             // json.dumps spelling
        if (double.IsInfinity(x)) return x > 0 ? "Infinity" : "-Infinity";
        if (x == 0) return double.IsNegative(x) ? "-0.0" : "0.0";

        string s = x.ToString("R", Inv);
        bool negative = s[0] == '-';
        if (negative) s = s[1..];
        int exp = 0;
        int e = s.IndexOfAny(['E', 'e']);
        if (e >= 0)
        {
            exp = int.Parse(s[(e + 1)..], Inv);
            s = s[..e];
        }
        int dot = s.IndexOf('.');
        string intPart = dot >= 0 ? s[..dot] : s;
        string frac = dot >= 0 ? s[(dot + 1)..] : "";
        string digitsAll = (intPart + frac).TrimStart('0');
        int lead = (intPart + frac).Length - digitsAll.Length;
        // Scientific exponent of the first significant digit.
        int sci = intPart.Length - 1 - lead + exp;
        string digitsStr = digitsAll.TrimEnd('0');
        if (digitsStr.Length == 0) digitsStr = "0";

        string body;
        if (sci >= -4 && sci < 16)
        {
            if (sci >= 0)
            {
                string ip = digitsStr.Length > sci + 1 ? digitsStr[..(sci + 1)] : digitsStr.PadRight(sci + 1, '0');
                string fp = digitsStr.Length > sci + 1 ? digitsStr[(sci + 1)..] : "0";
                body = ip + "." + fp;
            }
            else body = "0." + new string('0', -sci - 1) + digitsStr;
        }
        else
        {
            body = digitsStr[..1] + (digitsStr.Length > 1 ? "." + digitsStr[1..] : "") +
                   "e" + (sci < 0 ? "-" : "+") + Math.Abs(sci).ToString("00", Inv);
        }
        return negative ? "-" + body : body;
    }

    /// <summary>
    /// Python <c>json.dumps(node, sort_keys, separators=(',', ':'), ensure_ascii=False)</c>.
    /// With <paramref name="roundFloats"/>, every JSON float is first rounded to that many decimals
    /// (the reference tool's <c>json.loads(..., parse_float=lambda x: round(float(x), 3))</c>).
    /// JSON integers stay integers; a number counts as a float when its text has '.', 'e' or 'E'.
    /// </summary>
    public static string Dumps(JsonNode? node, bool sortKeys = true, int? roundFloats = null)
    {
        var sb = new StringBuilder();
        Write(sb, node, sortKeys, roundFloats);
        return sb.ToString();
    }

    static void Write(StringBuilder sb, JsonNode? node, bool sortKeys, int? roundFloats)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                return;
            case JsonObject obj:
            {
                sb.Append('{');
                IEnumerable<KeyValuePair<string, JsonNode?>> items = obj;
                if (sortKeys) items = items.OrderBy(kv => kv.Key, StringComparer.Ordinal);
                bool first = true;
                foreach (var (key, value) in items)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, key);
                    sb.Append(':');
                    Write(sb, value, sortKeys, roundFloats);
                }
                sb.Append('}');
                return;
            }
            case JsonArray arr:
            {
                sb.Append('[');
                for (int i = 0; i < arr.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Write(sb, arr[i], sortKeys, roundFloats);
                }
                sb.Append(']');
                return;
            }
            case JsonValue value:
                WriteValue(sb, value, roundFloats);
                return;
        }
    }

    static void WriteValue(StringBuilder sb, JsonValue value, int? roundFloats)
    {
        if (value.TryGetValue(out JsonElement e))
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.String: WriteString(sb, e.GetString()!); return;
                case JsonValueKind.True: sb.Append("true"); return;
                case JsonValueKind.False: sb.Append("false"); return;
                case JsonValueKind.Null: sb.Append("null"); return;
                case JsonValueKind.Number:
                {
                    string raw = e.GetRawText();
                    if (raw.IndexOfAny(['.', 'e', 'E']) < 0)
                    {
                        sb.Append(BigInteger.Parse(raw, Inv).ToString(Inv));
                        return;
                    }
                    WriteFloat(sb, double.Parse(raw, Inv), roundFloats);
                    return;
                }
            }
        }
        if (value.TryGetValue(out string? s)) { WriteString(sb, s); return; }
        if (value.TryGetValue(out bool b)) { sb.Append(b ? "true" : "false"); return; }
        if (value.TryGetValue(out long l)) { sb.Append(l.ToString(Inv)); return; }
        if (value.TryGetValue(out int i)) { sb.Append(i.ToString(Inv)); return; }
        if (value.TryGetValue(out double d)) { WriteFloat(sb, d, roundFloats); return; }
        if (value.TryGetValue(out float f)) { WriteFloat(sb, f, roundFloats); return; }
        sb.Append(value.ToJsonString());
    }

    static void WriteFloat(StringBuilder sb, double d, int? roundFloats) =>
        sb.Append(Repr(roundFloats is int n ? Round(d, n) : d));

    static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    public static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>Python <c>str(float)</c> is <c>repr</c>; <c>str(round(x, n))</c> appears in every log line.</summary>
    public static string R(double x, int digits) => Repr(Round(x, digits));
}
