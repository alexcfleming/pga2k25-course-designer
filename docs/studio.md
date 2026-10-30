# Course Designer Studio

The desktop app (`src/Pga2K25.Studio`, Avalonia 12 on .NET 8). It reads course files and never writes them.

```bash
dotnet run --project src/Pga2K25.Studio -c Release
```

Pass a `.course` path on the command line to open straight into that course.

## Library

- **What's listed:** every `.course` in the courses folder (default `Documents\My Games\PGA TOUR 2K25\Courses`), plus any added with **Add course…**.
- **Each card shows:**
  - the in-game thumbnail saved in the file
  - its status: *Routed*, *In progress* (greens or fairways drawn) or *Blank* (no holes)
  - holes, par and back-tee yards
  - when it was last saved
  - which products are already built
- **Finding a course:** filter by status, or search by name.
- **Settings:** the courses folder, the output folder (default `Documents\PGA 2K25 Greens Books`) and the **designer credit**. The credit is the user's own name, printed on the terrain map, hole sheets, greens book, yardage book and scorecard maps. It is blank by default, and a blank credit prints nothing. **Allow online wind data** (off by default) lets a Place card fetch a real place's wind record; with it off Studio makes no network calls. Whatever the credit, every product also carries a small note: "Created using Alex Fleming's Course Designer Studio" (`CourseFormat.Attribution`). It goes in the terrain map's notes, the hole sheet, greens book, yardage book and scorecard footers, the course cards' bottom-right corner and above the estimate's footer rule. They're saved in `%AppData%\CourseDesignerStudio\settings.json`.

## Course workspace

The left-hand menu has the course's sections, its products with page counts, and recently opened courses.

- **Terrain map:** drawn live by the same code as the printed terrain map.
  - Relief / Slope / Plain shading; Surfaces, Grid and Hilltops layers.
  - Zoom with the mouse wheel or the buttons, drag to pan, and turn it with right-drag, Shift + drag or the turn buttons. Fit (or **Whole map**) shows the whole map north up.
  - **Hole view** turns and zooms to the selected hole's sheet, tee at the bottom.
  - **Hole sketches:** scanned hole sheets with pencil on them, laid on the ground, one hole or all at once, coloured per hole. See [Hole sketches and notes](hole-sketches.md).
  - **Notes:** pinned to the ground (Build, Planting, Concept, Note), saved in `Notes.json` beside the products.
  - **Not water…:** shown when the course has modelled ponds or creeks. Turn it on, then click a piece of water that is dry in game. It's marked with a ✕ pin, saved to `NotWater.json` beside the products, and the map re-surveys without it. Rebuild the products to update them. The marks are listed under Map options, each with Remove.
  - **Planting:** the trees, bushes, grass, flowers and walls placed in the designer, drawn into the map by the terrain renderer, with layer toggles and **Trees by type**. Its panel card counts each layer (≈ for scatter groups and planted areas) and lists the tree species. **On printed sheets** decides whether builds print it: on for the terrain map, off for hole sheets by default. Generated vegetation isn't shown. See [terrain-map.md](terrain-map.md#planting).
  - **Routing test:** drop par 3, 4 and 5 arrows of fixed placeholder length (185 / 420 / 545 yd), aim them, and bend par 4s and 5s into doglegs. Landing zones show drive and layup areas with their rise, and corridors flag holes that crowd each other. The Routing test panel adds up total par, approximate yards, walks and rise, and its table renumbers holes. Saved to `RoutingTest.json` beside the products; Studio only. See [routing-test.md](routing-test.md).
  - Hovering shows the ground height, slope and grid square.
  - Picking a hole centres on it. The hole panel then shows tee and green heights, the change between them, the green site's tilt and ±ft as it lies, and any in-game measurements for its legs against the model. **Water in play** lists the ponds and creeks the hole plays over and those within 30 m of its line (`TerrainMap.HoleWater`):
    - **Over the line:** the front and carry yards where the line of play enters and leaves the water, plus the yards from there to the green.
    - **Beside the line:** which side the water is on, how far off, and at what yardage.
    - Yards are measured on the line itself, so a carry can read a few yards shorter than the yardage book. The book takes the farthest bank within 15 m of the line, which matters where a creek crosses at an angle.
  - The model check above the map is the RMS difference over every measured leg.
  - **Map options** (top of the right-hand panel):
    - **Area:** the routing plus a margin, the whole plot, or the buildable ground.
    - **Contours:** the interval (blank for Auto) and how often an index line is drawn.
    - **Grid:** the grid square size.
    - **Water:** whether to show the sea, ponds and streams. **Water-plane outlines** dashes each plane's rectangle (where lowering the ground would flood). It is off by default and only redraws, without re-surveying. **Green-site circles on drawn greens** (on by default) can be turned off to drop the 15-yd circle and fall arrow wherever a green spline is drawn; greens not yet drawn keep theirs. It also only redraws.
    - **Printed resolution:** px per metre of the built PNG.
    - **Saving:** Apply saves the options with the course and redraws the map; it re-surveys when the area, contours or water change. Reset to defaults restores the standard options.
    - **Toolbar:** shading and layer choices are also saved per course.
