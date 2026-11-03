# Publish readiness

Checks for getting a course ready to publish and submit for TGC Tours review, from Alex's course QA checklist, his design principles (Nov 2026 revision) and TGC Tours' published minimum approval requirements (March 2021). The principles are a personal collection, not rules, so every limit can be changed per course.

The design assumption is a **300–350 yd tee shot** from the back tee (Principles §12), one editable course setting. Every landing-zone figure is measured from it: yards left, fairway widths, the yardage book's width stations.

Some checks are **provisional**: the §12 length and width limits were chosen after seeing a handful of reviewed courses, measured by different methods, and reviewer comments outrank them. They show as a **Warning**: never a pass or a fail, and they don't hold the publish gate. **Prompts** and **notes** (shown as Info) are things to look at, with no limit.

The work splits three ways:

- **From the course file:** everything below. It reruns whenever the file changes.
- **Partly from the file:** green speed and fringe work from the file now, mapped from in-game readings. Trees in the play area and playtest coverage come later.
- **Judgement calls:** waypoints and default club, whether every route works, sand and surface edges, realism, cameras, concept drift, the per-hole and planting passes, the hole design notes, and the TGC minimums the file can't show. Studio tracks them with a status (Open, Done, Deliberate, N/A) and notes; the per-hole ones are **Hole QA** (below).

The library is `src/Pga2K25.Readiness`; the Studio's **Publish readiness** section uses it (see [studio.md](studio.md)), and so does the `readiness` command. The mockup it was built from is at https://claude.ai/artifact/S4JeBUYFRV3ZBmnvWWs9J3; Hole QA's is at https://claude.ai/artifact/BuCjBSWNfJVd94E3e7bAwE.

## Saved progress, import and the record

- **`Readiness.json`** beside the products holds everything the designer decides:
  - item statuses (Open, Done, Deliberate, N/A) and notes
  - deliberate marks on course-file checks, for the whole check (`corridor`) or one hole (`corridor@12`), each with its reason
  - pin difficulties and playtest coverage
  - per hole (`holes`): the hole design notes, a fairway start typed over the file's, a shared-fairway mark with the holes it's shared with, and a note
  - the course's limits (the design tee shot among them), added checks, designer, version and tee names
  - the sign-off with the course file's fingerprint (the first 16 hex digits of its SHA-256)
- **Item keys** are the HTML checklist's own (`pre.score`, `b.fringe`, `hc.3.sightTee`, `tr.12.aim`, `pl.5.g360`; checks added since use `tgm.`, `hq.` and `hn.`), so its backup imports straight across (`ChecklistImport`). An import fills only what's still open or empty and never undoes a decision. Typed pars, yardages, green areas and widths are left out, because the file supplies them, and so are the checklist's retired drivable and "220 left" limits. The lighting values typed into the checklist are kept for the record.
- **Items the file answers** (for example the scorecard, Section 7, corridors, speed, fringe, elevation and green relief) count as done while their checks pass or are marked deliberate, unless the designer sets them otherwise. A provisional warning doesn't tick an item: the width and length audits stay for the designer.
- **The gate** opens when every course-file check is cleared (a Warning counts as cleared), every item, Hole QA check and hole design note asked of a hole, pin difficulty and playtest square is closed, and the sign-off is complete and matches the current file.
- **The record** (`ReadinessRecord`) is a dated JSON of the report and state, plus a printable HTML page of the same, with the hole design notes. It's written when the work on a course is done, replacing the HTML checklist's final export.

The record's HTML embeds the course's colour mark before its title when the course has a logo set (see [Course logo](course-logo.md)).

## Hole QA

Up to 50 checks a hole, in eight groups. 14 are TGC Tours' published minimums (marked with a dot in Studio and `*` in the record). A check a hole isn't asked shows no square: hole design is for par 4s and 5s, the second-shot note for par 5s, and the width reason for holes over 60 yds wide somewhere at 300–350.

