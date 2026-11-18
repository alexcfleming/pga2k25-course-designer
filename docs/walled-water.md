# Walled water

Some designers don't use the retaining-wall tool. They draw a long, thin bunker outline as the wall and fill it with water planes, so the bunker's cut digs the channel and the planes fill it. Alex's Berwick Coast stream is built this way. Since 4.0, Studio recognises the combination and draws and plays it as water held in by a wall, not as sand.

## How it's recognised

`WalledWaterRules.Find` (CourseFormat) looks at every closed bunker outline (surface 0, `state` 3). The outline is walled water when water sits on it, built either of two ways:

- **Water planes:** at least **3** water planes (`water[]` entries) whose centres are inside the outline or within **5 m** of its edge, sharing one level (within 0.01 m). Berwick Coast's stream is one outline about 850 m long and 5 m wide (4,357 m²) holding 55 painted dabs at 8.51 m: 48 inside, the other 7 within 4.4 m of the edge.
- **A water-tool body of water drawn round it:** a closed water-tool path with a set height that holds at least **80 %** of the outline's points (inside it or within 5 m of its edge), where the outline is at least **20 %** of the body's area, so a sand island in a lake stays sand. A tester's course builds its ponds on holes 7–9 this way, the bunker edge lined with retaining-wall pieces (`RetainWallAB/AC/AD`). The walled ponds are 27 %, 60 % and 80 % of the bodies drawn round them.

Its level is the planes' level or the body's height. Compactness (4π × area ÷ perimeter²) under **0.15** makes it a stream; anything rounder is a pond or lake. Berwick Coast's stream scores 0.019, and the tester's ponds 0.63–0.97. The bunker's own depth sliders and the retaining-wall pieces aren't needed for a match. A wall painted with bunker brushes isn't recognised yet.

## What changes

- **Maps (every scorecard style, terrain map, hole sheets, 3D view):** the outline is water, edged with the wall, which is a dark line with pale stones picked out along it (`WaterWall`). Each style uses its own ink: chalk on the Thompson blueprint, pencil on the Coore & Crenshaw sketches. `CourseLayers` keeps the outline in `WaterWalls`, out of the bunkers and apart from `WaterAreas`. Walled water near play is kept whole on the page, like traced ponds.
- **Layering by drawing order:** the game paints later outlines over earlier ones. So walled water is drawn after the turf and covers whatever was drawn before its bunker, such as the heavy rough under the tester's hole 7 pond. Playing surfaces drawn after the wall (fairway, rough, heavy rough, green, cart path, waste) stay on top: `CourseLayers.WaterWallOver` lists them, and the water and wall stop at them, as with the tester's hole 9 fairway at the pond's edge (Alex, 18 Nov 2026). Greens and bunkers drawn after the water on a map still sit on top. The watercolour and pencil hole illustrations wash the water before the turf, so turf can still show over it there.
- **The water isn't traced on its own:** `WaterTracer` leaves out the planes and water-tool bodies that belong to a wall (`WaterOptions.IncludeWalled` puts them back, for study). Berwick Coast's entries are 10 m painted dabs, which trace as a continuous cut stroke 10 m wide. That stroke wandered either side of the 5 m wall.
- **Yardage book:** the outline is water. It gets carry numbers like any water crossing and is drawn with its wall (`HoleAnalysis.WaterWalls`).
- **Hole design simulator, Publish readiness, course difficulty:** the outline plays as water (a penalty), not sand, because they take their surfaces from the same layers.

Before 4.0 the outline was a sand ribbon on every map, with a separate 10 m stroke of water running beside and over it, and every product played the stream as a bunker.
