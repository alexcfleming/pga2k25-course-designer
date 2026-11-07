# Hole illustrations

One illustrated plate per hole, in watercolour and ink, on US Letter portrait (8.5 × 11 in). The look is after the hole plates posted by Jim Ryan (`jimryangcdesign`), such as the Wakonda Club plates illustrated by Tyler J. Rae, and every plate credits both in its footer, before the designer credit, the same way the Plan Sheet scorecard map credits the RGD Artworks Course Graphics Dept.

```
pga2k25 illustrations <file.course> [-o <dir>] [--holes 1,4,11] [--png] [--no-pdf] [--waste]
                      [--credit "NAME"] [--year 2026] [--verbose]
```

Output: `<course> - Hole Illustrations.pdf` (one plate per page) and, with `--png`, `<course> - Hole Illustrations/Hole_NN.png` at 300 dpi (2550 × 3300 px). The Studio builds both from **Products → Hole illustrations**, with the designer credit from Settings.

Tee set names and hole names come from `Holes.json` in the output folder (the Studio's Holes card). Every number comes from the course file.

## What the plate shows

The plate is the yardage book's per-hole geometry (`HoleAnalysis`) with almost all of the yardage detail left out: no distance arcs, carries, widths, spot heights, green depth or north arrow.

- **Header:** the course name in spaced capitals, the hole's name in italic under it (only when the Holes card has one; there's never a placeholder), and `HOLE NO. n`, `PAR p` and the back-tee yards on the right.
- **Ruler:** a tick every 50 yds up the left side, `0 YDS.` at the back tee. The ruler is fitted to each hole: its top is the back-tee yards plus 30, rounded up to the next 50 (Cassimbo H5 218 yds → 250, H1 374 → 450, H7 629 → 700). The map is drawn at the ruler's scale, so plates of the same par don't share a scale. Ticks measure straight up the page from the back tee, as on the Wakonda plates; they are not distances along a dogleg. On Cassimbo H5 the green sits at 207 on the ruler: 218 yds along the line of play, which bends at an aim point.
- **Art:** play runs up the page (the yardage book's rotation), centred on the line of play's spread. A sage rough wash wraps this hole's fairways, greens, tees, bunkers and the line of play (buffered 28 yds), with a ragged edge 32 yds out; other holes' surfaces, planting, rock and paths fade into the paper beyond it. Then, in order: water, other holes' fairways and sand (pale, unlined), fairways (straw, 12-yd mow bands, faint contour hatching, one ink line round the union), tee pads (5 × 9 m), tree masses (scalloped canopies with a cast shadow and leaf speckle), rock outcrops, paths (white ribbons with grey edges), waste, bunkers, greens (blush pink inside a collar, the firmest ink line) and the dashed line of play from the back tee through the aim points to the green centre.
- **Bunkers:** white sand with stipple, a grey lip, a wobbled ink edge and a hachured face on the side the player looks into: toward the green for a bunker within 45 yds of the green centre, else toward the back tee.
- **Waste:** a merged bunker over 1,500 sq yd (Cassimbo H1 and H5's sandy expanses) is drawn as waste: a peach wash, sparse stipple and a soft line, no hachures. `--waste` also draws surface-15 waste areas this way.
- **Footer:** "AFTER THE HOLE PLATES OF JIM RYAN · ILLUSTRATED BY TYLER J. RAE", then "DESIGNED BY *credit* · *year*" when a credit is set, then "PGA TOUR 2K25 · ILLUSTRATED FROM THE COURSE FILE" and the studio's "Created using Alex Fleming's Course Designer Studio" note; the tee sets and their yards on the right.

Type is Lora only (bundled in `fonts/lora`; Georgia if it's missing, which the build log says).

## How it's drawn

The art is raster, the type is vector. `HoleIllustrationRenderer` renders the art zone (x 147–806, y 132–973 in page units of 1/100 in) at 300 dpi in float RGB over white paper (`WatercolourCanvas`): masks rasterised from the projected shapes, three seeded value-noise fields, Gaussian blur, a Euclidean distance transform for the rough corridor and its fade, multiply washes with pigment pooling at the edges, opaque covers, and ink drawn with Skia and composited in. The noise and every random choice are seeded by the hole number, so a hole renders the same pixels every time. The art feathers to paper inside the zone's edges (more tightly under the tee).

The PDF embeds each plate's art as a JPEG (quality 90) and draws the frame, ruler and every word as vectors on top, so the text is sharp and selectable. A plate is about 0.5 MB.

The hole analysis is the yardage book's, with 30 m behind the tee and 45 m past the green, its window widened and lengthened to cover the whole art zone at the plate's scale (`HoleIllustrationBuilder.Analyze`).

## Tests

`HoleIllustrationTests`: the ruler formula; the back tee on the `0 YDS.` line and the green at its up-page yards on Cassimbo H1, H5 and H7; the hole-name line only when a name is given; byte-identical re-renders; no art at the zone's edges; and a built PDF set in Lora with a PNG per hole.
