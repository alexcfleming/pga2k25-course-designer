COURSE DESIGNER STUDIO - release 1.9.0
Analysis and print products for PGA TOUR 2K25 course designer files.
By Alex C. Fleming

WHAT IT DOES
Reads your .course files and makes printable products from them:
  - Terrain map: contours, relief and the routing for the whole course
    (PNG and PDF)
  - Hole sheets: a terrain sketch sheet per hole (PNG and PDF)
  - Greens book: slope and fall of every green
  - Yardage book, scorecard maps (four styles) and course cards
  - Construction estimate: what the course might cost to build as a real
    course, with a low / likely / high range (illustrative, not a bid)
It is read only. It never changes or writes a course file.

INSTALL
1. Unzip this folder anywhere (for example Documents\Course Designer Studio).
2. Run CourseDesignerStudio.exe. Nothing else needs installing.
   Keep the "fonts" and "estimate" folders next to the exe; the course
   cards, the yardage book, the Plan Sheet map and the construction
   estimate use them.
3. Windows may show "Windows protected your PC" because the app is not
   signed. Click "More info", then "Run anyway".
Needs 64-bit Windows 10 or 11.

FIRST RUN
The library lists every .course file in
  Documents\My Games\PGA TOUR 2K25\Courses
Products are saved to
  Documents\PGA 2K25 Greens Books\<course name>
Change either folder in Settings (top right of the library).
Also in Settings: "Designer credit". Type your name there to print it on
the products as the course designer. It is blank to start with, and a
blank credit prints nothing.

USING IT
- Open a course from the library to see its live terrain map. Hover for
  ground height and slope; pick a hole to centre on it.
- Map options (right-hand panel): area (routing, whole plot or buildable
  ground), contour interval, grid and printed resolution. Lower printed
  resolution means smaller terrain map files.
- Build all products, or build each one from its page under Products.
- Hole sketches: draw on the printed hole sheets, scan them (a phone
  scanner PDF or photos) and click "Import scans..." in the terrain map
  panel. Each page is matched to its hole and your pencil is laid on the
  map where you drew it. Show one hole or all holes (a colour per hole)
  to spot overlaps between holes.
  Your sketch is traced into clean lines (tee, fairway, hazard and green
  edges). Straight shot lines through lettered aim points are set aside;
  tick "Shot lines and aim points" to show them dashed. Scans imported
  with an earlier version show the old pencil image until imported again.
- Hole view: turns and zooms the map to the selected hole's sheet, tee
  at the bottom. "Whole map" backs out. Mouse wheel zooms, right-drag
  turns the map.
- Notes: "+ Add note", then click the map to pin a build, planting or
  concept note. Notes are saved in Notes.json beside the products.
- Construction estimate: build it from its page under Products. The
  report covers the summary and range, a table per hole, scenarios,
  every assumption and price source, and a cut/fill map per hole. Ground
  inside the play splines counts as built; everything else is natural.
  Many prices are placeholders and are marked as such.
- Estimate inputs (left menu): set a zone class per hole (how much of
  the ground is natural), the fidelity offset, regional factor,
  contingency and fees, and which course-wide items the course needs
  (0 for no practice range, for example). These are saved for that
  course in Estimate.json beside its products. Unit prices can be
  replaced with quotes (with source and year); those apply to every
  course and are kept in %APPDATA%\CourseDesignerStudio\estimate.
  "Save and rebuild estimate" rebuilds the report and opens it.
- Green sizes (new in 1.4.0): the greens book sheet draws every green
  at one scale, so their sizes compare at a glance, with the square
  footage under each. Each green study page shows how the green differs
  from the course mean and median, its size rank, and its outline length
  against a 100-120 yd target. The course cards add a Green Size Card.
  Only drawn greens count; greens the game generates are left out.
- Ponds and water planes (new in 1.5.0): water planes show only where
  the ground dips below the plane, as in the game, so islands stay dry
  and creeks keep their shape. Shallow creeks dug with lower brushes stay
  joined up. Ponds are tinted by depth and labelled with their level on
  the terrain map and hole sheets. They are also drawn in the yardage
  book and on the scorecard maps.
  The yardage book numbers water like other hazards (front edge and
  carry), including short forced carries off the tee. The hole panel
  lists the water each hole plays over or beside. "Water-plane outlines"
  in Map options shows where lowering the ground would flood. Narrow
  blue paths drawn with the water surface now show as walking paths.
