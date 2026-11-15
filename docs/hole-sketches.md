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
   - Each hole takes its best page. A page that loses its hole to a better page takes its next hole only when that one matched nearly as well (within 0.05).
   - On Murray Estate, Berwick Coast and Sagan-Khushun, every sheet matched at 0.69–0.91, with the next-best hole at 0.22 or less.
   - **Printed heights.** Unless a course's heights are from the sea, a sheet measures them from the plot's lowest point. Reworking the ground there, or a terrain model change, moves that point and so every printed contour. For each scan file, the best-matched page searches for how far its print's heights are off, within one index-contour period around today's (`HoleSketches.FitDatum`):
     - quarter-interval steps, compared by thumbnail;
     - then sixteenths around the best, scored by how much of the print would be left as pencil.
   - The search reuses the hole's ground and re-traces only the contours. When the print is off, every hole is rendered that way too and the pages are matched again. The log gives the shift, for example "read 0.8 ft lower than today's".
   - Contours repeat every index period, so a print off by a whole period (10 ft at 2 ft contours) lines up anyway. Only its labels differ.
   - Wakinyan Dunes' sheets, printed before its low point moved 0.8 ft, matched at 0.30–0.48 (half the colour scans were skipped) and came in as a cloud of contours. Fitted, they match at 0.66–0.85 with no contours left.
   - **Ground reshaped since printing.** A sheet printed before the ground was reshaped has contours no render explains, and matches poorly (0.31–0.50 on Nodens Estate, whose sheets were printed 600 height stamps earlier). A page whose best match is under 0.55 is matched again on what a sheet shows whatever its ground: its dark, neutral ink (the route, its ticks and numbers, the grid's labels), as a coarser thumbnail.
     - On that match a page must beat its next hole by 0.04, since two straight holes of a length look alike.
     - A page still without a hole takes the one its place in the scan gives, when the file's other pages run in hole order and that hole matched within 0.12 of its best. The log says to check its fit.
     - A page that fits no free hole is skipped. Its hole's routing has probably changed since printing, so the sheet's frame is no longer where today's would be.
     - Nodens Estate: 16 of 17 hole pages matched at 0.36–0.67; hole 1, re-routed since, was skipped.
3. **Pencil.** Pencil is grey and darker than anything printed within 3 px on the matching sheet. Printed lines, labels and the route drop out even where the frame fit is a pixel or two off.
   - **Paper that isn't flat.** A phone scan of a curled sheet is still several pixels off in places after straightening. So the sheet is first moved onto the scan tile by tile (`SheetScan.AlignTo`, 40 pt tiles, up to 8 px each). A tile with too little print to tell, or one out of step with its neighbours, takes their median. Pencil within 8 px of the frame is dropped.
   - Coloured ink and the faded blue of the sea are rejected, and specks under 25 px are dropped.
   - Pencil that crosses the printed route or text is lost there in the pencil image; the traced lines bridge it.
   - **Print told by its ink.** When more than a fifth of what is left as pencil is contour brown (or the page was matched on its route and grid), today's contours aren't the printed ones. The page is then compared with the sheet without its brown ink, lined up on black ink alone, so today's contours don't cut the pencil. Printed ink is taken out by what it is made of (`SketchTracer.KeepHandInk`), before the lines are traced:
     - **Brown** (index contours and, on a scan that kept its colour, every contour): warmer, red over blue, than 15 and than the paper by 4. Ink as dark as black pen is never brown, even drawn along a contour.
     - **Green:** the green site's ring.
     - **Hairlines:** a phone scanner that whitens the paper leaves 1 ft contours pale and nearly grey. Ink darkness, measured over the ground around it, then has two peaks; the lighter one goes when the split is clean and that ink is in at least half of the strip's 40 pt tiles (hairlines cover the sheet; a light pencil drawing doesn't).
     - Lines that run along today's route (what is left of the printed one) are dropped, and the pencil image keeps only what is within 2 pt of a traced line.
     - A greyscale scan has no colour to go by, so only the hairline split applies.
     - Nodens Estate hole 12: 2,070 lines and 3,525 construction marks before, 58 lines after. The same holds for older scans whose course has moved on (Murray Estate hole 3: 4,323 and 5,657 before, 84 and 1 after).
4. **Lines.** The pencil is traced into lines:
   - **Growing.** Strokes are grown from confident pixels into fainter ones, so a stroke that fades stays whole.
   - **Tracing.** Strokes are thinned to centre lines and joined through junctions into long strokes, following the straightest way on.
   - **Bridging.** Breaks of up to 6 pt are bridged when both ends point at each other (where a line crossed the printed route or text).
   - **Stubs.** Strokes under 8 px are dropped (what is left of a printed line where it crossed the pencil).
   - **Smoothing.** The strokes are smoothed.
5. **Edges or construction.** Each stroke is judged whole.
   - **Edges:** tee, fairway, hazard and green outlines.
   - **Construction (hidden by default):**
     - a ruler-straight stroke (shot lines from the tee through lettered aim points to the green)
     - on a sheet drawn in two weights, a light stroke that is straight or short (aim dots, letters, hatch ticks), unless it belongs to a cluster of curving line such as a lightly drawn scalloped bunker
   - **Two weights:** the strokes' darkness splits cleanly, with the light group the smaller. Blackwolf Lenz Upper's sheets are pen outlines over pencil shot lines.
   - **One weight:** the sheet only loses its straight strokes.
6. **Reference sheet.** Each page is compared with a fresh render of the hole's sheet and, when present, with the **Hole sheets** build's PNG (`<course> - Hole Sheets/Hole_NN.png`, usually what was printed). Whichever matches better is used.
   - **Every layout:** every hole is rendered as today's strip sheet (with the green detail, since 3.2), as the strip sheet printed before 3.2 (no green detail) and as the full-width sheet printed before 2.5. The page is tied to the ground through whichever it matches, and the log says when it was a pre-2.5 sheet. Only pencil on the strip goes on the ground, so writing over the green detail or the notes stays on the paper.
   - **Notes column:** on a strip sheet, only pencil on the map strip is kept. Writing in the notes column stays on the paper and isn't laid on the ground.
   - Where the ground has changed since printing, only the built PNG still explains the printed contours. A change that only moved the plot's lowest point is handled by the height fit in step 2.
   - If the sheets were rebuilt after printing *and* the ground the sheet shows was reshaped, nothing explains the old contours; they are told from the pencil by their ink (step 3). On a greyscale scan, some will come through as pencil.

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
- Tests: `tests/Pga2K25.Tests/SketchTests.cs`, including an end-to-end import of a rendered, turned and pencilled sheet, one printed 0.8 ft off today's heights on bowed paper (Murray Estate), and one printed before a quarter of the height stamps were removed (the brown-ink test; the hairline split is only exercised by real scans).
