# PGA 2K25 Course Designer

A custom Windows desktop application for PGA TOUR 2K25 course design workflows.

> Status: **Greens books** (one-page PNG of all greens with slope colour, per-green studies, PDF; terrain calibrated against in-game spot elevations) and **scorecard maps** (vintage and modern themes) are working. **Yardage book pages** are next.

## Documentation

- [Course file format spec](docs/course-format/pga2k25-course-format.md): how `.course` files are structured and how to read and write them safely.
- [Reference app analysis](docs/reference-analysis/course-finisher-analysis.md): architecture, UI, features and algorithms of *Mal's TGC 2K25 Course Finisher*, plus a blueprint for our app.
- [Greens book](docs/greens-book.md): what it produces, how the terrain is reconstructed, current limits, and calibration.
- [Scorecard maps](docs/scorecard-maps.md): themes, layout, how features are traced, and known limits.

## Solution layout

| Project | Purpose |
|---|---|
| `src/Pga2K25.CourseFormat` | Lossless `.course` codec (`CourseFile`), typed read model (`CourseModel`: holes, pins, splines, surface brushes, height stamps), shared spline geometry |
| `src/Pga2K25.GreensBook` | Terrain replay, green analysis, calibration, SkiaSharp greens-book renderer |
| `src/Pga2K25.Maps` | Course feature layers, auto-rotating projection, themed scorecard-map renderer |
| `src/Pga2K25.Cli` | `pga2k25 info` / `greensbook` / `scorecard` / `calibrate` command line |
| `src/Pga2K25.App` | WPF desktop app (Greens Book and Scorecard Map tabs) |
| `tests/Pga2K25.Tests` | xUnit: codec round-trip, metadata derivations, contouring, calibration regression, map projection, output builds |
| `tools/course_codec.py` | Python CLI to inspect / unpack / pack `.course` files |
| `samples/courses/` | Sample `.course` files used as test fixtures (incl. Cassimbo) |
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
dotnet run --project src/Pga2K25.App
```

## Next steps

1. Yardage book pages for individual holes (reusing the map layers and the calibrated terrain).
2. Add calibration captures from more courses and holes (`pga2k25 calibrate`).
3. Reproduce game-generated green outlines and levelling.
