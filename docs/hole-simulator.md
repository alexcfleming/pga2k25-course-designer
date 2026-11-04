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

## Stage 2 (this build): read-only slice, geometry only

1. **Pick.** Hole, tee and pin. Changing the hole or tee clears the read-back confirmation and results; changing the pin or player clears results.
2. **Read back.** Par, yards from each tee, aim marker, fairway widths (yardage book stations, from the back tee), green depth, front and back, pins relative to the green centre, and bunkers, scrub and water near the line. Anything listed is within 40 yds of the line of play or 12 yds of the hole's own fairway or green, measured in yards from the chosen tee along the tee-to-green line. Surfaces the model has no rule for (waste 15, unknown ids) within 60 yds of the line are listed with a choice of how they play. Every row carries its source tag. The run stays locked until you confirm the read-back.
3. **Assumptions.** Every row needs a confirm or an edit. Values confirmed in an earlier session prefill the sheet and show when they were last confirmed, but never skip confirmation. A reused placeholder stays PLACEHOLDER (orange). Values you change in the sheet are tagged PRIOR the next time. Course settings (`greenSpeed`, firmness and speeds) are shown as the file has them, with `greenSpeed` read on Alex's two-point Stimp line; the model doesn't use them yet.
4. **Run (geometry only).** Aims from 60 yds left to 60 right of the game's aim marker, every 5 yds, 2,000 drives each. Drives land with the player's carry and side spread, then roll on **flat ground** by surface until they stop; bunkers, scrub and water stop the ball. There is no slope, no elevation and no wind. Every aim uses the same random draws (paired), and the run is deterministic for a seed. Each finish is valued against the baseline:

   `SG = E(tee, distance to pin) − E(finish lie, distance to pin) − 1 − penalty`

   Distance is straight-line to the pin, in yards (feet on the green). Results show the share of finishes by surface, yards left, SG with a 95% band, expected strokes for the hole (E(tee) − SG) and the share of finishes outside the baseline's rows.

### Save watcher

The rest of Studio only notices a new course save after backing out to the menu. This section watches the file while it is open: every 2 s it checks the file's time and size, and when a change has held still across two checks, it hashes the file and compares it with what's loaded. A new save is read in the background and compared with the current hole. The banner lists what changed on this hole: tees, pins, aim marker, widths, hazards, surface areas and height brushes near the line. Nothing reloads or runs until you click **Reload this hole**. Reloading keeps confirmed assumptions, asks for the read-back again when surfaces, tees or pins changed, and keeps a one-line summary of the previous results. **Not now** sets the save aside until the file changes again. A waiting save blocks the run.

This deliberately replaces the continuation prompt's "no file watcher" rule, at Alex's request (4 Nov 2026). It still never runs anything by itself.

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
```

The CLI uses the shipped defaults as they are (most are PLACEHOLDER). The Studio section is where they get confirmed.

## Fixtures

- Cassimbo hole 3 (`samples/courses/cassimbo.course`): tees 499 / 512 / 531 / 537, 4 tees and 4 pins, aim point 267 from the back tee, as in the proof of concept.
- Pilolcura Analysis hole 8 (`samples/private/pilolcura-analysis-2026-11-02.course`, git-ignored): tees 460 / 483 / 500 / 515, aim marker 286, widths 78 / 79 / 71 / 68 at 250 / 300 / 325 / 350, green 38 deep, a bunker right at about 298–328. The waste (15) and id-16 brushes sit 60+ yds off the line, so none are listed.

## Not yet

Stage 3: in-game height readings and the terrain gate, then slope roll, elevation and sightlines. Stage 4: approaches, the approach map by pin, design experiments (paired edits of the hole), in-game verdicts and the session record. Stage 5: wind, the par 5 lay-up decision and session comparison.
