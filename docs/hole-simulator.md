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
4. **Live checks.** Suggested MEASURE spots along the line (about eight: every 50 yds on a long hole, closer on a par 3, alternating 10 yds either side of the line) plus the green's front and back, shown on a map. The game's MEASURE readout gives the cursor's yards TO TEE (each tee) and TO GREEN (the route's green point) and its height against each tee, so each spot lists its yards to the chosen tee and to the green. Move the cursor until both match. The zero defaults to the chosen tee, which is what the readout shows; the green centre or a pin can be chosen instead. Spot **G** is the green itself: copy GREEN HEIGHT for the chosen tee from the game's tee table, no cursor needed. Type each label as the game shows it: ▲ or + is above, ▼ or − below, `+1'4"` is 16 in, `+1'` is 12 in, a missing sign reads as above, inches of 12 or more are refused, and every value is said back. The model's height for a spot, against the same zero, appears only after its reading is in, so it can't steer what you type. Every fourth spot is held back to test a fitted correction; tick or untick to change that. The step also asks how much of the fairway at 300–350 (on a par 3, the green) you can see from the tee: All, Most, Some or None. You can enter up to five drives too: carry, total, and finish side by eye, since playtest shows only carry and total. Their mean and spread are set against the assumed carry. Everything saves as you type to `Hole Sim/HNN live checks.json` beside the products, in world coordinates, so another tee reads the same spots.
5. **Terrain gate.** Each candidate model is compared with your readings, with fit bands of Trustworthy under 10% of the readings' range, Indicative 10–18%, and Check over 18% or with fewer than five readings. The zero counts toward the range. A corrected model is judged only on the readings held back from its fit. Until you pick a candidate, the gate picks the best band, then the lowest error. A pass allows **Use the terrain**. A Check offers **Take more readings**, **Geometry only** or **Override: terrain unverified**, which puts a label on every result.
6. **Run.** Aims from 60 yds left to 60 right of the game's aim marker, every 5 yds, 2,000 drives each. Drives land with the player's carry and side spread, then roll by surface until they stop; bunkers, scrub and water stop the ball. On flat ground by default. With the terrain on, carry changes for height (PLACEHOLDER 1 yd per 3 ft the landing sits below the tee), and the ball rolls downhill under gravity. It stops when it is slow and on ground flatter than its friction, the proof of concept's rule. There is no wind yet. Every aim uses the same random draws (paired), and the run is deterministic for a seed. Each finish is valued against the baseline:

   `SG = E(tee, distance to pin) − E(finish lie, distance to pin) − 1 − penalty`

   Distance is straight-line to the pin, in yards (feet on the green). Results show the share of finishes by surface, yards left, SG with a 95% band, expected strokes for the hole (E(tee) − SG) and the share of finishes outside the baseline's rows.

7. **Green read** (Stage 4a). Expected putts from every spot on the hole's green to the chosen pin, drawn over the green with arrows pointing downhill. Cells too steep for a stopped ball to stay put are hatched. For each pin it shows:
   - the average putts from the whole green
   - the share of the green that needs 2.3 putts or more
   - from 15–30 ft, putts from above the hole against putts from below
   - the slope at the cup, in red when a ball can't stop there

   For the green as a whole it shows its size, Stimp, typical and steepest slope, and how much of it can't hold a stopped ball. Maps build in the background when a pin is opened (a few seconds each), or all at once with **Build every pin**. They are kept until the green, its speed or the putting assumptions change.

   On a par 3 the driver sweep in step 6 is switched off with a note; par 3 tee shots are played in step 8.
8. **Approach** (Stage 4b). Shots at the green, 600 of them, from a spot you pick to an aim you pick:
   - **From**: yards to the pin and yards right or left of the line of play; on a par 4 or 5, where the best line's drives typically finish (the median of its fairway finishes, once step 6 has run); on a par 3, the tee.
   - **Aim**: the best aim (25 tried on a 3-yd grid round the pin, the same shots for each), the pin, the green's centre, an aim distance straight at the pin, or a point so many yards right or left and beyond or short of the pin.
   - **Spreads**: 1 is the golfer fitted to the anchors; 1.5 tries the hole for a weaker player.

   It shows where the shots finish (on the green or not), the share that hold the green, their average distance from the hole, where the misses go, bunker, water and scrub, the expected strokes to hole out and the strokes gained for the shot against the baseline from that lie and distance, with the best aims when it searched. A card underneath shows the fitted golfer against every anchor.

