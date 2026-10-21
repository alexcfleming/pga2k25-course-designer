# Publish readiness

Checks for getting a course ready to publish and submit for TGC Tours review, from Alex's course QA checklist and design principles. The principles are a personal collection, not rules, so every limit can be changed per course.

The work splits three ways:

- **From the course file:** everything below. It reruns whenever the file changes.
- **Partly from the file:** green speed and fringe work from the file now, mapped from in-game readings. Trees in the play area and playtest coverage come later.
- **Judgement calls:** waypoints and default club, whether every route works, sand and surface edges, realism, cameras, concept drift, and the per-hole and planting passes. These will be tracked in Studio (phase 2) with a status (Open, Done, Deliberate, N/A) and notes.

The library is `src/Pga2K25.Readiness`; the Studio's **Publish readiness** section uses it (see [studio.md](studio.md)), and so does the `readiness` command. The mockup it was built from is at https://claude.ai/artifact/S4JeBUYFRV3ZBmnvWWs9J3.

## Saved progress, import and the record

- **`Readiness.json`** beside the products holds everything the designer decides:
  - item statuses (Open, Done, Deliberate, N/A) and notes
  - deliberate marks on course-file checks, for the whole check (`green-scale`) or one hole (`green-scale@2`), each with its reason
  - pin difficulties and playtest coverage
  - the course's limits, added checks, designer, version and tee names
  - the sign-off with the course file's fingerprint (the first 16 hex digits of its SHA-256)
- **Item keys** are the HTML checklist's own (`pre.score`, `b.fringe`, `hc.3.sightTee`, `tr.12.aim`, `pl.5.g360`), so its backup imports straight across (`ChecklistImport`). An import fills only what's still open or empty and never undoes a decision. "Size is deliberate" on a hole becomes deliberate marks on that hole's green size and landing width. Typed pars, yardages, green areas and widths are left out, because the file supplies them. The lighting values typed into the checklist are kept for the record.
- **Items the file answers** (for example the scorecard, Section 7, scale, variety, corridors, speed, fringe, elevation and green relief) count as done while their checks pass or are marked deliberate, unless the designer sets them otherwise.
- **The gate** opens when every course-file check is cleared, every item, hole cell, pin difficulty and playtest square is closed, and the sign-off is complete and matches the current file.
- **The record** (`ReadinessRecord`) is a dated JSON of the report and state, plus a printable HTML page of the same. It's written when the work on a course is done, replacing the HTML checklist's final export.

## Command

```bash
pga2k25 readiness <file.course> --drive 360 --holes
```

| Option | Default | Meaning |
|---|---|---|
| `--drive` | none | The test drive measured with the power profile (yds). Without it the drivable and par-5 checks ask for one. |
| `--limits` | defaults | A JSON file overriding any `ReadinessLimits` value, e.g. `{"Par5MinLeftYards": 230, "CorridorYards": 30}` |
| `--holes` | off | Adds the per-hole table: drive class, green size and landing width against the median, tee-to-green change, green relief, nearest other green, pins |
| `--json` | off | Writes the whole report |

The exit code is 1 when any check is High (hole design and variety, which the tour feedback weighed most), otherwise 0.

## The checks

Yardages are from the back tees. Severity, mildest first: **Pass**, **Info** (recorded only), **Confirm** (needs a reading or a figure the file can't give), **Flag** (fix it or mark it deliberate), **High**.

| Id | Check | Limit (default) | Source |
|---|---|---|---|
| `par` | 18 holes; par, OUT and IN | | Publish gate |
| `tee-totals` | Every tee set's total | 6,500–7,500 yds | §2 |
| `same-par` | Consecutive holes sharing a par, judged by run length, yardage spread and whether the run opens the round | narrow under 40 yds, safe from 60 | §7 |
| `drivable-front`, `drivable-back` | Par 4s within 20 yds (drivable) or 50 yds (near) of the test drive, per nine | at most 2 | §11.3 A |
| `par5` | Yards a par 5 leaves after the test drive | at least 220 | §11.3 A |
| `green-scale` | Drawn green area against the course median; estimated greens are left out | 0.6–1.6× | §11.3 B |
| `landing-width` | Fairway width across the line, median over the 40 yds up to the landing point (test drive or first aim point); also notes tee shots that land in the rough | 0.6–1.6× | §11.3 B |
| `elevation` | Back tee to green site | 50 ft down, 30 up | §3 |
| `plot-range` | Ground across tees, aim points and greens | 50–70 ft | §3 |
| `green-relief` | Highest minus lowest point on each drawn green (greens book analysis) | 1.5–3 ft | §3 |
| `corridor` | Nearest other green, or practice green, to each line of play. Crossing one is High | 25 yds | §11.3 A |
| `aim-spacing` | Aim points of two holes | 35 yds apart | §11.3 A |
| `own-green` | Each route ends on a drawn green of its own; a shared green is flagged (fine for a double green) | | §11.3 F |
| `green-speed` | `greenSpeed` as Stimp | 8–13 | Build checks |
| `fringe` | `greenFringeRadius` | 0.5 or less | §5 |
| `lighting` | Time of day, sun, cloud layers, fog, haze, rain as recorded | | Lighting section |

Elevation, plot range and green relief come from the replayed terrain, so they say "confirm in game". Rounded figures are judged as printed: a green shown at 3.0 ft isn't flagged as over 3.

**Green speed.** Two in-game readings set the scale: Cassimbo's 0.97 shows Stimp 10.9 and Sagan-Khushun's 0.905 shows 10.1. The line through them is used, `Stimp = 10.9 + (setting − 0.97) × 12.31`. A third reading would show whether it's straight. −1 is the game's default and asks for a reading.

**Fringe.** Cassimbo and Sagan-Khushun are both set to 0.5 and show no visible fringe in game. Other files have 0.54–0.63, and −1 is the game's default.

**Pins.** Each pin is placed by thirds of its green along the approach (F, C, B) and across it (L, C, R, as the golfer faces the green).

## The Cassimbo test

Cassimbo wasn't approved for TGC Tours. With a 360-yd test drive (about what a scheduler hit), the file alone shows:

- **Hole 3** is a very short par 5: it leaves 177 yds. So does hole 18, which leaves 188.
- **Front nine:** a third drivable par 4 (holes 1, 4 and 8). The back nine has four drivable or near-drivable par 4s.
- **Green sizes:** green 2 is 0.30× the median and green 7 is 2.35×. The review said greens were "wildly different", 2 against 7.
- **Hole 12:** its line passes 22 yds from hole 10's green. The review said hole 12 is played over another green, so the corridor check measures distance to a green rather than only crossing one.
- **Hole 7** climbs 52 ft from tee to green.

`ReadinessTests` holds these as a regression.
