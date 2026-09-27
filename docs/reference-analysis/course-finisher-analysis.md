# Reference Application Analysis — *Mal's TGC 2K25 Course Finisher*

| | |
|---|---|
| Binary | `reference/Mals_TGC_2K25_Course_Finisher.exe` (73.8 MB, PE32+ x64 GUI) |
| App version | `v0.10 REV10E` (window title); startup log still says `REV10D`. Internal `FINISHER_VERSION = 10` |
| Build timestamp | PE header 2026-09-27 |
| Author tag | `malificaCourseFinisher` (the provenance key written into course files) |
| Analysis method | **Static only.** The executable was never run. The PyInstaller archive was unpacked with a purpose-written parser, and the Python 3.11 bytecode was disassembled with `xdis` in an isolated scratch venv. No reference source is committed to this repo. |

The companion document [../course-format/pga2k25-course-format.md](../course-format/pga2k25-course-format.md) specifies the `.course` file format in detail. This document covers the application itself: packaging, architecture, UI, features, algorithms, and how it reads and writes course data. It ends with recommendations for building our own app in the same pattern.

---

## 1. Executive summary

- **What it is.** A *post-processor* for PGA 2K25 courses that were already built, typically imported from LiDAR/heightmaps with the community "TGC Tool". It reads a `.course`, runs a pipeline of optional automated "finishing" passes (spline smoothing, tee creation and levelling, smart pins, heavy-rough edging, cart-path levelling, native water, green-slope compression, weathering), and writes a **new** `.course` plus an optional PDF greens book.
- **It is not an editor.** There is no map view and no interactive editing. The UI is a form of checkboxes plus a log pane. All of the value is in the domain algorithms.
- **Tech stack:** Python 3.11 + Tkinter/ttk, NumPy, SciPy (`cKDTree`, `ndimage`), Pillow (greens book), packaged as a PyInstaller one-file executable. Matplotlib is bundled (≈95 modules) but not referenced by the app code; it is packaging bloat.
- **Code shape:** two modules. `tgc_finisher.py` (≈300 lines) is the UI shell. `course_finisher.py` (≈7,140 lines, ~250 functions, 3 classes) is a procedural domain library with one orchestrator, `finish_course()`.
- **Key design patterns worth copying:** a pure-function operation pipeline over an in-memory JSON document, **tail-append edit logs with provenance/signatures** for idempotent re-runs, KD-tree terrain sampling, and a strict "never overwrite the source" rule.
- **Key weaknesses to avoid:** lossy float rounding on save, `CourseMetadata` never regenerated, processing blocks the UI thread, a 7 k-line single module, and hard dependency on imported fine-terrain data. None of our three sample courses satisfy that dependency (see §7.1).

---

## 2. Packaging and runtime

| Aspect | Finding |
|---|---|
| Bootloader | PyInstaller ≥ 5 one-file (`MEI\x0c\x0b\x0a\x0b\x0e` cookie). Extracts to `%TEMP%\_MEIxxxx` on each launch, so it starts slowly |
| Python | `python311.dll` |
| Archive contents | 1,401 entries: 1,387 binaries/data files (Tcl/Tk runtime, SciPy/NumPy `.pyd`s), 7 scripts, 1 `PYZ` with 983 modules |
| Entry script | `tgc_finisher` (plus PyInstaller runtime hooks for tkinter, multiprocessing, inspect, pkgutil and matplotlib config) |
| App modules in PYZ | `course_finisher` only. Everything else is stdlib or third-party |
| Third-party libs (PYZ counts) | scipy 371, numpy 117, PIL 95, matplotlib 95, dateutil, pyparsing, contourpy, kiwisolver, cycler, certifi, packaging |
| Size drivers | SciPy is the bulk. The app uses only `scipy.spatial.cKDTree` and `scipy.ndimage` |
| Network | None observed (no `requests`/`urllib` use in app code) |
| Persistence | None. No settings file, registry or recent-files list. Every run starts from defaults |

---

## 3. Architecture

### 3.1 Module and layer diagram