- **Products:**
  - **Viewer:** each product's pages as thumbnails, with the selected page large (zoom, pan, fit). Open PDF, Open image, Show in folder and Rebuild are in the toolbar.
  - **Not built yet:** a product shows a Build button instead.
  - **Building:** use the viewer, the products panel, or **Build all products**.
- **Measurements:** in-game measure-tool readings along the route, from the tee marker to the aim point and from the aim point to the green. The yards fill in from the course file. Heights can be typed as the tool shows them (`+13'6"`, `-0'7`, `13 ft 6 in`) or in decimal feet. Readings are saved to `Measurements.csv` in the course's output folder, in the same format as `samples/calibration/route-heights`.
- **Course settings:** the course's **Place**, a hole-data workbook (tee and hole names, hazards) and a course-card settings file for this course.
  - **Place** (`CoursePlace`) says where the course is, real or invented: up to three place lines, a one-line description, the sea side and the prevailing wind. It saves as you type to `Place.json` in the course's output folder, and every product reads it from there. The place lines print under the name on the Plan Sheet scorecard map (`--location` overrides them) and before the terrain map's subtitle. The description, sea side and wind fill the course cards wherever the card settings file leaves them unset. "Detect from the plot" keeps the cards' surf-prop detection, shown under the box. **Locator** "The site" adds a site inset to the terrain map; "Generated region" adds an invented landscape round the course instead (shape, seed with **Reroll**, size, and a watermark box for the Plan Sheet), with a live preview on the card (see [Terrain map](terrain-map.md)). **Wind rose** adds a Wind Card to the course cards and a wind block to the terrain map, with a live preview and hole summary on the card:
    - *Climate model (invented place)* (`CourseFormat.WindClimate`): trade winds, maritime westerlies, sea-breeze coast, monsoon, inland plains or mountain valley, with strength, steadiness and a seed with **Reroll**. Directions are a mix of von Mises lobes round the compass, speeds a Weibull curve per lobe; the main wind blows from "Prevailing wind from" (west if not set) and the sea-breeze lobe blows onshore from the sea side. The same settings always give the same rose.
    - *Real place (wind record)*: latitude and longitude, and a **Fetch wind data** button that shows only when Allow online wind data is on. Pressing it downloads ten whole years of hourly 10 m wind (ERA5 reanalysis from the free Open-Meteo archive, CC BY 4.0, about 2.5 MB; `Services.WindDataFetcher`), bins it into 16 directions × mph bands (`WindRose.FromOpenMeteo`) and saves the rose in `Place.json` with its source and date. Nothing is fetched otherwise. A saved record keeps working when the setting is turned off; until one is saved the climate model stands in. ERA5 cells are about 25 km across, so speeds read low on an exposed site; directions are reliable.
