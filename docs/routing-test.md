# Routing test

A quick way to try out a routing on the terrain map in [Course Designer Studio](studio.md). Drop par 3, par 4 and par 5 arrows, aim them, bend the par 4s and 5s, and read off the total par and approximate yards. Landing zones show where drives and layups finish, and corridors warn when two holes crowd each other. It works on blank plots and routed courses alike. On a routed course the real holes stay on the map underneath, for comparison.

The routing test lives only in Studio: no product draws it, and the course file is never touched.

## Using it

1. Turn on **Routing test** in the map toolbar. A second toolbar row and a Routing test panel appear.
2. Pick **Par 3**, **Par 4** or **Par 5**. Press on the map where the tee goes, drag toward the green and let go. A plain click drops the arrow pointing north. Every arrow starts straight. The par stays picked, so you can drop several in a row; click it again to stop.
3. Click an arrow to select it. It shows a dashed circle at the end of each leg.
   - **Move:** drag one of its legs or its numbered badge.
   - **Aim:** drag the first circle (the end of the leg off the tee), or use **↺ 15°** / **↻ 15°**. The whole hole turns.
   - **Bend:** drag a later circle to swing that leg left or right. The legs after it follow. Bends snap to 5° and stop at 80° either way.
   - **Straighten** takes every bend out. **Delete arrow** removes it.
4. **Landing zones**, **Corridors**, **Walks** and **Ranges** switch their layers on and off. Ranges starts off.

## Arrows

| Par | Length | Usual range | Bends | Colour |
|---|---|---|---|---|
| 3 | 185 yd | 140–240 yd | none | blue |
| 4 | 420 yd | 350–480 yd | at 275 yd, where the drive lands | orange |
| 5 | 545 yd | 500–600 yd | at 218 and 436 yd (40 % and 80 %) | purple |

Lengths are fixed placeholders: every arrow of a par is the same length along its line of play, drawn to true map scale (`RoutingPar` in `src/Pga2K25.TerrainMap/RoutingTest.cs`). Bending a hole never changes its yards. Only the straight-line distance from tee to green gets shorter. The range band follows the line of play and shows how far the green could move shorter or longer. A bearing is degrees clockwise from game north (−Z; east is −X). A turn is degrees clockwise too: plus is right, minus is left.

## Landing zones

A band 40 yd wide laid along the line of play, in the hole's colour:

- **Drive zone**, 250–300 yd from the tee, on par 4s and par 5s.
- **Layup zone**, 420–470 yd from the tee (75–125 yd short of the green), on par 5s.

Each zone is labelled with its rise from the tee, taken from the terrain model the same way as the hole table's rise. Par 3s have none.

## Corridors

Each hole gets a 70 yd band along its line of play (35 yd either side), tinted in its colour. Where two bands cross, the tint gets darker.

Every pair of holes is checked at its closest approach, centre line to centre line, sampled every 10 yd:

- Closer than 70 yd: the corridors **overlap** (red).
- From 70 to 100 yd: they are **tight**, with less than 30 yd between them (amber).

Each warning gets a dashed line between the two closest points, a marker and the distance on the map. The panel's Corridors section lists every pair, closest first, and clicking a row selects the later hole. For back-to-back holes, the last 60 yd of one and the first 60 yd of the next aren't checked, because a green and the next tee are meant to sit close. The numbers are in `RoutingCorridors`.

## The panel

- **Total par** and **approximate yards** (the sum of the placeholder lengths), and how many of 18 holes are down.
- The par mix (`4 × par 3   10 × par 4   4 × par 5`). Past nine holes, Out and In par and yards.
- The total green-to-tee walk and the longest walk.
- A hole table: par, yards, **rise** (ground at the green less ground at its tee, from the terrain model), and the walk to the next tee. A bent hole shows its shape under its row (`L 25°`, `R 20° · L 15°`). Click a hole to select its arrow.
- **The selected hole:** its shape in words ("Dogleg left 25° at 275 yd"), the rise to each landing zone and the green, and whether its corridor is clear.
- **Corridors:** the pairs of holes that overlap or are tight, or an all-clear.
- **Renumbering:** holes number in the order they are dropped. **▲** and **▼** swap a hole with the one before or after; the arrows stay where they are and only the numbers change. **×** removes a hole.

## Saving

There is one routing test per course, saved as `RoutingTest.json` beside the products (with `Notes.json` and `NotWater.json`) after every change. It holds each arrow's par, tee (world X/Z, metres), bearing and bends (the turn at each bend point, in degrees), in hole order. A file saved by Studio 1.7 has no bends, so its holes load straight. **Clear routing test** asks to confirm, then deletes every arrow and the file. There are no versions.
