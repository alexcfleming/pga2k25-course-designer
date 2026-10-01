# Terrain map

A single high-resolution, north-up PNG of a routed but unbuilt plot, made for planning. Sketch fairways, bunkers and green complexes over it by hand or in an image editor.

```bash
dotnet run --project src/Pga2K25.Cli -- terrain samples/courses/sagan-khushun.course -o out --credit "NAME"
```

The output is `<course> - Terrain Map.png`. At the default 4 px per metre, a 1.5 km routing gives roughly 7,000 × 6,500 px, which is about 28 × 26 in at 250 dpi. The Windows app has the same tool on its **Terrain Map** tab.

## What's on the sheet

- **Contours.** The interval is the smallest of 1 / 2 / 5 / 10 / 20 … ft that keeps the ground within 80 m of play to at most 60 levels. Every 5th line is a heavier **index contour**, labelled in line (the line breaks around the number). On steep ground, minor contours are dropped where they would run closer than 7 px; index contours are always drawn.
- **Shading.** `relief` (default) is a pale elevation tint with a soft north-west hillshade. `slope` shows planning classes: 0–3 % green sites, 3–8 % fairway, 8–15 % steep for play, 15–25 % needs grading, over 25 % severe. `none` is plain paper.
- **Sea.** If the plot has ocean props (`OceanChurn…` surf and wave prefabs), sea level is their placed height: 0 m on stock plots, with the surface a hair above the plot's untouched flat base. Ground at or below sea level (2 cm tolerance for blur) is filled blue, edged with a shoreline, and not contoured. Elevations are then measured **above sea level**. Inland hollows that reach 0 m fill too, as they would under a flat ocean. The log reports how many surf props lie offshore and how far they sit from the modelled shoreline, as a check on the terrain. On Sagan-Khushun the median is about 30 m. `--sea-level <m>` overrides the detected level; `--no-water` turns the sea off.
- **Spot heights** on hilltops (the highest point within 60 m that stands proud of its surroundings).
- **Lettered reference grid** of 100-yd squares, with columns A, B, C… and rows 1, 2, 3…, so notes can say "bunker at F7".
- **Routing as sticks and dots.** Each hole shows:
  - its back tee (●), route line and aim points (○), each aim point labelled with yards *from tee · to green*
  - a tick every 50 yd from the back tee, numbered every 100
  - a 15-yd green-site circle with a flag, and an arrow showing which way the site falls
  - a numbered badge with par, scorecard yards, and the rise or fall from tee to green
- **Drawn surfaces.** Any fairways, greens, bunkers, water and paths already in the file are shown faintly. Waste, rock and vegetation are left off.
- **Side panel**:
  - totals, plus the sheet's relief and the elevation range in play
  - a hole table: par, yards, tee and green elevation, change, and mean green-site slope (red above 5 %)
  - legend, scale bar, north arrow and notes

Elevations are in **feet above sea level** when the plot has a sea, otherwise above the lowest point on the sheet. Yardages follow the game's rule: back tee → second waypoint → … → green, the same as the scorecard; the course file is the source of truth.

## Terrain model

This uses the same stamp replay as the greens book (`TerrainReplay`), with one change. Brushes **40 m or more across** use a softer falloff (`LargeBrushHardness`, default 0.3) than the calibrated 0.85.

The reason: the greens-book calibration only measures heights on the flat tops of stamps, where greens sit. It says nothing about how big sculpting brushes fall off. At plot scale, 0.85 turns every large brush into a circular terrace with a cliff edge. Neither the game's thumbnails nor any real terrain shows that. With 0.3, the plot reads as natural rolling ground, and on Sagan-Khushun the average green-site slope roughly halves.

The greens book and yardage book keep their calibrated settings, because `LargeBrushHardness` is null there.

This softer falloff is a judgement, not a calibration. Before committing to a design, check critical grades in the game: green sites, landing areas and forced carries. If you capture in-game spot heights away from greens, the large-brush falloff can be calibrated the same way the greens were.

## Options

| CLI | App | Default | |
|---|---|---|---|
| `--full-plot` | Area | routing + 150 m | Map every terrain stamp instead (the whole plot) |
| `--margin` | | 150 | Metres around the routing |
| `--interval` | Contour (ft) | auto | Contour interval in feet |
| `--index` | | 5 | Every n-th contour is an index contour |
| `--shading` | Shading | relief | `relief`, `slope` or `none` |
| `--ppm` | px / m | 4 | Output pixels per metre; text and line weights scale with it |
| `--cell` | | 1.0 | Terrain raster (m) |
| `--grid` | | 100 | Grid square (yd); 0 hides the grid |
| `--hardness` / `--large-hardness` | | 0.85 / 0.3 | Brush falloff for small / large brushes |
| `--smooth` | | 0 | Extra Gaussian smoothing (sigma, m) before contouring |
| `--no-features`, `--no-summits` | Drawn surfaces | shown | |
| `--sea-level`, `--no-water` | | from ocean props | Sea level (world m), or no sea |

## Code

`src/Pga2K25.TerrainMap`:

- `TerrainSurvey`: the window, replay, datum, interval choice, contours (via `GreensBook.MarchingSquares`, run in parallel per level), summits and per-hole figures
- `TerrainMapRenderer`: SkiaSharp drawing. Shading is rasterised at the terrain resolution and drawn with cubic resampling. Contour labels are placed first so the lines can be clipped around them, and route labels try 16 positions to stay clear of routes and other labels.
- `TerrainMapBuilder`: writes the PNG

Tests are in `tests/Pga2K25.Tests/TerrainMapTests.cs` and run against `samples/courses/sagan-khushun.course`.