- **Publish readiness:** the QA and tour-readiness checklist for getting a course ready to publish (see [Publish readiness](readiness.md)). The checks re-run when the section first opens and on **Re-read course file**. Every change saves at once to `Readiness.json` beside the products.
  - **Overview:** the publish gate and overall progress, the course-file findings and progress by stage. A Flag or High finding can be marked deliberate with its reason, and **Show hole** jumps to the hole on the terrain map. The limits can be edited here; **Apply and re-check** runs the checks again.
  - **By hole:** the file's measures for every hole (test drive, green size, landing width, tee to green, green relief, nearest other green), with cells outside a limit shaded. Next to them sit the judgement calls for one stage (tour readiness, per-hole checks or planting passes), clicked through Open, Done, Deliberate, N/A. **Size deliberate** marks one hole's green size and landing width as deliberate.
  - **Checklists:** the pre-build gate, the course-level tour checks, the build checks and the publish gate. Each item has its status, source, note, and, when the file answers it, the evidence. An item the file answers counts as done until you set it yourself. You can add your own checks.
  - **Pins and playtest:** each pin's place on its green from the file, with its difficulty clicked through hard, medium, easy. Two playtest profiles track the winds played at high intensity, the tee sets and the pins.
  - **Publish gate:** the publish items, what still blocks the gate, designer, version and tee names, the sign-off, and the lighting recorded from the file. **Sign off this version** ties the sign-off to the course file's fingerprint, so a later change re-opens the gate.
  - **Import checklist backup…** reads the HTML checklist's Export backup and brings in the course of the same name, or asks which. It only fills what's still open or empty.
  - **Export record** writes `<course> - Readiness record <date>.html` (printable) and `.json` into the output folder: the final record of the course.

## What a Build makes

Products are written to `<output folder>\<course>\`:

| Product | Studio builds |
|---|---|
| Terrain map | The PNG and a PDF (vector contours and text, 250 dpi page) from the course's saved map options. Defaults: the routing plus a 150 m margin, relief shading, Auto contours, 4 px/m |
| Hole sheets | Every hole: PNGs and one PDF. They use the map options for area, shading, layers and water; each sheet picks its own finer contour interval |
| Greens book | The sheet, a study PNG per green, and the PDF |
| Yardage book | The PDF and a PNG per hole; tee names from the workbook |
| Scorecard map | Vintage, Modern, Illustrated and Plan Sheet, each as PNG and PDF; names from the workbook |
| Course cards | From the course-card settings file and workbook; errors stop the build |

Everything else is on the command line (`src/Pga2K25.Cli`): hole subsets, scorecard orientation and waste, forcing a draft course card, and so on. See each product's page in `docs/`.

## Packaging a release

```
powershell -ExecutionPolicy Bypass -File tools\package-release.ps1
```

This writes `CourseDesignerStudio-<version>-win-x64.zip` to `releases\` beside the repo (or `-OutDir <folder>`). The version is the `<Version>` in the Studio project. The zip contains:

- **`CourseDesignerStudio.exe`:** self-contained and single-file, so testers don't need to install .NET. 64-bit Windows 10 or 11.
- **`fonts\lora`:** the course cards load the fonts from beside the exe. The single-file bundler drops them, so the script copies them in.
- **`README.txt`:** for testers, from `docs/release-readme.txt`.

The exe isn't signed, so Windows SmartScreen asks testers to confirm the first run (More info → Run anyway).

## Notes for development

- **XAML loading:** Avalonia 12's XAML source generator needs a newer C# compiler than the .NET 8 SDK has. The views load their XAML with `AvaloniaXamlLoader` and look up named controls with `FindControl`, not `InitializeComponent`.
- **SkiaSharp:** Avalonia 12.1.3 runs on the libraries' SkiaSharp 4.152.
- **Earlier app:** the WPF app (`src/Pga2K25.App`) was retired on 2026-10-03. Its last version is tagged `wpf-app-final`.
