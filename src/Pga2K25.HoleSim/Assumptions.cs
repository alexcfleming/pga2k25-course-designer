using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pga2K25.HoleSim;

/// <summary>Where a value came from. PLACEHOLDER values must look different everywhere they appear.</summary>
public enum SourceTag { File, Measured, Prior, Alex, Placeholder }

public enum RowKind { Number, Numbers, Choice, Text }

public sealed class AssumptionRow
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public required string Id { get; init; }
    public required string Group { get; init; }
    public required string Label { get; init; }
    public required RowKind Kind { get; init; }
    public required string Value { get; set; }
    public string Unit { get; init; } = "";
    public IReadOnlyList<string> Options { get; init; } = [];
    public required SourceTag Source { get; set; }
    public string Note { get; set; } = "";
    public string Date { get; init; } = "";
    public required string DefaultValue { get; init; }
    public required SourceTag DefaultSource { get; init; }

    /// <summary>When this value was last confirmed in an earlier session (shown beside a prefilled row).</summary>
    public DateTime? PriorConfirmed { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public bool Confirmed => ConfirmedAt is not null;

    /// <summary>True when the row's value is unvalidated, whether it came straight from the defaults or was reused.</summary>
    public bool IsPlaceholder => Source == SourceTag.Placeholder;
    public bool Editable => Kind != RowKind.Text && Source != SourceTag.File;

    public double Number => double.Parse(Value, Inv);
    public double[] Numbers => Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(v => double.Parse(v, Inv)).ToArray();

    public static string Tag(SourceTag s) => s switch
    {
        SourceTag.File => "FILE", SourceTag.Measured => "MEASURED", SourceTag.Prior => "PRIOR", SourceTag.Alex => "ALEX", _ => "PLACEHOLDER",
    };

    /// <summary>Sets an edited value (tagged ALEX and confirmed) or explains why it can't be read.</summary>
    public string? TrySet(string text, DateTime now)
    {
        text = text.Trim();
        switch (Kind)
        {
            case RowKind.Number:
                if (!double.TryParse(text, NumberStyles.Float, Inv, out var v)) return $"\"{text}\" isn't a number.";
                text = v.ToString("0.###", Inv); break;
            case RowKind.Numbers:
                var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                int want = Numbers.Length;
                if (parts.Length != want || parts.Any(p => !double.TryParse(p, NumberStyles.Float, Inv, out _)))
                    return $"Enter {want} numbers separated by commas ({Unit}).";
                text = string.Join(", ", parts.Select(p => double.Parse(p, Inv).ToString("0.###", Inv))); break;
            case RowKind.Choice:
                var match = Options.FirstOrDefault(o => o.Equals(text, StringComparison.OrdinalIgnoreCase));
                if (match is null) return $"Choose one of: {string.Join(", ", Options)}.";
                text = match; break;
            default:
                return "This row can't be edited here.";
        }
        if (text != Value)
        {
            Value = text; Source = SourceTag.Alex;
            Note = $"Your value this session; the default is {DefaultValue} ({Tag(DefaultSource)}).";
        }
        ConfirmedAt = now;
        return null;
    }
}

/// <summary>
/// The session's assumption sheet. Every row needs a confirm or an edit before a run. A value saved from an earlier
/// session may prefill a row, shown with when it was last confirmed, but never skips the confirmation.
/// </summary>
public sealed class AssumptionSheet
{
    public const string CourseGroup = "Course settings";

    public List<AssumptionRow> Rows { get; } = [];
    public string DefaultsUpdated { get; private set; } = "";

    public IEnumerable<string> Groups => Rows.Select(r => r.Group).Distinct();
    public bool AllConfirmed => Rows.All(r => r.Confirmed);
    public int ConfirmedCount => Rows.Count(r => r.Confirmed);
    public AssumptionRow this[string id] => Rows.First(r => r.Id == id);

