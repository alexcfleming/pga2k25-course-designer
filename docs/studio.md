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
- **Settings:** the courses folder, the output folder (default `Documents\PGA 2K25 Greens Books`) and the **designer credit**. The credit is the user's own name, printed on the terrain map, hole sheets, greens book, yardage book and scorecard maps. It is blank by default, and a blank credit prints nothing. Whatever the credit, every product also carries a small note: "Created using Alex Fleming's Course Designer Studio" (`CourseFormat.Attribution`). It goes in the terrain map's notes, the hole sheet, greens book, yardage book and scorecard footers, the course cards' bottom-right corner and above the estimate's footer rule. They're saved in `%AppData%\CourseDesignerStudio\settings.json`.

## Course workspace

The left-hand menu has the course's sections, its products with page counts, and recently opened courses.

- **Terrain map:** drawn live by the same code as the printed terrain map.
  - Relief / Slope / Plain shading; Surfaces, Grid and Hilltops layers.
  - Zoom with the mouse wheel or the buttons, drag to pan, and turn it with right-drag, Shift + drag or the turn buttons. Fit (or **Whole map**) shows the whole map north up.
  - **Hole view** turns and zooms to the selected hole's sheet, tee at the bottom.
  - **Hole sketches:** scanned hole sheets with pencil on them, laid on the ground, one hole or all at once, coloured per hole. See [Hole sketches and notes](hole-sketches.md).
  - **Notes:** pinned to the ground (Build, Planting, Concept, Note), saved in `Notes.json` beside the products.
  - Hovering shows the ground height, slope and grid square.
  - Picking a hole centres on it. The hole panel then shows tee and green heights, the change between them, the green site's tilt and ±ft as it lies, and any in-game measurements for its legs against the model.
  - The model check above the map is the RMS difference over every measured leg.
  - **Map options** (top of the right-hand panel):
    - **Area:** the routing plus a margin, the whole plot, or the buildable ground.
    - **Contours:** the interval (blank for Auto) and how often an index line is drawn.
    - **Grid:** the grid square size.
    - **Water:** whether to show the sea and painted water.
    - **Printed resolution:** px per metre of the built PNG.
    - **Saving:** Apply saves the options with the course and redraws the map; it re-surveys when the area, contours or water change. Reset to defaults restores the standard options.
    - **Toolbar:** shading and layer choices are also saved per course.
- **Products:**
  - **Viewer:** each product's pages as thumbnails, with the selected page large (zoom, pan, fit). Open PDF, Open image, Show in folder and Rebuild are in the toolbar.
  - **Not built yet:** a product shows a Build button instead.
  - **Building:** use the viewer, the products panel, or **Build all products**.
- **Measurements:** in-game measure-tool readings along the route, from the tee marker to the aim point and from the aim point to the green. The yards fill in from the course file. Heights can be typed as the tool shows them (`+13'6"`, `-0'7`, `13 ft 6 in`) or in decimal feet. Readings are saved to `Measurements.csv` in the course's output folder, in the same format as `samples/calibration/route-heights`.
- **Course settings:** a hole-data workbook (tee and hole names, hazards) and a course-card settings file for this course.

## What a Build makes

Products are written to `<output folder>\<course>\`:

| Product | Studio builds |
|---|---|
| Terrain map | The course's saved map options. Defaults: the routing plus a 150 m margin, relief shading, Auto contours, 4 px/m |
| Hole sheets | Every hole: PNGs and one PDF. They use the map options for area, shading, layers and water; each sheet picks its own finer contour interval |
| Greens book | The sheet, a study PNG per green, and the PDF |
| Yardage book | The PDF and a PNG per hole; tee names from the workbook |
| Scorecard map | Vintage and Modern, each as PNG and PDF; names from the workbook |
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
