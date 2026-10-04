# Scorecard Maps

A one-page overhead course map traced from a `.course` file. It shows fairways, tees, greens, bunkers, waste areas, paths, the hole routes and numbers, and the scorecard. The output is a PNG at 300 dpi on an 11 × 8.5 in page (3300 × 2550 px), with an optional PDF.

```bash
dotnet run --project src/Pga2K25.Cli -- scorecard "path/to/course.course" -o out
```

Options:

- `--theme vintage|modern|both` (default `both`)
- `--landscape` / `--portrait` (default: whichever fits better)
- `--north-up`: disable auto-rotation
- `--data holes.xlsx|.csv`: designer hole data. It supplies only what the course file lacks (tee set names). The course file is always treated as the most correct source for pars and yardages; any disagreement is logged, never applied.
- `--waste`: draw stippled waste areas (off by default; on desert courses everything off the fairway is waste)
- `--raw-bunkers` / `--bunker-merge M`: draw raw bunker pieces, or change the merge distance (default 10 m)
- `--no-paths`
- `--pdf`
- `--credit "NAME"`, `--subtitle "TEXT"`

[Course Designer Studio](studio.md) builds both themes as PNG and PDF, using the course's hole-data workbook if one is set. The other options are on the command line.

## Themes

| | Vintage | Modern |
|---|---|---|
| Inspired by | Engraved course guides (e.g. the Sand Valley / Lido card) | Contemporary flat illustrations (e.g. the ClubTFE Lido map) |
| Ground | Parchment `#ebe0c2`, double ink frame | Sand `#f6e7ba` |
| Fairway / tee corridor | Parchment fill with an ink outline around the union of shapes | Flat green `#adbb69` |
| Greens | Darker parchment, ink outline | Dark green `#3b481c` |
| Bunkers | Solid ink | Orange `#d98b3a` |
| Waste areas (optional) | Ink stipple | Tan with orange stipple |
| Ball flight | Dashed ink, back tee → aim points → green | Thin solid line |
| Hole numbers | Circled, on the middle of the hole | Bold on a soft disc, on the middle of the hole |

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
| Water | Surface-10 splines and ribbons (none on Cassimbo) |
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