| Group | Checks (TGC minimums in bold) |
|---|---|
| Lines and views | Sightline from the tee; approach views; bail-out views; looking back from green and at tee; aim line at its own green; waypoints and default club from every tee; **no unavoidable obstruction on the tee shot or approach** |
| Hole design | Notes: what a miss costs; what changes if the player goes left, right or over each hazard; the par 5 second-shot decision after a 350 drive; why the fairway is over 60 yds. Ticks: no hazard a 350 drive flies with no consequence; no blind hazards in the landing area; every lobe or extra area of fairway has a sensible play |
| Shaping | Green contour study; fairway contour heights; **tee boxes flat**; multiple tees; bunkers: sand even and in style; edges blended; **light rough mostly symmetrical with fairway and green**; **bunkers don't cut into several layers of rough or fairway (recommended)** |
| Play | Played out of every hazard; every route works; built hazards match the concept; hole image test and draw; **fairway reachable in any wind, from every tee**; **a shot carried onto the front doesn't roll off the back**; **bunkers fair** |
| Pins | **Locations varied within each pin set**; **every pin gives a fair chance to get close (playtested)**; **no pin in the middle of a large yellow or red slope**; slope run-out checked on the longer approaches |
| Look | Highlight grasses; rocks, cliffs and props believable; hole camera and fly-in; **no plants on fairway, green, tees or cart path**; **stock shapes blended, not overused**; **the same tree or rock isn't repeated over and over**; **cart paths smoothed, spline kinks cleaned up** |
| Finish | Sculpting around the bunkers; surfacing (weathering and transitions); no unintended bare areas |
| Planting | Open waste; cliff and hole edge; green 360; tee 360; hole wastes; open hill light; around bunkers |

The hole design notes are required: written on the hole's card (`holes.N.notes`), each closes once it has text, and an empty one keeps the hole open. Their squares in the grid are dashed until written.

They replace the HTML checklist's three per-hole stages (per-hole checks, tour readiness, planting passes), keeping their keys. Two pairs merged: *Bunkers* with *Bunker sand sculpting* (`hc.N.bunk` and `tr.N.sand`), and *Transitions* with *Fairway-to-sand blended* (`hc.N.trans` and `tr.N.blend`). A merged check reads both keys and the more closed status wins (Done, then Deliberate, then N/A); setting it writes its own key and drops the twin's.

The course-level TGC minimums are a checklist stage of their own (**TGC minimums**: par, yardage range, fairway width, pins, and accuracy for a real-course recreation).

## Command

```bash
pga2k25 readiness <file.course> --tee-shot 300-350 --holes
```

| Option | Default | Meaning |
|---|---|---|
| `--tee-shot` | 300-350 | The design tee shot from the back tee (yds) |
| `--drive` | none | The test drive measured with the power profile (yds), recorded only |
| `--limits` | defaults | A JSON file overriding any `ReadinessLimits` value, e.g. `{"MedianPar4MinYards": 420, "CorridorYards": 30}` |
| `--holes` | off | Adds the per-hole table: card and straight-line yards, green size against the median, widths at the tee shot, fairway start, width swing, tee-to-green change, green relief, nearest other green, hazards in the landing zone |
| `--json` | off | Writes the whole report |

The exit code is 1 when any check is High (a line of play crossing another green), otherwise 0.

## The checks

Yardages are from the back tees. Lengths use card yards; yards left use the straight line from the back tee, and both show where they differ by 15 yds or more. Severity, mildest first: **Pass**, **Info** (recorded, a prompt or a note), **Warning** (provisional), **Confirm** (needs a reading or a figure the file can't give), **Flag** (fix it or mark it deliberate), **High**.

| Id | Check | Limit (default) | Source |
|---|---|---|---|
| `par` | 18 holes; par, OUT and IN | | Publish gate |
| `same-par` | Consecutive holes sharing a par, judged by run length, yardage spread and whether the run opens the round. A design habit, not an approval rule, so it flags at most | narrow under 40 yds, safe from 60 | §7 |
| `par-range` | Total par; every hole a par 3, 4 or 5; 18 holes | par 69–74 | TGC minimums |
| `same-par-range` | Shortest to longest hole of each par | at least 30 yds | TGC minimums |
| `pins` | Each pin's distance from the green's edge, and the steepest of the greens book's slope samples within 2.5 m of it | 2 yds in (standing in for three grid squares); under 5.8% (the greens book's red, 3.3°) | TGC minimums |
| `length-total` | Back-tee total as a range (Info); forward tees listed, no limit | about 7,050–7,600 yds | §2 |
| `median-par4` | Median par 4, card yards (Warning) | 430+ | §12.4 |
| `par4-under400` | Par 4s under the length (Warning) | 400 yds, at most 3 | §12.4 |
| `shortest-par5` | Shortest par 5 (Warning); lists what each par 5 leaves after a 350 drive | 560+ | §12.4 |
| `card-straight` | Card and straight-line yards 15+ apart (Info) | | §12 |
| `landing-widths` | Fairway width across the line at 300, 325 and 350 yds (Warning). Shared fairways count. Over 60 needs a written reason on the hole | 25–70 (target 35–50) | §12.2 |
| `width-steadiness` | Past 200 yds, the line's fairway width at its 90th over its 10th percentile (Warning), so one neck or one gap between fairways doesn't decide it | under 3× | §12.2 |
| `width-pattern` | Roomier at 250–290 yds than at the tee shot: warns when the landing zone is over 10% wider than the lay-up (median widths) | | §12.2 |
| `elevation` | Back tee to green site | 50 ft down, 30 up | §3 |
| `plot-range` | Ground across tees, aim points and greens | 50–70 ft | §3 |
| `green-relief` | Highest minus lowest point on each drawn green (greens book analysis) | 1.5–3 ft | §3 |
| `corridor` | Nearest other green, or practice green, to each line of play. Crossing one is High | 25 yds | §11.3 A |
| `aim-spacing` | Aim points of two holes | 35 yds apart | §11.3 A |
| `own-green` | Each route ends on a drawn green of its own; a shared green is flagged (fine for a double green) | | §11.3 F |
| `green-speed` | `greenSpeed` as Stimp | 8–13 | Build checks |
| `fringe` | `greenFringeRadius` | 0.5 or less | §5 |
| `lighting` | Time of day, sun, cloud layers, fog, haze, rain as recorded | | Lighting section |
| `green-scale` | Drawn green area against the course median (prompt): it flagged an approved course and cleared one that wasn't | 0.6–1.6× | §12.3 |
| `green-approach` | Each par 4 and 5's green size with the yards left after 325 (prompt, no limit yet) | | §12.3 |
| `landing-hazards` | Bunkers and water within reach of the line at 275–360 yds (prompt): write what changes left, right or over. Not scored by count | 25 yds | §12.3 |
| `fairway-start` | Where the main fairway starts on each par 4 and 5 (note, no warning: no reviewer has mentioned it) | band 150–225 | |
| `forward-tees` | The longest carry from a forward tee to where the main fairway starts (prompt) | | §2 |

