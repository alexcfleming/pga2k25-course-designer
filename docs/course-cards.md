# Course cards

Four print pages that describe a course's orientation, lengths and green sizes, built from a `.course` file:

| Page | File | Shows |
|---|---|---|
| Compass rose | `<course> - Compass Rose.png/.pdf` (13 × 14 in) | Every hole as a path from a common origin, by bearing, scaled to the longest tee set |
| Hole data card | `<course> - Hole Data Card` (16 × 11.6 in) | Every hole × every tee set, tee and approach bearings, hazards, OUT / IN / TOTAL |
| Course data card | `<course> - Course Data Card` (17 × 11.6 in) | Rose + bar chart in hole order + bar chart sorted by length, with live header totals |
| Green size card | `<course> - Green Size Card` (17 × 11.6 in) | Green area by hole against the mean, median and ±1 SD; difference from the mean; spread and average by par. Only when the course has drawn greens |

The layouts follow the reference matplotlib build from the Cassimbo card handoff, which was approved. The fonts, sizes and figure fractions carry over.

```bash
dotnet run --project src/Pga2K25.Cli -- cards "<file.course>" -o out --config samples/data/sagan-khushun-card.json
```

[Course Designer Studio](studio.md) builds them from the course's card settings file and hole-data workbook, set on its Course settings page.

Options: `--data holes.xlsx`, `--formats png,pdf,svg`, `--dpi 300`, `--wind 225`, `--ocean WEST|none` and `--force`.

## Green size card

- **Area** is the area inside each green's outline, the same smoothed spline the greens book draws (`GreenOutline` / `GreenSizes` in the course format library), so the card, the sheet captions and the study pages always agree.
- **Only drawn greens count.** A green the game generates has no spline in the file, so the greens book uses a stand-in ellipse. Those greens are hatched on the sequential chart, shown as "–" on the difference chart, and left out of the mean, median, SD, CV, range and ranks. The header says which holes they are.
- **Statistics:** mean, median, population SD and CV of the drawn greens. The spread panel draws every drawn green as a numbered dot, over a box for the middle half and the median, and lists each par's average and range.
- A course with no drawn greens gets no green size card; the validation file says so (INFO).

On Sagan-Khushun (16 drawn greens; 16 and 17 estimated) the mean is 6,831 sq ft, the median 6,157 and the SD 2,200 (CV 32 %), from 3,939 (hole 2) to 12,007 (hole 8, a par 3).

## Where the numbers come from

The **course file is the source of truth**.

- **Pars and yardages:** the game's scorecard lengths for each tee set. For each set that is tee → second waypoint → … → green, the same figures `CourseMetadata.teeSetInfos` stores.
- **Bearings:** whole degrees from 0–359, clockwise from north, measured on the routing.
  - Game north is **−Z** and east is **−X**. The handoff note's "X east, Y north" doesn't apply to the file.
  - The tee bearing is back tee → first aim point, and the approach bearing is last aim point → green.
  - Par 3s use straight tee → green for both, matching the workbook convention that they are equal on par 3s.
- **Rose shape:** the corner on a dogleg sits at the route's real corner, as a share of its length. Path length equals the hole's yardage, so a dogleg ends inside its own yardage ring. The paths are drawn from a common origin; this is **not a routing plan**.
- **Optional workbook:** only supplies what the file lacks, namely tee set names, hazard tags and green notes. Its pars, yardages and bearings are compared with the file. Any difference is reported and the file is used.
- **Tee names:** taken from the settings file, then the workbook, otherwise "Back" (a single set) or "Tee 1 … Back".

## Validation (reports, never corrects)

| Check | Severity |
|---|---|
| Exactly 18 holes; par is 3, 4 or 5 | ERROR |
| Tee yardages ascend from forward to back on each hole | ERROR |
| Dogleg bend over 45° between tee and approach bearings | WARN |
| Workbook par, yardage or bearing (over 10° off) differs from the file | WARN, file used |
| Ocean side detected; no wind set | INFO |

- **What gets written:** issues print to the log and go to `<course> - Card Validation.txt`.
- **ERRORs stop the build.** `--force` (command line only) draws anyway for a draft. The errors are still reported.

On Cassimbo the game's own scorecard has hole 4's Red tee longer than its Green (364 vs 362 yds), so a normal build stops. The workbook is also stale on holes 4, 7, 11, 12 and 16. Hand-measured bearings differ from the routing by up to 35° on a few holes.

**Blind spot.** An error that shifts every bearing the same way, such as a course whose bearings are all reversed by 180°, triggers no dogleg warning. Bearings taken from the file can't be reversed this way. Hand bearings in a workbook can be, but the bearing comparison catches them: every hole would differ by about 180°. To check visually, compare the rose against the terrain map. For example, Sagan-Khushun hole 1 is 90° (due east) on both.

## Settings file (JSON)

Everything is optional. Nothing in it is hole data.

```json
{
  "title": "Cassimbo",
  "description": "Arid plateau headland, Angola coast (fictional)",
  "palette": { "par3": "#5F7D8C", "par4": "#C08A2E", "par5": "#9E3B28", "ink": "#262A2C", "salt": "#F2F0EC", "fog": "#B9C3C7" },
  "teeNames": [ "Red", "Green", "White", "Blue" ],
  "oceanSide": "WEST",
  "oceanLabel": "Ocean & cacimbo fog",
  "windFromDegrees": 225,
  "windNote": "onshore",
  "logoPath": "cassimbo-roundel.png",
  "roseMaxYards": 700,
  "ringStepYards": 100,
  "spokeStepDegrees": 30,
  "dpi": 300,
  "formats": [ "png", "pdf", "svg" ]
}
```

- **Palette:** defaults to Cassimbo's "Option A". Sagan-Khushun's ([samples/data/sagan-khushun-card.json](../samples/data/sagan-khushun-card.json)) reuses the colours of its terrain sheets: grid slate, contour brown and flag red.
- **Text on par colours:** switches from salt to ink when a swatch's relative luminance is over 0.55.
- **Ocean side:** when unset, it is detected from the surf props (`OceanChurn…` prefabs), each weighted by 1/d² of its distance to the nearest hole. That gives WEST on Sagan-Khushun and SOUTH-WEST on Cassimbo, whose designer calls it west, so set it when it matters. `"none"` hides it.
- **Wind:** the course file stores no wind. Without `windFromDegrees` no arrow is drawn.
- **Place fallback:** `description`, `oceanSide` and `windFromDegrees` left unset here are taken from the course's `Place.json` in the output folder (the Studio Place card). Values set in this file always win.
- **Logo:** an optional square PNG or JPG. Cassimbo's Voronoi roundel is course art and isn't generated here.

## Type

Lora (SIL Open Font License 1.1) is bundled in `assets/fonts/lora/` and copied next to the app. The cards fall back to an installed Lora, then Georgia.

The approved cards set titles, totals and markers in the regular weight, and so do these. The title uses real letter-spacing, not spaced-out capitals.

## Code

`src/Pga2K25.CourseCard`:

- `CourseCardData`: per-hole data from the course file, totals and validation
- `CourseCardConfig`: settings and `OceanFinder`
- `CourseCardRenderer`: the three pages, drawn in points
- `CourseCardBuilder`: validation and PNG / PDF / SVG output

Tests are in `tests/Pga2K25.Tests/CourseCardTests.cs`. They cover:
- the compass convention
- Sagan-Khushun data and live header totals
- Cassimbo's inverted tees and stale workbook being reported but not corrected
- ERRORs stopping the build unless forced
