# Greens Book

The tool generates greens-book graphics straight from a Course Designer `.course` file.

**Default output: one PNG with every green** (`<Course> - Greens Book.png`, 4800 × 3000 px). Each green is shown in a 6 × 3 grid with:

- a slope-colour heat map (0–8 %+ on a dark green → yellow → red ramp, the same scale the reference Course Finisher uses)
- half-foot contours, slope arrows, pin dots, and a "falls X in. · median Y %" caption

**Optional outputs:**

- `--studies`: one high-resolution study PNG per green (`<Course> - Green Studies/Hole_NN.png`, 3300 × 2550 px = 11 × 8.5 in at 300 dpi). Each has the heat map, 2-inch hairline and 6-inch labelled contours, arrows, pins with ft/in spot elevations, a stats panel, a scale bar and a legend.
- `--pdf`: the sheet as page 1, followed by one study page per green.
- `--mono`: black-and-white linework only (the original plate style, no colour).

Each green is oriented so the approach comes from the bottom. The linework follows the green surface study's plate conventions (`reference/green_surface_study_continuation.md`):

- ink `#1f1d1a`, grey `#6b665e`, Lora typeface (falls back to Georgia)
- one solid arrow shape, with steep (> 3.3°) drawn heavier than gentle
- line-only contours and the credit line at top right
- on colour, arrows and labels get white halos so they stay legible
- a single world→page transform shared by every renderer, so the y-flip bug can't recur

## Running it

```bash
dotnet run --project src/Pga2K25.Cli -- greensbook "path/to/course.course" -o out --studies
```

Other options: `--pdf`, `--mono`, `--no-sheet`, `--holes 1,18`, `--hardness 0.85`, `--blur 1.0`, `--credit "NAME"`.

The Windows app (`src/Pga2K25.App`) offers the same options (slope colour, per-green studies, PDF, hardness, credit) and opens the finished sheet.

## How the surface is built (and why it's experimental)

The `.course` file does **not** contain a heightmap. It holds an ordered log of brush stamps (`height[]`), which the game replays using brush shapes and post-processing that aren't in the file. `TerrainReplay` approximates this:

1. Replay `height[]` in file order. **tool 0** blends toward the stamp's absolute value, and **tool 1** adds its value. Each stamp's weight is a circular plateau with a smoothstep falloff (default `Hardness` **0.85**, calibrated; see below). Brush shape ids and rotation are ignored.
2. If the course sets `blurHeight`, apply a Gaussian blur (σ = 1 m, calibrated).
3. Sample a 0.25 m raster around the green. Statistics skip the outer 1 m (collar roll-off). Compute gradients (arrows), marching-squares contours, and spot elevations relative to the green anchor (0).

### Green outlines

- **Drawn greens** (a closed surface-1 spline containing the green anchor or a pin) use that spline, lightly Fourier-smoothed.
- **Game-generated greens** have no outline in the file. The game builds them from `greenRadius`/`greenPoints`/`greenVariance`/seed. We draw an approach-aligned ellipse (15 × 12 m, grown so every pin sits 3 m inside), shown **dashed**.

In the three samples only 5 of 54 greens are drawn splines.

### Cassimbo (hand-built, all 18 greens drawn)

A hardness sweep (0 → 0.85) and a blur sweep (1 → 3 m) settled on hardness 0.7 with 1 m blur. At that setting, median green slopes are 1.4–3.6 % on 17 of 18 greens (hole 9 is 6.2 %), and 11 of 18 greens stay under a 12 % 95th percentile. More blur made things *worse*, because it smears the terrain walls outside the green into the putting surface.

Remaining artifacts:

- **Edge walls** on holes 1, 3, 6, 7 and 9, where the falloff of very large base stamps (300–670 m across) crosses the green.
- **Narrow red bands**, which may be the falloff rings of small stamps under the plateau model rather than real tiers. Some are probably real, e.g. hole 10's two-tier green with a 6-inch step.

Only in-game ground truth can separate the two.

### What the samples show

| Green type | Result |
|---|---|
| Drawn spline greens (Shinnecock 1, 18; Jungle Plot 9, 18) | Plausible: falls of 15–18 in., median slope 1.0–1.6 %, 95th percentile 3–6 % |
| Drawn spline green (Delta Test 13) | Borderline: falls 52 in., 95th percentile 15 % |
| Game-generated greens (most holes) | **Not plausible**: the replay shows the surrounding hillside (falls of feet to tens of feet). The game clearly levels generated greens (`flattenGreens: true`) in a way we can't reproduce yet |

Pages whose 95th-percentile slope exceeds 12 % carry an *"Implausibly steep"* warning. Every page is marked *"EXPERIMENTAL — terrain model unvalidated"*.

## Calibration

**Data:** in-game overhead green screenshots of Cassimbo holes 1, 7, 10 and 13, each with manually placed measure labels. They are stored in `samples/calibration/cassimbo/` (the images plus `captures.json`).

**Pixel → world mapping.**

- The four flagstick cups (the dark dot at each pole base) are matched to the course file's four pins.
- A similarity transform is fitted with the screen image mirrored (image y points down). Every pin↔cup assignment is tried and the best is kept.
- Pins fit to within 6–30 cm on every hole, so a label position maps onto the course to within a few tens of centimetres.
- The circle marker in screenshots is **not** the green anchor: it sits 5–8 m away, consistent with it being a game artifact.

**Scoring.**

- Heights are compared offset-free, because the in-game 0 is arbitrary.
- Error is reported as RMS in inches and as % of that green's label range: under 10 % trustworthy, 10–18 % indicative, over 18 % check.
- Labels less than 1 m inside the green edge, or outside it (several "0" references were placed on the fringe), are reported but not scored. This matches the book's own edge exclusion.

**Grid colour.** In-game grid lines are coloured by slope *along the line*. Along-line slope correlates better with line hue than slope magnitude does, and across-line slope correlates poorly. The line hue was used as a second, slope-shape check.

**Result** (hardness 0.85, blur 1 m; run `pga2k25 calibrate samples/courses/cassimbo.course samples/calibration/cassimbo/captures.json`):

| Hole | Pin fit | Spot RMS | % of range |
|---|---|---|---|
| 1 | 0.06 m | 0.4 in | 2 % |
| 7 | 0.30 m | 2.2 in | 8 % |
| 10 | 0.08 m | 0.4 in | 10 % (range only 4 in) |
| 13 | 0.12 m | 0.6 in | 5 % |

**Model variants tried and rejected:**

- softer plateaus (hardness 0–0.7), which gave edge walls and 18–360 % errors
- heavier blur (1.5–2 m), which smeared walls into the green
- reduced brush radius
- Gaussian falloff
- a falloff width that grows sub-linearly with brush size (√r, fixed minimum, power laws)

All of these were worse on heights, slopes, or both.

**Known limits:**

- Heights match well, but the slope *shape* only moderately agrees with the in-game grid colours (rank correlation about 0.5). The model still concentrates slope into narrower bands than the game shows, so treat band sharpness as approximate.
- Surfaces beyond the green edge (fringe and walls) are unreliable.
- Game-generated greens with no drawn spline are still not reproduced.
- The calibration covers one course. Re-run `calibrate` with captures from another course before trusting it elsewhere.

**Adding captures:** for each hole, record the pixel position of the four cups and each label's text centre with its signed inch value in a `captures.json` like the Cassimbo one, then run `pga2k25 calibrate`.