- Measurements: type in-game measure-tool readings (for example +13'6")
  to check the terrain model against the game.
- Publish readiness (new in 1.6.0, left menu): the course QA and tour
  checklist in the Studio. The course file answers what it can:
  scorecard and tee totals, same-par runs, drivable par 4s and the par-5
  test (enter your test drive in Limits), green size and landing width
  against the course median, elevation, green relief, lines of play near
  other greens, green speed (as Stimp), fringe and lighting. Mark a flag
  "deliberate" with your reason, or fix it and press "Re-read course
  file". Tick the judgement calls by hole and by stage, set pin
  difficulty, and track playtest winds, tees and pins. Progress saves as
  you go in Readiness.json beside the products. "Import checklist
  backup..." brings in the HTML checklist's Export backup. "Sign off this
  version" ties the sign-off to the course file, and "Export record"
  writes a dated, printable record of the whole checklist when the course
  is done. Every limit can be changed per course.
- Not water in game (new in 1.6.0): if the map shows water somewhere
  that is dry in the game, click "Not water..." on the map toolbar, then
  click that piece. It's marked with a cross, saved in NotWater.json
  beside the products, and left out of every product you build after
  that. Remove marks under Map options.
- Green-site circles (new in 1.6.0): untick "Green-site circles on drawn
  greens" in Map options to hide the 15-yd circle and fall arrow once a
  hole's green is drawn.
- Routing test (new in 1.7.0): turn on "Routing test" on the map toolbar
  to try out a routing quickly. Pick Par 3, Par 4 or Par 5, press on the
  map where the tee goes and drag toward the green. The arrows are fixed
  placeholder lengths (185, 420 and 545 yd) drawn to scale, with a faint
  band showing each par's usual range. Drag an arrow to move it, or drag
  the dashed circle at its head to aim it. The panel adds up total par,
  approximate yards, the walks from each green to the next tee and the
  rise of each hole. Holes number in the order you drop them; the arrow
  buttons in the table renumber them. There is one routing test per
  course, saved in RoutingTest.json beside the products. "Clear routing
  test" deletes it. It stays in the Studio and is never printed.
  (1.7.1 fixes walks of 100 yd or more being cut off in the table.)
- Terrain map PDF (new in 1.7.0): building the terrain map now also makes
  a PDF. Contours, routing and labels stay sharp at any zoom or print
  size, and the file is much smaller than the PNG.
- Illustrated scorecard map (new in 1.7.0): a third scorecard style next
  to Vintage and Modern. It keeps Vintage's parchment and scorecard and
  draws the land like an old engraved map: shaded hills, a canopy for
  every tree you placed, stipple for bushes, lines on the sea and a
  compass rose. Building the scorecard map now makes all three styles.
- Routing test doglegs (new in 1.8.0): par 4s bend once at 275 yd, where
  the drive lands, and par 5s bend at 218 and 436 yd. Select a hole and
  drag the dashed circle at the end of a leg to swing it; the legs after
  it follow, and bends snap to 5 degrees. A hole's yards stay the same
  however it bends. "Straighten" takes the bends out.
  Landing zones show the drive area (250-300 yd) on par 4s and 5s, and
  the layup (420-470 yd) on par 5s, each with its rise from the tee.
  Corridors draw a 70 yd band along each hole and flag holes closer than
  100 yd, centre line to centre line (red overlaps, amber tight).
  Back-to-back holes may sit close at the green and the next tee.
  "Landing zones" and "Corridors" switch on the map toolbar; "Ranges" now
  starts off. Routing tests saved by 1.7 open with straight holes.
- Planting (new in 1.8.0): turn on "Planting" on the map toolbar to see
  the trees, bushes, grass, flowers, walls and fences you placed. Trees
  are drawn at their canopy size, conifers darker ("Trees by type" off
  draws one green). Scatter groups show as dashed circles with their
  count, and planted areas are hatched with an estimate. The Planting
  card counts each layer and lists the tree species. Builds print the
  planting on the terrain map (on by default) and on the hole sheets
  (off by default); hole sheets leave flowers off. Vegetation the game
  generates by itself isn't in the course file and isn't shown.
- Yardage book look (new in 1.8.0): the same information and layout in
  a new style. It has soft fills, gold for yards to the green, rounded
  width tags, a deep green par disc and the Archivo font. Contour lines
  are replaced by a soft hillshade and hachures down the slopes, fainter
  on fairways. Planting is drawn in masses, with conifers darker. The
  designer credit now sits at the bottom right of each page.
- Game budget (new in 1.8.0): the terrain map's side panel shows rough
  Objects and Memory meters for the designer's budget counters, which
  the course file doesn't store. They are estimated from what the file
  holds and fitted to four courses' in-game readings, so treat them as
  a guide; the game's own counters are the real figures.
- Plan Sheet scorecard map (new in 1.9.0): a fourth scorecard style,
  after the hand-drawn course plans of the RGD Artworks Course Graphics
  Dept. (Renaissance Golf Design), who are credited on the sheet before
  the designer credit. The Illustrated map sits in a framed panel beside
  a title column with the course name and scorecard. Red arrows show
  each shot, a dial shows the direction of every hole, and a bar scale
  gives the print ratio. It is always landscape. Building the scorecard
  map now makes all four styles.
- Greens (fixed in 1.9.0): a par 3 whose aim point sits on the green no
  longer collapses that green to a dot in the greens book. A green drawn
  as two overlapping splines is read as one green, so pins stay on it
  and its size and perimeter count the whole green.
- Terrain (improved in 1.9.0): small flatten brushes are read as the
  game's own ground height where they were dabbed. This removes false
  crowns on some greens and fits in-game measurements more closely.

GOOD TO KNOW
- Terrain is rebuilt from the course's brush strokes, so check critical
  grades in the game. Plots with pre-made terrain (set before any
  brushes) will not map correctly; set it to zero and flatten first.
- Green sites are shown as the ground lies before shaping.
- Brushes dragged past the edge of the plot are left out of the terrain
  (new in 1.6.0): the game doesn't apply them, and on one tester's course
  they had sunk the ground and flooded a pond's whole rectangle.

FEEDBACK
Please send Alex any problems, with the course file and a screenshot if
you can. Settings live in %APPDATA%\CourseDesignerStudio\settings.json.
If something goes wrong, please also send crash.log from the same folder.
