# Course Finisher (C# port)

`src/Pga2K25.Finisher` is a C# / .NET 8 rewrite of *Mal's TGC 2K25 Course Finisher* **v0.10 REV10E** (`course_finisher.py`, ~7,100 lines, plus the Tk shell `tgc_finisher.py`). It follows the same structure as the other projects in this solution: a class library, a `pga2k25 finish` / `pga2k25 analyze` CLI command, a **Course Finisher** tab in the WPF app, and xUnit tests.

The port was made from the Python 3.11 bytecode in the reference executable (see [reference-analysis/course-finisher-analysis.md](reference-analysis/course-finisher-analysis.md)). Every live function was translated one-to-one, keeping its constants, thresholds, ordering and tie-breaking, and each C# member's doc comment names the Python function it replaces.

## What it does

The Finisher post-processes courses that were **imported with the TGC Tool (LiDAR / heightmap)**. Its terrain passes need the imported high-resolution absolute lattice (`type 72` stamps, or any same-scale family of ≥ 500 absolute stamps). Hand-built Designer courses don't have one, so those passes stop with *"Could not identify a high-resolution absolute terrain lattice in height[]"*, exactly as the Python does. The Analyze summary says so up front.

Passes run in the reference order (`finish_course`):

| # | Pass | C# | Python |
|---|---|---|---|
| — | Remove prior tee-levelling / weathering tails | `TeeLeveling.RemovePrior`, `TopographicWeathering.RemovePrior` | `remove_prior_tee_flatten`, `_remove_untracked_rev2_tee_tap_tail`, `remove_prior_topographic_weathering` |
| 1 | Designer Smooth splines | `Splines/DesignerSmooth` | `designer_smooth_selected_splines` |
| 2 | Optimize splines (waypoint reduction) | `Splines/SplineOptimizer` | `optimize_selected_splines` → `smooth_selected_splines` |
| 3 | Heavy Rough edging | `Operations/HeavyRough` | `paint_heavy_rough_on_detailed_terrain` |
| 4 | Native water (REV10) | `Operations/NativeWater` | `generate_native_water_from_splines` |
| 5 | Cart-path levelling | `Operations/CartPathLeveling` | `apply_cartpath_flatten` |
| 6 | Tee creation / back-tee centring | `Tees/TeeCreation` | `smart_complete_tees` |
| 7 | Tee levelling (Brush 9) | `Tees/TeeLeveling` | `smart_flatten_tees` |
| 8 | Ultra Speed-Safe Greens | `Greens/UltraSafeGreens` | `apply_ultra_speed_safe_greens` |
| 9 | Smart pins (repair / rebuild) | `Pins/SmartPins` | `smart_place_pins` |
| 10 | Topographic weathering | `Operations/TopographicWeathering` | `apply_topographic_weathering` |
| 11 | Greens book (PNG per hole + PDF) | `Greens/FinisherGreensBook` | `export_greens_book` |
| — | Analyze (before / after) | `CourseAnalysis` | `analyze_course` |
| — | Orchestrator | `CourseFinisher.Finish` | `finish_course` |

Shared pieces:

| C# | Python |
|---|---|
| `Terrain/FineTerrainSampler` | `_fine_terrain_candidates`, `CourseTerrainSampler` |
| `Terrain/MultiScaleTerrain` (`Sample`, `SampleIdw`, `SampleMany`) | `WaterTerrainSampler`, `_water_sample_many`, `SmartPinTerrainSampler` |
| `FeatureClassifier` | `classify_spline_features`, `_surface1_role`, `find_cartpaths`, `find_water_splines`, `spline_feature_counts` |
| `Tees/TeeGeometry` | `_tee_surface_geometries`, `_assign_tee_geometry_owners`, `_tee_pad_candidates`, `_tee_crosswise_center`, `find_active_tee_regions`, `_hole_tee_axis` |
| `FinisherGeometry`, `Arc` | `spline_dense_polyline`, `_prepare_arc`, `_point_tangent`, `_point_in_polygon`, `_polygon_area`, `_polygon_centroid_point`, `_polygon_pca_dimensions`, `_polygon_edge_clearance` |
| `Greens/GreenGrid` | `_greens_book_green_grid` |
| `Provenance` | `FINISHER_KEY` record, `_surface_brush_tail_signature`, `_prior_tee_flatten_tail` |
| `Support/Py` | `round()`, `repr(float)`, `f"{x:.3f}"`, `json.dumps(sort_keys=True, separators=(',', ':'))` — byte-exact |
| `Support/KdTree`, `NumPy`, `LinAlg` | `cKDTree`, `np.percentile/interp/arange/gradient`, `ndimage.gaussian_filter(1d)`, `distance_transform_edt`, `lstsq`, `pinv`, `eigh` |

## Interoperability with the Python tool

