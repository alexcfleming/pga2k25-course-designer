# Course Difficulty Estimate

One sheet, `<course> - Course Difficulty` (PNG and PDF, 17 × 11.6 in like the course cards), that sums up the hole design simulator's whole-hole play-out (Hole › Summary) over the whole course. New in 3.9.

- **Studio:** Products › Course difficulty. "Build all products" leaves it out, because it takes a minute or more: build it from its own page.
- **CLI:** `pga2k25 difficulty <file.course> [-o <dir>] [--formats png,pdf] [--dpi 300] [--samples 600]`, which also prints each hole's figures.
- **Code:** `CourseDifficulty.Measure` (HoleSim) plays the course; `CourseDifficultyBuilder` and `CourseCardRenderer.DrawDifficultyPage` (CourseCard) draw it.

## How each hole is played

Every hole with a tee, a route and a pin, from the back tee, for **Power Drive** and **Normal**, every pin played 600 times (`HolePlay.Play`). Every hole is played the same way, as `holesim --whole` does:

- the green is the replayed ground smoothed outside greens (`ReplayTerrain.Smoothed`, the green step's surface), and outside it the ground is flat;
- the golfer is fitted to the baseline's anchors (cached in AppData, as in the simulator);
- par 4s and 5s drive at the best line from a quick sweep (400 drives an aim); par 3s use the simulator's par 3 tee shot, which is the same for both players.

No hole's live checks or terrain gate are used, so every hole stands on the same footing. A green the replay makes too steep (over 25 % of it can't hold a stopped ball) gets a hollow ring: the game levels greens, so the replay is probably wrong there and the hole may read harder than it plays.

A hole that can't be played yet (no tee marker, no route, no pins, or no green drawn near its green point) is left out and keeps its row on the scorecard, saying why; the headline and totals say which holes they leave out, and the handicap is numbered by rank until all 18 are played. Before 3.9.1 one such hole stopped the whole build (Nodens Estate, 17 Nov 2026: holes 16 and 18 had no green yet).

On Cassimbo, 18 holes take about a minute once the golfer fit is cached; Nodens Estate's 16 playable holes took 50 s.

## The sheet

**Left: how hard it plays**

- Expected score for each player, and to par. A hole's score is the average over its pins; the round adds the holes.
- Per round: fairways hit (par 4s and 5s), greens in regulation and scrambling (averaged over holes), birdies, bogeys or worse and holes with a penalty (added over holes).
- Where the strokes go: strokes lost to the baseline in each phase (tee, approach, short game, putting) per round, for each player. A phase where the player gains on the baseline draws to the left of zero. The four phases add up to the round's total against the baseline.

**Right: the difficulty scorecard**

- Hole (in its par colour), par, back-tee yards, each player's expected score.
- Over par: each player's strokes over par, coloured green (easier) to red (harder) by the hole's rank.
- Birdie · par · bogey+: the share of play-outs, both players averaged.
- Pin range: the easiest and the hardest pin, over par, both players averaged.
- Rank: 1 the hardest, by both players' average over par.
- HCP: a suggested handicap (stroke index) from the ranking (`CourseDifficulty.StrokeIndex`). On 18 holes the harder nine (more strokes over par in all; a tie goes to the nine with the hardest hole) takes the odd numbers and the other nine the even, each nine hardest first, so a player's handicap strokes fall on both nines. The course's single hardest hole can therefore be 2 when it sits on the easier nine. Other courses are numbered by rank.
- Out, In and Total rows (Total only on a course that isn't 18 holes).

## Reading it

The simulated golfer is fitted on a flat round green and reads harder than the Tour baseline, so the total is an estimate with an offset. The hole ranking is the sturdier figure: compare holes, and use the totals for the size of the gap between players and between nines.