    /// <summary>The shipped defaults, prefilled from <paramref name="priorsPath"/> when it exists.</summary>
    public static AssumptionSheet Load(string? priorsPath = null)
    {
        using var s = typeof(AssumptionSheet).Assembly.GetManifestResourceStream("holesim.assumptions.json")
            ?? throw new InvalidOperationException("assumptions.json isn't embedded.");
        var root = JsonNode.Parse(s)!.AsObject();
        var sheet = new AssumptionSheet { DefaultsUpdated = (string?)root["updated"] ?? "" };
        foreach (var n in root["rows"]!.AsArray())
        {
            var o = n!.AsObject();
            var src = ParseTag((string)o["source"]!);
            var value = (string)o["value"]!;
            sheet.Rows.Add(new AssumptionRow
            {
                Id = (string)o["id"]!, Group = (string)o["group"]!, Label = (string)o["label"]!,
                Kind = Enum.Parse<RowKind>((string)o["kind"]!, true), Value = value, DefaultValue = value,
                Unit = (string?)o["unit"] ?? "", Options = o["options"]?.AsArray().Select(x => (string)x!).ToList() ?? [],
                Source = src, DefaultSource = src, Note = (string?)o["note"] ?? "", Date = (string?)o["date"] ?? "",
            });
        }
        if (priorsPath is not null && File.Exists(priorsPath)) sheet.ApplyPriors(priorsPath);
        return sheet;
    }

    static SourceTag ParseTag(string s) => s.ToUpperInvariant() switch
    {
        "FILE" => SourceTag.File, "MEASURED" => SourceTag.Measured, "PRIOR" => SourceTag.Prior, "ALEX" => SourceTag.Alex, _ => SourceTag.Placeholder,
    };