Retired in the November revision (Principles §11.4): drivable and near-drivable par 4s per nine, par 5s leaving 220 after the test drive, every tee set 6,500–7,500, landing width against the course median, and the short-lived ≤380 par 4, small-green, par 3 green and under-25 narrow-fairway checks, which the checks above replace.

**Widths.** The line of play (tee, aim points, green) is measured across at each station; the fairway strip under the line, or the nearest within 15 yds of it, counts. No strip that close is "no fairway" and warns as under 25. A station within 40 yds of the green isn't measured, so a short par 4 may have fewer than three.

**Landing hazards.** Bunkers (painted dabs merged as the scorecard maps merge them), painted water, stream ribbons and the ponds the terrain survey traces. Each is listed once, at the station it comes nearest, with how far off the line and which side.

**Main fairway start.** From the back tee, the line of play is sampled every 5 yds from 60 yds out. The fairway starts at the first station where the strip by the line is at least 25 yds wide and stays so for 35 yds (seven stations). 60 means 60 or less. On Cassimbo it lands within 15 yds of the yardage-book measure on 12 of 14 holes, and on Sagan-Khushun 11 of 13; both give 1 hole inside the band, as the book did. A designer can type their own figure over the file's, for a moved aim point, say, and mark a hole's fairway as shared: shared holes are left out of the fairway-start median, but their widths count.

Elevation, plot range and green relief come from the replayed terrain, so they say "confirm in game". Rounded figures are judged as printed: a green shown at 3.0 ft isn't flagged as over 3.

**Green speed.** Two in-game readings set the scale: Cassimbo's 0.97 shows Stimp 10.9 and Sagan-Khushun's 0.905 shows 10.1. The line through them is used, `Stimp = 10.9 + (setting − 0.97) × 12.31`. A third reading would show whether it's straight. −1 is the game's default and asks for a reading.

**Fringe.** Cassimbo and Sagan-Khushun are both set to 0.5 and show no visible fringe in game. Other files have 0.54–0.63, and −1 is the game's default.

**Pins.** Each pin is placed by thirds of its green along the approach (F, C, B) and across it (L, C, R, as the golfer faces the green), with its distance from the green's edge and the slope by the cup. The game's grid square size hasn't been measured, so the edge limit is a stand-in until it is.

## The Cassimbo test

Cassimbo wasn't approved for TGC Tours. With the 300–350 design tee shot, the file alone shows:

- **Length:** the median par 4 is 387 yds and six par 4s are under 400. Hole 3 is a 537-yd par 5 that leaves 178 after a 350 drive; hole 18 is 548.
- **Width:** over 70 yds at 300–350 on 1, 7, 11 and 12; no fairway by the line, or under 25, on 16 and 18. Five holes are wider in the landing zone than the lay-up.
- **Green sizes:** green 2 is 0.30× the median and green 7 is 2.35×. The review said greens were "wildly different", 2 against 7. (A prompt now; see above.)
- **Fairway start:** the median is about 98 yds and only hole 9 starts inside 150–225.
- **Hole 12:** its line passes 22 yds from hole 10's green. The review said hole 12 is played over another green, so the corridor check measures distance to a green rather than only crossing one.
- **Hole 7** climbs 52 ft from tee to green.

`ReadinessTests` holds these as a regression.
