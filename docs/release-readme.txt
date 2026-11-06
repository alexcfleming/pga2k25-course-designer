COURSE DESIGNER STUDIO - release 2.4.1
Analysis and print products for PGA TOUR 2K25 course designer files.
By Alex C. Fleming

WHAT IT DOES
Reads your .course files and makes printable products from them:
  - Terrain map: contours, relief and the routing for the whole course
    (PNG and PDF)
  - Hole sheets: a terrain sketch sheet per hole (PNG and PDF)
  - Greens book: slope and fall of every green
  - Yardage book, scorecard maps (five styles) and course cards
    (including a Wind Card when the course has a wind rose)
  - Construction estimate: what the course might cost to build as a real
    course, with a low / likely / high range (illustrative, not a bid)
And a Hole design simulator that plays thousands of tee shots on one hole
and values where they finish in strokes gained.
It is read only. It never changes or writes a course file. It works
offline: the one online feature (wind data) is off unless you turn it on.

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
blank credit prints nothing. "Allow online wind data" (off to start with)
lets a course fetch real wind records; see Wind rose below.

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
- Place (new in 2.0.0): the Place card at the top of Course settings
  says where the course is, real or invented: up to three place lines
  (for example "Olkhon Island", "Lake Baikal"), a one-line description,
  the sea side (detected from the surf props unless you choose) and the
  prevailing wind. It saves as you type in Place.json beside the
  products. The place lines print under the name on the Plan Sheet map
  and before the terrain map's subtitle, and the course cards take the
  description and sea side from it, and the wind when there is no wind
  rose.
- Locator (new in 2.0.0, on the Place card): "The site" adds a small
  north-up map of the whole plot, its sea and the course to the top of
  the terrain map's panel. "Generated region" invents a landscape round
  the course instead: an island, peninsula, bay, lake shore or inland
  country, its shore turned to face the plot's sea side, with the plot in
  the corner. Pick the shape and size, and press Reroll for another
  coast; the same seed always draws the same coast. Place line 1 names
  the land. It is also drawn faintly behind the Plan Sheet map (untick
  the watermark box to leave it off).
- Wind rose (new in 2.0.0, on the Place card): how often the wind blows
  from each direction, by speed, with the calm hours in the middle.
  "Climate model" invents one for a made-up place: trade winds, maritime
  westerlies, sea-breeze coast, monsoon, inland plains or mountain
  valley, with strength, steadiness and a seed. "Real place" uses ten
  years of hourly wind for a latitude and longitude. To fetch it, turn on
  "Allow online wind data" in Settings, then press "Fetch wind data" on
  the Place card (about 2.5 MB from the free Open-Meteo archive). Nothing
  is fetched until you press it, and the finished rose is saved with the
  course, so it keeps working if you turn the setting off again.
  Builds add a Wind Card to the course cards: the rose, and a table of
  how each hole plays in it (the share of breezy hours into the wind,
  across from the left or right, and downwind), with the holes into the
  wind and downwind. The terrain map's panel shows the rose too, and the
  course data card's wind arrow follows it. The game sets the wind each
  round, so this is a design aid.
- Hole data card (new in 2.0.0): the Hazards and Green Contours & Hole
  Positions columns are filled in from the course file: fairway bunkers
  by carry, cross bunkers, water, greenside bunkers; and each green's
  shape and size, which way it falls, any tier, spine, crown or bowl, and
  where the pins sit. Anything written by hand on the Holes card wins.
  Building the course cards takes about a minute longer for it.
- Terrain map page (new in 2.0.0): "Open PDF" and "Open image" at the
  top open the built terrain map, so you no longer need the folder.

NEW SINCE 2.4.1
- Hole sheets are narrower. Each side now shows 75 yd past the widest
  point the route reaches on that side (tees, aim points and green), or
  75 yd past the tee-to-green line on a side the route never goes. A
  straight hole is about L75 to R75; a dogleg shows more on its corner
  side. Before, a sheet was as wide as the page allowed, about 560 yd
  across on a long par 5. The map is a strip down the left of the page,
  at the same scale as before.
- The space beside the strip holds the hole's design notes from the
  Readiness card: Miss cost and Hazards on par 4s and 5s, and Second
  shot on par 5s. Why wide appears once you've written it, and the
  hole's own note goes last. Written notes print as text. Unwritten ones
  print their question and lines to write on, and the rest of the column
  is ruled. Par 3s get a ruled column.
- Hole sketches: scans of sheets printed before this version still
  import and land in the right place. On the new sheets, writing in the
  notes column stays on the paper and isn't laid on the map.

FIXED IN 2.4.1
- Hole design simulator: the red "Depends on" line under a run's results
  listed the other player's placeholder values too (a Power Drive run
  named the Normal player's carry). It now lists only what the run used:
  the chosen player's values, the ground and scoring values, and the
  terrain values only when the terrain was on. The results were right;
  only the list was wrong.

