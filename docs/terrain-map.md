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
- **Painted water.** Streams and ponds painted with the designer's water brush (`water[]`, each dab carrying its water level) are filled blue wherever the modelled ground under them is below that level, edged, and drawn over the contours. The file stores a stroke as separate dabs, sometimes more than a brush width apart, so consecutive dabs at the same level and within two brush widths are joined into one stroke. A dashed outline shows the whole painted area, so a stretch painted over higher ground (which stays dry) still shows; check those in the game. The log gives the level and the wet area. On Berwick Coast a 700 m stream at 8.5 m comes out as about 4,300 m² of water with a few dry stretches. `--no-water` turns this off too.
- **Spot heights** on hilltops (the highest point within 60 m that stands proud of its surroundings).
- **Lettered reference grid** of 100-yd squares, with columns A, B, C… and rows 1, 2, 3…, so notes can say "bunker at F7".
- **Routing as sticks and dots.** Each hole shows:
  - its back tee (●), route line and aim points (○), each aim point labelled with yards *from tee · to green*
  - a tick every 50 yd from the back tee, numbered every 100
  - a 15-yd green-site circle with a flag, and an arrow showing which way the site falls. When a green is already drawn at the end of the route, the circle sits on that green's centre.
  - a numbered badge with par, scorecard yards, and the rise or fall from tee to green
- **Drawn surfaces.** Any fairways, greens, bunkers, water and paths already in the file are shown faintly. Waste, rock and vegetation are left off.
- **Side panel**:
  - totals, plus the sheet's relief and the elevation range in play
  - a hole table: par, yards, tee and green elevation, change, and the green site's **tilt** and **±ft** (see *Green sites* below)
  - legend, scale bar, north arrow and notes

Elevations are in **feet above sea level** when the plot has a sea, otherwise above the lowest point on the sheet. Yardages follow the game's rule: back tee → second waypoint → … → green, the same as the scorecard; the course file is the source of truth.

## Blank plots (no holes yet)

A plot with no holes, such as one with only a clubhouse, range and practice area, is mapped whole. Use it to sketch routings.

- **Contour interval:** comes from the **buildable ground**. That is the gentle ground (20 % or less) connected to the clubhouse and practice facilities, found by flood fill, or the largest gentle area if there are no facilities. On a mesa such as Falcon's Nest this is the plateau, which gets 2 ft contours, rather than 800 ft of cliffs.
- **Steep-ground thinning:**
  - Minor contours thin on steep ground.
  - Index contours thin at twice that slope, except every fifth index line (each 50 ft at a 2 ft interval).
  - No contour labels are set on cliff faces.
  - Each sheet has at most about 120 index levels.
- **Landmarks** found among the placed props are marked and labelled:
  - the clubhouse
  - the driving range tee (from its dividers, bag stands and ball pyramids), with each yardage marker shown as a dot and its yards
  - the practice green (its flags)
  - a helipad
- **Side panel:** shows the plot's size and relief and each landmark's elevation in place of the hole table.
- **`--buildable`** (app: Area → *Buildable ground*) maps only that buildable ground plus a 40 m margin. On Falcon's Nest that is the plateau top. This works for routed courses too, seeded from the holes.

Terrain map PNGs are written losslessly at maximum zlib compression.

## Hole sheets

`--holes 2,7,16` (or `all`), or **Create Hole Sheets** in the app, makes one **US Letter portrait page per hole** for sketching hole features by hand. You get a 300 dpi PNG per hole in `<course> - Hole Sheets/` and one PDF, `<course> - Hole Sheets.pdf`. Both print at true scale; the footer says how many yards an inch is at 100 %.

```bash
dotnet run --project src/Pga2K25.Cli -- terrain samples/courses/sagan-khushun.course --holes 2,7,16 --credit "NAME"
```

- **Orientation is the yardage book's:** back tee at the bottom, green straight up the page, using the same rotation as `HoleAnalysis` (checked by a test). A north arrow turned to match is in the footer.
- **Window:** 40 m behind the back tee to 50 m past the green, widened to the page. Doglegs keep at least 60 m either side of the route. It always fits on one page, so long holes are smaller scale: about 31 yds/in for a 160-yd par 3 and 60–80 yds/in for long par 4s and 5s.
- **Finer terrain:** a 0.5 m raster. The contour interval is the smallest of 1 / 2 / 5 … ft that keeps the ground **within 50 m of this hole's route** to 60 levels or fewer, so a hillside or the sea at the edge of the page doesn't coarsen it. On Sagan-Khushun every hole tried came out at 2 ft (index every 10 ft). Minor contours are thinned where they would run closer than 2 pt, and slivers left by the thinning are dropped.
- **A grid on the hole:** lines every 50 yds from the back tee (labelled in yards down both edges), and every 50 yds left and right of the tee→green line (`L50 · C · R50`, as you walk to the green) along the top and bottom. A spot height sits at each crossing.
- **The hole:**
  - route, aim point and ticks every 25 yds, with the ground height at every 50
  - the green site as an outline only, so the ground under it stays clear for sketching, with its fall arrow
  - neighbouring holes as faint dashed lines with their numbers
  - drawn surfaces and sea as on the plot map
- **Header:** hole, par and yards, tee → green elevations, the green site's tilt and ±ft, and the elevation range near the hole. There is no badge on the map itself.
- **0 ft** is the same on every sheet and on the plot map: sea level, or the plot map's lowest point when there is no sea.

## Green sites

Green sites are shaped and levelled in the game when the green is built. The figures describe the ground **as it lies**, so you can see how much shaping a site needs. They are not a warning that the green will play that way.

- **The site** is the drawn green, when one already sits at the end of the route (its last point can be well off the green's centre: 11 m on Murray Estate 1). Otherwise it is a 15-yd circle around that point.
- **Tilt** is the grade of the plane that best fits the ground over the site, and the fall arrow points down it. A steep, even hillside has a high tilt.
- **±ft** is the furthest the ground departs from that plane: how lumpy the site is. A site among dunes can be nearly level overall but uneven (Berwick Coast 11: 3 % tilt, ±18 ft).
- **Green ft** is the ground at the site's centre.

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
| `--holes` | Create Hole Sheets / Holes | | Hole sheets instead of the plot map (`all` or a list) |
| `--sheet-grid` | | 50 | Hole sheet grid (yd) |
| `--no-pdf`, `--no-png` | | both | Hole sheet outputs |
| `--sea-level`, `--no-water` | | from ocean props | Sea level (world m), or no sea |

## Code

`src/Pga2K25.TerrainMap`:

- `TerrainSurvey`: the window, replay, datum, interval choice, contours (via `GreensBook.MarchingSquares`, run in parallel per level), summits and per-hole figures
- `TerrainInk`: SkiaSharp drawing shared by both sheets.
  - Shading is rasterised at the terrain resolution and drawn through the projection with cubic resampling, at any rotation.
  - Contour labels are placed first so the lines can be clipped around them.
  - Route labels try 16 positions to stay clear of routes and other labels.
  - It also draws the routing and green sites.
- `TerrainMapRenderer` / `TerrainMapBuilder`: the plot map
- `HoleSheet` / `HoleSheetRenderer` / `HoleSheetBuilder`: hole sheets (window, finer survey, Letter layout, PNG + PDF)

Tests are in `tests/Pga2K25.Tests/TerrainMapTests.cs` and run against `samples/courses/sagan-khushun.course`.