    void ApplyPriors(string path)
    {
        JsonObject? root;
        try { root = JsonNode.Parse(File.ReadAllText(path))?.AsObject(); } catch (Exception) { return; }
        if (root?["rows"] is not JsonObject rows) return;
        foreach (var r in Rows)
        {
            if (rows[r.Id] is not JsonObject p) continue;
            var value = (string?)p["value"];
            DateTime? at = DateTime.TryParse((string?)p["confirmedAt"], Inv, DateTimeStyles.RoundtripKind, out var d) ? d : null;
            r.PriorConfirmed = at;
            if (value is null || value == r.DefaultValue || r.Kind == RowKind.Text) continue;
            if (r.Kind == RowKind.Choice && !r.Options.Contains(value)) continue;
            r.Value = value;
            r.Source = SourceTag.Prior;
            r.Note = $"Your value from {at:d MMM yyyy}; the default is {r.DefaultValue} ({AssumptionRow.Tag(r.DefaultSource)}). " + r.Note;
        }
    }

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Saves the confirmed values so the next session can prefill them (course settings aren't kept: they come from the file).</summary>
    public void SavePriors(string path)
    {
        var rows = new JsonObject();
        foreach (var r in Rows.Where(r => r.Confirmed && r.Group != CourseGroup))
            rows[r.Id] = new JsonObject { ["value"] = r.Value, ["source"] = AssumptionRow.Tag(r.Source), ["confirmedAt"] = r.ConfirmedAt!.Value.ToString("o", Inv) };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new JsonObject { ["version"] = 1, ["rows"] = rows }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Replaces the course-settings rows with the file's values. Rows whose value changed need confirming again.</summary>
    public void SetCourseSettings(HoleModel model)
    {
        var old = Rows.Where(r => r.Group == CourseGroup).ToDictionary(r => r.Id);
        Rows.RemoveAll(r => r.Group == CourseGroup);
        foreach (var s in model.Settings)
        {
            string id = "course." + s.Key, value = s.Text, note = "Shown as set in the file. How it maps to ball behaviour in the game is unknown, so the model doesn't use it yet.";
            var tag = SourceTag.File;
            if (s.Key == "greenSpeed" && Stimp(s.Value) is { } st)
            {
                value = $"{s.Text} → about Stimp {st:0.0}";
                note = "Stimp from your two-point line (0.97 = 10.9, 0.905 = 10.1). Not used by the model yet.";
                tag = SourceTag.Alex;
            }
            var row = new AssumptionRow
            {
                Id = id, Group = CourseGroup, Label = s.Key, Kind = RowKind.Text, Value = value, DefaultValue = value,
                Source = tag, DefaultSource = tag, Note = note,
            };
            if (old.TryGetValue(id, out var o) && o.Value == value) row.ConfirmedAt = o.ConfirmedAt;
            Rows.Add(row);
        }
    }

    /// <summary>The game's Stimp reading for a greenSpeed setting, on Alex's two-point line (as Publish readiness uses it).</summary>
    public static double? Stimp(double greenSpeed) => greenSpeed < 0 ? null : 10.9 + (greenSpeed - 0.97) * (0.8 / 0.065);

    public void Confirm(string id, DateTime now) { var r = this[id]; r.ConfirmedAt ??= now; }
    public void ConfirmGroup(string group, DateTime now) { foreach (var r in Rows.Where(r => r.Group == group)) r.ConfirmedAt ??= now; }

    /// <summary>Ids of placeholder rows, for the "depends on" note on each result.</summary>
    public IReadOnlyList<AssumptionRow> Placeholders => Rows.Where(r => r.IsPlaceholder).ToList();

    /// <summary>Rows only a terrain run uses: the smoothing candidate, slope roll and the carry change for height.</summary>
    static readonly HashSet<string> TerrainOnly = ["terrain.smoothing", "ground.slope", "ground.elevationCarry"];

    /// <summary>
    /// The placeholder rows a tee-shot run actually leans on: the chosen player's rows only (a Power Drive run doesn't
    /// use the Normal player's carry), the ground and scoring rows, and the terrain rows only when the run used terrain.
    /// </summary>
    public IReadOnlyList<AssumptionRow> PlaceholdersFor(string playerId, bool terrain) => Rows.Where(r =>
        r.IsPlaceholder
        && (r.Group == "Player" ? r.Id.StartsWith($"player.{playerId}.") : r.Group is "Ground" or "Scoring")
        && (terrain || !TerrainOnly.Contains(r.Id))).ToList();

    // Typed views for the engine.

    public PlayerProfile Profile(string id) => id switch
    {
        "power" => new PlayerProfile("power", "Power Drive", this["player.power.carry"].Number, this["player.power.carrySd"].Number, this["player.power.sideSd"].Number),
        _ => new PlayerProfile("normal", "Normal", this["player.normal.carry"].Number, this["player.normal.carrySd"].Number, this["player.normal.sideSd"].Number),
    };

    /// <summary>Ground rules for a run. The green's rolling friction comes from the Stimp reading (<see cref="GreenPhysics"/>).</summary>
    public GroundTable GroundRules(double greenFriction = 0.0545)
    {
        GroundParams P(string id) { var v = this[id].Numbers; return new GroundParams(v[0], v[1], v[2]); }
        var heavy = P("ground.heavy");
        var bounce = this["ground.greenBounce"].Numbers;
        return new GroundTable(this["ground.landingSpeed"].Number, new Dictionary<Ground, GroundParams>
        {
            [HoleSim.Ground.Native] = heavy, [HoleSim.Ground.HeavyRough] = heavy, [HoleSim.Ground.Rough] = P("ground.rough"),
            [HoleSim.Ground.Fairway] = P("ground.fairway"), [HoleSim.Ground.Green] = new GroundParams(greenFriction, bounce[0], bounce[1]),
        });
    }

    public PuttingSettings Putting() => new()
    {
        CaptureSpeed = this["greens.capture"].Number, PastM = this["greens.past"].Number, StaticFactor = this["greens.static"].Number,
    };

    public ShotRules ShotRules()
    {
        var r = this["approach.release"].Numbers;
        return new ShotRules { ReleasePct = r[0], ReleaseMin = r[1], ReleaseMax = r[2], ChipShare = this["approach.chipShare"].Number, YardsPerFootRise = this["approach.heightCarry"].Number };
    }

    /// <summary>Where the golfer fit starts from, with the values the anchors can't fit.</summary>
    public GolferParams GolferStart()
    {
        var d = this["approach.drive"].Numbers; var g = new GolferParams();
        return g with { SidePct = g.DistPct * this["approach.sideShare"].Number, HeavyMult = this["approach.heavyMult"].Number, TourDriveCarrySd = d[0], TourDriveSideSd = d[1] };
    }

    public LieMap Lies() => new(
        Enum.Parse<Lie>(this["lie.native"].Value, true), Enum.Parse<Lie>(this["lie.heavyRough"].Value, true),
        Enum.Parse<Lie>(this["lie.scrub"].Value, true), (int)Math.Round(this["lie.waterPenalty"].Number));

    public string BaselineId => this["scoring.baseline"].Value;
}