NEW IN 2.4.0
- Hole design simulator (left menu, under Terrain map): a test session
  for one hole at a time, with the game open in playtest. Pick the hole,
  tee and pin, check the read-back (par, yards, aim marker, fairway
  widths, green, pins, hazards near the line) against the game, and
  confirm every assumption. Values you confirmed before fill in, but still
  need confirming, and unvalidated defaults stay orange. Then run
  thousands of tee shots across aims left and right of the game's aim
  marker for a Power Drive or Normal player. Each finish is valued in
  strokes gained against a PGA Tour baseline (only the published anchor
  values for now, so read the shape, not the exact numbers).
  Live checks: MEASURE spots along the hole, each listed with its yards to
  the tee and to the green, so you can place the cursor by matching the
  game's readout. Type the height against your tee as the game shows it
  (the arrows work), copy GREEN HEIGHT from the tee table, say how much
  of the fairway you can see, and enter a few drives. Readings save as you
  type in a Hole Sim folder beside the products.
  Terrain gate: your readings are compared with the terrain model. Under
  10% of the height range is Trustworthy, 10-18% Indicative, more is
  Check. Only a pass lets runs roll the ball on the slopes and change
  carry for height; otherwise runs stay on flat ground, or you can
  override with a visible "terrain unverified" label.
  Save watcher: while the section is open it notices when the designer
  saves the course again and lists what changed on that hole. It reloads
  only when you click "Reload this hole", so you don't have to back out
  to the library. It never runs anything by itself.
- Course cards for unfinished courses: a course still being built (tees
  out of order, fewer than 18 holes) now gets its cards. Each card has a
  red DRAFT line along the bottom naming the first problem; the full list
  is in Card Validation.txt.
- New order: products are listed, and built by "Build all", as hole
  sheets, course cards, greens book, yardage book, construction estimate,
  scorecard map.
- Bunker merge (Course settings): painted bunker dabs closer than this
  print as one bunker on the scorecard maps. 10 m is the default and
  suits sand painted as scattered dabs. Choose 4 m for a course built
  from tight pot bunker complexes, so each pot stays separate. Rebuild
  the scorecard maps after changing it.
- Publish readiness, Hole QA: the "By hole" tab is now Hole QA, one
  compact grid of every hole's checks. Each check is a small square you
  click through Open, Done, Deliberate, N/A. The three per-hole stages
  are one list of 39 checks in six groups, with your earlier ticks kept.
  Next to each hole, short notes show only the course-file figures that
  are outside a limit. Click a hole number for its card: every figure,
  its pins, every check written out, and a note.
- TGC Tours' published minimums are in the checks: 14 per hole (marked
  with a dot; "TGC minimums" shows only those) and a new course-level
  checklist. From the course file: total par 69-74 with par 3, 4 and 5
  holes only, a range of yardages for each par, and each pin's distance
  from the edge and slope at the cup.
- Design tee shot: one course setting, 300-350 yds by default. Yards
  left, fairway widths and the yardage book's width stations all follow
  it. The test drive stays as a record to check it against.
- Length and width checks (provisional: warnings only, never a pass or
  fail): median par 4 430+, at most 3 par 4s under 400, shortest par 5
  560+, and fairway width at 300, 325 and 350 yds between 25 and 70
  (35-50 is the target; over 60 needs a reason written on the hole).
  Shared fairways count. Also: width swings past 200 yds, and holes
  wider in the landing zone than the lay-up.
- Hole design notes on par 4s and 5s: what a miss costs, what changes
  if you go left, right or over each hazard, the par 5 second shot, and
  why a fairway is over 60 yds. Each is required and closes once
  written. New ticks for hazards a 350 drive flies, blind hazards in the
  landing area, unused fairway, pin run-out, and a Finish group.
- Prompts with no limit: hazards near the line in the landing zone,
  green size against the approach, forward tees reaching the fairway,
  card against straight-line yards, the main fairway start, and green
  size against the course median. Every limit can be changed.
- Retired: drivable par 4s per nine, par 5s leaving 220 after the test
  drive, every tee set 6,500-7,500, and landing width against the
  course median.
- Yardage book: fairway widths at 250 yds and across the design tee
  shot (300, 325 and 350 by default), instead of 200, 250 and 300.

FIXED IN 2.4.0
- Modern Illustrated scorecard map: fairways no longer show darker
  rough-coloured patches. Where two fairway shapes overlapped, the
  overlap was drawn as rough; it is fairway now.
- Terrain map: with a small margin (even 0), green sites at the edge of
  the sheet were cut in half. The sheet now always keeps 20 m round every
  tee and green site. The default 150 m margin map is unchanged.

