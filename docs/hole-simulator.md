# Hole design simulator

A test session for **one hole at a time**, on demand, with the game open in playtest. It reads the hole from the `.course` file, asks you to confirm every assumption, plays thousands of tee shots across a range of quality, and values each finish in strokes gained. The aim is to help decide what to test or change on the hole. It doesn't score the design and doesn't predict a reviewer's verdict.

Proposal and mockup: https://claude.ai/artifact/KUGRHi71TAL55uNvtpPUXm. Pilot hole: Pilolcura Analysis hole 8.

## Where it lives

| Piece | What |
|---|---|
| `src/Pga2K25.HoleSim` | The engine: hole model, surface grid, change list, save watcher, baselines, assumption sheet, geometry run, map renderer |
| Studio, "Hole design simulator" | The bar, sections and tabs (`HoleSimViewModel` and its partial files, `Views/HoleSimView.axaml`) |
| CLI `holesim` | The same engine for checks and fixtures. One hole per run; there is no batch or 18-hole mode |
| `assets/holesim/assumptions.json` | Assumption defaults, each with its source tag and date (embedded in the library) |
| `assets/holesim/baselines/*.csv` | Strokes-gained baselines with provenance columns (embedded) |

The app stays analysis-only: the simulator reads course files and never writes them.

## The session

The section has a bar along the top and three sections under it (since 6 Nov 2026; before that it was ten numbered steps).

- **The bar:** hole, tee, pin and player. Every view uses them: change the pin there and the green read, approach, map and second shot follow. Changing the hole or tee clears the read-back confirmation and results; changing the pin or player clears results.
- **Set up**, done once per hole: Hole, Assumptions, Live checks, Terrain. The section's label counts how many are done ("2 of 4") and each finished tab gets a tick. Shots and Hole stay open while setup is unfinished, but they say what's left and every result carries its warnings.
- **Shots**, by what the hole's par has: Drive, Second shot (par 5s), Approach (called Tee shot on a par 3) and Green. Approach has a switch between **Play a shot** and **Map**.
- **Hole:** Summary and Experiments, the whole hole tee to cup, and the session Record.

Each section remembers the tab it was left on.

### Set up

- **Hole** (the read back). Par, yards from each tee, aim marker, fairway widths (yardage book stations, from the back tee), green depth, front and back, pins relative to the green centre, and bunkers, scrub and water near the line. Anything listed is within 40 yds of the line of play or 12 yds of the hole's own fairway or green, measured in yards from the chosen tee along the tee-to-green line. Surfaces the model has no rule for (waste 15, unknown ids) within 60 yds of the line are listed with a choice of how they play. Every row carries its source tag. Drive stays locked until you confirm the read-back.
- **Assumptions.** Every row needs a confirm or an edit. Values confirmed in an earlier session prefill the sheet and show when they were last confirmed, but never skip confirmation. A reused placeholder stays PLACEHOLDER (orange). Values you change in the sheet are tagged PRIOR the next time. Course settings (`greenSpeed`, firmness and speeds) are shown as the file has them, with `greenSpeed` read on Alex's two-point Stimp line; the model doesn't use them yet.
- **Live checks.** Suggested MEASURE spots along the line (about eight: every 50 yds on a long hole, closer on a par 3, alternating 10 yds either side of the line) plus the green's front and back, shown on a map. The game's MEASURE readout gives the cursor's yards TO TEE (each tee) and TO GREEN (the route's green point) and its height against each tee, so each spot lists its yards to the chosen tee and to the green. Move the cursor until both match. The zero defaults to the chosen tee, which is what the readout shows; the green centre or a pin can be chosen instead. Spot **G** is the green itself: copy GREEN HEIGHT for the chosen tee from the game's tee table, no cursor needed. Type each label as the game shows it: ▲ or + is above, ▼ or − below, `+1'4"` is 16 in, `+1'` is 12 in, a missing sign reads as above, inches of 12 or more are refused, and every value is said back. The model's height for a spot, against the same zero, appears only after its reading is in, so it can't steer what you type. Every fourth spot is held back to test a fitted correction; tick or untick to change that. The step also asks how much of the fairway at 300–350 (on a par 3, the green) you can see from the tee: All, Most, Some or None. You can enter up to five drives too: carry, total, and finish side by eye, since playtest shows only carry and total. Their mean and spread are set against the assumed carry. Everything saves as you type to `Hole Sim/HNN live checks.json` beside the products, in world coordinates, so another tee reads the same spots.
- **Terrain** (the terrain gate, with the green contours card under it). Each candidate model is compared with your readings, with fit bands of Trustworthy under 10% of the readings' range, Indicative 10–18%, and Check over 18% or with fewer than five readings. The zero counts toward the range. A corrected model is judged only on the readings held back from its fit. Until you pick a candidate, the gate picks the best band, then the lowest error. A pass allows **Use the terrain**. A Check offers **Take more readings**, **Geometry only** or **Override: terrain unverified**, which puts a label on every result.

