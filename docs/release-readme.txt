COURSE DESIGNER STUDIO - release 1.5.0
Analysis and print products for PGA TOUR 2K25 course designer files.
By Alex C. Fleming

WHAT IT DOES
Reads your .course files and makes printable products from them:
  - Terrain map: contours, relief and the routing for the whole course
  - Hole sheets: a terrain sketch sheet per hole (PNG and PDF)
  - Greens book: slope and fall of every green
  - Yardage book, scorecard map and course cards
  - Construction estimate: what the course might cost to build as a real
    course, with a low / likely / high range (illustrative, not a bid)
It is read only. It never changes or writes a course file.

INSTALL
1. Unzip this folder anywhere (for example Documents\Course Designer Studio).
2. Run CourseDesignerStudio.exe. Nothing else needs installing.
   Keep the "fonts" and "estimate" folders next to the exe; the course
  cards and the construction estimate use them.
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

GOOD TO KNOW
- Terrain is rebuilt from the course's brush strokes, so check critical
  grades in the game. Plots with pre-made terrain (set before any
  brushes) will not map correctly; set it to zero and flatten first.
- Green sites are shown as the ground lies before shaping.

FEEDBACK
Please send Alex any problems, with the course file and a screenshot if
you can. Settings live in %APPDATA%\CourseDesignerStudio\settings.json.
If something goes wrong, please also send crash.log from the same folder.
