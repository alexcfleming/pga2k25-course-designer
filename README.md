# PGA 2K25 Course Designer

A custom Windows desktop application that analyses PGA TOUR 2K25 Course Designer `.course` files and produces print-style companion material. It only reads course files; it never edits or writes them.

> Status: **Greens books** (one-page PNG of all greens with slope colour, per-green studies, PDF; terrain calibrated against in-game spot elevations) **scorecard maps** (vintage and modern themes) **yardage books** (an 18-page portrait PDF workbook, optional PNG per hole) and **terrain maps** (one large contour sheet of a routed plot, plus a Letter-size sketch sheet per hole) and **course cards** (compass rose, hole data card, course data card and green size card) are working.

## Documentation

- [Course file format spec](docs/course-format/pga2k25-course-format.md): how `.course` files are structured and how to read them.
- [Reference app analysis](docs/reference-analysis/course-finisher-analysis.md): architecture, UI, features and algorithms of *Mal's TGC 2K25 Course Finisher*, plus a blueprint for our app.
- [Greens book](docs/greens-book.md): what it produces, how the terrain is reconstructed, current limits, and calibration.
- [Scorecard maps](docs/scorecard-maps.md): themes, layout, how features are traced, and known limits.
- [Yardage book](docs/yardage-book.md): page contents, hazard numbering, rock footprint estimation, and PDF output.
- [Terrain map](docs/terrain-map.md): contour planning sheet of the plot with the routing as sticks and dots; terrain model notes and options.
- [Course Designer Studio](docs/studio.md): the desktop app, with a course library, a live terrain map, measurements, product viewers and builds.
- [Course cards](docs/course-cards.md): compass rose, hole data card, course data card and green size card from the course file; validation rules, settings, the 180° blind spot.

## Solution layout

| Project | Purpose |
|---|---|
| `src/Pga2K25.CourseFormat` | Read-only `.course` decoder (`CourseFile`), typed read model (`CourseModel`: holes, pins, splines, surface brushes, height stamps), shared spline geometry |
| `src/Pga2K25.GreensBook` | Terrain replay, green analysis, calibration, SkiaSharp greens-book renderer |
| `src/Pga2K25.Maps` | Course feature layers, auto-rotating projection, themed scorecard-map renderer |
| `src/Pga2K25.YardageBook` | Per-hole analysis (hazard numbers, widths, spot heights, rock footprints) and the yardage page / PDF workbook renderer |
| `src/Pga2K25.TerrainMap` | Plot-scale terrain survey (contours, summits, per-hole elevations and green-site slope) and the terrain planning map renderer |
| `src/Pga2K25.CourseCard` | Course cards: data from the course file (bearings, scorecard yards), validation, rose / table / composite renderer (bundled Lora font) |
| `src/Pga2K25.Cli` | `pga2k25 info` / `greensbook` / `scorecard` / `yardage` / `terrain` / `cards` / `calibrate` command line |
| `src/Pga2K25.Studio` | **Course Designer Studio**, the desktop app (Avalonia; see [docs/studio.md](docs/studio.md)): a course library read from the game's Courses folder, app settings, and a per-course workspace. The workspace has a section menu (terrain map, measurements, course settings) and recent courses, a live terrain map, a hole inspector with in-game measurements against the model, a measurements page to add and remove readings, in-app viewers for every built product (page thumbnails, zoom and pan, open the PDF), and one-click product builds |
| `tests/Pga2K25.Tests` | xUnit: course decoding, metadata derivations, contouring, calibration regression, map projection, output builds |
| `tools/course_codec.py` | Python CLI to inspect / unpack `.course` files (read only) |
| `samples/courses/` | Sample `.course` files used as test fixtures (incl. Cassimbo, and the unbuilt routed plot Sagan-Khushun) |
| `samples/calibration/` | In-game green screenshots + spot-label captures used to calibrate the terrain model |

Requires the .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`).

```bash
dotnet test
```

```bash
dotnet run --project src/Pga2K25.Cli -- greensbook samples/courses/shinnecockhills.course -o out --studies
```

```bash
dotnet run --project src/Pga2K25.Cli -- scorecard samples/courses/cassimbo.course -o out
```

```bash
dotnet run --project src/Pga2K25.Cli -- yardage samples/courses/cassimbo.course -o out --data samples/data/cassimbo-hole-data.xlsx --png
```

```bash
dotnet run --project src/Pga2K25.Cli -- terrain samples/courses/sagan-khushun.course -o out
```

```bash
dotnet run --project src/Pga2K25.Cli -- cards samples/courses/sagan-khushun.course -o out --config samples/data/sagan-khushun-card.json
```

```bash
dotnet run --project src/Pga2K25.Studio
```

Studio reads every `.course` in `Documents\My Games\PGA TOUR 2K25\Courses`, plus any you add, and writes products to `Documents\PGA 2K25 Greens Books\<course>\`. Settings live in `%AppData%\CourseDesignerStudio\settings.json`. In-game route measurements go in `Measurements.csv` beside a course's products (same format as `samples/calibration/route-heights`). The workspace then shows game against model for each hole and an overall model check. Pass a `.course` path on the command line to open straight into that course.

Studio uses Avalonia 12 on .NET 8. Avalonia's XAML source generator needs a newer compiler than the .NET 8 SDK has, so the views load their XAML with `AvaloniaXamlLoader` and look up named controls themselves.

## Next steps

1. Add calibration captures from more courses and holes (`pga2k25 calibrate`).
2. Check yardage-book hazard numbers against in-game rangefinder readings on a second course.
3. Reproduce game-generated green outlines and levelling.

## Archived: Course Finisher

The C# port of *Mal's TGC 2K25 Course Finisher* (v0.10 REV10E) edited course files, so it was taken out of this analysis-only app on 2026-10-03. The full code is kept, with its tests, docs (`docs/course-finisher.md`) and packaging script. It is on the `archive/course-finisher` branch and tagged `course-finisher-archive`:

```bash
git checkout course-finisher-archive
```

