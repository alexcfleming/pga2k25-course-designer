using Pga2K25.CourseCard;
using Pga2K25.CourseFormat;

namespace Pga2K25.Tests;

public class HoleNotesTests
{
    [Fact]
    public void HoleNotes_DescribeEveryGreen_AndTheWorkbookWins()
    {
        var course = CourseFile.Load(Path.Combine(TestPaths.SamplesDir, "sagan-khushun.course")).ReadModel();
        var notes = HoleNotes.For(course);
        Assert.Equal(course.Holes.Count, notes.Count);
        foreach (var n in notes)
        {
            Assert.Matches(@"^(Round|Oval|Kidney|Long, narrow|Wide, shallow) \d+×\d+ yd", n.GreenNotes);
            if (course.Holes.First(h => h.Number == n.Hole).Pins.Count > 0) Assert.Contains("pin", n.GreenNotes);
            Assert.All(n.Hazards, h => Assert.DoesNotContain("…", h));
        }

        // Course-file notes fill blanks only: a hole written by hand keeps its own text.
        var manual = new HoleNote(1, ["Hand-written hazard"], "Hand-written green");
        var data = CourseCardData.From(course, null, null, [], [manual, .. notes.Skip(1)]);
        Assert.Equal("Hand-written green", data.Holes[0].GreenNotes);
        Assert.Equal(notes[1].GreenNotes, data.Holes[1].GreenNotes);
    }
}
