# Routing test

A quick way to try out a routing on the terrain map in [Course Designer Studio](studio.md). Drop par 3, par 4 and par 5 arrows, aim them, and read off the total par and approximate yards. It works on blank plots and routed courses alike. On a routed course the real holes stay on the map underneath, for comparison.

The routing test lives only in Studio: no product draws it, and the course file is never touched.

## Using it

1. Turn on **Routing test** in the map toolbar. A second toolbar row and a Routing test panel appear.
2. Pick **Par 3**, **Par 4** or **Par 5**. Press on the map where the tee goes, drag toward the green and let go. A plain click drops the arrow pointing north. The par stays picked, so you can drop several in a row; click it again to stop.
3. Click an arrow to select it.
   - **Move:** drag its shaft or numbered badge.
   - **Aim:** drag the dashed circle at its head, or use **↺ 15°** / **↻ 15°**.
   - **Delete arrow** removes it.
4. **Walks** shows a dashed line from each green to the next tee, with its yards. **Ranges** shows each par's usual length as a band near the arrow head.

## Arrows

| Par | Length | Usual range | Colour |
|---|---|---|---|
| 3 | 185 yd | 140–240 yd | blue |
| 4 | 420 yd | 350–480 yd | orange |
| 5 | 545 yd | 500–600 yd | purple |

Lengths are fixed placeholders: every arrow of a par is the same length, drawn to true map scale (`RoutingPar` in `src/Pga2K25.TerrainMap/RoutingTest.cs`). The range band shows how far the green could move shorter or longer. A bearing is degrees clockwise from game north (−Z; east is −X).

## The panel

- **Total par** and **approximate yards** (the sum of the placeholder lengths), and how many of 18 holes are down.
- The par mix (`4 × par 3   10 × par 4   4 × par 5`). Past nine holes, Out and In par and yards.
- The total green-to-tee walk and the longest walk.
- A hole table: par, yards, **rise** (ground at the arrow head less ground at its tee, from the terrain model), and the walk to the next tee. Click a hole to select its arrow.
- **Renumbering:** holes number in the order they are dropped. **▲** and **▼** swap a hole with the one before or after; the arrows stay where they are and only the numbers change. **×** removes a hole.

## Saving

There is one routing test per course, saved as `RoutingTest.json` beside the products (with `Notes.json` and `NotWater.json`) after every change. It holds each arrow's par, tee (world X/Z, metres) and bearing, in hole order. **Clear routing test** asks to confirm, then deletes every arrow and the file. There are no versions.
