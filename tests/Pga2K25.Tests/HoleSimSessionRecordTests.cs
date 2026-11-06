using Pga2K25.HoleSim;

namespace Pga2K25.Tests;

/// <summary>Stage 4e: the session record, its findings and the in-game verdicts.</summary>
[Trait("Area", "HoleSim")]
public class HoleSimSessionRecordTests
{
    static FileStamp Stamp(string hash) => new("x.course", new DateTime(2026, 11, 6, 13, 0, 0, DateTimeKind.Utc), 100, hash);

    [Fact]
    public void Records_live_beside_the_live_checks_one_per_hole_and_day()
    {
        var path = SessionRecord.PathFor(@"C:\products", 8, new DateTime(2026, 11, 6));
        Assert.Equal(Path.Combine(@"C:\products", "Hole Sim", "H08 session 2026-11-06.json"), path);
        Assert.Equal(Path.GetDirectoryName(LiveChecks.PathFor(@"C:\products", 8)), Path.GetDirectoryName(path));
    }

    [Fact]
    public void A_finding_keeps_its_verdict_when_run_again_and_goes_stale_on_a_new_save()
    {
        var r = new SessionRecord { Hole = 8 };
        var t = new DateTime(2026, 11, 6, 9, 0, 0);
        r.Upsert(t, "summary.pins.power", "Summary", "Pin 4 plays 0.47 harder than pin 1.", "Play both.", Stamp("aaaaaaaaaa11"));
        r.SetVerdict("summary.pins.power", Verdicts.Confirmed, "Bogeyed pin 4 twice.", t.AddMinutes(5));
        // The same result again on the same save: one finding, the verdict kept, the text updated.
        r.Upsert(t.AddMinutes(10), "summary.pins.power", "Summary", "Pin 4 plays 0.45 harder than pin 1.", "Play both.", Stamp("aaaaaaaaaa11"));
        var f = Assert.Single(r.Findings);
        Assert.Equal(Verdicts.Confirmed, f.Verdict);
        Assert.Equal("Bogeyed pin 4 twice.", f.Note);
        Assert.Contains("0.45", f.Text);
        Assert.False(f.VerdictStale);
        // A new save: the verdict stays but is marked for checking again, until a new verdict is given.
        r.Upsert(t.AddMinutes(20), "summary.pins.power", "Summary", "Pin 4 plays 0.30 harder than pin 1.", "Play both.", Stamp("bbbbbbbbbb22"));
        Assert.True(r.Findings[0].VerdictStale);
        r.SetVerdict("summary.pins.power", Verdicts.NotSeen, "", t.AddMinutes(30));
        Assert.False(r.Findings[0].VerdictStale);
    }

    [Fact]
    public void Saves_json_and_markdown_and_reads_them_back()
    {
        var dir = Path.Combine(Path.GetTempPath(), "holesim-record-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = SessionRecord.PathFor(dir, 3, new DateTime(2026, 11, 6));
            var r = SessionRecord.LoadOrNew(path, "Pilolcura Analysis", 3, 3, new DateTime(2026, 11, 6, 9, 0, 0));
            Assert.Empty(r.Entries);
            r.Baseline = "pga-tour-anchors";
            r.Log(new DateTime(2026, 11, 6, 9, 5, 0), "Tee shot map", "pin 4", ["From T4: best aim 8 yds right and 8 yds long of the pin, 3.83."], "Green contours unverified", Stamp("cccccccccc33"));
            r.Upsert(new DateTime(2026, 11, 6, 9, 5, 0), "tee.aim.4.T4", "Tee shot map", "Pin 4 from T4: aiming 8 yds right and 8 yds long saves 0.18.", "Hit a few each way.", Stamp("cccccccccc33"));
            r.SetVerdict("tee.aim.4.T4", Verdicts.Contradicted, "Long misses ran off the back.", new DateTime(2026, 11, 6, 9, 30, 0));
            r.Save(path);
            Assert.True(File.Exists(path));
            var md = File.ReadAllText(Path.ChangeExtension(path, ".md"));
            Assert.Contains("hole 3 (par 3)", md);
            Assert.Contains("Pin 4 from T4", md);
            Assert.Contains("Contradicted", md);
            Assert.Contains("Long misses ran off the back.", md);
            Assert.Contains("sha256 cccccccccc", md);
            // Opening the hole again the same day carries on the same record.
            var back = SessionRecord.LoadOrNew(path, "Pilolcura Analysis", 3, 3, new DateTime(2026, 11, 6, 14, 0, 0));
            Assert.Single(back.Entries);
            Assert.Equal(Verdicts.Contradicted, Assert.Single(back.Findings).Verdict);
            Assert.Equal(new DateTime(2026, 11, 6, 9, 0, 0), back.Started);
            // A record for another hole in the same file isn't taken over.
            var other = SessionRecord.LoadOrNew(path, "Pilolcura Analysis", 4, 4, new DateTime(2026, 11, 6, 14, 0, 0));
            Assert.Empty(other.Entries);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
