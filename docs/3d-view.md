# 3D view

A course's ground as a 3D model you can turn, zoom and stand in. It is the terrain map's own ground (the same brush replay, sea and water) seen from any angle, with the drawn surfaces laid over it, the planted trees and bushes standing on it, and a view down every hole.

It is one web page. Studio shows it in place of the terrain map; the command line writes it as a file that opens in any browser with no network, so it can be sent to someone who doesn't have Studio.

## In Studio

On the **Terrain map** page, **3D view** swaps the map for the model. The first time takes a few seconds while the page is built.

- **Drag** to orbit, **right-drag** (or Shift + drag) to move, **scroll** to zoom, **double-click** the ground to centre on it.
- **Pick a hole** in the Holes strip to stand behind its back tee. The box at the top left then offers **From the tee**, **At the green** and **Fly the hole**.
- **Hole view** goes to the selected hole's tee again; **Whole map** backs out to the whole course.
- The panel at the top right turns trees, contour lines, routing lines, hole numbers and shadows on and off, and moves the sun through the day.
- The readout at the bottom left gives the height and slope under the pointer.
- **Open in browser** opens the same page in your web browser.

The model covers the same area as the map (Map options: routing with its margin, whole plot or buildable ground) and is rebuilt whenever the map is surveyed again, for instance after a "not water" mark. The map's own layers (shading, grid, notes, sketches, routing test) aren't shown in 3D yet.

## From the command line

```
pga2k25 view3d <file.course> [-o <dir>] [--margin 200] [--full-plot] [--grid 1024] [--texture 4096] [--plants 60000] [--cell 1.0]
```

Writes `<course> - 3D View.html` (about 4.6 MB for Sagan-Khushun, built in about 4 seconds). `--grid` is the most ground points along the longer side, `--texture` the ground picture's longer side in pixels, `--plants` the most trees and bushes.

## What it shows, and what it doesn't

| Shown | How |
|---|---|
| Ground | The terrain replay on a regular grid (1.55 m for Sagan-Khushun), cut out as a block with earth sides. |
| Sea | A flat surface at sea level, when the map has a sea. |
| Ponds and lakes | Flat surfaces at their level, inside their traced shores. Painted streams are drawn on the ground. |
| Surfaces | Rough, fairway (with mowing stripes), green and fringe, bunkers, waste, paths and roads, from the same layers the scorecard maps use. |
| Trees and bushes | Every one placed singly, where the file puts it. Scatter groups and planted areas get the right number, at made-up spots (the game scatters them from a seed the file doesn't hold). |
| Holes | Routing line, tee markers, a flag at the first pin, and numbers at tee and green. |
| Contours | Drawn from each pixel's height at the terrain map's interval, every fifth one heavier. |

Not shown, or only roughly:

- **The game's look.** Its tree models, grasses and lighting aren't available. Trees are three stand-in shapes (broadleaf, conifer, bush) sized from the plant's scale and tinted by species.
- **Trees right beside the eye.** Anything of a tree within 20 m of the camera is cut away, so a tee or green among trees still gives a clear view; the tree keeps its shadow.
- **Grass and flowers**, and anything the game plants by itself (that vegetation isn't in the course file).
- **Rocks, buildings and other props.**
- **The sun's real path.** The view opens lit by the course's own sun setting (its orientation as a compass bearing and its inclination as the height; the clock reads "as in game"), so shadows fall the game's way. Moving the slider takes over and moves the sun east, south, west through the day. Reading the orientation as a compass bearing is worked out from how the setting turns the sun, and isn't yet checked against a screenshot.
- **Pre-made hills**, as on the terrain map.

## How it is made

`src/Pga2K25.View3D`:

- **`Scene3D`** turns a `TerrainSurvey` into an engine-neutral scene: heights on a grid, water surfaces, plants and holes. Studio passes the live map's survey (`Scene3D.From`), so the two never disagree.
- **`GroundPicture`** draws the draped picture with SkiaSharp, looking straight down: flat colour only, the page does the lighting.
- **`View3DBuilder`** packs everything into `Web/view3d.html`: the scene as JSON, the heights as gzipped 16-bit steps, the picture as a JPEG, and three.js itself.
- **three.js r159** (MIT) is bundled as `Web/three.min.js`. It is the last release with a classic, non-module build, which is what lets the page be one file.

Two things to know when changing it:

- **Handedness.** The game's ground plane is X/Z with height up, left-handed. three.js is right-handed, so world Z is negated on the way in. Seen from above the model then matches the map and isn't mirrored (checked against Sagan-Khushun's terrain map).
- **Studio's web view** is Avalonia's `NativeWebView` (package `Avalonia.Controls.WebView`, Microsoft Edge WebView2 on Windows, which Windows 10 and 11 already have). It is a native window inside Studio's, so nothing of Studio's can draw on top of it, and it needs `app.manifest` in the Studio project: without the manifest Windows refuses the child window and the view stays blank. Its browser profile is kept in `%LocalAppData%\CourseDesignerStudio\WebView2`, and built pages in `%TEMP%\CourseDesignerStudio\3D view`.

`CourseDesignerStudio.exe <file.course> --3d` opens a course straight into the 3D view.