```
┌──────────────────────────── tgc_finisher.py (UI shell) ────────────────────────────┐
│ class App                                                                          │
│   __init__  → builds Tk widgets + ~30 tk.*Var option bindings                      │
│   pick_input / pick_output  → file dialogs (default output "<stem> - Finished")    │
│   analyze   → course_finisher.load_course + analyze_course → summary label + log   │
│   process   → validates inputs → course_finisher.finish_course(**options, printf)  │
│   print     → appends to ScrolledText + update_idletasks()   (the "printf" sink)   │
└──────────────────────────────────────┬─────────────────────────────────────────────┘
                                       │ plain function calls (same thread)
┌──────────────────────────────────────▼──────── course_finisher.py (domain) ────────┐
│  I/O          get_course_version · load_course · _decode_inner · save_course        │
│  Geometry     spline_dense_polyline · _bezier_segment · point-in-polygon ·          │
│               polygon area/PCA/centroid · half-plane clipping · support polygons    │
│  Terrain      CourseTerrainSampler · WaterTerrainSampler · SmartPinTerrainSampler   │
│  Classify     classify_spline_features · find_cartpaths · find_water_splines ·      │
│               find_active_tee_regions · detect_tgctool_background_terrain          │
│  Operations   designer_smooth_selected_splines · optimize_selected_splines ·        │
│  (passes)     paint_heavy_rough_on_detailed_terrain · generate_native_water… ·      │
│               apply_cartpath_flatten · smart_complete_tees · smart_flatten_tees ·   │
│               apply_ultra_speed_safe_greens · smart_place_pins ·                    │
│               apply_topographic_weathering · export_greens_book                     │
│  Provenance   remove_prior_* · *_tail_signature · FINISHER_KEY metadata             │
│  Reports      analyze_course (before/after)                                         │
│  Orchestrator finish_course(input, output, 30+ flags, printf) → report dict         │
└─────────────────────────────────────────────────────────────────────────────────────┘
```

### 3.2 Architectural patterns

| Pattern | How it's used | Adopt? |
|---|---|---|
| **Document-in-memory pipeline** | `load → mutate dict in place through N passes → save`. Every pass has the signature `(course_json, course_version, …, printf) → report dict` | ✅ Yes. Model it as `ICourseOperation` with `Analyze()` / `Apply()` |
| **Orchestrator with fixed order** | `finish_course` runs passes in a deliberate sequence (below). Order encodes dependencies: spline edits come before terrain sampling, and tees come before pins | ✅ Yes, with explicit dependency declarations instead of hidden ordering |
| **Injected logger (`printf`)** | Every pass receives a callable. The UI passes `App.print` | ✅ Use `IProgress<T>` / `ILogger` + structured events |
| **Before/after report** | `analyze_course` runs before and after; each pass returns stats merged into `report` | ✅ Yes. Great for a diff/summary panel |
| **Provenance & idempotency** | Generated edits are appended as a *tail* and recorded under `course_json["malificaCourseFinisher"][feature] = {start, count, signature(sha256)}`. Re-runs remove the prior tail only if the signature still matches; otherwise they preserve it and log a warning | ✅ Strongly adopt. This is the best idea in the tool |
| **Version gate** | Every pass raises unless `get_course_version()==25` | ✅ Yes, via a format-capability check |
| **Magic-number constants** | ~150 module-level tuning constants (`TEE_CREATE_*`, `SMART_PIN_*`, `WATER_*`…) | ✅ Adopt as a typed, user-overridable settings object |
| **Procedural module** | All logic in one 7 k-line file of free functions | ❌ Split into feature modules/classes |
| **Synchronous UI** | `finish_course` runs on the Tk main thread; `update_idletasks()` only repaints the log | ❌ Run on a background task with cancellation |

### 3.3 Pipeline order (`finish_course`)