### Shots

- **Drive** (was Run; the button is **Run drives**). Aims from 60 yds left to 60 right of the game's aim marker, every 5 yds, 2,000 drives each. Drives land with the player's carry and side spread, then roll by surface until they stop; bunkers, scrub and water stop the ball. On flat ground by default. With the terrain on, carry changes for height (PLACEHOLDER 1 yd per 3 ft the landing sits below the tee), and the ball rolls downhill under gravity. It stops when it is slow and on ground flatter than its friction, the proof of concept's rule. There is no wind yet. Every aim uses the same random draws (paired), and the run is deterministic for a seed. Each finish is valued against the baseline:

  `SG = E(tee, distance to pin) − E(finish lie, distance to pin) − 1 − penalty`

  Distance is straight-line to the pin, in yards (feet on the green). Results show the share of finishes by surface, yards left, SG with a 95% band, expected strokes for the hole (E(tee) − SG) and the share of finishes outside the baseline's rows.
- **Second shot** (Stage 4d, par 5s only). On a par 5: go for the green or lay up. From where the best line's drives typically finish (once Drive has run) or from yards and side, for the player in the bar with its longest fairway club, and optionally with wider spreads. It gives the verdict and by how much, going for it (or the longest club at the green when it's out of reach) against the best lay-up, and the best lay-up for each distance left. The map colours every lay-up target against going for it.
- **Approach: Play a shot** (Stage 4b; on a par 3, **Tee shot**). Shots at the green, 600 of them, from a spot you pick to an aim you pick:
  - **From**: yards to the pin and yards right or left of the line of play; on a par 4 or 5, where the best line's drives typically finish (the median of its fairway finishes, once Drive has run); on a par 3, the tee.
  - **Aim**: the best aim (25 tried on a 3-yd grid round the pin, the same shots for each), the pin, the green's centre, an aim distance straight at the pin, or a point so many yards right or left and beyond or short of the pin.
  - **Spreads**: 1 is the golfer fitted to the anchors; 1.5 tries the hole for a weaker player.

  It shows where the shots finish (on the green or not), the share that hold the green, their average distance from the hole, where the misses go, bunker, water and scrub, the expected strokes to hole out and the strokes gained for the shot against the baseline from that lie and distance, with the best aims when it searched. A card underneath shows the fitted golfer against every anchor.
- **Approach: Map** (Stage 4c). For the chosen pin, built in the background when the pin is opened (a few seconds) and kept until the green, the terrain or the assumptions change:
  - **Par 4s and 5s:** the heat map of where to approach from. Every 5 yds of fairway, rough and bunker within the golfer's reach, each spot played with its best of nine aims, coloured against the other spots the same distance from the pin. The best angle is ringed, and a table compares the best and worst fairway spot in each 10-yd band.
  - **Par 3s:** tee shots from every tee, 49 aims each, with the best aim, its expected strokes and green hits, the pin itself and strokes gained against the baseline's tee rows.
  - **Every hole:** the leave map (expected strokes still to play by where a shot finishes round the green) with the ideal-angle spokes, and the ideal angle in from 150 yds.
- **Green** (Stage 4a). Expected putts from every spot on the hole's green to the chosen pin, drawn over the green with arrows pointing downhill. Cells too steep for a stopped ball to stay put are hatched. For each pin it shows:
  - the average putts from the whole green
  - the share of the green that needs 2.3 putts or more
  - from 15–30 ft, putts from above the hole against putts from below
  - the slope at the cup, in red when a ball can't stop there

  For the green as a whole it shows its size, Stimp, typical and steepest slope, and how much of it can't hold a stopped ball. Maps build in the background when a pin is opened (a few seconds each), or all at once with **Build every pin**. They are kept until the green, its speed or the putting assumptions change.


### Hole

