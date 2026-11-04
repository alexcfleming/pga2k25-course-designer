# Hole design simulator

A test session for **one hole at a time**, on demand, with the game open in playtest. It reads the hole from the `.course` file, asks you to confirm every assumption, plays thousands of tee shots across a range of quality, and values each finish in strokes gained. The aim is to help decide what to test or change on the hole. It doesn't score the design and doesn't predict a reviewer's verdict.

Proposal and mockup: https://claude.ai/artifact/KUGRHi71TAL55uNvtpPUXm. Pilot hole: Pilolcura Analysis hole 8.

## Where it lives

| Piece | What |
|---|---|
| `src/Pga2K25.HoleSim` | The engine: hole model, surface grid, change list, save watcher, baselines, assumption sheet, geometry run, map renderer |
| Studio, "Hole design simulator" | The step flow (`HoleSimViewModel`, `Views/HoleSimView.axaml`) |
| CLI `holesim` | The same engine for checks and fixtures. One hole per run; there is no batch or 18-hole mode |
| `assets/holesim/assumptions.json` | Assumption defaults, each with its source tag and date (embedded in the library) |
| `assets/holesim/baselines/*.csv` | Strokes-gained baselines with provenance columns (embedded) |

The app stays analysis-only: the simulator reads course files and never writes them.

## The session, step by step

Stage 2 built steps 1–3 and a flat-ground run; Stage 3 added live checks, the terrain gate, and slope and elevation in the run.

1. **Pick.** Hole, tee and pin. Changing the hole or tee clears the read-back confirmation and results; changing the pin or player clears results.
2. **Read back.** Par, yards from each tee, aim marker, fairway widths (yardage book stations, from the back tee), green depth, front and back, pins relative to the green centre, and bunkers, scrub and water near the line. Anything listed is within 40 yds of the line of play or 12 yds of the hole's own fairway or green, measured in yards from the chosen tee along the tee-to-green line. Surfaces the model has no rule for (waste 15, unknown ids) within 60 yds of the line are listed with a choice of how they play. Every row carries its source tag. The run stays locked until you confirm the read-back.
3. **Assumptions.** Every row needs a confirm or an edit. Values confirmed in an earlier session prefill the sheet and show when they were last confirmed, but never skip confirmation. A reused placeholder stays PLACEHOLDER (orange). Values you change in the sheet are tagged PRIOR the next time. Course settings (`greenSpeed`, firmness and speeds) are shown as the file has them, with `greenSpeed` read on Alex's two-point Stimp line; the model doesn't use them yet.
4. **Live checks.** Suggested MEASURE spots along the line (about eight: every 50 yds on a long hole, closer on a par 3, alternating 10 yds either side of the line) plus the green's front and back, shown on a map. You pick the zero (green centre or a pin) and type each label as the game shows it. `+1'4"` is 16 in, `+1'` is 12 in, a missing sign reads as above, inches of 12 or more are refused, and every value is said back. The model's height for a spot appears only after its reading is in, so it can't steer what you type. Every fourth spot is held back to test a fitted correction; tick or untick to change that. The step also asks how much of the fairway at 300–350 (on a par 3, the green) you can see from the tee: All, Most, Some or None. You can enter up to five drives too: carry, total, and finish side by eye, since playtest shows only carry and total. Their mean and spread are set against the assumed carry. Everything saves as you type to `Hole Sim/HNN live checks.json` beside the products, in world coordinates, so another tee reads the same spots.
5. **Terrain gate.** Each candidate model is compared with your readings, with fit bands of Trustworthy under 10% of the readings' range, Indicative 10–18%, and Check over 18% or with fewer than five readings. The zero counts toward the range. A corrected model is judged only on the readings held back from its fit. Until you pick a candidate, the gate picks the best band, then the lowest error. A pass allows **Use the terrain**. A Check offers **Take more readings**, **Geometry only** or **Override: terrain unverified**, which puts a label on every result.
6. **Run.** Aims from 60 yds left to 60 right of the game's aim marker, every 5 yds, 2,000 drives each. Drives land with the player's carry and side spread, then roll by surface until they stop; bunkers, scrub and water stop the ball. On flat ground by default. With the terrain on, carry changes for height (PLACEHOLDER 1 yd per 3 ft the landing sits below the tee), and the ball rolls downhill under gravity. It stops when it is slow and on ground flatter than its friction, the proof of concept's rule. There is no wind yet. Every aim uses the same random draws (paired), and the run is deterministic for a seed. Each finish is valued against the baseline:

   `SG = E(tee, distance to pin) − E(finish lie, distance to pin) − 1 − penalty`

   Distance is straight-line to the pin, in yards (feet on the green). Results show the share of finishes by surface, yards left, SG with a 95% band, expected strokes for the hole (E(tee) − SG) and the share of finishes outside the baseline's rows.

### Save watcher

The rest of Studio only notices a new course save after backing out to the menu. This section watches the file while it is open: every 2 s it checks the file's time and size, and when a change has held still across two checks, it hashes the file and compares it with what's loaded. A new save is read in the background and compared with the current hole. The banner lists what changed on this hole: tees, pins, aim marker, widths, hazards, surface areas and height brushes near the line. Nothing reloads or runs until you click **Reload this hole**. Reloading keeps confirmed assumptions, asks for the read-back again when surfaces, tees or pins changed, and keeps a one-line summary of the previous results. **Not now** sets the save aside until the file changes again. A waiting save blocks the run.

This deliberately replaces the continuation prompt's "no file watcher" rule, at Alex's request (4 Nov 2026). It still never runs anything by itself.

## Terrain