```
load_course ─► version==25 gate
  ├─ (if flatten_tees)       remove_prior_tee_flatten, _remove_untracked_rev2_tee_tap_tail
  ├─ (if weathering)         remove_prior_topographic_weathering
  ├─ report.before = analyze_course()
  1  designer_smooth_selected_splines   (tee/green/fairway/rough/bunker/cartpath)
  2  optimize_selected_splines          (waypoint reduction)
  3  paint_heavy_rough_on_detailed_terrain
  4  generate_native_water_from_splines
  5  apply_cartpath_flatten
  6  smart_complete_tees
  7  smart_flatten_tees
  8  apply_ultra_speed_safe_greens
  9  smart_place_pins
  10 apply_topographic_weathering
  11 export_greens_book  (PNG per hole + PDF, next to output)
save_course ─► report.after = analyze_course() ─► return report
```

---

## 4. User interface

### 4.1 Window

- Title: `Mal's TGC 2K25 Course Finisher v0.10 REV10E`
- Size: 75 % × 88 % of the screen (at least 1100×860), centred; min size ≈ 1180×820; fallback 1440×950.
- Layout: a horizontal `ttk.Panedwindow` split 3:2 between an options column and an info column.

```
┌───────────────────────────── options (weight 3) ─────────────────────┬──── info (weight 2) ─────┐
│ [Course File]                                                         │ [Course Analysis]         │
│   Input .course  [______________________________] [Browse...]         │  Course: … | version: 25  │
│   Output .course [______________________________] [Browse...]         │  Fine terrain: Brush …    │
│ (Analyze Course)                                                      │  Feature splines: …       │
│ [1. Designer Smooth Splines]                                          │  Tee markers / Pins …     │
│   ☐ Tee Boxes  ☐ Greens  ☐ Fairways / ☐ Rough ☐ Bunkers ☐ Cart Paths  │  Cart paths / Water …     │
│ [2. Optimize Splines]                                                 ├───────────────────────────┤
│   ☐ Tee Boxes  ☐ Greens  ☐ Fairways / ☐ Rough ☐ Bunkers ☐ Cart Paths  │ [Output Information]      │
│ ┌[3. Terrain Finishing]──────────────┐┌[4. Native Water Finishing]──┐ │  ScrolledText log         │
│ │☑ Create / complete tee sets        ││☑ Create native water       │ │  (58×40, word wrap)       │
│ │  Target total tees per hole [4]▲▼  ││☑ Replace generated water   │ │                           │
│ │☑ Level tee boxes (Brush 9)         ││ Shoreline clearance (m)[.15]│ │                           │
│ │☐ Heavy Rough: outside feature edges││ Merge level spread (m) [.50]│ │                           │
│ │☐ Smart pin placement (4 per green) │└─────────────────────────────┘ │                           │
│ │   ◉ Preserve good/repair ○ Rebuild │                                │                           │
│ │☐ Ultra Speed-Safe Greens           │                                │                           │
│ │   Severity ○Safe ◉More Safe ○Most  │                                │                           │
│ │☐ Organic topographic weathering    │                                │                           │
│ │☑ Export greens book (18 PNGs+PDF)  │                                │                           │
│ │☑ Level cart paths                  │                                │                           │
│ │☑ Replace prior cart-path leveling  │                                │                           │
│ │☑ Preserve manual cart-path edits   │                                │                           │
│ └────────────────────────────────────┘                                │                           │
│ (Create Finished Course) (Clear Output)                               │                           │
└───────────────────────────────────────────────────────────────────────┴───────────────────────────┘
```

Each group has a short grey help label (e.g. *"Back tee centered left-right; unresolved tees park 1 m forward."*).

### 4.2 Option bindings and defaults

