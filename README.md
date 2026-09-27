# PGA 2K25 Course Designer

A custom Windows desktop application for PGA TOUR 2K25 course design workflows.

> Status: **analysis complete, stack not yet scaffolded.** See the docs below.

## Documentation

- [Course file format spec](docs/course-format/pga2k25-course-format.md): how `.course` files are structured and how to read and write them safely.
- [Reference app analysis](docs/reference-analysis/course-finisher-analysis.md): architecture, UI, features and algorithms of *Mal's TGC 2K25 Course Finisher*, plus a blueprint for our app.

## Repository layout

| Path | Purpose |
|------|---------|
| `src/` | Application source (planned: C# / .NET 8, WPF + MVVM) |
| `docs/` | Design and architecture documentation |
| `tools/course_codec.py` | Python CLI to `info` / `unpack` / `pack` `.course` files (lossless round-trip) |
| `samples/courses/` | Sample `.course` files used as test fixtures |
| `reference/` | Local copy of the reference application (git-ignored, not redistributed) |

## Quick start: inspect a course

```bash
python tools/course_codec.py info samples/courses/shinnecockhills.course
```

```bash
python tools/course_codec.py unpack samples/courses/shinnecockhills.course out/shinnecock
```

## Next steps

1. Scaffold the .NET solution (`CourseFormat`, `Geometry`, `Terrain`, `Operations`, `App`, tests).
2. Port the codec to C# with a byte-stable round-trip test over `samples/courses/`.
3. Build the heightfield replay and a 2D map view.
