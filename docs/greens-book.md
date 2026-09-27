# Greens Book (experimental)

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

Other options: `--pdf`, `--mono`, `--no-sheet`, `--holes 1,18`, `--hardness 0.7`, `--blur 1.0`, `--credit "NAME"`.

The Windows app (`src/Pga2K25.App`) offers the same options (slope colour, per-green studies, PDF, hardness, credit) and opens the finished sheet.

## How the surface is built (and why it's experimental)

The `.course` file does **not** contain a heightmap. It holds an ordered log of brush stamps (`height[]`), which the game replays using brush shapes and post-processing that aren't in the file. `TerrainReplay` approximates this:

1. Replay `height[]` in file order. **tool 0** blends toward the stamp's absolute value, and **tool 1** adds its value. Each stamp's weight is a circular plateau with a smoothstep falloff (default `Hardness` **0.7**). Brush shape ids and rotation are ignored.
2. If the course sets `blurHeight`, apply a Gaussian blur (default σ = 1 m).
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

## Calibration plan

Your screenshot pipeline already extracts in-game spot elevations (ft/in labels) and slope-arrow classes from the designer grid. That is exactly the ground truth this model needs:

1. For a course we have as a `.course` file, capture the overhead green grid for a mix of holes, including drawn greens and generated greens.
2. Read the spot labels (the existing `--find-labels` workflow). Map each label's pixel position to world X/Z using two known points, such as the green anchor and a pin.
3. Fit the model parameters (hardness, blur, green-levelling behaviour) to minimise RMS against the spot elevations. Report the error as % of each green's range, as in the study (under 10 % trustworthy, 10–18 % indicative, over 18 % check).
4. Once generated greens fit, remove the experimental banner for those holes.