| Variable | Default | → `finish_course` kwarg |
|---|---|---|
| `native_{teebox,green,fairway,rough,bunker,cartpath}_var` | False | `designer_smooth_*` |
| `optimize_{…}_var` | False | `optimize_*` |
| `complete_tees_var` / `target_tees_var` | True / "4" (Spinbox 1–5, readonly) | `complete_tees`, `target_tees_per_hole` |
| `flatten_tees_var` | True | `flatten_tees` |
| `heavy_rough_var` | False | `paint_detail_heavy_rough` |
| `smart_pins_var` / `smart_pin_mode_var` | False / `"repair"` (`"rebuild"`) | `smart_pins`, `smart_pin_mode` |
| `ultra_safe_greens_var` / `ultra_safe_severity_var` | False / `"more_safe"` | `ultra_speed_safe_greens`, `ultra_speed_safe_severity` |
| `weathering_var` | False | `topographic_weathering` |
| `greens_book_var` | True | `export_greens_book_enabled` |
| `flatten_var`, `replace_legacy_var`, `preserve_manual_var` | True ×3 | cart-path flags |
| `water_var`, `replace_water_var`, `elevation_water_var` | True ×3 | water flags |
| `clearance_var` | "0.15" (validated 0–2 m) | `water_clearance` |
| `water_plane_tolerance_var` | "0.50" (validated 0–2 m) | `water_plane_tolerance` |

### 4.3 Interaction flow

1. **Browse input.** This fills the output as `<stem> - Finished.course` (if empty) and auto-runs **Analyze**.
2. **Analyze** loads the course and shows a multi-line summary (version, fine-terrain brush/spacing/stamps, feature-spline counts, tees/pins per hole, existing Finisher artefacts, water/cart-path counts). On failure it shows `"Analysis failed: …"` and logs the traceback.
3. **Create Finished Course** validates the numeric fields and requires output ≠ input (*"Output must be a new file; the finisher will not overwrite the source course."*). It then calls `finish_course` synchronously and streams log lines.
4. Errors use `messagebox.showerror("Course Finisher", …)`.

---

## 5. Course-data interactions (the core of what we need)

### 5.1 I/O layer

| Function | Behaviour |
|---|---|
| `load_course(path)` | gzip → UTF-16 → JSON outer → `_decode_inner(binaryData.CourseDescription)`. Returns `(outer, desc, inner_encoding)` |
| `_decode_inner(blob)` | base64 → gunzip → tries `utf-16`, `utf-16-le`, `utf-8` in turn. Raises `"Could not decode CourseDescription JSON"` |
| `save_course(path, outer, desc, enc)` | `json.dumps` → `json.loads(parse_float=round(x,3))` (**rounds every float in the document to 3 dp**) → compact dumps → encode → `gzip level 1` → base64 into a deep-copied outer → compact dumps → UTF-16 → `gzip level 1` → write. **Only `CourseDescription` is replaced; `CourseMetadata` and `Thumbnail` are left as they were.** |
| `get_course_version(desc)` | 25 / 23 / 19 / -1 by marker keys (see format doc §2) |

### 5.2 Read/write matrix per feature

| Feature | Reads | Writes | Provenance key under `malificaCourseFinisher` |
|---|---|---|---|
| Analyze | everything | – | reads all |
| Designer Smooth | `surfaceSplines2`, `holes3` (for tee/green role) | `surfaceSplines2[].waypoints[].pointOne/pointTwo` | – |
| Optimize Splines | `surfaceSplines2` | removes waypoints (tolerance per category) | – |
| Heavy Rough edging | closed feature splines (green/fairway/bunker/teebox), cart paths | **appends** `surfaceBrushes2` (type 8, `surfaceCategory` 4, value 1.0) | `detailTerrainHeavyRough {start,count,signature,storage}` |
| Native water | `surfaceSplines2` surface 10, fine terrain | `placedObjects4["…/GeneratedRiverSetupPrefab"].Value.objectPaths2[]` (`isWater`) | `nativeWater {…}` |
| Cart-path levelling | `surfaceSplines2` surface 7, fine terrain | **appends** `height[]` absolute stamps (tool 0, type 10) | `cartpathFlatten {stamps,maxCorrection,preservedPaths}` |
| Tee creation | `holes3[].waypoints/tees`, tee-pad splines, terrain | `holes3[].tees[]` (adds/re-centres; y = 0.0) | `teeCreation {targetPerHole,added,holesTouched,…}` |
| Tee levelling | tee-pad polygons, terrain | **appends** `height[]` (tool 1, type 9, value 0.0) | `teeFlatten {startIndex,stamps}` |
| Ultra Speed-Safe Greens | green splines, fine terrain | **modifies `value` of existing fine-terrain stamps in place** within green + 4 m collar | `ultraSpeedSafeGreens {enabled,label,changedNodes,originalValues…}` (stores originals so it can restore them) |
| Smart pins | green splines, bunkers, terrain, existing pins | `holes3[].pins[]` = `{position: offset from waypoints[-1], description: 0}` | `smartPins {countPerGreen,totalPins,mode,preserved/moved/added,…,details}` |
| Topographic weathering | green/fairway/rough geometry, terrain | **appends** `surfaceBrushes2` (surfaceCategory 18, types 10/48/49/50) | `topographicWeathering {count, brushKeys[]}` (per-brush string keys) |
| Greens book | green splines, pins, terrain | files: `<out> - Greens Book/Hole_NN.png`, `<out> - Greens Book.pdf` | `greensBook {…}` |

