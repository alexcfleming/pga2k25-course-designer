# Publish readiness

Checks for getting a course ready to publish and submit for TGC Tours review, from Alex's course QA checklist, his design principles and TGC Tours' published minimum approval requirements (March 2021). The principles are a personal collection, not rules, so every limit can be changed per course.

Some checks are **provisional**: their bands come from a small sample of reviewed courses, measured by different methods, and no reviewer comments have been logged against them yet. They show as a Warning: never a pass or a fail, and they don't hold the publish gate.

The work splits three ways:

- **From the course file:** everything below. It reruns whenever the file changes.
- **Partly from the file:** green speed and fringe work from the file now, mapped from in-game readings. Trees in the play area and playtest coverage come later.
- **Judgement calls:** waypoints and default club, whether every route works, sand and surface edges, realism, cameras, concept drift, the per-hole and planting passes, and the TGC minimums the file can't show. Studio tracks them with a status (Open, Done, Deliberate, N/A) and notes; the per-hole ones are **Hole QA** (below).

The library is `src/Pga2K25.Readiness`; the Studio's **Publish readiness** section uses it (see [studio.md](studio.md)), and so does the `readiness` command. The mockup it was built from is at https://claude.ai/artifact/S4JeBUYFRV3ZBmnvWWs9J3.

## Saved progress, import and the record

- **`Readiness.json`** beside the products holds everything the designer decides:
  - item statuses (Open, Done, Deliberate, N/A) and notes
  - deliberate marks on course-file checks, for the whole check (`green-scale`) or one hole (`green-scale@2`), each with its reason
  - pin difficulties and playtest coverage
  - per hole (`holes`): a fairway start typed over the file's, a shared-fairway mark with the holes it's shared with, and a note
  - the course's limits, added checks, designer, version and tee names
  - the sign-off with the course file's fingerprint (the first 16 hex digits of its SHA-256)
- **Item keys** are the HTML checklist's own (`pre.score`, `b.fringe`, `hc.3.sightTee`, `tr.12.aim`, `pl.5.g360`; checks added since use `tgm.` and `hq.`), so its backup imports straight across (`ChecklistImport`). An import fills only what's still open or empty and never undoes a decision. "Size is deliberate" on a hole becomes deliberate marks on that hole's green size and landing width. Typed pars, yardages, green areas and widths are left out, because the file supplies them. The lighting values typed into the checklist are kept for the record.
- **Items the file answers** (for example the scorecard, Section 7, scale, variety, corridors, speed, fringe, elevation and green relief) count as done while their checks pass or are marked deliberate, unless the designer sets them otherwise.
- **The gate** opens when every course-file check is cleared (a Warning counts as cleared), every item, Hole QA check, pin difficulty and playtest square is closed, and the sign-off is complete and matches the current file.
- **The record** (`ReadinessRecord`) is a dated JSON of the report and state, plus a printable HTML page of the same. It's written when the work on a course is done, replacing the HTML checklist's final export.

The record's HTML embeds the course's colour mark before its title when the course has a logo set (see [Course logo](course-logo.md)).

## Hole QA

Every hole gets the same 39 checks, in six groups. 14 are TGC Tours' published minimums (marked with a dot in Studio and `*` in the record).

| Group | Checks (TGC minimums in bold) |
|---|---|
| Lines and views | Sightline from the tee; approach views; bail-out views; looking back from green and at tee; aim line at its own green; waypoints and default club from every tee; **no unavoidable obstruction on the tee shot or approach** |
| Shaping | Green contour study; fairway contour heights; **tee boxes flat**; multiple tees; bunkers: sand even and in style; edges blended; **light rough mostly symmetrical with fairway and green**; **bunkers don't cut into several layers of rough or fairway (recommended)** |
| Play | Played out of every hazard; every route works; built hazards match the concept; hole image test and draw; **fairway reachable in any wind, from every tee**; **a shot carried onto the front doesn't roll off the back**; **bunkers fair** |
| Pins | **Locations varied within each pin set**; **every pin gives a fair chance to get close (playtested)**; **no pin in the middle of a large yellow or red slope** |
| Look | Highlight grasses; rocks, cliffs and props believable; hole camera and fly-in; **no plants on fairway, green, tees or cart path**; **stock shapes blended, not overused**; **the same tree or rock isn't repeated over and over**; **cart paths smoothed, spline kinks cleaned up** |
| Planting | Open waste; cliff and hole edge; green 360; tee 360; hole wastes; open hill light; around bunkers |

They replace the HTML checklist's three per-hole stages (per-hole checks, tour readiness, planting passes), keeping their keys. Two pairs merged: *Bunkers* with *Bunker sand sculpting* (`hc.N.bunk` and `tr.N.sand`), and *Transitions* with *Fairway-to-sand blended* (`hc.N.trans` and `tr.N.blend`). A merged check reads both keys and the more closed status wins (Done, then Deliberate, then N/A); setting it writes its own key and drops the twin's.