Both tools write the same `malificaCourseFinisher` record inside CourseDescription, with the same keys. Signatures (SHA-256 of sorted-key JSON with floats rounded to 3 dp), weathering brush keys and the deterministic weathering "random" numbers are reproduced byte for byte; the tests pin them against values computed by CPython. So either tool can re-run over the other's output: prior tails are recognised and replaced, and edited tails are preserved.

Numbers the port writes keep the game's spelling (`1.0`, never `1`), using Python's shortest-repr digits.

## Deliberate differences

| Area | Python REV10E | C# port |
|---|---|---|
| Save | Rounds **every** float in the course to 3 dp; gzip level 1 | Lossless: only values a pass changed are touched (`CourseFile`) |
| Hand-built courses | `analyze_course` raises without a fine lattice | Analysis still runs and reports `FineTerrainError` |
| Greens-book par | Prints `creatorDefinedPar` as-is, so "Par -1" when the game stores -1 | Falls back to `par` when `creatorDefinedPar` ≤ 0 |
| NaN in metadata | `json.dumps` writes a bare `NaN` (invalid JSON) e.g. a pin's slope outside the grid | Writes `null` |
| Ultra-safe after-metrics | `zip(details, holes)` pairs the wrong hole once a hole has no green | Looks each detail's hole up by number |
| Water sampler miss | `WaterTerrainSampler.sample` returns `None` and `np.isfinite(None)` raises, failing the pond | Returns NaN; the sample is skipped |
| Processing | On the UI thread | Background task, cancellable between passes |
| Legacy water switches | `elevation_plane_water`, `smart_compact_water`, `merge_compatible_water` accepted but ignored by REV10 | Not exposed |

## Findings worth passing back

**Dead code (28 functions).** A call graph from `finish_course` and the UI shows these are never reached, so they were not ported: the whole *smart-compact* water solver (`_evaluate_compact_covers`, `_select_compact_cover`, `_smart_group_water_records`, `_highest_safe_cover_level`, `_sample_cover_land_floor`, `_group_span_ok`, `_group_cover_efficiency`, `_generate_smart_compact_water_areas`), the *elevation-plane* water solver (`_generate_elevation_plane_water_areas`, `_elevation_clusters`, `_dry_exposure_stats`, `_split_water_records_spatially`, `_solve_water_plane_group`, `_safe_water_plane_groups`, `_water_plane_waypoints`, `_water_record_centroid`), open-stream water (`_shoreline_level_for_line`, `_buffer_open_polyline`, `_straight_waypoints`), and `_nearest_distance_to_paths`, `_tee_local_pad_center`, `_rotated_brush_polygon`, `_existing_level_tap_near`, `_grid_points_for_flatten`, `_legacy_surface_tail_signature`, `_smart_pin_terrain_metrics`, `_smart_pin_set_objective`, `_ultra_safe_node_metrics`.

**`_polygon_area` is defined twice.** The second definition (line 3007, the NumPy shoelace) silently replaces the first (line 1280) for every caller.

**Re-runs are not perfectly stable** (the same in both implementations, documented by the tests):

- Heavy Rough classifies tee boxes *before* tee creation (pass 3 vs 6), so the second run sees the first run's new tee markers and may paint a few extra brushes around newly recognised pads.
- Cart-path levelling (pass 5) samples terrain *before* Ultra Speed-Safe Greens (pass 8) restores the original node values, so a second run's cart-path stamps (and anything sampled after them) can differ slightly. Ultra-safe's source rows also include the cart-path Brush 10 stamps.
- The Heavy Rough tail is only removable while weathering is removed first; `finish_course` does that, but running Heavy Rough alone after a weathering run preserves (and so duplicates) it.

**Native water on lattices.** The strict dry-land check samples dry points right next to the shoreline by IDW over the 8 nearest lattice nodes, which mixes in lake-bed nodes. Where the bed is more than ~0.2 m below the bank, the convex cover usually fails and the pond falls back to an exact copy of its spline.

## Using it

```bash
dotnet run --project src/Pga2K25.Cli -- analyze "my lidar course.course"
```

```bash
dotnet run --project src/Pga2K25.Cli -- finish "my lidar course.course" --smooth all --heavy-rough --pins repair --ultra-safe more_safe --weathering
```

Without `-o`, the output is `<course> - Finished.course` next to the input, and the greens book goes to `<output> - Greens Book/`. In the desktop app, pick the course, open the **Course Finisher** tab, press **Analyze Course**, choose options, then **Create Finished Course**.

## Testing

The repo's sample courses are all hand-built, so `FinisherFixture` builds a TGC-Tool-style course for the tests: a 2 m `type 72` lattice over Cassimbo holes 1–3, sampled from the calibrated terrain replay, plus a pond on a flat bank. `FinisherTests` covers Python byte-compatibility, the numeric helpers, every pass on that fixture, re-running on its own output, and preserving an edited tail.

Not yet done: a side-by-side diff against the Python tool on a real LiDAR course. That needs a real import and running the reference executable, neither of which was available here.
