using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pga2K25.CourseFormat;

/// <summary>
/// A PGA TOUR 2K25 <c>.course</c> file: gzip &gt; UTF-16 JSON envelope whose
/// <c>binaryData</c> values are each base64 &gt; gzip &gt; UTF-16 JSON.
/// The JSON trees are kept as <see cref="JsonNode"/> so unknown keys and
/// original number text survive a load/save round-trip untouched.
/// </summary>
public sealed class CourseFile
{
    public const string DescriptionKey = "CourseDescription";
    public const string MetadataKey = "CourseMetadata";
    public const string ThumbnailKey = "Thumbnail";

    static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    /// <summary>The outer envelope (<c>data</c>, <c>binaryData</c>, ...) minus the decoded blobs.</summary>
    public JsonObject Envelope { get; }

    /// <summary>Decoded <c>binaryData</c> documents in their original order.</summary>
    public IReadOnlyList<KeyValuePair<string, JsonNode>> Blobs => _blobs;

    readonly List<KeyValuePair<string, JsonNode>> _blobs;

    CourseFile(JsonObject envelope, List<KeyValuePair<string, JsonNode>> blobs)
    {
        Envelope = envelope;
        _blobs = blobs;
    }

    public JsonObject Description => (JsonObject)GetBlob(DescriptionKey)!;
    public JsonObject? Metadata => GetBlob(MetadataKey) as JsonObject;

    public JsonNode? GetBlob(string name) =>
        _blobs.FirstOrDefault(kv => kv.Key == name).Value;

    public CourseModel ReadModel() => CourseModel.From(Description);

    public static CourseFile Load(string path) => Load(File.ReadAllBytes(path));

    public static CourseFile Load(byte[] fileBytes)
    {
        var envelope = ParseText(Gunzip(fileBytes)) as JsonObject
            ?? throw new InvalidDataException("Course envelope is not a JSON object.");
        var binary = envelope["binaryData"] as JsonObject
            ?? throw new InvalidDataException("Course envelope has no binaryData object.");

        var blobs = new List<KeyValuePair<string, JsonNode>>();
        foreach (var (name, value) in binary)
        {
            var raw = Gunzip(Convert.FromBase64String(value!.GetValue<string>()));
            blobs.Add(new(name, ParseText(raw)
                ?? throw new InvalidDataException($"binaryData.{name} decoded to null.")));
        }
        envelope.Remove("binaryData");
        return new CourseFile(envelope, blobs);
    }

    public void Save(string path, CompressionLevel level = CompressionLevel.Optimal) =>
        File.WriteAllBytes(path, ToBytes(level));

    public byte[] ToBytes(CompressionLevel level = CompressionLevel.Optimal)
    {
        var binary = new JsonObject();
        foreach (var (name, node) in _blobs)
            binary[name] = Convert.ToBase64String(Gzip(EncodeText(node), level));

        // Rebuild the envelope preserving the original key order with binaryData in its slot.
        var outer = new JsonObject();
        bool placed = false;
        foreach (var (key, value) in Envelope)
        {
            if (!placed && key == "data")
            {
                outer[key] = value?.DeepClone();
                outer["binaryData"] = binary;
                placed = true;
                continue;
            }
            outer[key] = value?.DeepClone();
        }
        if (!placed) outer["binaryData"] = binary;
        return Gzip(EncodeText(outer), level);
    }

    static JsonNode? ParseText(byte[] raw)
    {
        string text = DecodeText(raw);
        return JsonNode.Parse(text);
    }

    static string DecodeText(byte[] raw)
    {
        if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
            return Encoding.Unicode.GetString(raw, 2, raw.Length - 2);
        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
            return Encoding.UTF8.GetString(raw, 3, raw.Length - 3);
        // No BOM: UTF-16 LE text has a zero high byte on the leading '{'.
        return raw.Length >= 2 && raw[1] == 0 ? Encoding.Unicode.GetString(raw) : Encoding.UTF8.GetString(raw);
    }

    static byte[] EncodeText(JsonNode node)
    {
        string text = node.ToJsonString(WriteOptions);
        var preamble = Encoding.Unicode.GetPreamble();
        var body = Encoding.Unicode.GetBytes(text);
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }

    static byte[] Gunzip(byte[] data)
    {
        using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    static byte[] Gzip(byte[] data, CompressionLevel level)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, level))
            gz.Write(data);
        return output.ToArray();
    }
}
