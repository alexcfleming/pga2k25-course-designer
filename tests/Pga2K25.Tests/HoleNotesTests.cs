using Pga2K25.CourseCard;
using Pga2K25.CourseFormat;

namespace Pga2K25.Tests;

public class HoleNotesTests
{
    [Fact]
    public void HoleNotes_DescribeEveryGreen_AndHandWrittenNotesWin()
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

        // Course-file notes fill blanks only: a hole written by hand on the Holes card keeps its own text.
        var entries = new CourseHoles();
        entries.Entry(1).GreenNotes = "Hand-written green";
        entries.Entry(2).Name = "Named, nothing written";
        var data = CourseCardData.From(course, entries, null, [], notes);
        Assert.Equal("Hand-written green", data.Holes[0].GreenNotes);
        Assert.Equal(notes[0].Hazards, data.Holes[0].Hazards);
        Assert.Equal(notes[1].GreenNotes, data.Holes[1].GreenNotes);
    }
}