### 5.3 Provenance and safe re-run mechanics

These are the pattern to replicate:

- **Tail records**: `{start, count}` for arrays the tool appended to. Before re-applying, verify that `start + count == len(array)`. If it doesn't match, *"prior generated tail was edited/reordered; preserving it."*
- **Signatures**: `sha256` of the tail serialized with sorted keys and floats rounded to 3 dp. If the signature differs, the user touched it, so it is preserved.
- **Brush keys** (weathering): a multiset of `"cat|type|x|z|sx|sz|ry|value"` strings (3 dp). This allows removal even when the brushes are no longer contiguous.
- **Legacy fingerprinting**: `legacy_integrated_cartpath_tail` recognises edits from an old version *without* metadata by their deterministic scale formula (`max(3.40, 1.40·width)` / `max(2.80, 1.15·width)`).
- **Restore data**: Ultra-Safe Greens saves the original stamp values so a re-run starts from the source terrain.
- `meta.version = max(existing, FINISHER_VERSION)`.

---

## 6. Feature catalogue and algorithms

### 6.1 Terrain sampling (foundation for everything)

- **`_fine_terrain_candidates`** finds the high-resolution absolute terrain lattice in `height[]`. It prefers `tool 0, type 72` stamps (≥ 500), using the most common scale. Otherwise it takes the largest `(type, scale)` group with ≥ 500 stamps. Failing both it raises *"Could not identify a high-resolution absolute terrain lattice in height[]."* It ignores the Finisher's own prior tee tail.
- **`CourseTerrainSampler.sample(x, z)`** runs a KD-tree query of k = 12 neighbours. An exact hit (≤ 3 cm) returns the stamp value. Otherwise it fits a **weighted least-squares plane** (w = 1/(d² + max(0.2, 0.15·spacing)²)) over neighbours within `max(3.25·spacing, 4 m)` and clamps to the neighbour range ± 0.25 m. It returns NaN when data is too sparse.
- **`WaterTerrainSampler`** builds one KD-tree per `(type, scale)` group (≥ 10 stamps) and samples the finest group first, making it multi-resolution ("Adaptive/Dense" groups).
- **`SmartPinTerrainSampler.sample_many`** is a vectorised variant for grids of candidates.

> This is an *approximation* of the game's heightfield, built by replaying only absolute stamps. It ignores relative brushes (tool 1) and falloff shapes. Our app could do better by implementing a proper stamp rasterizer.

### 6.2 Spline geometry

- `spline_dense_polyline(spline, version, samples_per_segment, close)` samples each cubic Bézier segment (see format doc §3.3). Closed splines wrap.
- Classification (`classify_spline_features`): surface 0 = bunker, 1 = green or tee (whichever hole anchor it contains, else nearest), 2 = fairway, 3 = rough, 7 with width ≥ 1.9 and open = cart path. Splines detected as active tee pads override this to "teebox".
- **Designer Smooth** rewrites the Bézier handles of each waypoint along the neighbour tangent, with handle length `clamp(0.375 × adjacent segment length, 1 m, 30 m)`. Open endpoints use a one-sided tangent. Tee boxes with fewer than 6 waypoints are skipped (guarded).
- **Optimize** repeatedly removes waypoints whose removal keeps the curve within tolerance (tee/green/bunker 0.05 m, fairway/rough 0.10 m, cart path 0.08 m). It protects sharp corners, and keeps at least 4 points on closed splines and 2 on open ones.