The course-level TGC minimums are a checklist stage of their own (**TGC minimums**: par, yardage range, fairway width, pins, and accuracy for a real-course recreation).

## Command

```bash
pga2k25 readiness <file.course> --drive 360 --holes
```

| Option | Default | Meaning |
|---|---|---|
| `--drive` | none | The test drive measured with the power profile (yds). Without it the drivable and par-5 checks ask for one. |
| `--limits` | defaults | A JSON file overriding any `ReadinessLimits` value, e.g. `{"Par5MinLeftYards": 230, "CorridorYards": 30}` |
| `--holes` | off | Adds the per-hole table: drive class, green size and landing width against the median, fairway start, narrowest fairway and width steadiness, tee-to-green change, green relief, nearest other green, pins with their edge distance and slope |
| `--json` | off | Writes the whole report |

The exit code is 1 when any check is High (hole design and variety, which reviewers weighed most), otherwise 0.

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
| `par-range` | Total par; every hole a par 3, 4 or 5; 18 holes | par 69–74 | TGC minimums |
| `same-par-range` | Shortest to longest hole of each par | at least 30 yds | TGC minimums |
| `pins` | Each pin's distance from the green's edge, and the steepest of the greens book's slope samples within 2.5 m of it | 2 yds in (standing in for three grid squares); under 5.8% (the greens book's red, 3.3°) | TGC minimums |
| `narrow-fairway` | Narrowest the line's fairway gets from where it starts to 40 yds short of the green. Info only | 25 yds (recommended) | TGC minimums |
| `fairway-start` | Where the main fairway starts on each par 4 and 5 (below); median and count in the band | 150–225 yds | Provisional |
| `short-par4` | Par 4s of the length or shorter | 380 yds, at most 2 | Provisional |
| `width-steadiness` | Past 200 yds, the line's fairway width at its 90th over its 10th percentile, so one neck or one gap between fairways doesn't decide it | up to 3× | Provisional |
| `small-greens` | Drawn green area | 3,000 sq ft | Provisional |
| `par3-greens` | Par 3 greens grow with the hole's length | | Provisional |

`green-scale` is provisional now too: the band flagged an approved course and cleared one that wasn't, so it warns. Same-par runs (Section 7) are a design habit, not an approval rule (approved courses have four par 4s in a row), so they flag at most.

**Main fairway start.** From the back tee, the line of play (tee, aim points, green) is sampled every 5 yds from 60 yds out. At each station the fairway strip under the line, or the nearest one within 15 yds of it, is measured across. The fairway starts at the first station where that strip is at least 25 yds wide and stays so for 35 yds (seven stations). 60 means 60 or less. On Cassimbo it lands within 15 yds of the yardage-book measure on 12 of 14 holes, and on Sagan-Khushun 11 of 13; both give 1 hole inside the band, as the book did. A designer can type their own figure over the file's, for a moved aim point, say, and mark a hole's fairway as shared: shared holes are left out of the fairway-start median, the landing-width median and flags, the width steadiness and the narrow-fairway note. Why a later start would help isn't known; the band is a pattern, not a reason.

Elevation, plot range and green relief come from the replayed terrain, so they say "confirm in game". Rounded figures are judged as printed: a green shown at 3.0 ft isn't flagged as over 3.

**Green speed.** Two in-game readings set the scale: Cassimbo's 0.97 shows Stimp 10.9 and Sagan-Khushun's 0.905 shows 10.1. The line through them is used, `Stimp = 10.9 + (setting − 0.97) × 12.31`. A third reading would show whether it's straight. −1 is the game's default and asks for a reading.

**Fringe.** Cassimbo and Sagan-Khushun are both set to 0.5 and show no visible fringe in game. Other files have 0.54–0.63, and −1 is the game's default.

**Pins.** Each pin is placed by thirds of its green along the approach (F, C, B) and across it (L, C, R, as the golfer faces the green), with its distance from the green's edge and the slope by the cup. The game's grid square size hasn't been measured, so the edge limit is a stand-in until it is.

## The Cassimbo test

Cassimbo wasn't approved for TGC Tours. With a 360-yd test drive (about what a scheduler hit), the file alone shows:

- **Hole 3** is a very short par 5: it leaves 177 yds. So does hole 18, which leaves 188.
- **Front nine:** a third drivable par 4 (holes 1, 4 and 8). The back nine has four drivable or near-drivable par 4s.
- **Green sizes:** green 2 is 0.30× the median and green 7 is 2.35×. The review said greens were "wildly different", 2 against 7. (A warning now; see above.)
- **Fairway start:** the median is about 98 yds and only hole 9 starts inside 150–225.
- **Hole 12:** its line passes 22 yds from hole 10's green. The review said hole 12 is played over another green, so the corridor check measures distance to a green rather than only crossing one.
- **Hole 7** climbs 52 ft from tee to green.

`ReadinessTests` holds these as a regression.
