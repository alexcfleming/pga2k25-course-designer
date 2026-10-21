using System.Text.Json;
using Pga2K25.Readiness;
using Severity = Pga2K25.Readiness.Severity;

namespace Pga2K25.Tests;

/// <summary>Saved progress, the gate, the HTML checklist import and the final record (Cassimbo, 360-yd drive).</summary>
public class ReadinessStateTests
{
    static ReadinessReport Report => ReadinessTests.Report.Value;
    static string CassimboPath => Path.Combine(TestPaths.SamplesDir, "cassimbo.course");

    static string TempDir() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"readiness-{Guid.NewGuid():N}")).FullName;

    static ChecklistItem Item(string key) =>
        Checklist.Stages.SelectMany(s => s.Items).Single(i => i.Key == key);

    [Fact]
    public void State_SavesAndLoadsEverything()
    {
        string dir = TempDir();
        string path = ReadinessStore.PathFor(dir);
        var s = new ReadinessState { Limits = new ReadinessLimits { TestDriveYards = 345, CorridorYards = 30 } };
        s.Set("pre.bear", ItemStatus.Done, "locked 12 Oct");
        s.Set("hc.4.sightTee", ItemStatus.NotApplicable);
        s.Deliberate["green-scale@2"] = new DeliberateMark { Reason = "Short par 3, the green is the hole" };
        s.Pins["3.2"] = new PinState { Difficulty = "R" };
        s.Profile(1).Winds.Add("NE");
        s.Custom.Add(new CustomItem { Stage = "build", Key = "b.xfence", Text = "Fence by 14 looks right" });
        s.SignOff.Name = "Alex";
        ReadinessStore.Save(path, s);

        var back = ReadinessStore.Load(path);
        Assert.Equal(345, back.Limits.TestDriveYards);
        Assert.Equal(30, back.Limits.CorridorYards);
        Assert.Equal(220, back.Limits.Par5MinLeftYards);
        Assert.Equal(ItemStatus.Done, back.StatusOf("pre.bear"));
        Assert.Equal("locked 12 Oct", back.Items["pre.bear"].Note);
        Assert.Equal(ItemStatus.NotApplicable, back.StatusOf("hc.4.sightTee"));
        Assert.Equal("Short par 3, the green is the hole", back.Deliberate["green-scale@2"].Reason);
        Assert.Equal("R", back.Pins["3.2"].Difficulty);
        Assert.Equal(["NE"], back.Playtest[1].Winds);
        Assert.Equal("b.xfence", Assert.Single(back.Custom).Key);
        Assert.Contains("\"done\"", File.ReadAllText(path).ToLowerInvariant());   // statuses saved as words
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void ItemsTheFileAnswers_CountAsDone_UntilTheDesignerSaysOtherwise()
    {
        var s = new ReadinessState();
        Assert.Equal(ItemStatus.Done, ReadinessEvaluator.Effective(Item("pre.score"), Report, s, out bool fromFile));
        Assert.True(fromFile);
        Assert.Equal(ItemStatus.Open, ReadinessEvaluator.Effective(Item("tcl.scale"), Report, s, out _));   // greens and widths flagged
        Assert.Equal(ItemStatus.Open, ReadinessEvaluator.Effective(Item("pre.drive"), Report, s, out _));   // no drive in these limits

        s.Set("pre.score", ItemStatus.NotApplicable);
        Assert.Equal(ItemStatus.NotApplicable, ReadinessEvaluator.Effective(Item("pre.score"), Report, s, out fromFile));
        Assert.False(fromFile);
    }

    [Fact]
    public void DeliberateMarks_ClearAFlag_WholeOrHoleByHole()
    {
        var s = new ReadinessState();
        var scale = Report.Check("green-scale")!;
        Assert.False(ReadinessEvaluator.IsCleared(scale, s));
        foreach (int h in scale.Holes.SkipLast(1)) s.Deliberate[$"green-scale@{h}"] = new DeliberateMark();
        Assert.False(ReadinessEvaluator.IsCleared(scale, s));
        s.Deliberate[$"green-scale@{scale.Holes[^1]}"] = new DeliberateMark();
        Assert.True(ReadinessEvaluator.IsCleared(scale, s));

        s.Deliberate["landing-width"] = new DeliberateMark { Reason = "Width is the strategy here" };
        Assert.Equal(ItemStatus.Done, ReadinessEvaluator.Effective(Item("tcl.scale"), Report, s, out bool fromFile));
        Assert.True(fromFile);
    }

    [Fact]
    public void Gate_NeedsEverythingClosed_AndASignOffOnThisVersion()
    {
        var s = new ReadinessState();
        var why = ReadinessEvaluator.GateBlockers(Report, s, "AAAA");
        Assert.Contains(why, w => w.Contains("course-file check"));
        Assert.Contains("sign-off missing", why);

        // Close everything.
        foreach (var c in Report.Checks.Where(c => !c.Passed)) s.Deliberate[c.Id] = new DeliberateMark();
        foreach (var stage in Checklist.Stages) foreach (var i in stage.Items) s.Set(i.Key, ItemStatus.Done);
        foreach (var hs in Checklist.HoleStages) foreach (var h in Report.Holes) foreach (var c in hs.Columns) s.Set(hs.Key(h.Number, c.Id), ItemStatus.Done);
        foreach (var h in Report.Holes) for (int p = 1; p <= 4; p++) s.Pins[$"{h.Number}.{p}"] = new PinState { Difficulty = "G" };
        for (int i = 0; i < 2; i++)
        {
            s.Profile(i).Winds.AddRange(Checklist.WindDirections);
            s.Profile(i).Tees.AddRange(Enumerable.Range(1, Report.TeeSets.Count));
            s.Profile(i).Pins.AddRange([1, 2, 3, 4]);
        }
        Assert.Equal(["sign-off missing"], ReadinessEvaluator.GateBlockers(Report, s, "AAAA"));
        Assert.All(ReadinessEvaluator.Progress(Report, s), p => Assert.True(p.Complete, p.Title));

        s.SignOff.Name = "Alex";
        s.SignOff.Date = "2026-10-21";
        s.SignOff.CourseStamp = "AAAA";
        Assert.Empty(ReadinessEvaluator.GateBlockers(Report, s, "AAAA"));
        Assert.Equal(["the course file has changed since the sign-off"], ReadinessEvaluator.GateBlockers(Report, s, "BBBB"));
    }

    [Fact]
    public void Stamp_IsTheFilesContent()
    {
        string a = ReadinessStore.Stamp(CassimboPath);
        Assert.Equal(16, a.Length);
        Assert.Equal(a, ReadinessStore.Stamp(CassimboPath));
        Assert.NotEqual(a, ReadinessStore.Stamp(Path.Combine(TestPaths.SamplesDir, "berwick-coast.course")));
    }

    /// <summary>A backup as the HTML checklist's Export backup writes it.</summary>
    const string Backup = """
        {"current":"c1","courses":{
          "c0":{"id":"c0","name":"Dead Man's Meadow","k":{"pre.bear":true},"t":{},"pins":{},"custom":{}},
          "c1":{"id":"c1","name":"cassimbo","k":{"pre.bear":true,"pre.marker":false,"b.fringe":true,"hc.3.sightTee":true,"tr.12.aim":true,
             "pl.5.g360":true,"td.2":true,"play.0.NE":true,"play.0.t2":true,"play.1.p4":true,"atmok.time":true,"b.x1":true},
           "t":{"designer":"Alex Fleming","version":"1.2","tees":"Red, Green, White, Blue","prevWind":"SW","driveYds":"360","lim5":"230",
             "h.1.par":"4","h.1.yds":"374","t.1.green":"4300","refTee":"Red","soName":"","pf0n":"Draw","pf0g":"Pro","atmTime":"15:40"},
           "pins":{"1.1":{"d":"B","l":"R","r":"A"},"1.2":{"r":"G"}},
           "custom":{"build":[{"id":"x1","t":"Fence by 14 looks right"}]}}}}
        """;

    [Fact]
    public void Import_BringsTheChecklistAcross()
    {
        var courses = ChecklistImport.ReadBackup(Backup);
        Assert.Equal(2, courses.Count);
        var c = ChecklistImport.Match(courses, "Cassimbo")!;
        Assert.Equal("c1", c.Id);

        var s = new ReadinessState();
        s.Set("b.fringe", ItemStatus.NotApplicable);     // already decided here: an import never overrides it
        var summary = ChecklistImport.Import(c, s);

        Assert.Equal(ItemStatus.Done, s.StatusOf("pre.bear"));
        Assert.Equal(ItemStatus.Open, s.StatusOf("pre.marker"));
        Assert.Equal(ItemStatus.NotApplicable, s.StatusOf("b.fringe"));
        Assert.Equal(ItemStatus.Done, s.StatusOf("hc.3.sightTee"));
        Assert.Equal(ItemStatus.Done, s.StatusOf("tr.12.aim"));
        Assert.Equal(ItemStatus.Done, s.StatusOf("pl.5.g360"));
        Assert.Equal(ItemStatus.Done, s.StatusOf("b.x1"));
        Assert.Equal("Fence by 14 looks right", Assert.Single(s.Custom).Text);
        Assert.True(s.Deliberate.ContainsKey("green-scale@2"));
        Assert.True(s.Deliberate.ContainsKey("landing-width@2"));
        Assert.Equal(["NE"], s.Playtest[0].Winds);
        Assert.Equal([2], s.Playtest[0].Tees);
        Assert.Equal([4], s.Playtest[1].Pins);
        Assert.Equal("Draw", s.Playtest[0].Style);
        Assert.Equal("A", s.Pins["1.1"].Difficulty);
        Assert.Equal("B", s.Pins["1.1"].NotedDepth);
        Assert.Equal("G", s.Pins["1.2"].Difficulty);
        Assert.Equal(360, s.Limits.TestDriveYards);
        Assert.Equal(230, s.Limits.Par5MinLeftYards);
        Assert.Equal(["Red", "Green", "White", "Blue"], s.Info.TeeNames);
        Assert.Equal("Alex Fleming", s.Info.Designer);
        Assert.Equal("15:40", s.Imported["atmTime"]);
        Assert.Equal("matched", s.Imported["atmok.time"]);
        Assert.Contains(summary.Notes, n => n.Contains("course file supplies them"));
        Assert.Contains(summary.Notes, n => n.Contains("Red"));

        // Importing twice changes nothing.
        var again = ChecklistImport.Import(c, s);
        Assert.Equal(0, again.Ticks + again.Deliberate + again.Pins + again.Custom + again.Fields);
    }

    [Fact]
    public void Import_RejectsAFileThatIsNotABackup()
    {
        Assert.Throws<InvalidDataException>(() => ChecklistImport.ReadBackup("""{"name":"not a backup"}"""));
    }

    [Fact]
    public void Record_WritesADatedJsonAndPrintablePage()
    {
        string dir = TempDir();
        var s = new ReadinessState();
        s.Set("pre.bear", ItemStatus.Done, "Locked after the routing review");
        s.Deliberate["green-scale@2"] = new DeliberateMark { Reason = "Short par 3, the green is the hole" };
        s.SignOff.Notes = "Known: hole 7 climbs 52 ft";
        var files = ReadinessRecord.Write(dir, CassimboPath, Report, s, new DateTime(2026, 10, 21, 16, 30, 0));

        Assert.EndsWith("Cassimbo - Readiness record 2026-10-21.json", files.Json);
        string html = File.ReadAllText(files.Html);
        Assert.Contains("Cassimbo: publish readiness record", html);
        Assert.Contains("Not ready", html);
        Assert.Contains("Locked after the routing review", html);
        Assert.Contains("Hole 2: Short par 3, the green is the hole", html);
        Assert.Contains("Known: hole 7 climbs 52 ft", html);
        Assert.Contains("Cumulus_02", html);

        using var doc = JsonDocument.Parse(File.ReadAllText(files.Json));
        Assert.Equal("Cassimbo", doc.RootElement.GetProperty("course").GetString());
        Assert.False(doc.RootElement.GetProperty("ready").GetBoolean());
        Assert.Contains(doc.RootElement.GetProperty("report").GetProperty("checks").EnumerateArray(),
            c => c.GetProperty("severity").GetString() == "High");
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Checklist_KeysMatchTheHtmlChecklist()
    {
        var keys = Checklist.Stages.SelectMany(s => s.Items).Select(i => i.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Equal(11, Checklist.PreBuild.Items.Count);
        Assert.Equal(16, Checklist.Build.Items.Count);
        Assert.Equal(10, Checklist.TourCourse.Items.Count);
        Assert.Equal(12, Checklist.Publish.Items.Count);
        Assert.Equal(13, Checklist.HoleChecks.Columns.Count);
        Assert.Equal(8, Checklist.TourHoles.Columns.Count);
        Assert.Equal(7, Checklist.Planting.Columns.Count);
        // Every check an item points at exists (or is one of the evaluator's own).
        var own = new[] { Checklist.AllChecks, "drive-entered", "playtest-winds", "playtest-tees", "drive" };
        var ids = Report.Checks.Select(c => c.Id).Concat(own).Concat(["drivable-front", "drivable-back", "par5"]).ToHashSet();
        Assert.All(Checklist.Stages.SelectMany(s => s.Items).SelectMany(i => i.Checks), id => Assert.Contains(id, ids));
        Assert.Equal(Severity.High, Report.Checks.Max(c => c.Severity));
    }
}