### Save watcher

The rest of Studio only notices a new course save after backing out to the menu. This section watches the file while it is open: every 2 s it checks the file's time and size, and when a change has held still across two checks, it hashes the file and compares it with what's loaded. A new save is read in the background and compared with the current hole. The banner lists what changed on this hole: tees, pins, aim marker, widths, hazards, surface areas and height brushes near the line. Nothing reloads or runs until you click **Reload this hole**. Reloading keeps confirmed assumptions, asks for the read-back again when surfaces, tees or pins changed, and keeps a one-line summary of the previous results. **Not now** sets the save aside until the file changes again. A waiting save blocks the run.

This deliberately replaces the continuation prompt's "no file watcher" rule, at Alex's request (4 Nov 2026). It still never runs anything by itself.

## Terrain

`ITerrainModel` gives ground height in feet relative to the green centre, and slope, in the hole frame. The candidates are:

| Id | Model |
|---|---|
| `replay` | `TerrainReplay` with the books' settings (brush shapes, flatten samples, plot-edge rule), 1 m cells |
| `smoothed-8m` | The replay with an 8 m Gaussian outside the greens and tees, blended back to the replay on the greens (fading over 6 yds) and round every tee (within 8 yds, fading by 16). Tee boxes are small, sharp flats and readings are taken against a tee; smoothed tees put the Pilolcura 8 green 6'2" above T4 instead of the game's 5'7" |
| `…+fit` | Either of those plus a correction fitted to this session's readings: Gaussian-weighted residuals, 25 yd length, shrunk toward zero away from the readings |

The proof of concept's other idea, falloff fitted by brush `type`, is already covered: the replay stamps each brush type's real shape. `TerrainReplay` itself is unchanged, so the books don't move.

### Terrain study (Sangoma, 4 Nov 2026)

`pga2k25 holesim-terrain samples/private/sangoma.course --holes 11,3,4` compares the candidates with Sangoma's 2,290 distance markers, which carry the game's own ground height. The fitted rows simulate a routine session: a correction fitted to the markers nearest the suggested spots (7–10 of them), then tested on every other marker near the line.

| Hole | Candidate | RMS | Share of range | Band | Fairway slope p99 |
|---|---|---|---|---|---|
| 11 (par 3) | replay | 20.8 in | 26% | Check | 41% |
| | smoothed 8 m | 16.9 in | 21% | Check | 32% |
| | smoothed + fit (7) | 16.8 in | 21% | Check | 32% |
| 3 (par 4) | replay | 69.4 in | 64% | Check | 41% |
| | smoothed 8 m | 65.5 in | 61% | Check | 15% |
| | smoothed + fit (10) | 43.8 in | 41% | Check | 16% |
| 4 (par 5) | replay | 38.6 in | 21% | Check | 33% |
| | smoothed 8 m | 26.9 in | 15% | Indicative | 29% |
| | smoothed + fit (10) | 14.5 in | 8% | **Trustworthy** | 29% |

