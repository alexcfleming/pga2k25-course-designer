# Terrain map

A single high-resolution, north-up PNG of a routed but unbuilt plot, made for planning. Sketch fairways, bunkers and green complexes over it by hand or in an image editor.

```bash
dotnet run --project src/Pga2K25.Cli -- terrain samples/courses/sagan-khushun.course -o out --credit "NAME"
```

The output is `<course> - Terrain Map.png`. At the default 4 px per metre, a 1.5 km routing gives roughly 7,000 × 6,500 px, which is about 28 × 26 in at 250 dpi. [Course Designer Studio](studio.md) draws this map live in its workspace and builds the PNG and the PDF with the default options.

`--pdf` also writes `<course> - Terrain Map.pdf`, the same sheet drawn by the same code. Contours, routing, labels, grid and panel are vectors, so they stay sharp at any zoom or print size; only the relief or slope shading is an embedded image. The page is the PNG at `--print-dpi` (default 250): Sagan-Khushun's sheet is 28.2 × 25.8 in, and the PDF is about 4 MB against the PNG's 19 MB. A sheet that would pass PDF's 200 in page limit gets a higher dpi instead.

## What's on the sheet

- **Contours.** The interval is the smallest of 1 / 2 / 5 / 10 / 20 … ft that keeps the ground within 80 m of play to at most 60 levels. Every 5th line is a heavier **index contour**, labelled in line (the line breaks around the number). On steep ground, minor contours are dropped where they would run closer than 7 px; index contours are always drawn.
- **Shading.** `relief` (default) is a pale elevation tint with a soft north-west hillshade. `slope` shows planning classes: 0–3 % green sites, 3–8 % fairway, 8–15 % steep for play, 15–25 % needs grading, over 25 % severe. `none` is plain paper.
- **Sea.** Every plot has the game's sea plane at world 0 m; it shows wherever the ground dips below it. Berwick Coast has no ocean props, yet its in-game coastline follows the modelled 0 m line. When the plot does have ocean props (`OceanChurn…` surf and wave prefabs), their placed height is used instead (also 0 m on stock plots). Ground at or below sea level (2 cm tolerance for blur) is filled blue, edged with a shoreline, and not contoured, and elevations are then measured **above sea level**. A sheet whose ground never reaches 0 m has no sea (Murray Estate). Inland hollows below 0 m fill too, as they would under a flat ocean. With ocean props, the log reports how far they sit from the modelled shoreline as a check. `--sea-level <m>` overrides the level; `--no-water` turns the sea off.
- **Ponds and streams** (`water[]`, traced by `GreensBook.WaterTracer`; see the course-format doc for the evidence). Each entry carries its water level, and the file holds two kinds:
  - **Painted streams.** These are brush-sized dabs, with a median longer half-size under 30 m. They are filled blue over their whole stroke, because the game cuts the channel to the water level: Berwick Coast's stream runs unbroken in game. Consecutive dabs at the same level and within two brush widths are joined into one stroke.
  - **Water planes.** These are large rectangles (Luke Muscat's course). Water shows only where the ground inside the rectangle lies below the level, so islands stay dry. Water is tinted by depth, from pale at the margins to darker below 3 m. Small pieces cut off by a rectangle's edge are dropped: in game every shoreline is rounded. Two pieces of water are joined by a 4 m shallow channel when a route between them over ground cut by lower brushes rises no more than 2 m above the level. On Luke's course that is the hole 1 creek, whose bed the replay leaves about 1.6 m high. Sangoma's five planes give four ponds, checked against in-game screenshots (holes 1–2, 4, 11 and 14). A 294 × 1,116 m strip shows nothing because the ground under it stands above its level. Water shows only where the replay is right: before edge-centred stamps were left out, hole 14's plane flooded its whole rectangle 20 m deep.
  - Both kinds are edged, drawn over the contours and labelled with their level in feet. `--water-footprints` dashes the plane rectangles, which show where lowering the ground would flood. The log lists each body's level, area, depth and any joining channel. `--no-water` turns all of this off too.
- **Not water in game.** A plane shows water wherever the replayed ground dips below its level, and over shallow ground the replay can be a metre or two out. On Sangoma hole 11 the model floods a strip along the path west of the pond, about 0.6 m deep, that is dry in game. No marker or surface rule in the file separates it from real shallow water such as Luke's creek. So the designer marks such pieces: in the Studio, **Not water…** on the map toolbar, then a click on the piece. The marks save to `NotWater.json` in the course's output folder (`NotWaterMarks`). The tracer drops every separate piece a mark falls in before it joins pieces with channels, so the strip's channel goes too. Every product reads the file: terrain map, hole sheets, yardage book and scorecard maps. A fringe attached to a real pond can't be marked off without the whole pond.
- **Walking paths.** Open surface-10 splines narrower than 4 m are walking paths, not streams, and are drawn with the paths.
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
- **`--buildable`** maps only that buildable ground plus a 40 m margin. On Falcon's Nest that is the plateau top. This works for routed courses too, seeded from the holes.

Terrain map PNGs are written losslessly at maximum zlib compression.

## Hole sheets

`--holes 2,7,16` (or `all`), or **Build** on Hole sheets in [Course Designer Studio](studio.md) (every hole), makes one **US Letter portrait page per hole** for sketching hole features by hand. You get a 300 dpi PNG per hole in `<course> - Hole Sheets/` and one PDF, `<course> - Hole Sheets.pdf`. Both print at true scale; the footer says how many yards an inch is at 100 %.

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

## Game budget (rough estimate)

The side panel shows two meters for the course designer's budget counters: **Objects** (how many things are in the plot) and **Memory** (driven mostly by how many different things there are). The file doesn't store either counter; the game works them out when it loads the course. `GameBudget` in `src/Pga2K25.TerrainMap/GameBudget.cs` estimates them from what the file holds:

- **Objects** = placed items + plants in scatter groups + planted-area trees (area × `fillPct` × 31.9) + 2.4 per metre of wall, fence or other object path + 46 per hole, out of about 119,350.
- **Memory %** ≈ 9.8 + 0.32 × the number of different assets + 0.10 × √objects.

The constants come from four in-game readings taken on 2026-10-26 (object % / memory %): Sagan-Khushun 72.5 / 62.7, Wakinyan Dunes 0.7 / 14.3, Blackwolf Lenz Upper 6.3 / 46.9 and Murray Estate 11.7 / 28.1. Those four fit the object formula exactly and the memory formula to within 0.9 points. Neither formula has been checked on a course it wasn't fitted to, so the panel labels both as rough, and the designer's own counters remain the real figures. The meters turn red from 90 %.

Readings from Halvoy (almost nothing placed, no holes) and Berwick Coast (few items, 833 m of walls) would test the per-hole and per-metre parts. A count of Murray's pines in game would test the planted-area scale. That scale suggests planted areas hold far more trees than the plain area × `fillPct` the planting overlay uses.

## Planting

With planting on (Studio's **Planting** toggle, or `--planting` on the CLI), the map shows what the designer planted. It is drawn over the contours and under the routing:

| Layer | Drawn as |
|---|---|
| Trees | A crown at its canopy size (4 m radius at scale 1). Broadleaf trees are light green with a dark edge and conifers are dark green, so they read apart in black and white; **Trees by type** off (`--trees-one-colour`) draws one green. |
| Bushes, flowers | Small dots (bushes green, flowers rose). Hole sheets leave flowers off. |
| Grass and fescue | Stipple. |
| Walls and fences | A line, dashed for fences. |

The course file stores planting three ways, and all three are shown:

- **Placed one by one**: each item's position and size (`placedObjects4[].Value.items`).
- **Scatter groups** (`clusters`): a centre, a radius and a count. The game places the copies itself, so the map draws the circle dashed and tree groups carry their count (×14).
- **Planted areas** (`splines` with `fillPct`): a closed outline, hatched, labelled with an estimate of area × fill. Reading `fillPct` as instances per m² is inferred: Murray's four Eastern White Pine areas come to 49,371 m², or about 370 trees.

The side panel adds a **Planting** section, with tree totals (≈ where groups or areas are involved) and the six commonest tree species, plus legend rows for the layers that are on. Kind and species come from the asset's folders (`…/Trees/Birch_River/…` is a broadleaf "Birch River"; names with Pine, Larch, Cedar, Spruce, Fir and the like are conifers).

Vegetation the game generates by itself (its tree, plant and grass density settings, which change with each theme) isn't stored in the file and isn't shown. Neither are the `clearTrees` / `clearDetail` layers, which only remove that generated vegetation. Most designers clear it from near the playing area anyway.

The code is `Planting` and `PlantSpecies` in `src/Pga2K25.TerrainMap/Planting.cs`, plus `TerrainInk.DrawPlanting`. The extra records (`PlacedCluster`, `PlacedArea`, `ObjectPath`) are read in `CourseModel`.

## Green sites

Green sites are shaped and levelled in the game when the green is built. The figures describe the ground **as it lies**, so you can see how much shaping a site needs. They are not a warning that the green will play that way.

- **The site** is the drawn green, when one already sits at the end of the route (its last point can be well off the green's centre: 11 m on Murray Estate 1). Otherwise it is a 15-yd circle around that point.
- **Tilt** is the grade of the plane that best fits the ground over the site, and the fall arrow points down it. A steep, even hillside has a high tilt.
- **±ft** is the furthest the ground departs from that plane: how lumpy the site is. A site among dunes can be nearly level overall but uneven (Berwick Coast 11: 3 % tilt, ±18 ft).
- **Green ft** is the ground at the site's centre.
- **Hiding circles on drawn greens.** Once a hole's green spline is drawn, the spline shows the green, so its 15-yd circle and fall arrow only add clutter. `--no-drawn-green-sites` (Studio: **Green-site circles on drawn greens**, under the map options) hides them on those holes. Greens not yet drawn keep theirs, the flag stays, and the figures in the holes table and on the hole sheet header are unchanged. The legend drops the green-site row when no circle is left.

## Terrain model

The terrain is rebuilt by replaying the file's height stamps in order (`TerrainReplay`, shared with the greens book and yardage book). Each stamp is drawn with **its real brush shape**:

- The designer has 69 landscaping brushes (`assets/brushes`, cut from the palette). A stamp's `type` is the palette slot + 8 for slots 0–29 and + 9 for slots 30–68. Type 38 isn't in the palette. This was found on a test plot with one dab of each brush (`samples/courses/brush-test-plot.course`).
- The brush image spans **±scale** metres around the stamp. The designer's "100 yd" brush is stored as scale 17.81, and its hard circle is about 32 m across in game.
- At rotation 0 the image's top points to +Z. `rotation.y` turns it like a Unity Y rotation.
- **Raise** (tool 1) adds the value × the image's brightness, so soft brushes add less. The most-used soft round brush (type 54) peaks at about 0.45.
- **Flatten** (tool 0) moves the ground towards the value, by the brightness divided by the brush's peak, so every flatten reaches its value at its centre.
- **Small flattens are read as ground samples** (`FlattenSamples`). The designer's flatten picks up the height under the cursor, so a small flatten's value is the game's own ground there: 85 % of Sagan-Khushun's sub-4 m flatten dabs sit within 3 in of the replay. Where a flatten under 8 m (`FlattenSampleMaxScale`) is up to 1 m (`FlattenSampleMaxMetres`) away from the replayed ground, the ground is first shifted by that gap, fading out at twice the stamp's scale (`FlattenSampleReach`). Larger gaps are taken as a height set on purpose. Without it, Sagan-Khushun green 9 showed a 17.6 in crown at pin 1, where the game has a gentle hollow: two 7.1 m dabs landed on ground the big sculpting brushes had left 0.84 m too low. Settings were swept against Cassimbo's spot heights: flattens up to 30 m with a 1.5 m gap also cleaned up the rest of Sagan green 9, but took Cassimbo holes 1 and 7 to 22 % and 26 % of range. An in-game capture of Sagan green 9 (docs/greens-book.md) scores 10.3 in RMS without and 5.4 in with the defaults.
- Unknown types fall back to the old round disc (diameter = scale, `Hardness` 0.85, `LargeBrushHardness` for brushes 40 m or more).
- **Stamps centred on the plot edge are left out** (`SkipEdgeStamps`). The designer clamps the centre of a brush dragged past the edge to ±1000 m, and the game doesn't apply those stamps to the plot. On Sangoma, six 1000 m lower squares centred on z = ±1000 would put the ground 35–100 m below the game's own marker heights, even 6 m from the edge. Every course has some edge-centred stamps. Leaving them out moves the ground along the routes by at most 0.4 m on the other samples (2 m on deltatest), but it can move scenery past the routing a long way, and with it the sheet's lowest point, which is the datum for elevations.

**Checks:**

- **Berwick Coast:** 36 tee → aim and aim → green height changes, measured in game on the route markers (`samples/calibration/route-heights`). RMS error 2.2 ft; the old round-disc model was 22 ft.
- **Murray Estate:** 7 legs, 7.4 ft (was 15 ft). Hole 3 aim → green is still 17 ft out.
- **Cassimbo greens:** calibration stays within 3–13 % of each green's range (holes 1, 7, 10, 13: 3.3, 12.6, 7.7, 6.6 % with flatten samples; 4.1, 11.5, 9.2, 6.1 % without).
- **Sangoma distance markers:** the file's 2,290 `distanceMarkers` carry the game's own ground height (`CourseModel.DistanceMarkers`). The replay is a median 0.4 m low, with an RMS of 2.3 m (5.7 m before edge-centred stamps were left out). The rest of the error is spread over hundreds of overlapping 400–700 m soft brushes, and capping big brush sizes didn't help.
- Overlaid on an in-game overhead of Berwick, the old model's coins and craters are gone and the modelled 0 m line follows the real coastline.

- **Test plot peaks:** raises set to +100 ft, measured in game. The rough streak (type 53) reaches at least 94.7 ft (model 91). The splatter (type 49) has peaks of 49, 68 and 71 ft (model 60). So a raise does rise by value × brightness, with dim brushes rising less. Rescaling raises to the icons' white level (243, not 255) made the route heights slightly worse, so it isn't applied.

**Pre-made plot terrain.** Some designer plots come with terrain that exists before any brushes, controlled by a plot setting. It behaves differently from brush terrain and isn't modelled here: the replay only knows the brushes in `height[]`. Set that option to zero and flatten the plot (as Alex does) before relying on these maps. If a plot still has its pre-made terrain, the maps will be wrong wherever it shows.

The images come from screenshots of the palette icons, so fine detail is approximate (texture brushes, exact edge softness). That is the likely cause of Murray Estate hole 3: its green sits partway out on a splatter and two streak raises, where the placement of the texture's lumps matters. Check critical grades in the game.

## Options

| CLI | Default | |
|---|---|---|
| `--full-plot` | routing + 150 m | Map every terrain stamp instead (the whole plot) |
| `--margin` | 150 | Metres around the routing |
| `--interval` | auto | Contour interval in feet |
| `--index` | 5 | Every n-th contour is an index contour |
| `--shading` | relief | `relief`, `slope` or `none` |
| `--ppm` | 4 | Output pixels per metre; text and line weights scale with it |
| `--cell` | 1.0 | Terrain raster (m) |
| `--grid` | 100 | Grid square (yd); 0 hides the grid |
| `--hardness` / `--large-hardness` | 0.85 / 0.3 | Falloff for small / large brushes of unknown shape (round-disc fallback) |
| `--smooth` | 0 | Extra Gaussian smoothing (sigma, m) before contouring |
| `--no-features`, `--no-summits` | shown | |
| `--holes` | | Hole sheets instead of the plot map (`all` or a list) |
| `--sheet-grid` | 50 | Hole sheet grid (yd) |
| `--no-pdf`, `--no-png` | both | Hole sheet outputs |
| `--sea-level`, `--no-water` | from ocean props | Sea level (world m), or no sea |
| `--water-footprints` | off | Dash the water planes' rectangles |
| `--no-drawn-green-sites` | shown | Hide the green-site circle and fall arrow on holes whose green spline is drawn |

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
