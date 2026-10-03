using Pga2K25.Studio.Services;

namespace Pga2K25.Tests;

public class StudioTests
{
    [Theory]
    [InlineData("+13'6\"", 13.5)]
    [InlineData("-0'7", -0.5833)]
    [InlineData("13 ft 6 in", 13.5)]
    [InlineData("-5.33", -5.33)]
    [InlineData("−2 ft 4 in", -2.3333)]
    [InlineData("9", 9)]
    public void TryParseFeet_ReadsTheMeasureToolAndTypedForms(string text, double feet)
    {
        Assert.True(Measurements.TryParseFeet(text, out double v));
        Assert.Equal(feet, v, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("3'14\"")]
    public void TryParseFeet_RejectsNonsense(string text) => Assert.False(Measurements.TryParseFeet(text, out _));

    [Fact]
    public void Measurements_SaveThenLoad_KeepsEveryReading()
    {
        string path = Path.Combine(Path.GetTempPath(), $"studio-measure-{Guid.NewGuid():N}", "Measurements.csv");
        var legs = new List<MeasuredLeg> { new(3, false, 235, -4.08), new(1, true, 302, 3), new(3, false, 235, -3.5) };
        Measurements.Save(path, legs);
        var back = Measurements.Load(path);
        Assert.Equal(3, back.Count);
        Assert.Equal(new MeasuredLeg(1, true, 302, 3), back[0]);
        Assert.Contains(back, m => m.Hole == 3 && m.Feet == -4.08);
        // The same format as the repository's calibration files.
        var sample = Measurements.Load(Path.Combine(TestPaths.SamplesDir, "..", "calibration", "route-heights", "berwick-coast.csv"));
        Assert.Equal(36, sample.Count);
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    [Fact]
    public void CourseLibrary_ReadsACourseWithoutChangingIt()
    {
        string path = Path.Combine(TestPaths.SamplesDir, "berwick-coast.course");
        var before = File.GetLastWriteTimeUtc(path);
        var entry = CourseLibrary.Read(path);
        Assert.Null(entry.Error);
        Assert.Equal("Berwick Coast", entry.Name);
        Assert.Equal(18, entry.Holes);
        Assert.Equal(72, entry.Par);
        Assert.Equal(7035, entry.Yards);
        Assert.NotNull(entry.Thumbnail);
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Settings_RecentKeepsTheNewestSix()
    {
        var s = new StudioSettings();
        for (int i = 0; i < 8; i++) s.Touch($"c{i}.course");
        s.Touch("c3.course");
        Assert.Equal(StudioSettings.MaxRecent, s.Recent.Count);
        Assert.Equal("c3.course", s.Recent[0]);
        Assert.Equal("c7.course", s.Recent[1]);
    }
}