What it says:
- Smoothing never hurt and always calmed the fairway slopes. That suits a ball model whose roll depends on slope.
- A correction from about ten readings helped on the two longer holes. A 25 yd length did best of 25, 40, 80 and 150 yds. A 16 m smoothing was no better than 8 m.
- Sangoma's replay is far off on holes 3 and 11 (its ground was built with big soft brushes; the replay is known to be about 2.3 m RMS on its markers), and nothing passes there. The gate holds those holes to geometry only, which is the intended behaviour.
- Pilolcura hole 8, from Alex's first MEASURE screen (4 Nov 2026): the game's TEE HEIGHT for the four tees (67'4", 67'7", 67'7", 67'6") matches the replay's absolute heights to about an inch, so the game and the model share one datum. GREEN HEIGHT against each tee (▲5'9", ▲5'6", ▲5'6", ▲5'7") matches the read-back's tee elevations to about an inch. The cursor in the same screen sat 179/201/217/232 yds from the four tees, which fixes it about 230 out and 34 left of the line (to 0.1 yd). It read ▲0'7" against Blue: the smoothed model says +9 in there, the plain replay +27 in. `HoleSimTerrainTests` keeps these as a check.

The study takes about a minute: most of it is tracing Sangoma's water once per course, which the simulator and Studio do only once per course.

## Greens (Stage 4a)

- **Green roll.** The rolling friction comes from the Stimp reading (`greenSpeed` on Alex's two-point line): a ball released at the Stimpmeter's 1.83 m/s rolls the Stimp distance, so Stimp 10.3 gives 0.054. The same friction is used for drives that reach a green; the old green friction row is now bounce only (`ground.greenBounce`). How the game's greens roll isn't known, so this is a PLACEHOLDER row.
- **The green.** The green cells are the green surface connected to the green point, from the hole's grid. Heights and slopes are sampled once onto a half-yard raster (`GreenSurface`), so putts can roll thousands of times quickly.
- **Putting model** (`PuttingModel`):
  - Putts roll with that friction and gravity down the slope. The hole (4.25 in) drops a ball passing over it slower than 1.3 m/s, and a stopped ball stays put on slopes up to 1.15 × the friction.
  - The golfer reads each putt. For each pace from very soft to firm, it finds the middle of the aims that hole a perfect stroke, then picks the pace and aim that do best under the golfer's own errors, using the same random draws for each.
  - Pace error scales with the flat-green speed for the distance, because feel is for distance. So a soft downhill putt carries a larger share of error.
  - A miss costs one stroke plus the expected putts from where it stops, on a flat-green curve.
- **Fitted to the anchors.** The aim and pace errors are fitted, by a coarse then fine search on a flat green, to the baseline's own green rows, never to values interpolated between them. On the shipped anchors that is 1.04 at 3 ft and 1.62 at 10 ft, against 1.04 and 1.61, with an aim error of 1.4° and a pace error of 10%. The contours then decide every other spot.
- **The green gate.** Live checks suggest two or three readings per pin: about 4 yds above the pin and 4 below on its fall line, plus one across the slope, held back to test the fit. The readings are compared against G, the green spot read from the tee table, so the tee's height drops out. The candidates are:
  - the replay's contours
  - the contours scaled by k, fitted to the readings by least squares and faded out over 3 yds of fringe so the green edge gets no false step (the game may smooth drawn greens)
  - a flat green

  It uses the same bands as the fairway gate, judged on at least 4 readings and at least 3 in of relief. A green rated Check still gets its putting maps, labelled "contours unverified".

Prototype figures on Pilolcura (the replay contours, before any green readings):

| Hole | Green | Slope typical / steepest tenth | Too steep to hold | Pins |
|---|---|---|---|---|
| 8 (par 4) | 577 sq yd | 2.6% / 4.7% | 1% | average putts 2.16, 2.19, 2.24, 2.75; pin 4 sits on the falling front left |
| 12 (par 3) | 271 sq yd | 4.0% / 13.1% | 30% | pin 4's cup slope is 14.1%, steeper than a ball can stop on |

Hole 12 is the one to read in the game: either the green really is that steep, or the game smooths drawn greens and the scaled candidate will show by how much.

`pga2k25 holesim <file.course> --hole 8 --green [--green-png file.png --pin 4]` prints the same figures and draws a pin's map.

## Approach shots (Stage 4b)

- **The shot.** The golfer aims at a point and works out the plays-like distance, adding 1 yd for every 3 ft the target sits above the ball. It plans a release (5% of the shot, between 1 and 14 yds; chips release more: a 20-yd chip a quarter of its length) and carries the ball short of the aim by that much.
  - The carry spreads long and short, and side to side, by a share of the shot's length that grows past 150 yds, plus a small spread every shot has. Rough, sand, scrub and the tee multiply it.
  - The ball lands at the speed that would roll the planned release on a flat green, then bounces and rolls on the ground under it: on the green's contours, with the green's Stimp friction; elsewhere on flat ground (or the gated terrain) with each surface's friction. A ball landing into a slope holds; one landing on a downslope runs on. Bunkers, scrub and water stop it where it lands.
  - The golfer can't hit further than its reach: past it, it lays up.
- **What a shot leaves.** On the green: the putting map (Stage 4a). Within 35 yds of the green: a short-game map, built once per pin, of expected strokes from every chip-and-pitch spot (the best of five aims round the pin, then the putting map or the baseline). Further away, the baseline for the lie and distance; water adds its penalty stroke.
- **The golfer, fitted to the anchors** (`GolferCalibration`). On a plain calibration hole (flat, a 34-yd fairway with rough either side, a round 26-yd green with the pin in the middle) the golfer plays out from every one of the baseline's 21 rows off the green. Tee rows beyond its reach start with a Tour drive; otherwise it goes at the pin when in reach and lays up leaving at least 40 yds when not, then putts with the fitted putter. Its spread, spread floor, long-shot growth, rough, sand, recovery and tee multipliers, reach, recovery reach and Tour drive carry are fitted (Nelder–Mead in two passes, the same random draws every try, kept inside plausible ranges) until the averages match the anchors. The residuals are reported on fresh draws. The fit takes about 3 seconds and is kept in `%AppData%\CourseDesignerStudio\holesim\golfer-fit-<key>.json`, keyed by the baseline rows, the putter, the ground rules, the shot rules and the starting values.
- **On the shipped anchors** (Stimp 10.3): RMS 0.11 strokes. Every fairway, rough and tee row from 100 to 240 yds is within 0.15. The worst rows are recovery at 100 yds (−0.32: the simple golfer either goes for the green or lays up, while a real recovery is often blocked) and sand at 540 (−0.22). Without the plausible ranges the fit reaches RMS 0.09, but only with a recovery spread ten times the fairway's, so the ranges stay. The fitted reach, about 210–230 yds, is shorter than a Tour 3-wood. That is what makes 240-yd shots from the fairway as hard as the anchors say, and the par 5 second shot (4d) will need the player's real club distances instead.
- The side spread is kept at 0.78 of the long-and-short spread (the anchors can't tell them apart), and heavy rough isn't fitted (no anchor covers it). Both are PLACEHOLDER rows in the new Approach group, with the release and plays-like rules.

Figures on Pilolcura (green contours unverified, flat outside the green):

| Hole | From | Pin | Green hit | Proximity | Expected | SG for the shot |
|---|---|---|---|---|---|---|
| 8 | 153 yds, fairway, best aim (3 right, 3 beyond) | 1 | 78% | 24 ft | 3.21 | −0.17 |
| 8 | 143 yds, the best drive's typical finish, at the pin | 1 | 74% | 22 ft | 3.17 | −0.17 |
| 12 (par 3) | the tee, 168 yds, best aim | 1 | 20% | 24 ft | 3.98 | −0.90 |
| 12 (par 3) | the tee, 179 yds, best aim | 4 | 18% | 34 ft | 4.55 | −1.44 |

Hole 12's replay green sheds most shots (30% of it is too steep to hold a ball) and its pin 4 can't be played at all, so its numbers say "read this green in the game" more than anything about the hole.

`pga2k25 holesim <file.course> --hole 8 --approach --from 153[,side] --aim best|pin|centre|right,long [--aim-distance 150] [--approach-png file.png]` prints the golfer fit and the run; `--from tee` plays a par 3 tee shot.

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
                [--green [--green-png green.png]]
                [--approach --from 153[,side]|tee --aim best|pin|centre|right,long [--aim-distance 150] [--approach-png a.png]]
pga2k25 holesim-terrain <file.course> --holes 11,3,4 [--smooth 8] [--readings 10] [--fit-length 25]
```

`holesim` always runs on flat ground (the gate needs in-game readings, which are a Studio step) and uses the shipped defaults as they are, most of them PLACEHOLDER. The Studio section is where they get confirmed. `holesim-terrain` is the terrain study, for courses whose distance markers carry the game's heights.

## Fixtures

- Cassimbo hole 3 (`samples/courses/cassimbo.course`): tees 499 / 512 / 531 / 537, 4 tees and 4 pins, aim point 267 from the back tee, as in the proof of concept.
- Pilolcura Analysis hole 8 (`samples/private/pilolcura-analysis-2026-11-02.course`, git-ignored): tees 460 / 483 / 500 / 515, aim marker 286, widths 78 / 79 / 71 / 68 at 250 / 300 / 325 / 350, green 38 deep, a bunker right at about 298–328. The waste (15) and id-16 brushes sit 60+ yds off the line, so none are listed.

## Not yet

Sightlines from the tee (the visibility map) wait on a trusted terrain and the sightline answers collected in step 4. Stage 4 continues (proposal: https://claude.ai/artifact/7KdJjbPdzKH2tbabsoMX8Q): 4c the approach heat maps by pin and par 3 tee-shot sweeps, 4d the par 5 second shot on Pilolcura hole 10, 4e whole holes. The original Stage 4 list: approaches, the approach map by pin, design experiments (paired edits of the hole), in-game verdicts and the session record. Stage 5: wind, the par 5 lay-up decision and session comparison.
