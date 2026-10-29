# Scorecard Maps

A one-page overhead course map traced from a `.course` file. It shows fairways, tees, greens, bunkers, waste areas, paths, the hole routes and numbers, and the scorecard. The output is a PNG at 300 dpi on an 11 × 8.5 in page (3300 × 2550 px), with an optional PDF.

```bash
dotnet run --project src/Pga2K25.Cli -- scorecard "path/to/course.course" -o out
```

Options:

- `--theme vintage|modern|illustrated|plansheet|both|all` (default `all`; `both` is Vintage and Modern)
- `--landscape` / `--portrait` (default: whichever fits better)
- `--north-up`: disable auto-rotation
- `--data holes.xlsx|.csv`: designer hole data. It supplies only what the course file lacks (tee set names). The course file is always treated as the most correct source for pars and yardages; any disagreement is logged, never applied.
- `--waste`: draw stippled waste areas (off by default; on desert courses everything off the fairway is waste)
- `--raw-bunkers` / `--bunker-merge M`: draw raw bunker pieces, or change the merge distance (default 10 m)
- `--no-paths`
- `--pdf`
- `--credit "NAME"`, `--subtitle "TEXT"`
- `--location "PLACE, REGION"`: Plan Sheet only, printed under the course name, one line per comma-separated part (blank prints nothing)

[Course Designer Studio](studio.md) builds all four themes as PNG and PDF, using the course's hole-data workbook if one is set. The other options are on the command line.

## Themes

| | Vintage | Modern | Illustrated |
|---|---|---|---|
| Inspired by | Engraved course guides (e.g. the Sand Valley / Lido card) | Contemporary flat illustrations (e.g. the ClubTFE Lido map) | Old engraved estate maps |
| Ground | Parchment `#ebe0c2`, double ink frame | Sand `#f6e7ba` | As Vintage, with the landscape below |
| Fairway / tee corridor | Parchment fill with an ink outline around the union of shapes | Flat green `#adbb69` | As Vintage |
| Greens | Darker parchment, ink outline | Dark green `#3b481c` | As Vintage |
| Bunkers | Solid ink | Orange `#d98b3a` | As Vintage |
| Waste areas (optional) | Ink stipple | Tan with orange stipple | As Vintage |
| Ball flight | Dashed ink, back tee → aim points → green | Thin solid line | As Vintage |
| Hole numbers | Circled, on the middle of the hole | Bold on a soft disc, on the middle of the hole | As Vintage |
| North | Small arrow | Small arrow | 32-point compass rose |

## Plan Sheet

The Plan Sheet is the Illustrated map laid out as a hand-drawn course plan, in the style of the RGD Artworks Course Graphics Dept. (Renaissance Golf Design). The sheet credits that style first, then the designer credit.