### 6.3 Tee creation (`smart_complete_tees`)

- Hole axis: `waypoints[0]` is the back-tee anchor. The existing marker nearest to it is the back tee, and the forward vector points toward the next waypoint.
- It assigns each tee-pad polygon (closed splines of surface 1–4 that are not greens) to a single owner hole, then re-centres the back tee *crosswise* on its pad.
- New tee sites are searched within 8–180 m forward and ≤ 80 m lateral of the back tee, on pads with a major axis ≤ 110 m and area ≤ 1400 m², at ≥ 8 m separation. It uses a 1 m internal grid and scores sites by local terrain range.
- Tees it can't place become **placeholders 1.0 m in front of the back tee** so the user can move them by hand.
- Target 1–5 tees per hole (default 4). The final `tees[]` is re-ordered front → back.

### 6.4 Tee levelling (`smart_flatten_tees`)

- Appends neutral **Brush 9, tool 1, value 0.0** taps ("0.0 ft level"). A pad used by one tee gets covered whole; a pad shared by several tees is levelled locally around each marker.
- Scale = `clamp(0.42 × pad width, 1.75, 7.0)` on a 1.5 m grid. It skips a pad if the local terrain range is < 3.5 cm, and dedupes against existing taps within 0.85 × scale.

### 6.5 Smart pins (`smart_place_pins`)

- Builds a candidate grid (0.5 m) inside each green polygon, at least 1 m from the edge. For each point it computes slope %, max local grade (0.9 m radius), 3 m neighbourhood relief, edge clearance and bunker proximity.
- Quality tiers `(name, min edge m, max slope %, …)`: preferred (3.6, 3.5), good (3.0, 4.5), usable (2.5, 6.0), fallback (2.0, 8.0), emergency (1.5, 12.0). The fast-green target slope is ≤ 2.0 % with a 2.4 % ceiling.
- Selects **4 pins** with preferred separation 6 m (minimum 4 m), maximising coverage across quadrants and along/lateral positions relative to the approach axis.
- Mode `repair` keeps existing pins that pass the safety checks and fills or replaces the unsafe ones, using minimum-total-movement slot assignment. Mode `rebuild` replaces all 4. A forced-completion fallback guarantees 4 pins.

### 6.6 Heavy-rough edging

Walks closed feature outlines (green, fairway, bunker, teebox) and places heavy-rough brushes (scale 10) with their centres 2 m outside the edge every 9 m. On cart-path centrelines it uses scale 13 every 12 m.

### 6.7 Cart-path levelling

For each cart path (surface 7) it samples terrain at the centre and both sides along the path, interpolates gaps, and blends toward a smoothed target profile. It then appends absolute **Brush 10** stamps with scale `max(3.4, 1.4 × width)` at spacing `clamp(0.45 × scale, 1.3, 1.7)`. It optionally skips paths that already have manual post-import edits, and replaces the legacy V2 tail.

### 6.8 Native water (the most complex pass)

- Targets **closed** water splines only (surface 10, width ≤ 0.25). Open streams are ignored by design, and ribbon-shaped closed water is rejected using compactness, oriented fill and aspect thresholds.
- For each pond it infers the **highest safe water level** from the shoreline terrain minus a clearance of 0.15 m (default). It then finds a cover polygon (a 4-point rectangle first, then 6/8/10-sided support polygons, trying 12 orientations) and verifies zero "dry leak": no sampled land inside the polygon may sit below the water ceiling (0.65–1.0 m sampling grid).
- It can merge nearby ponds with compatible levels (gap ≤ 250–300 m, level spread ≤ the user tolerance) into a single water plane.
- Output: native water `objectPaths2` entries (format doc §4.11) with `height` = water level, straight edges, `absoluteY: true`, up to 10 waypoints each.

### 6.9 Ultra Speed-Safe Greens

This pass compresses green relief vertically: it scales terrain node values about a local reference within each green, blended over a 4 m smoothstep collar. Profiles:

