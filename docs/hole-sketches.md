# Hole sketches and notes

Sketch on the printed [hole sheets](terrain-map.md#hole-sheets), scan them, and the Studio lays the pencil on the live terrain map where it was drawn. Notes can be pinned anywhere on the ground. Neither changes the course file.

## Importing scans

**Import scans…** in the terrain map's *Hole sketches* panel takes PDFs (a phone-scanner PDF with a page per sheet, such as TurboScan's) or JPG/PNG photos. From the command line:

```bash
dotnet run --project src/Pga2K25.Cli -- sketches <file.course> "Murray Full sketches.pdf" [-o <output dir>] [--sheets <Hole Sheets folder>]
```

For each page:

1. **Frame.** The printed map frame is found and straightened.
   - The left and right sides are line-fitted (RANSAC) to the darkest pixel across a band around where they should be.
   - The top and bottom are looked for only near where the sides end. The header's rule runs just above the frame and would otherwise pull the top line.
   - A perspective warp takes the four corners to a rectangle at 3 px per point.
   - Pages with no frame (a plot map, a cover) are skipped.
2. **Hole.** The page is compared (normalised cross-correlation of small thumbnails) with every hole's sheet, either way up.
   - Page order doesn't matter, and a page that matches nothing (below 0.3) is skipped.
   - On Murray Estate, Berwick Coast and Sagan-Khushun, every sheet matched at 0.69–0.91, with the next-best hole at 0.22 or less.
3. **Pencil.** Pencil is grey and darker than anything printed within 3 px on the matching sheet. Printed lines, labels and the route drop out even where the frame fit is a pixel or two off.
   - Coloured ink and the faded blue of the sea are rejected, and specks under 25 px are dropped.
   - Pencil that crosses the printed route or text is lost there in the pencil image; the traced lines bridge it.
4. **Lines.** The pencil is traced into lines:
   - **Growing.** Strokes are grown from confident pixels into fainter ones, so a stroke that fades stays whole.
   - **Tracing.** Strokes are thinned to centre lines and joined through junctions into long strokes, following the straightest way on.
   - **Bridging.** Breaks of up to 6 pt are bridged when both ends point at each other (where a line crossed the printed route or text).
   - **Smoothing.** The strokes are smoothed.
5. **Edges or construction.** Each stroke is judged whole.
   - **Edges:** tee, fairway, hazard and green outlines.
   - **Construction (hidden by default):**
     - a ruler-straight stroke (shot lines from the tee through lettered aim points to the green)
     - on a sheet drawn in two weights, a light stroke that is straight or short (aim dots, letters, hatch ticks), unless it belongs to a cluster of curving line such as a lightly drawn scalloped bunker
   - **Two weights:** the strokes' darkness splits cleanly, with the light group the smaller. Blackwolf Lenz Upper's sheets are pen outlines over pencil shot lines.
   - **One weight:** the sheet only loses its straight strokes.
6. **Reference sheet.** Each page is compared with a fresh render of the hole's sheet and, when present, with the **Hole sheets** build's PNG (`<course> - Hole Sheets/Hole_NN.png`, usually what was printed). Whichever matches better is used.
   - Where the ground has changed since printing, only the built PNG still explains the printed contours.
   - If the sheets were rebuilt after printing *and* the terrain changed, some old contours will come through as pencil.

Written to `<output folder>/<course> - Sketches/`:

- `Hole_NN lines.json`: the traced lines in world X/Z, each marked edge or construction.
- `Hole_NN pencil.png`: the pencil on a transparent background, at 2 px per point.
- `Hole_NN scan.jpg`: the straightened scan.
- `sketches.json`: per hole, the source file and page, the match, and the **world X/Z of the frame's four corners**.

Because the sketch is tied to the ground, it stays put if the map's area or the routing changes later. Re-importing a hole replaces it; **Remove** in *Imported sheets* deletes it.

## On the map

- **Sketches** (toolbar or panel): *Off*, *This hole* (the selected one) or *All holes*.
  - *All holes* overlays every sheet to show overlaps and conflicts.
  - With **Colour each hole**, each hole's pencil has its own colour (the swatches in *Imported sheets*); the selected hole is drawn on top.
  - **Opacity** sets how strongly the sketches show.
- **Traced lines:** drawn as lines at a steady width at any zoom, in each hole's colour.
  - **Shot lines and aim points** adds the construction lines, thin and dashed.
  - **Raw pencil instead of traced lines** shows the pencil image as scanned.
  - Sketches imported before tracing existed show their pencil image until re-imported.
- **Whole scan for the selected hole:** shows the straightened scan itself instead of its pencil, for checking the fit.
- **Hole view:** turns and zooms the map to the selected hole's sheet frame, tee at the bottom and green at the top, as on paper.
  - Picking another hole while in hole view moves to that hole's sheet.
  - **Whole map** (or **Fit**) backs out, north up.
- **Moving the map:**
  - mouse wheel zooms about the pointer, and drag pans
  - right-drag or Shift + drag turns the map
  - the ⟲ ⟳ buttons turn it 15°
  - the north arrow shows north; click it to turn north up

## Notes

- **Adding:** **+ Add note**, then click the map. The note goes to the selected hole if its route is within 120 m, otherwise to the nearest hole within 120 m, otherwise to the course. Change it with *Belongs to*.
- **Kinds:** Build, Planting, Concept or Note, each with its own pin colour.
- **Editing:** text is saved as you type. Drag a pin to move it, and click a pin or a list entry to select it.
- **Storage:** saved in `<output folder>/Notes.json` (world X/Z, kind, text, hole, times), beside `Measurements.csv`.

## Code

- `SheetScan` (`src/Pga2K25.TerrainMap`): frame finding, straightening, normalising, pencil and matching.
- `SketchTracer`: pencil to lines, and edges vs construction.
- `ScanPages`: page images from PDFs (PdfPig) or image files.
- `HoleSketches`: the import and `sketches.json`.
- `HoleSheet.LayoutFor`: a sheet's window, rotation and frame corners without surveying terrain.
- Studio: `Views/MapSurface` (the map canvas: view transform, sketch layers, pins, animation), `ViewModels/WorkspaceViewModel.Sketches`, `Services/CourseNotes`.
- Tests: `tests/Pga2K25.Tests/SketchTests.cs`, including an end-to-end import of a rendered, turned and pencilled sheet.
