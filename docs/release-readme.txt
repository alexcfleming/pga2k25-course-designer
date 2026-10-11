COURSE DESIGNER STUDIO - release 1.0.1
Analysis and print products for PGA TOUR 2K25 course designer files.
By Alex C. Fleming

WHAT IT DOES
Reads your .course files and makes printable products from them:
  - Terrain map: contours, relief and the routing for the whole course
  - Hole sheets: a terrain sketch sheet per hole (PNG and PDF)
  - Greens book: slope and fall of every green
  - Yardage book, scorecard map and course cards
It is read only. It never changes or writes a course file.

INSTALL
1. Unzip this folder anywhere (for example Documents\Course Designer Studio).
2. Run CourseDesignerStudio.exe. Nothing else needs installing.
   Keep the "fonts" folder next to the exe; the course cards use it.
3. Windows may show "Windows protected your PC" because the app is not
   signed. Click "More info", then "Run anyway".
Needs 64-bit Windows 10 or 11.

FIRST RUN
The library lists every .course file in
  Documents\My Games\PGA TOUR 2K25\Courses
Products are saved to
  Documents\PGA 2K25 Greens Books\<course name>
Change either folder in Settings (top right of the library).

USING IT
- Open a course from the library to see its live terrain map. Hover for
  ground height and slope; pick a hole to centre on it.
- Map options (right-hand panel): area (routing, whole plot or buildable
  ground), contour interval, grid and printed resolution. Lower printed
  resolution means smaller terrain map files.
- Build all products, or build each one from its page under Products.
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
