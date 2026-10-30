using Pga2K25.CourseCard;
using Pga2K25.CourseFormat;

namespace Pga2K25.Tests;

public class CoursePlaceTests
{
    static string TempDir() => Path.Combine(Path.GetTempPath(), "pga2k25-place-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Place_RoundTripsAndSkipsBlankLines()
    {
        string dir = TempDir();
        try
        {
            Assert.Empty(CoursePlaces.Load(dir).PlaceLines);   // no file: an empty place
            CoursePlaces.Save(dir, new CoursePlace { Lines = ["Olkhon Island", "  ", " Lake Baikal "], SeaSide = "WEST", WindFromDegrees = 315 });
            var place = CoursePlaces.Load(dir);
            Assert.Equal(["Olkhon Island", "Lake Baikal"], place.PlaceLines);
            Assert.Equal("Olkhon Island, Lake Baikal", place.Line);
            Assert.Equal("WEST", place.SeaSide);
            Assert.Equal(315, place.WindFromDegrees);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Place_UnreadableFileIsAnEmptyPlace()
    {
        string dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(CoursePlaces.PathFor(dir), "{ not json");
            Assert.Empty(CoursePlaces.Load(dir).PlaceLines);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CourseCards_TakeSeaSideFromThePlace_UnlessTheCardSettingsSetIt()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(TestPaths.SamplesDir, "sagan-khushun.course");
            CoursePlaces.Save(dir, new CoursePlace { SeaSide = "EAST", WindFromDegrees = 270 });
            var fromPlace = CourseCardBuilder.Build(path, dir, new CourseCardConfig { Formats = ["png"] }, force: true);
            Assert.Equal("EAST", fromPlace.OceanSide);
            Assert.DoesNotContain(fromPlace.Issues, i => i.Message.Contains("No prevailing wind"));

            var fromCard = CourseCardBuilder.Build(path, dir, new CourseCardConfig { Formats = ["png"], OceanSide = "SOUTH" }, force: true);
            Assert.Equal("SOUTH", fromCard.OceanSide);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
