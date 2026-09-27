# PGA 2K25 Course Designer

A custom Windows desktop application for PGA TOUR 2K25 course design workflows.

> Status: **.NET solution scaffolded. First feature working: a greens book (one-page PNG of all greens with slope colour, plus optional per-green studies and PDF), with terrain calibrated against in-game spot elevations.**

## Documentation

- [Course file format spec](docs/course-format/pga2k25-course-format.md): how `.course` files are structured and how to read and write them safely.
- [Reference app analysis](docs/reference-analysis/course-finisher-analysis.md): architecture, UI, features and algorithms of *Mal's TGC 2K25 Course Finisher*, plus a blueprint for our app.
- [Greens book](docs/greens-book.md): what it produces, how the terrain is reconstructed, current limits, and the calibration plan.

## Solution layout

| Project | Purpose |
|---|---|
| `src/Pga2K25.CourseFormat` | Lossless `.course` codec (`CourseFile`) + typed read model (`CourseModel`: holes, pins, splines, height stamps) |
| `src/Pga2K25.GreensBook` | Geometry, terrain replay, green analysis, SkiaSharp PDF/PNG renderer |
| `src/Pga2K25.Cli` | `pga2k25 info` / `pga2k25 greensbook` command line |
| `src/Pga2K25.App` | WPF desktop app (greens book front-end) |
| `tests/Pga2K25.Tests` | xUnit: codec round-trip, metadata derivations, contouring, PDF build |
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
dotnet run --project src/Pga2K25.App
```

## Next steps

1. Add calibration captures from more courses and holes (`pga2k25 calibrate`, see [docs/greens-book.md](docs/greens-book.md#calibration)).
2. Reproduce game-generated green outlines and levelling.
3. Add a 2D course map view to the app.