- **Summary** (Stage 4e). Every pin played 600 times tee to cup for the player in the bar. The drive goes on its best line: the Drive tab's, when it ran for this pin and player, otherwise a quick 400-drive sweep. The par 5 second shot follows the go-or-lay-up plan, approaches use each spot's best aim from the heat map, chips aim at the pin, and putts come from the putting map. Per pin it shows the score (with expected putts, so not a whole number), the score against par, strokes gained off the tee, on approach, around the green and putting (they add up to the hole), fairways, greens in regulation, scrambling (par or better after missing the green in regulation), and birdie and bogey-or-worse shares. Over the pins it shows the average, the hardest and easiest pin, and where most strokes go.
- **Experiments** (Stage 4e). Design changes played against the hole as it is, every pin, with the same draws: the landing zone 5 yds wider and 5 yds narrower each side (par 4s and 5s; the zone is where the player's drives land, from the 10th to the 90th percentile, plus 10 yds), and each hazard near play taken out (the six nearest the landing zone or the green). After you reload a new save while the section is open, your last save against the one before is added at the top, both on the replay's green contours and flat ground outside the green, so only your edit differs. Each shows the change in strokes averaged over the pins, with its paired 95% range and the change per pin, marked easier, harder or no clear effect.
- **Record** (Stage 4e). The session record for this hole and today. Every result is logged as it finishes. The ones worth checking in the game become findings, each with a way to check it:
  - the best drive line
  - a clearly better side of the fairway (0.05 or more against its distance band)
  - an ideal angle worth 0.1 or more
  - a par 3 aim that saves 0.05 or more on firing at the pin
  - every second-shot verdict
  - a pin 0.15 or more harder than the easiest
  - a phase losing 0.15 or more
  - every experiment with a clear effect

  For each finding you pick Not checked, Confirmed in game, Not seen in game or Contradicted, and can add a note. The tab shows how many are still to check. Running the same result again updates the finding's text and keeps your verdict; on a newer save the verdict is marked for checking again.

### Save watcher

The rest of Studio only notices a new course save after backing out to the menu. This section watches the file while it is open: every 2 s it checks the file's time and size, and when a change has held still across two checks, it hashes the file and compares it with what's loaded. A new save is read in the background and compared with the current hole. The banner lists what changed on this hole: tees, pins, aim marker, widths, hazards, surface areas and height brushes near the line. Nothing reloads or runs until you click **Reload this hole**. Reloading keeps confirmed assumptions, asks for the read-back again when surfaces, tees or pins changed, and keeps a one-line summary of the previous results. **Not now** sets the save aside until the file changes again. A waiting save blocks Run drives.

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

## Approach maps (Stage 4c)

- **The heat map** (`ApproachMaps.Heat`). Spots every 5 yds on fairway, rough and bunkers within 45 yds of the line of play, from 40 yds out to the golfer's reach. At each spot, nine aims round the pin (3 × 3, 4 yds apart) on 200 shots, then the best of them on 600 fresh shots; every spot uses the same draws. On Pilolcura hole 8 that is about 450 spots, about 1 s from the command line.
- **Colour by angle, not distance.** Each spot is compared with the median spot in its own 10-yd distance band. Against the baseline instead, the colours followed distance: the model's curve and the baseline's don't match at every distance. So green on the map means "better than other spots this far out", which is the side, the angle and the lie. The best angle is the fairway spot, 60 yds or more out, furthest ahead of its band, and only when that's at least 0.03 strokes (smaller is within the run's noise).
- **Harder than Tour overall.** Nearly every spot plays worse than the baseline from the same lie and distance (Pilolcura hole 8: −0.17 for pin 1, −0.58 for pin 4). The golfer was fitted on a flat, round green, so real greens with contours and surrounds always read harder. The map states that offset in words and colours only the differences.
- **The leave map** (`ApproachMaps.Leave`). Expected strokes still to play from every yard within 35 yds of the green: the putting map on it, the short-game map round it, water and scrub with their penalty. Coloured on the map's own range (5th to 95th percentile), so where to leave it shows even when everything off the green is costly.
- **The ideal angle** (`ApproachMaps.Angles`). Shots into the pin from 150 yds, from the line of play and every 15° either side out to 75°, all from a fairway lie with the same draws and each direction's best aim. Spots off the course are played as if they were fairway, so only the green and its surrounds count. The spokes on the leave map show them: the ideal one in ink, the others coloured against it.
- **On a par 3** the angles are played from the chosen tee's own distance and measured against the back tee's line, so the ideal direction belongs to the green and doesn't turn with the tee (`ApproachMaps.BackTeeLine`, `AngleOf`). Each tee's own line into the pin is drawn in blue (the chosen tee's heavier), the table marks where the chosen tee comes in, and the summary says how far that is from the ideal. Changing the tee rebuilds the map at the new distance. On Pilolcura hole 3, pin 1, the four tees are within 5° of each other. From T1 (178 yds) the tee shot comes in 0.15 off the ideal, which is 75° left of T4's line; from T4 (210 yds) it's 0.05 off. A tee shot more than 0.1 off the ideal becomes a finding in the record.
- **Par 3 tee shots** (`ApproachMaps.TeeShots`). From every tee, 49 aims (7 × 7, 4 yds apart, out to 12 yds from the pin) on 300 shots each.

Figures on Pilolcura (green contours unverified):

| Hole | Pin | What the maps say |
|---|---|---|
| 8 | 1 (centre) | The angle hardly matters: best and worst spots at the same distance differ by 0.04–0.10. From 150 yds, best straight down the line (3.20), worst from 75° right (3.43). |
| 8 | 4 (front left) | The left side is worth 0.10–0.14 strokes from 90–170 yds; best angle 91 yds out, 38 yds left of the tee line. From 150 yds, best from 15° left (3.60), worst from 75° right (3.81). |
| 3 (par 3) | 4 (front left) | Best aim 8 yds right and 8 long, 3.74–3.83 from the four tees; firing at the pin costs about 0.2. Best approached from 45° right (3.54, 66% on the green), worst from 45° left (3.92). |

Hole 3 is the par 3 test green for now: its cup slopes are all under 1.2%, against hole 12, whose replay green is too steep to trust until it's read in the game.

`pga2k25 holesim <file.course> --hole 8 --pin 4 --heat [--heat-png heat.png] --angles [--leave-png leave.png]` and, on a par 3, `--tee-shots`.

## Par 5 second shot (Stage 4d)

- **The choice** (`SecondShot.Plan`). From a spot after the drive, the player can do one of two things:
  - **Go:** take its longest fairway club at the green, with the best of nine aims round the pin, 4 yds apart. When the green is out of reach, the same club "as close as it goes".
  - **Lay up:** aim at any fairway spot of the approach heat map that is in reach, at least 40 yds from the pin, and on the golfer's side of the green.

  Every option is played with the same shots (150 each, the best go aim and the best five lay-ups again on fresh shots).
- **Valued by the shot it leaves.** A finish is worth the putting map on the green, the short-game map round it, the approach heat map's value for that spot (its own best third shot) on the fairway, rough and bunkers, and the baseline anywhere else, with water's penalty. So a lay-up wins only if the third shot it leaves, misses included, beats going for it.
- **The longest club** is a new Player row for each player: Power Drive 265 yds and Normal 240, carry and roll, both PLACEHOLDER until set from the game's club distances. The golfer fitted to the anchors only reaches about 230 yds, which is right for matching the anchors but too short for a par 5 second shot, so the player's own club decides reach here. The spreads stay the fitted golfer's.
- **Checked on a test hole:** with nothing round the green it goes for it; with water round the sides and back and wider spreads (×1.5) it lays up, because the go shots find the water and the lay-up doesn't.

Figures on Pilolcura (green contours unverified, flat outside the green), from the best drive's typical finish:

| Hole | Player | Pin | From | Verdict |
|---|---|---|---|---|
| 10 | Power Drive (265) | 1 | 204 yds | go, by 0.28 (55% on the green) |
| 10 | Power Drive, spreads ×1.5 | 4 | 188 yds | go, by 0.34 |
| 10 | Normal (240) | 4 | 247 yds, out of reach | the longest club as close as it goes, by 0.15 |
| 10 | Power Drive | 4 | 300 yds (set by hand) | lay up to 41 yds, by 0.05 (lay-ups from 40 to 100 yds all within 0.05) |
| 1 | Power Drive | 1 | 280 yds, scrub round the green | go, by 0.07; by 0.02 with spreads ×1.5 |

Pilolcura has no water near its par 5 greens, so going for it nearly always wins, by less where scrub or bunkers guard the green. The best lay-up is always the shortest allowed, because nothing near these greens makes a full wedge distance worth more than a short pitch.

`pga2k25 holesim <file.course> --hole 10 --pin 4 [--player normal] --second-shot [--from 230[,side]] [--second-png file.png]`.

## Whole holes and experiments (Stage 4e)

- **Play-out** (`HolePlay.Play`). Par 4s and 5s start with the player's drive (`GeometryRun.Drive`: the Drive tab's physics and draws, in the same order); par 3s with the best of 49 tee aims. Water costs its penalty and the next shot is played from rough at the spot. A shot that starts within the short-game map counts as around the green; the rest are approaches (a par 3 tee shot is an approach, as in Broadie's split).
- **Strokes gained by phase.** Every shot is scored against the baseline from its start and finish, and the putts against the baseline's green rows, so the phases telescope: they add up to E(tee) less the score, exactly (the tests check it to 1e-9).
- **Experiments** (`Experiments`). Surface edits only: `WidenFairway` turns rough, heavy rough and native within N yds of the fairway into fairway between two distances (or fairway within N yds of its edge into rough), and `RemoveHazard` turns one hazard's connected piece (found again from a seed cell kept with each hazard) into rough. The edited hole (`HoleModel.WithGrid`) keeps its green, golfer and putting maps; its short game and heat maps rebuild on the new surfaces. Both versions play the same draw numbers, and the 95% range comes from the spread of the paired differences.

On Pilolcura hole 8 (Studio, Power Drive, green contours unverified, flat outside the green):

| Pin | Score | SG tee | Approach | Short | Putting | GIR |
|---|---|---|---|---|---|---|
| 1 (centre) | 4.22 | +0.29 | +0.10 | +0.20 | −0.39 | 71% |
| 2 | 4.29 | +0.30 | −0.03 | +0.33 | −0.41 | 69% |
| 3 | 4.35 | +0.31 | +0.05 | +0.24 | −0.47 | 70% |
| 4 (front left) | 4.69 | +0.27 | −0.10 | −0.04 | −0.48 | 40% |

Pin 4 plays nearly half a stroke harder than pin 1, and putting is where most strokes go on every pin: the putting model on these contours is harder than the baseline's green rows, which is why reading the greens in the game matters. Experiments on the same hole: the landing zone 5 yds wider each side −0.021 ± 0.008, 5 yds narrower +0.039 ± 0.015; taking out any one hazard changes nothing for Power Drive, whose best line lands past them all. In a debug build the summary took 22 s and the eight experiments 156 s.

`pga2k25 holesim <file.course> --hole 8 [--player normal] --whole [--experiments]`.

## Session record

`SessionRecord` is saved as you go to `<products>/Hole Sim/HNN session YYYY-MM-DD.json` beside the live checks (Alex's choice, 6 Nov 2026), with a Markdown copy of the same name to read or share. It holds:
- the course, hole and par, when the session started and was last updated, and the baseline
- every course save the results ran on (time and short hash)
- the placeholder values in use
- each result (time, kind, title, headline lines and its warning badge)
- the findings with their verdicts and notes

Opening the same hole again the same day carries on the same record; a new day starts a new file. A finding keeps a stable id per result (for example `summary.pins.power` or `map.ideal.4`), which is how a rerun updates it instead of adding another.

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
                [--heat [--heat-png heat.png]] [--angles] [--tee-shots] [--leave-png leave.png]
                [--second-shot [--from 230[,side]] [--second-png second.png]] [--whole] [--experiments]
pga2k25 holesim-terrain <file.course> --holes 11,3,4 [--smooth 8] [--readings 10] [--fit-length 25]
```

`holesim` always runs on flat ground (the gate needs in-game readings, which are a Studio step) and uses the shipped defaults as they are, most of them PLACEHOLDER. The Studio section is where they get confirmed. `holesim-terrain` is the terrain study, for courses whose distance markers carry the game's heights.

## Fixtures

- Cassimbo hole 3 (`samples/courses/cassimbo.course`): tees 499 / 512 / 531 / 537, 4 tees and 4 pins, aim point 267 from the back tee, as in the proof of concept.
- Pilolcura Analysis hole 8 (`samples/private/pilolcura-analysis-2026-11-02.course`, git-ignored): tees 460 / 483 / 500 / 515, aim marker 286, widths 78 / 79 / 71 / 68 at 250 / 300 / 325 / 350, green 38 deep, a bunker right at about 298–328. The waste (15) and id-16 brushes sit 60+ yds off the line, so none are listed.

## Not yet

Sightlines from the tee (the visibility map) wait on a trusted terrain and the sightline answers collected in Live checks. Stage 4 (proposal: https://claude.ai/artifact/7KdJjbPdzKH2tbabsoMX8Q) is complete. The original Stage 4 list: approaches, the approach map by pin, design experiments (paired edits of the hole), in-game verdicts and the session record. Stage 5: wind, the par 5 lay-up decision and session comparison.
