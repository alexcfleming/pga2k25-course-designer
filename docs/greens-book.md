# Greens Book

The tool generates greens-book graphics straight from a Course Designer `.course` file.

**Default output: one PNG with every green** (`<Course> - Greens Book.png`, 4800 × 3000 px). Every green is drawn **at one scale**, the largest that lets each green fit its cell, so the greens compare by their real size; a 20 yd bar on the legend line gives the scale. Each green is shown in a 6 × 3 grid with:

- a slope-colour heat map (0–8 %+ on a dark green → yellow → red ramp, the same scale the reference Course Finisher uses)
- half-foot contours, slope arrows, pin dots, and a "N sq ft · falls X in. · median Y %" caption ("est. outline" for an estimated green)
- slope arrows re-laid on a lattice at least 24 sheet units apart, so each green carries arrows in proportion to its area and the small greens aren't crowded

**Optional outputs:**

- `--studies`: one high-resolution study PNG per green (`<Course> - Green Studies/Hole_NN.png`, 3300 × 2550 px = 11 × 8.5 in at 300 dpi). Each has the heat map, 2-inch hairline and 6-inch labelled contours, arrows, pins with ft/in spot elevations, a stats panel, a scale bar and a legend. The stats panel has two size blocks:
  - **Green size:** difference from the course mean and median (sq ft and %), the size rank among the drawn greens (1 = largest), and a strip of every drawn green with this one marked.
  - **Perimeter:** the length of the outline as drawn, against the **100–120 yd target**: on target, or how many yards short or over, with a gauge.

  Both compare against every drawn green on the course, even when `--holes` builds only some. An estimated green (no spline in the file) says it is not ranked or compared and has no perimeter block.
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

[Course Designer Studio](studio.md) builds the sheet, a study per green and the PDF, and shows them in its viewer. The other options are on the command line.

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

**Brush shapes (2026-10-03).** The replay now stamps each brush with its real shape from the designer's palette (see docs/terrain-map.md, "Terrain model"). The hardness and blur above still apply to brushes of unknown shape. With the shapes, the Cassimbo greens score 4 %, 12 %, 9 % and 6 % of range (holes 1, 7, 10, 13), against 2–10 % for the round-disc model. That model was tuned on these greens alone, and away from them it was badly wrong: 22 ft RMS on Berwick Coast's route heights, against 2 ft with the shapes.

**Flatten samples (2026-10-26).** Small flatten stamps are now read as the game's ground where they were clicked (docs/terrain-map.md, "Terrain model"). That removed a false 17.6 in crown at Sagan-Khushun green 9 pin 1, which in game sits in a gentle hollow. Cassimbo moves to 3.3, 12.6, 7.7 and 6.6 % of range, so the calibration test's limit is now 13 %.

**Sagan-Khushun green 9 capture** (`samples/calibration/sagan-khushun/`, 14 labels zeroed on the pin 1 cup, pins fit to 0.15 m). In game the green is nearly flat: everything above pin 1 is within 4 in, falling 7 in at 12 yd short and 18 in at 20 yd short. The replay scores 10.3 in RMS (49 % of a 21 in range) without flatten samples and 5.4 in (26 %) with them. Wider settings fit this green better (2.2 in with flattens up to 20 m and a 3× reach), but every setting that did took at least one Cassimbo green past 13 %, and so did heavier blur, so the defaults keep the balance. What is left on green 9 is a tilt of about ±9 in across pin 1 (west low, east high) that the game doesn't show, and a surface lumpier than the real one.

**Known limits:**

- Heights match well, but the slope *shape* only moderately agrees with the in-game grid colours (rank correlation about 0.5). The model still concentrates slope into narrower bands than the game shows, so treat band sharpness as approximate.
- Surfaces beyond the green edge (fringe and walls) are unreliable.
- Game-generated greens with no drawn spline are still not reproduced.
- The calibration covers one course. Re-run `calibrate` with captures from another course before trusting it elsewhere.

**Adding captures:** for each hole, record the pixel position of the four cups and each label's text centre with its signed inch value in a `captures.json` like the Cassimbo one, then run `pga2k25 calibrate`.
