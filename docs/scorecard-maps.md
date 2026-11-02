# Scorecard Maps

A one-page overhead course map traced from a `.course` file. It shows fairways, tees, greens, bunkers, waste areas, paths, the hole routes and numbers, and the scorecard. The output is a PNG at 300 dpi on an 11 × 8.5 in page (3300 × 2550 px), with an optional PDF.

```bash
dotnet run --project src/Pga2K25.Cli -- scorecard "path/to/course.course" -o out
```

Options:

- `--theme vintage|modern|illustrated|plansheet|modernillustrated|both|all` (default `all`; `both` is Vintage and Modern)
- `--landscape` / `--portrait` (default: landscape, unless portrait draws the course at least 15% larger)
- `--north-up`: disable auto-rotation
- `--waste`: draw stippled waste areas (off by default; on desert courses everything off the fairway is waste)
- `--raw-bunkers` / `--bunker-merge M`: draw raw bunker pieces, or change the merge distance (default 10 m)
- `--no-paths`
- `--pdf`
- `--credit "NAME"`, `--subtitle "TEXT"`
- `--location "PLACE, REGION"`: Plan Sheet only, printed under the course name, one line per comma-separated part. Without it the place lines come from the course's `Place.json` (the Studio Place card); with neither, nothing prints.

Tee set names and hole names come from `Holes.json` in the output folder (`-o`), which the Studio's Holes card writes. The course seal comes from `Logos.json` there (see [Course logo](course-logo.md)): in colour on Modern and Modern Illustrated, in ink on Vintage and Illustrated, before the course name in the strip; on the Plan Sheet, on a parchment disc in the emptiest top corner of the map. The wind rose comes from `Place.json` there (the Studio Place card's Wind rose), when it has one. Pars and yardages always come from the course file. (Earlier releases took tee names from a `--data holes.xlsx|.csv` workbook; it is retired.)

[Course Designer Studio](studio.md) builds all five themes as PNG and PDF, with the names from the course's Holes card and the bunker merge from its Course settings (**Bunker merge**: 10, 6, 4 or 2 m, saved per course). The other options are on the command line.

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
| North | 32-point compass rose | As Vintage, in Modern's ink | As Vintage |

## Plan Sheet

The Plan Sheet is the Illustrated map laid out as a hand-drawn course plan, in the style of the RGD Artworks Course Graphics Dept. (Renaissance Golf Design). The sheet credits that style first, then the designer credit.