- **Always landscape.** A title column on the left holds "A Plan of the", the course name (broken at spaces and hyphens, as large as fits), the optional location line, and the scorecard.
- **Ball flight:** red arrows, one per shot (back tee → aim points → green), each stopping just short of its target.
- **Golf hole direction dial:** one ray per hole from the back tee towards its green, with North, East, South and West arrows. Directions are on the page, so the dial turns with the map. A hole label near a cardinal arrow starts past its word. Labels of holes within 8° of each other step outwards on a dotted leader. The dial is sized to fit between the scorecard and the credits.
- **Bar scale:** yards in 100 yd segments (50 yd at large scales), two rows with alternate segments hatched, and the print ratio (100 yd = 3,600 in over the bar's printed length, to the nearest 100). It sits on a cartouche in the map corner with the least of the course under it.
- **Credits:** "In the style of the RGD Artworks Course Graphics Dept., Renaissance Golf Design, Inc.", then "Designed by" and the credit when set. The "Created using" note sits under the map.
- **Type:** Federo for display and Josefin Sans (SemiBold, set on the variable font) for small labels, both OFL and bundled in `assets/fonts/plansheet` (copied to `fonts/plansheet` next to the app); installed fonts, then Bahnschrift, if missing.
- No legend and no compass rose on the map.

## Illustrated landscape

The Illustrated theme is Vintage with the land around the holes drawn like an old engraved map (`Landscape`). Everything comes from the course file, and the course is drawn on top, so fairways, greens and bunkers stay clean.

- **Hills:** hachures, short strokes running downhill, from the replayed terrain (2 m cells, as on the terrain map). Slope is measured across 24 m and drawn only above 11 %, so dune humps and gentle ground stay quiet and the big landforms show. Steeper ground gets longer, heavier, darker strokes.
- **Sea:** engraved water lines below the sea level (ocean props' height, else the game's 0 m sea plane): every row in the shallows, every second below 2 m, every third below 6 m. Ponds and creeks keep Vintage's hatch.
- **Trees:** every placed object under `/Trees/` is a canopy (a centre puff and four lobes) sized by its scale, with a shadow to the lower right, drawn back to front so woods read as massed crowns. Sagan-Khushun has 3,045.
- **Bushes:** a stipple dot per `/Bushes/` object (Cassimbo has 4,810).
- **Compass rose:** turned to game north, in whichever of the top-left, top-right and bottom-right map corners has the least of the course near it. The scale bar gets a parchment panel.
- **Legend:** adds Trees and Hills when the course has them.

The landscape is built once per map and shared by the PNG and the PDF. On Sagan-Khushun and Cassimbo, the Illustrated map builds in about 12 seconds. Its PDF is 6–7 MB, because every hachure is a vector stroke.

## Layout

- **Compass.** Game north is **-Z** (east is -X), fitted from the designer's tee/approach bearings. North-up is a 180 degree rotation of the raw X/Z plane.
- **Auto-rotation.** Every rotation within 90 degrees of north-up is tried, so north always points into the upper half of the page, and the one that fills the map area at the largest scale is kept. Both page orientations are tried, and the better fit wins unless one is forced.
- **Zoom.** The fit uses hole routes, tees, and the parts of features within 60 m of a hole, so outlying shapes don't shrink the map. Paths and roads are drawn only within 45 m of a hole.
- **Landscape** puts the map on the left, with a panel on the right for the title, front- and back-nine scorecard tables, course totals, credit and legend.
- **Portrait** puts the title across the top, the map in the middle, and the two tables side by side at the bottom.
- **Scorecard:** one column per tee set, named from the hole data (Red / Green / White / Blue, with colour dots) or Tee 1..n. Yardages always come from the course file using the game's own rule, `|tee - waypoints[1]| + route to the green`, which matches `CourseMetadata.teeSetInfos` exactly.
- **Hole numbers** sit on the middle of each hole, halfway along the back-tee to green route. If two numbers collide, a label slides along its own hole until it's clear.
- **Scale bar** in yards.

## How features are traced

| Map layer | Source in the `.course` file |
|---|---|
| Fairway / tee corridor | Closed `surfaceSplines2` with surface 2 (fairway), 18 (inner fairway cut), 3 (rough) or 4 (heavy rough) |
| Greens | Closed surface-1 splines |
| Bunkers | Closed surface-0 splines, open surface-0 splines (ribbons) and surface-0 paint brushes, **merged**: rasterised at 0.35 m, morphologically closed (grow by R, fuse, shrink by R; R = half the merge distance) so gaps and pinholes under about 10 m fill, lightly rounded, and traced back to smooth outlines. The closing may not bridge across fairways or greens, except within 1 m of a real bunker piece |
| Waste areas | Surface-15 splines, ribbons and brushes (drawn only with `--waste`) |
| Water | Closed surface-10 splines and open ones at least 4 m wide (narrower ones are walking paths). Also ponds and creeks from `water[]` (water planes and painted streams, traced by `WaterTracer`): those reaching within 60 m of play are drawn whole with dry islands and count towards the page extent, and ponds out in the scenery are left off. Cassimbo has none. |
| Cart paths / roads | Open surface-7 / surface-8 splines |
| Tee pads | A 5 × 9 m pad at each tee marker, aligned with the hole |
| Ball flight | Back tee → `holes3[].waypoints[1..]` |

**How the surface IDs were identified.** Surfaces 0/1/2/3/4/7/10 come from the reference Course Finisher. Surfaces 8, 15 and 18 were identified by matching Cassimbo's layers against an in-game overhead capture:

- **18** is the lighter inner fairway cut drawn inside the surface-2 shapes. Surface-18 *brushes* are not drawn, because the reference tool uses brush category 18 for terrain weathering.
- **15** is the grey waste scrub.
- **8** is the winding service road.

Surfaces 14, 16 and 17 appear only as a few brushes and are not drawn yet.

## Known limits

- Brush footprints are treated as circles, since the game's brush shapes aren't stored in the file. Merging hides this at map scale.
- A 10 m merge can join two genuinely separate bunkers that sit very close together. Lower `--bunker-merge` if that happens.
- Game-generated greens (no spline) aren't drawn.
- Hole-number placement is greedy. Very crowded junctions can still sit close together.
