# Rocks

Rocks can be a big part of a course: shoreline boulders (Sagan-Khushun's are placed at scale 30–50), a ridge of rocks half
buried in a hill (Nodens Estate, hole 5), or the rock half of a pond's barrier with a retaining wall on the other side (a
tester's course, hole 7). Studio draws each rock as it shows above the ground.

## What a course records

Each rock is a shape (one of dozens of prefabs), a position with its height, a turn about the vertical (yaw) and one scale
applied to all three axes. Rocks never tilt. A height stored as `-Infinity` means the rock sits on the ground wherever it is.
The size of each shape isn't recorded, so it was measured in game.

## The measured shapes (`assets/rocks/catalogue.json`)

Two calibration plots placed every rock shape found in 26 course files at scale 1 and no turn on flat ground, some
sitting on the ground and some sunk. On each one, Alex measured with the game's measure tool:

- **Width:** a line across the rock where it meets the ground (north to south).
- **Shadow:** a line along its shadow. A "ladder" of one cliff sunk 0, 4, 8, 12 and 16 m gave the sun's height (17.4°,
  each metre of sinking shortening the shadow by 3.2 m), which turns every shadow into a height.
- **Sunk width:** how wide each shape still shows when sunk as designers usually sink it.

The measure tool's points sit on the ground, not on the rock, so heights come from shadows. Shadows of thin tops measure a
little short, so a shape's height is the larger of its shadow height, the deepest sinking where it still showed, and 10%
over the depth (per unit of scale) that 90% of its uses in those course files stay within: nobody places a rock they can't see.

| Kind | Width at scale 1 | Height at scale 1 |
|---|---|---|
| Stones and smooth rocks (Stone01, Rock1B, RP rocks, PV boulders) | 1–4 m | 0.3–1 m |
| SinkHole small and tiny | 1.3–4 m | 0.7–1.1 m |
| SinkHole medium (tall pillars) | 8–17 m | 12–26 m |
| Clusters, cliffs and rock mountains | 19–78 m | 6 to over 100 m |

40 shapes are measured. A shape that wasn't takes its family's figures (the same name without its last part, such as the
other `SinkHole_Rocks_Medium` shapes), and one with no family is drawn as a typical stone (2.4 m wide, 0.7 m tall).

## Each rock's outline (`RockOutlines`)

1. **How deep it sits:** the ground under the rock (the replayed terrain) less its stored height.
2. **How much shows:** the shape's width at that depth: its ground width, narrowing in a straight line to its measured sunk
   width, then on to nothing at its top. Narrower than 0.3 m, it isn't drawn.
3. **Its outline:** a ring for the shape, the same for every copy (as in game, where one rock is reused turned and scaled),
   turned by the rock's yaw and scaled. Craggy shapes (sinkhole pillars, cliffs, formations, mountains) get a rougher ring.

## Where rocks are drawn

- **Scorecard maps,** after the turf and walled water and before the lines of play, in each style's look: painted stone lit
  from the north-west with a soft shadow on Modern, Modern Illustrated and Hanse; ink outlines hatched on the shaded side on
  Vintage, Illustrated, RGD Style 1, Travis, MacKenzie and Thompson (in chalk); flat stone on Routing Plan, RGD Style 2 and
  Cutten.
- **Course Portrait,** in stone grey with a shaded edge and shadow.
- **Yardage book, hole sheets, hole illustrations and hole notes,** as outcrops: nearby rocks merged into one shape.
- **3D view,** on the ground picture.
- The **terrain map** leaves rocks off, as it leaves off waste areas: it's for planning the golf.

## Known limits

- **One direction measured:** only each shape's width north to south was measured, so its length is a guess (1.15–1.6 times
  its width). A long slab may be drawn rounder than it is.
- **Shapes not yet measured** use their family's size; a new pack's shapes are drawn as stones until measured.
- **The ground model:** how much of a half-buried rock shows depends on the replayed ground under it. Where the ground model
  is off (see the terrain map's known limits), a deeply sunk rock can show too much or too little.
- **The sun used for heights:** the sun's height on the calibration plots came from a ladder of shadows. The course's
  own sun settings, read as a path through the day, put it within 1.5° of that.