- **Always landscape.** A title column on the left holds "A Plan of the", the course name (broken at spaces and hyphens, as large as fits), the optional location line, and the scorecard: the championship tees, the front and back nines side by side. When the course has a wind rose (Place card), it sits under the scorecard, north-up with its mph key.
- **Ball flight:** red arrows, one per shot (back tee → aim points → green), each stopping just short of its target.
- **Golf hole direction dial:** one ray per hole from the back tee towards its green, with North, East, South and West arrows. Directions are on the page, so the dial turns with the map. A hole label near a cardinal arrow starts past its word. Labels of holes within 8° of each other step outwards on a dotted leader. The dial is sized to fit between the scorecard and the credits.
- **Bar scale:** yards in 100 yd segments (50 yd at large scales), two rows with alternate segments hatched, and the print ratio (100 yd = 3,600 in over the bar's printed length, to the nearest 100). It sits on a cartouche in the map corner with the least of the course under it.
- **Credits:** "In the style of the RGD Artworks Course Graphics Dept., Renaissance Golf Design, Inc.", then "Designed by" and the credit when set. The "Created using" note sits under the map.
- **Type:** Federo for display and Josefin Sans (SemiBold, set on the variable font) for small labels, both OFL and bundled in `assets/fonts/plansheet` (copied to `fonts/plansheet` next to the app); installed fonts, then Bahnschrift, if missing.
- **Region watermark:** with Locator "Generated region" on the Place card (and its watermark box ticked), or `--region SHAPE [--seed N] [--region-km K]`, the invented region is drawn faint behind the map, the course on its mark and place line 1 naming the land (see [Terrain map](terrain-map.md)).
- No legend and no compass rose on the map.

## Modern Illustrated

Modern with the land painted in, and room for every tee set in the table. Everything comes from the course file (`Scenery`, `ScorecardMapRenderer.ModernIllustrated.cs`).

- **Always landscape**, with the scorecard strip every theme uses (see [Layout](#layout)), drawn as a card with a green header and the Out / In / Total columns banded. Its legend lists just what the map shows.
- **Ground:** a soft hillshade on sand from the replayed terrain (2 m cells, slope across 12 m), lit from the upper left of the page whatever the map's rotation. It is a raster at 1.5 samples per page unit under the vector course, drawn smoothed.
- **Sea:** ground below the sea level (ocean props' height, else 0 m) is tinted from pale shallows to deep blue-green over 8 m of depth.
- **Grass:** rough and heavy rough in one green, then the fairways (closed surface-2 splines, `CourseLayers.Fairways`) mown lighter with diagonal stripes. Overlapping fairway splines are unioned into one mown area (in one path, two outlines drawn round opposite ways would cancel where they overlap and leave rough-coloured patches, as on Sangoma before 2.4.0). Greens have a lighter collar. Bunkers (merged as on the other themes) have a dark lip and a lit far edge. Tee pads are pale with a fine edge.
- **Water:** ponds, creeks and water areas get a pale shore band and short ripple strokes; streams are drawn at their width with the same band.
- **Planting:** every placed tree is a canopy sized by its scale (4 m radius at scale 1). Broadleaf trees are round and lit on the upper left. Conifers (pine, larch, cedar, spruce and the like, by species) are darker seven-point stars. Each tree throws a soft shadow to the lower right, and all the shadows are drawn first, so woods read as massed crowns. Bushes are small lit blobs, grasses three-stroke tufts and flowers coloured dots. Walls are grey lines and fences dashed brown.
- **Scatter groups and planted areas:** the game places those plants itself, so they are spread at random (seeded, so every build is the same) inside the group's circle or the area's outline. They are right in number and area, but not plant by plant. A group is capped at 0.6 plants per square page unit, so a 250,000-plant grass area prints as a texture.
- **Furniture:** the compass rose and wind rose in the map's emptiest corners (as on every theme, panels in lit sand), the scale bar on a sand panel bottom left, the hole numbers in circles, and the "Created using" note under the strip.

On Sagan-Khushun (4,305 trees) and Devil's Perch, a PNG and PDF build in 7–15 seconds. The PDF is 5–10 MB.

## Illustrated landscape

The Illustrated theme is Vintage with the land around the holes drawn like an old engraved map (`Landscape`). Everything comes from the course file, and the course is drawn on top, so fairways, greens and bunkers stay clean.

- **Hills:** hachures, short strokes running downhill, from the replayed terrain (2 m cells, as on the terrain map). Slope is measured across 24 m and drawn only above 11 %, so dune humps and gentle ground stay quiet and the big landforms show. Steeper ground gets longer, heavier, darker strokes.
- **Sea:** engraved water lines below the sea level (ocean props' height, else the game's 0 m sea plane): every row in the shallows, every second below 2 m, every third below 6 m. Ponds and creeks keep Vintage's hatch.
- **Trees:** every placed object under `/Trees/` is a canopy (a centre puff and four lobes) sized by its scale, with a shadow to the lower right, drawn back to front so woods read as massed crowns. Sagan-Khushun has 3,045.
- **Bushes:** a stipple dot per `/Bushes/` object (Cassimbo has 4,810).
- **Scale bar** on a parchment panel.
- **Legend:** adds Trees and Hills when the course has them.

The landscape is built once per map and shared by the PNG and the PDF. On Sagan-Khushun and Cassimbo, the Illustrated map builds in about 12 seconds. Its PDF is 6–7 MB, because every hachure is a vector stroke.

## Layout

- **Compass.** Game north is **-Z** (east is -X), fitted from the designer's tee/approach bearings. North-up is a 180 degree rotation of the raw X/Z plane.
- **Auto-rotation.** Every rotation within 90 degrees of north-up is tried, so north always points into the upper half of the page, and the one that fills the map area at the largest scale is kept. Both page orientations are tried, and the better fit wins unless one is forced.
- **Zoom.** The fit uses hole routes, tees, and the parts of features within 60 m of a hole, so outlying shapes don't shrink the map. Paths and roads are drawn only within 45 m of a hole.
Vintage, Modern, Illustrated and Modern Illustrated share one layout (`ScorecardMapRenderer.Strip.cs`); the Plan Sheet keeps its title column.

- **Landscape** (the default, and always for Modern Illustrated) puts the map across the whole page, 10.4 × 6.1 in (10.2 × 5.9 in inside the Vintage frame). Under it, a title column holds the course name, the subtitle and designer credit on one line, and the legend in two columns. Beside that is the **scorecard strip**.
- **Portrait** is chosen only when it draws the course at least 15% larger (or with `--portrait`). The title goes across the top, the map in the middle, then the strip across the page with the totals, credit and legend in a row under it.
- **Scorecard strip:** the holes run 1–9, Out, 10–18, In, Total (a nine-hole course has 1–9 and Total). There is a row for each tee set, longest first and in bold, up to five (a course with more shows its longest five), then a Par row. Tee names come from the Holes card (with a colour dot for Red / Blue / White and the like), else "Tee n", or "Yards" for a single set. With hole names on the Holes card, a Name row follows the hole numbers; each name wraps onto two lines, then shrinks, then is cut with "…" to fit its column. Yardages always come from the course file using the game's own rule, `|tee - waypoints[1]| + route to the green`, which matches `CourseMetadata.teeSetInfos` exactly.
- **Compass rose and wind rose:** a 32-point compass rose turned to game north, and, when the Place card has a wind rose, the rose (north-up, with its mph key, as on the Wind Card) on a 1.7 × 1.5 in panel. Each goes where the least of the course lies under it (grass and green outlines, and points along every route): the top corners and bottom right first, then spots along the map's edges, never the bottom left (the scale bar's corner). The wind rose chooses first, as it needs the most room, and the compass rose keeps clear of it.
- **Hole numbers** sit on the middle of each hole, halfway along the back-tee to green route. If two numbers collide, a label slides along its own hole until it's clear.
- **Scale bar** in yards.

## How features are traced

| Map layer | Source in the `.course` file |
|---|---|
| Fairway / tee corridor | Closed `surfaceSplines2` with surface 2 (fairway), 3 (rough) or 4 (heavy rough) |
| Greens | Closed surface-1 splines |
| Bunkers | Closed surface-0 splines, open surface-0 splines (ribbons) and surface-0 paint brushes, **merged**: rasterised at 0.35 m, morphologically closed (grow by R, fuse, shrink by R; R = half the merge distance) so gaps and pinholes under about 10 m fill, lightly rounded, and traced back to smooth outlines. The closing may not bridge across fairways or greens, except within 1 m of a real bunker piece |
| Waste areas | Surface-15 splines, ribbons and brushes (drawn only with `--waste`) |
| Water | Closed surface-10 splines and open ones at least 4 m wide (narrower ones are walking paths). Also ponds and creeks from `water[]` (water planes and painted streams) and from the designer's running-water and body-of-water tools (object paths), all traced by `WaterTracer`: those reaching within 60 m of play are drawn whole with dry islands and count towards the page extent, and ponds out in the scenery are left off. Cassimbo has none. |
| Cart paths / roads | Open surface-7 / surface-8 splines |
| Tee pads | A 5 × 9 m pad at each tee marker, aligned with the hole |
| Ball flight | Back tee → `holes3[].waypoints[1..]` |

**How the surface IDs were identified.** Surfaces 0/1/2/3/4/7/10 come from the reference Course Finisher. Surfaces 8 and 15 were identified by matching Cassimbo's layers against an in-game overhead capture; 18 comes from the designers who use it:

- **18** is surface weathering, splines and brushes alike: a colour tint over the ground beneath, not a playing surface, so it is not drawn. drgnslyr221 uses it for bare ground on Devil's Perch. On Cassimbo, "reverse weathering" splines along the fairway edges carry the green out over brown heavy rough. From overhead they look like a lighter fairway cut, which is how this page first read them.
- **15** is the grey waste scrub.
- **8** is the winding service road.

Surfaces 14, 16 and 17 appear only as a few brushes and are not drawn yet.

## Known limits

- Brush footprints are treated as circles, since the game's brush shapes aren't stored in the file. Merging hides this at map scale.
- A 10 m merge can join genuinely separate bunkers that sit close together: Sangoma's pot complexes, painted as dabs, print as one big bunker. Choose 4 m for such a course (Studio: Course settings, Bunker merge; CLI: `--bunker-merge 4`). It can't be the default: Cassimbo's sand areas are painted as scattered dabs and turn to lace below about 8 m, and some of Devil's Perch's bunkers break into dots at 4 m.
- Game-generated greens (no spline) aren't drawn.
- Hole-number placement is greedy. Very crowded junctions can still sit close together.