NEW IN 2.3.0
- Modern Illustrated scorecard map: a fifth scorecard style. It keeps
  Modern's colours and paints the land in: soft hill shading, the sea
  shaded by depth, mown stripes on the fairways, and every tree, bush,
  grass and flower you placed (conifers as darker stars, each tree with
  a shadow). Ponds and streams get a pale shore. The map runs across the
  whole page, with a scorecard strip underneath like the other styles.
  Plants the game scatters in groups or planted areas are spread at
  random inside them, right in number but not plant by plant. Building
  the scorecard map now makes all five styles.
- Scorecard map layout: every style except the Plan Sheet now puts the
  map across the whole page with a scorecard strip underneath: holes
  1-9, Out, 10-18, In, Total, a row for each tee set (up to five,
  longest first) and Par. The map is larger than before and no longer
  shares the page with a tall table of one column. Tall courses still
  get a portrait page, with the strip under the map.
- Compass rose and wind rose on every scorecard map: a compass rose
  turned to the map's north, and the course's wind rose when the Place
  card has one, each in the corner of the map with the least course
  under it. The Plan Sheet shows the wind rose in its title column,
  with the front and back nines side by side above it.
- Course logo (new Logo card in Course settings, under Holes): add your
  course's logo in as many versions as you have - a seal (round, with
  the name), a mark (no text) and a lockup (mark beside the name), each
  in colour, ink (one dark colour) and light. Every product prints the
  version that suits its paper: the seal before the title on the
  scorecard maps (ink on Vintage, Illustrated and the Plan Sheet),
  greens book and course cards, and at the foot of the terrain map's
  panel; the mark in each yardage page's header, on green studies, hole
  sheets and the readiness record; the lockup on the estimate. A missing
  version falls back to the nearest one. The files are copied into a
  Logos folder beside the products.

NEW IN 2.2.0
- Holes card (in Course settings, under Place): tee set names, and a
  name for each hole. The names add a Name column to every scorecard
  table (the four scorecard maps, the hole data card and the Wind Card)
  and go in each yardage book page's header; with no names set there is
  no Name column. You can also write a hole's hazards or green notes by
  hand there; blank boxes show, faintly, what is read from the course
  file. It saves as you type in Holes.json beside the products. Tee names
  typed on the Publish readiness page carry over and stay in step.
- The scorecard maps' table now shows only the championship (longest)
  tees, so hole names have room; the yardage book and hole data card
  still list every tee set. Tee headers and the Out / In / Total figures
  no longer run together in narrow tables.
- The hole-data workbook (.xlsx / .csv) is retired, and its box has gone
  from Course settings. Tee names and hole names come from the Holes
  card; pars, yardages and bearings from the course file; hazards and
  green notes from the course file unless written on the Holes card. If
  you used a workbook, type its tee names (and any hand-written notes)
  into the Holes card once.
- The course data card's prevailing-wind arrow now matches the Wind
  Card: with a wind rose it points from the rose's most common direction,
  named the same way (for example WNW). A wind set by hand that differs
  is noted in the card validation file. Without a rose the arrow follows
  the card settings, then Prevailing wind from on the Place card.

FIXED IN 2.1.0
- Streams and ponds made with the designer's "running water" and
  "create a body of water" tools now show on every map: terrain map,
  hole sheets, scorecard maps, yardage book and hole notes. Before, only
  water planes and painted water appeared. A stream is drawn at its path's
  width; one whose water amount is set near zero holds no water in game
  and is left off.
- Surface weathering (bare-ground or "reverse weathering" splines and
  brushes) is no longer drawn as fairway on the scorecard maps or counted
  as fairway in the yardage book.
- A tree or rock island splitting a fairway still gets its carry numbers
  in the yardage book when it spills past the fairway edges.
- Pins hang from the exact green point the game uses when a hole's last
  two route points sit on top of each other.

GOOD TO KNOW
- Terrain is rebuilt from the course's brush strokes, so check critical
  grades in the game. Plots with pre-made terrain (set before any
  brushes) will not map correctly; set it to zero and flatten first.
- Green sites are shown as the ground lies before shaping.
- Brushes dragged past the edge of the plot are left out of the terrain
  (new in 1.6.0): the game doesn't apply them, and on one tester's course
  they had sunk the ground and flooded a pond's whole rectangle.
- Fetched wind is from the ERA5 reanalysis, whose cells are about 25 km
  across, so speeds read low for an exposed site; directions are
  reliable. Wind data: Open-Meteo.com, CC BY 4.0.
- The hole data card leaves out the sea, cliffs, trees and rocks as
  hazards for now.

FEEDBACK
Please send Alex any problems, with the course file and a screenshot if
you can. Settings live in %APPDATA%\CourseDesignerStudio\settings.json.
If something goes wrong, please also send crash.log from the same folder.