| Profile | target p95 local grade | target relief (90th pct) | min factor |
|---|---|---|---|
| Safe | 5.5 % | 1.5 m | 0.65 |
| More Safe | 4.75 % | 1.25 m | 0.55 |
| Most Safe | 4.0 % | 1.0 m | 0.45 |

The pass stops with an error if it changed zero nodes, and warns if the p95 slope didn't improve.

### 6.10 Topographic weathering

Finds exposed convex terrain (ridges and mounds) in rings around greens, fairways and rough, and paints `surfaceCategory 18` weathering brushes (types 10/48/49/50). Types are weighted per role, scale ranges are per type, and there are per-feature brush caps (green 11, fairway 16, rough 10). Placement uses a deterministic pseudo-random function (`_weather_rand01(item, salt)`) so results are reproducible.

### 6.11 Greens book export

Pillow renders one 1200×1500 PNG per hole: a slope heat-map (0–8 %, 11-stop green→red palette), 5 cm contours, downhill arrows, pin markers and an approach orientation. Pages are combined into a PDF. It notes *"Color scale is visual; calibrated approx. to Green Speed 10"*.

---

## 7. Findings from applying this to our sample courses

### 7.1 Our samples would be rejected by most passes

None of the three sample courses contain a fine-terrain lattice. There are 0 `type 72` stamps, and the largest same-`(type, scale)` absolute group is 99 stamps (the threshold is 500). I checked this with my own re-implementation of the detection rule, not by running the tool. `analyze_course` builds a `CourseTerrainSampler` unconditionally for 2K25 files, so **Analyze would fail on hand-built Designer courses**. The tool is designed for LiDAR-imported courses only.

**Implication for us:** we need our own heightfield model that replays *all* `height[]` stamps (absolute + relative, with brush shapes). That lets terrain-aware features work on hand-built courses too.

### 7.2 Metadata drift

After the tool adds tees or moves pins, `CourseMetadata.teeSetInfos`, `pinSets`, `teePositions`/`pinPositions` and `length` are stale. The game may recompute them when the course is next saved in the Designer, but lobby and browse data in the meantime is wrong.

### 7.3 Lossy save

Rounding every float to 3 decimals touches thousands of values the tool never meant to change (rotations, handles, cloud offsets). It is usually harmless at millimetre scale, but it breaks byte-level diffing and signatures computed by other tools.

---

## 8. Blueprint: building ours in the same pattern

Recommended stack: **C# / .NET 8, WPF (or WinUI 3) + MVVM** (CommunityToolkit.Mvvm). This gives a native Windows feel, fast startup (no 70 MB self-extract), and strong typing for the format.

```
src/
  Pga2K25.CourseFormat/        ← codec + typed model (preserve unknown keys)
      CourseFile.cs            Load/Save: gzip ▸ UTF-16 ▸ JSON ▸ base64 blobs
      Model/…                  CourseDescription, Hole, Spline, Brush, PlacedObjectGroup…
      MetadataBuilder.cs       regenerates CourseMetadata (length, tee sets, swapped keys)
      FormatVersion.cs         25/23/19 detection
  Pga2K25.Geometry/            Bézier sampling, polygons, KD-tree, simplification
  Pga2K25.Terrain/             heightfield replay of height[] (+ sampler interface)
  Pga2K25.Operations/          one class per feature (the Finisher passes, reimagined)
      ICourseOperation         Analyze(ctx) / Apply(ctx, options, IProgress) → OperationReport
      Provenance/              tail records + SHA-256 signatures (own key, e.g. "pga2k25Designer")
  Pga2K25.App/                 WPF shell (MVVM)
      Views/                   File panel, operation option panels, analysis panel, log
      ViewModels/              bind options ⇄ typed settings; async Run with CancellationToken
tests/
  Pga2K25.CourseFormat.Tests   round-trip all samples/courses/*.course byte-stable
```

Carry forward:

1. **Pipeline of operations** with explicit dependencies, each reporting before/after stats.
2. **Provenance tails + signatures** for idempotent, user-safe re-runs.
3. **"Never overwrite source"** with an auto-suggested output name.
4. **Analyze-first UX**: pick a file, see an instant summary, choose options, run, then read the log and report.
5. **Tunable constants** kept as a typed settings object (exposable under an "Advanced" section).

Improve on:

1. No global rounding. Preserve unknown JSON keys and original number text where possible.
2. Regenerate `CourseMetadata` (and optionally the thumbnail) on save.
3. Run work in the background with progress and cancellation. Persist the last-used options.
4. Add a real **2D map view** (splines, holes, brushes, heightfield shading). The reference has none, and it's the natural foundation for a *designer* app.
5. Replay the full heightfield so terrain features work on hand-built courses, not only LiDAR imports.
6. Use smaller packaging (framework-dependent or trimmed single-file .NET).

---

## Appendix A — Key constants recovered

| Group | Constants |
|---|---|
| Water | `WATER_GROUP_PATH="Assets/CourseGen/Detail/Walls/GeneratedRiverSetupPrefab"`, `SMART_WATER_MAX_GROUP_GAP=300`, `…MAX_GROUP_SPAN=700`, `…GROUP_PAD=8`, `…SINGLE_PAD=2.5`, `…MAX_WAYPOINTS=10`, `…COVER_SIDES=(4,6,8,10)`, `…ORIENTATION_STEPS=12`, `WATER_REV10_PAD=0.2`, `…DRY_MARGIN=0.08`, `…GRID_STEP 0.65–1.0`, `…SEPARATOR_ANGLES=144`, `…MAX_MERGE_GAP=250`, `…MIN_COMPACTNESS=0.25` |
| Tees | `TEE_CREATE_DEFAULT_TOTAL=4 (1–5)`, `MAX_FORWARD=180`, `MAX_LATERAL=80`, `MAX_MAJOR=110`, `MAX_AREA=1400`, `MIN_SEPARATION=8`, `TEE_ALLOWED_SURFACES=(1,2,3,4)`, `TEE_LEVEL_BRUSH_TYPE=9`, `TEE_LEVEL_TOOL=1`, `TEE_LEVEL_TAP_VALUE=0.0`, `SCALE 1.75–7.0 (0.42×width)` |
| Splines | tolerances teebox/green/bunker 0.05, fairway/rough 0.10, cartpath 0.08; `DESIGNER_HANDLE_FACTOR=0.375 (1–30 m)`, `DESIGNER_TEE_MIN_WAYPOINTS=6` |
| Heavy rough | `SURFACE=4`, closed scale 10 / offset 2 m / step 9 m; cart scale 13 / step 12 m |
| Pins | `COUNT=4`, grid 0.5 m, edge preferred 3.6 / target 4.5 / absolute 2.0 m, separation 6 / 4 m, fast slope 2.0 / 2.4 % |
| Weathering | `surfaceCategory=18`, types (10,48,49,50), per-role step/ring/edge/value ranges |
| Greens book | grid 0.35 m, contour 0.05 m, page 1200×1500 |
| Native water defaults | `spacingRule 0, flexibilityRule 0, heightRule 1, spacing 0.25, heightOffset -2.0, edgeHeightOffset 0.5, water{flowDirection 1.0, flowSpeed 0.0}, isWater true` |

## Appendix B — Reproducing this analysis

1. Parse the PyInstaller CArchive TOC from the trailing `MEI` cookie (88-byte struct `!8sIIII64s`). Extract entries, zlib-inflating where `cflag=1`.
2. Read the `PYZ.pyz` TOC (a marshalled list at the offset in bytes 8–12) and inflate the non-stdlib modules.
3. Prefix each code body with a 16-byte Python 3.11 pyc header (`A7 0D 0D 0A` + 12 zero bytes) and disassemble with `xdis`/`pydisasm` (cross-version, pure Python).
4. Recover module constants by emulating the module-level `LOAD_CONST`/`BUILD_*`/`STORE_NAME` sequence.

`tools/course_codec.py` in this repo is an independent, clean-room implementation of the file codec (§5.1 without the rounding).
