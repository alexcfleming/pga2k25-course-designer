# Yardage book

This builds a yardage book from a `.course` file. It uses one page per hole and one page format.

## Look (Studio 1.8, "Clubhouse")

Since 1.8 the page has its own look. Earlier versions followed a familiar commercial yardage book too closely. The information and layout are unchanged:

- **Palette:** a white page with soft, edgeless fills on a warm ground. Green is used only for turf. Gold is used for the yards to the green, the line of play and the page's accents. Deep green is used for the course name and the par disc.
- **Type:** Archivo, regular, bold and extra bold. It is read from `fonts/archivo/` next to the app (`Archivo-Regular.ttf`, `Archivo-Bold.ttf`, `Archivo-ExtraBold.ttf`, OFL) when those files are there, then from an installed Archivo, then Segoe UI or Arial.
- **Header:** a gold rule, then the course name and "Hole · Par · yards", with the north arrow at the right.
- **Markers:** fairway widths sit in white, gold-edged rounded tags with tick marks at the ends. The green depth sits in a gold-edged diamond. Carries are plain stacked numbers.
- **Footer:** a deep green par disc, the tee table (elevation change in gold) and the hole number in a gold ring. The designer credit, when set, is at the bottom right above the Studio note.
- **Relief** (`Relief.cs`): no contour lines, so nothing fights the distance circles.
  - A hillshade wash lit from the top left of the page. It is a small smoothed raster, so a PDF page stays light.
  - Hachures down the fall line, longer and closer on steeper ground and none below an 8 % slope.
  - Both read the slope across 5 m of the hole's terrain model, so the brush replay's small bumps don't show.
  - Both are full strength in the rough, and about 40 % on this hole's fairways (fainter, so the surfaces read first). There are none on the greens, whose slopes are the greens book's job, and no hachures in water or bunkers.

## Output

| Output | Default | What it is |
|---|---|---|
| `<Course> - Yardage Book.pdf` | on | The workbook: one **portrait 4.25 × 11 in** page per hole (306 × 792 pt), in hole order. Pages are drawn as vectors, so they print sharply at any size. Metadata: title, credit, par. |
| `<Course> - Yardage Book/Hole_NN.png` | off | One PNG per hole, 1275 × 3300 px (300 dpi). |

```bash
dotnet run --project src/Pga2K25.Cli -- yardage samples/courses/cassimbo.course -o out --data samples/data/cassimbo-hole-data.xlsx
```

```bash
dotnet run --project src/Pga2K25.Cli -- yardage samples/courses/cassimbo.course -o out --holes 1,4,11 --png --verbose
```

CLI options:
- `--holes 1,2`: a subset of holes.
- `--data`: tee names only. Yardages always come from the course file.
- `--png`: also write PNGs.
- `--no-pdf`: PNGs only, together with `--png`.
- `--verbose`: explains why each hazard was or wasn't labelled.
- `--credit "NAME"`.

[Course Designer Studio](studio.md) builds every hole as the PDF and PNGs, with tee names from the course's hole-data workbook if one is set. Hole subsets and `--verbose` are on the command line.

## Page contents

- **View:** the page is cut along the line of play, from 22 m behind the back tee to 32 m past the green, and rotated so play runs up the page. A north arrow in the header shows the true bearing (north = −Z).
- **Surfaces:**
  - Drawing order: rough, paths, water, fairways, bunkers, vegetation and rock masses, in-play hazards, tee pads, greens.
  - Shapes owned by another hole are faded. Ownership is decided by a majority vote of nearby hole routes, and any shape within 15 m of this hole's line of play always belongs to this hole.
  - Water includes drawn water shapes and the course's `water[]` entries (painted streams and water planes) and the water tools' streams and ponds, traced once per course by `WaterTracer` (see [terrain-map.md](terrain-map.md)). Islands stay dry.
- **Vegetation and rocks:**
  - Planting is shown in masses. Bushes and trees are fused into smooth sage canopy masses, and the placed conifers' crowns are drawn darker inside them. Scatter groups of trees and bushes (`clusters`) join the masses as their whole circle, and planted areas (`splines` with a fill) join as their outline, because the game places those plants itself. Grass and flowers are left off at this scale. Vegetation the game generates by itself isn't in the file and isn't shown (see [terrain-map.md](terrain-map.md#planting)).
  - Rocks are fused into masses too: their radius is estimated from the course itself (see below). Both use the same merge as the scorecard bunkers: rasterise, close, smooth, trace.
  - Background masses are clipped away from this hole's fairways, greens and tees.
  - Masses sitting in the fairway are **in-play hazards**. They are drawn strongly and given numbers.
- **Hazard numbers:** dark is yards from the back tee and gold is yards to the green centre.
  - The carry point is the part of the hazard nearest the line of play: within 15 m, falling back to 40 m.
  - A hazard crossing the line in several places is split into stretches, with each stretch labelled separately. Water is never split: its near and far banks are one crossing.
  - Long stretches (25 yd or more) also get a front number (a dot marks the point).
  - Carries within 40 yds of the green, or past the green, are left out. Bunker and in-play carries under 120 yds (fronts under 100) are left out too, but water carries are always numbered: a forced carry off the tee is the number a golfer needs (Luke Muscat's hole 1 creek, 54 yds). On a par 3, a water carry is numbered even within 40 yds of the green, because the tee shot is the approach: Sangoma's hole 11 clears its pond 39 yds short of the green.
- **Arcs:** green-centred distance circles every 25 yds are labelled on the right, in gold. Back-tee circles (dashed) are labelled on the left.
- **Widths:** fairway width at 200, 250 and 300 yds, measured across the page. Bunkers and in-play hazards are excluded.
- **Spot heights:** feet relative to the green centre, every 50 yds from 125 yds. The calibrated greens-book terrain is used: hardness 0.85, blur 1 m. A spot height is skipped where it would collide with a number.
- **Aim points and green:**
  - Aim points (dogleg and lay-up) show the yards from the back tee and to the green.
  - Green depth, front and back.
- **Footer:** par disc, then a row for each tee set with name, yards and elevation change to the green, then the hole number. The designer credit and the Studio note are at the bottom right.

## Rock footprints

A `.course` file stores each rock prefab as a path, position, yaw and uniform scale. The mesh size isn't in the file. Plants can't grow inside a rock, though, so the clearance from each rock to the nearest bush, grass or tree gives an upper bound on its radius.

The estimate works like this:
1. Rocks outside fairways and greens are sampled.
2. The clearance is divided by the rock's scale and grouped by prefab family: cluster, small, tiny, stone or rock.
3. The **lower quartile** of each family is taken as that family's radius per unit scale. It is capped at 25 m and needs at least 5 samples, otherwise a default is used.

The estimates for Cassimbo are cluster 8.6 m, small 2.0, stone 1.2 and rock 0.8.

## PDF size

Merged shapes are traced on a 0.35 m grid, far finer than print can show. Two steps keep the PDF small:
- Every projected outline is simplified (Douglas–Peucker, 0.1 page units = 0.001 in) as it is added to a path (`MapProjection.AddPolyline`).
- Distance arcs are drawn only where they cross the map. Whole dashed circles would otherwise add hundreds of invisible dashes per arc.

Together these bring the 18-hole Cassimbo workbook from 39 MB down to about 6 MB, with no visible change.

## Known limits

- Par 3s under about 175 yds get no spot heights or widths. The page is mostly tee, green and arcs.
- Rock and tree footprints are estimates. A prefab that is much wider than its clearance suggests will be drawn too small.
- Hole data spreadsheets supply names only. Any yardage differences are left for the course file to settle.