`ITerrainModel` gives ground height in feet relative to the green centre, and slope, in the hole frame. The candidates are:

| Id | Model |
|---|---|
| `replay` | `TerrainReplay` with the books' settings (brush shapes, flatten samples, plot-edge rule), 1 m cells |
| `smoothed-8m` | The replay with an 8 m Gaussian outside the greens, blended back to the replay on the greens and within 6 yds of them |
| `…+fit` | Either of those plus a correction fitted to this session's readings: Gaussian-weighted residuals, 25 yd length, shrunk toward zero away from the readings |

The proof of concept's other idea, falloff fitted by brush `type`, is already covered: the replay stamps each brush type's real shape. `TerrainReplay` itself is unchanged, so the books don't move.

### Terrain study (Sangoma, 4 Nov 2026)

`pga2k25 holesim-terrain samples/private/sangoma.course --holes 11,3,4` compares the candidates with Sangoma's 2,290 distance markers, which carry the game's own ground height. The fitted rows simulate a routine session: a correction fitted to the markers nearest the suggested spots (7–10 of them), then tested on every other marker near the line.

| Hole | Candidate | RMS | Share of range | Band | Fairway slope p99 |
|---|---|---|---|---|---|
| 11 (par 3) | replay | 20.8 in | 26% | Check | 41% |
| | smoothed 8 m | 16.9 in | 21% | Check | 25% |
| | smoothed + fit (7) | 16.8 in | 21% | Check | 25% |
| 3 (par 4) | replay | 69.4 in | 64% | Check | 41% |
| | smoothed 8 m | 65.5 in | 61% | Check | 13% |
| | smoothed + fit (9) | 41.5 in | 38% | Check | 15% |
| 4 (par 5) | replay | 38.6 in | 21% | Check | 33% |
| | smoothed 8 m | 27.2 in | 15% | Indicative | 29% |
| | smoothed + fit (10) | 14.4 in | 8% | **Trustworthy** | 29% |

What it says:
- Smoothing never hurt and always calmed the fairway slopes. That suits a ball model whose roll depends on slope.
- A correction from about ten readings helped on the two longer holes. A 25 yd length did best of 25, 40, 80 and 150 yds. A 16 m smoothing was no better than 8 m.
- Sangoma's replay is far off on holes 3 and 11 (its ground was built with big soft brushes; the replay is known to be about 2.3 m RMS on its markers), and nothing passes there. The gate holds those holes to geometry only, which is the intended behaviour.
- With no in-game heights on Pilolcura yet, its gate result has to come from Alex's readings.

The study takes about a minute: most of it is tracing Sangoma's water once per course, which the simulator and Studio do only once per course.

## Strokes gained

The method follows Broadie, as set out in the Strokes Gained method prompt (4 Nov 2026):

- Positive = strokes gained against the baseline, negative = lost. Every result states the baseline id.
- Baselines are CSVs with provenance columns (`baseline_id, lie, distance, unit, expected, source, source_type, retrieved`). Values are interpolated linearly between published points. A distance outside a lie's rows is flagged (`BelowTable` / `AboveTable`) and valued at the nearest row, never extrapolated. Local reversals are reported as anomalies, not smoothed.
- Lies: tee, fairway, rough, sand, recovery, green. Yards off the green, feet on it. A table that mixes baselines or units is rejected.
- The shipped baseline, `pga-tour-anchors`, holds only the 23 anchors the prompt lists, quoted from Broadie by **secondary sources**. It is sparse and labelled PLACEHOLDER in the assumption sheet. Replace it with the full table from *Every Shot Counts* or Broadie (2012) when supplied; don't fill gaps from memory or scale it into a handicap table.
- How the simulator's grounds score is an assumption: native and heavy rough as rough, scrub as recovery, water as rough plus one penalty stroke (all PLACEHOLDER).

Tests cover the prompt's acceptance tests A (four-shot birdie, +0.65), B (OB, −2.00) and C (interpolation and coverage), plus the telescoping invariant. Tests D–G concern imported round data, which the simulator doesn't use.

## CLI

```
pga2k25 holesim <file.course> --hole 8 [--tee T4|4] [--pin 1] [--player power|normal]
                [--samples 2000] [--seed 7] [--treat 15=native,16=bunker] [--png map.png] [--json run.json]
pga2k25 holesim-terrain <file.course> --holes 11,3,4 [--smooth 8] [--readings 10] [--fit-length 25]
```

`holesim` always runs on flat ground (the gate needs in-game readings, which are a Studio step) and uses the shipped defaults as they are, most of them PLACEHOLDER. The Studio section is where they get confirmed. `holesim-terrain` is the terrain study, for courses whose distance markers carry the game's heights.

## Fixtures

- Cassimbo hole 3 (`samples/courses/cassimbo.course`): tees 499 / 512 / 531 / 537, 4 tees and 4 pins, aim point 267 from the back tee, as in the proof of concept.
- Pilolcura Analysis hole 8 (`samples/private/pilolcura-analysis-2026-11-02.course`, git-ignored): tees 460 / 483 / 500 / 515, aim marker 286, widths 78 / 79 / 71 / 68 at 250 / 300 / 325 / 350, green 38 deep, a bunker right at about 298–328. The waste (15) and id-16 brushes sit 60+ yds off the line, so none are listed.

## Not yet

Sightlines from the tee (the visibility map) wait on a trusted terrain and the sightline answers collected in step 4. Stage 4: approaches, the approach map by pin, design experiments (paired edits of the hole), in-game verdicts and the session record. Stage 5: wind, the par 5 lay-up decision and session comparison.
