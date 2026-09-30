# PGA TOUR 2K25 `.course` File Format

Reverse-engineered specification, derived from:

- the three sample courses in `samples/courses/` (Shinnecock Ridge, Delta Test, Jungle Plot 2 — all saved from the 2K25 Course Designer, format `version: 49`), and
- static analysis of the reference tool *Mal's TGC 2K25 Course Finisher v0.10 REV10E* (see [../reference-analysis/course-finisher-analysis.md](../reference-analysis/course-finisher-analysis.md)).

Confidence markers used below:

| Marker | Meaning |
|---|---|
| **Confirmed** | Observed consistently in all samples *and/or* hard-coded in the reference tool |
| **Inferred** | Consistent with the data but not proven |
| **Unknown** | Present in the data; purpose not yet determined |

All distances are **metres**. The world plane is **X/Z**; **Y is up**. Compass: **north = -Z, east = -X**. This was fitted from the Cassimbo designer's 36 tee/approach bearings (residual about 14 degrees RMS against estimated bearings) and confirmed by the designer. Seen from above with north up, +X points west and +Z points south.

---

## 1. Container layers (Confirmed)

A `.course` file is a three-layer onion:

```
.course  (file on disk)
└─ gzip
   └─ UTF-16 LE text (with BOM FF FE)
      └─ JSON  "outer envelope"
         {
           "data": {},
           "binaryData": {
             "CourseDescription": "<base64>",   ← the course itself
             "Thumbnail":         "<base64>",   ← JPEG preview wrapper
             "CourseMetadata":    "<base64>"    ← summary / browse info
           }
         }
            each <base64> value decodes to:
            └─ gzip
               └─ UTF-16 LE text (with BOM)
                  └─ JSON document
```

Decode (Python):

```python
outer = json.loads(gzip.decompress(open(p, "rb").read()).decode("utf-16"))
def blob(name):
    raw = gzip.decompress(base64.b64decode(outer["binaryData"][name]))
    return json.loads(raw.decode("utf-16"))
desc, meta, thumb = blob("CourseDescription"), blob("CourseMetadata"), blob("Thumbnail")
```

Encoding notes:

- Python's `"utf-16"` codec emits a BOM + little-endian, which matches the game output byte-for-byte at the start of the stream.
- Game files are gzip with header `XFL=0`; compressed sizes are close to zlib level 6–9. The reference tool writes `compresslevel=1`, which makes files ~1.9× larger (still loads fine, but we should use level 6+).
- JSON is compact (`separators=(',', ':')`) with non-ASCII characters left unescaped.
- A round-trip `decode → encode → decode` with this recipe returns an identical object for all three samples (tested).

Sizes (Shinnecock Ridge): file 204 KB → outer JSON 409 KB → CourseDescription 969 KB of UTF-16 text (~485 K characters).

---

## 2. Format version detection (Confirmed)

The reference tool identifies the game generation by marker keys in `CourseDescription`:

| Condition | Returned version | Game |
|---|---|---|
| `useV43AltSurfaceSplineScale` **and** `holes3` present | `25` | PGA 2K25 |
| `useV30Rough` **and** `holes2` present | `23` | PGA 2K23 |
| `useV19DetailPlacement` **and** `holes` present | `19` | PGA 2K21 |
| otherwise | `-1` | unsupported |

Separately, `CourseDescription.version` is the designer save-schema number (`49` in every sample) and `CourseMetadata.courseVersion` mirrors it.

**Spline coordinate axis differs by generation**: for 2K25 (`25`) spline points use `x`/`z`; older generations stored the second planar coordinate in `y`. (`_dim2()` in the reference tool.)

---

## 3. Shared primitive shapes

### 3.1 Vector3

```json
{"x": -805.7563, "y": 0.0, "z": -706.1112}
```

`position.y` is the **string** `"-Infinity"` on every brush / stamp / placed object that should snap to terrain (2,626 of 2,626 height stamps; 66 of 70 placed objects). A numeric `y` means an explicitly elevated object. Our serializer must preserve the literal string `"-Infinity"`, **not** a float `-inf`. **Confirmed**

### 3.2 Brush (a.k.a. stamp / pixel)

Used by `height[]`, `surfaceBrushes2[]`, and the `brushes[]` of every map-edit layer.

```json
{
  "tool": 0,                                   // height[] only
  "surfaceCategory": 18,                       // surfaceBrushes2[] only
  "position": {"x": 494.5, "y": "-Infinity", "z": -474.1},
  "rotation": {"x": 0.0, "y": 56.925, "z": 0.0},   // only Y (yaw, degrees) is used
  "scale":    {"x": 809.4, "y": 1.0, "z": 809.4},  // brush diameter-ish in metres; x == z
  "type": 54,                                  // brush shape / falloff id
  "value": 5.61,                               // meaning depends on array + tool
  "holeId": -1                                 // always -1 in samples (course-wide)
}
```

The reference tool also emits `_orientation`, `radius`, `orientation` (all `0.0`) on terrain stamps it creates; the game tolerates these extra keys.

### 3.3 Spline

Used by `surfaceSplines2[]`, map-edit layers (`clearTrees`, `clearDetail`, …) and object paths.

```json
{
  "waypoints": [
    {
      "pointOne": {"x":…, "y":0.0, "z":…},   // incoming Bézier handle
      "waypoint": {"x":…, "y":0.0, "z":…},   // anchor point
      "pointTwo": {"x":…, "y":0.0, "z":…},   // outgoing Bézier handle
      "width": 4.7
    }
  ],
  "width": 4.7,
  "hasCurves": false,
  "state": 3,
  "absoluteY": false
}
```

**Curve evaluation (Confirmed):** each segment `a → b` is a cubic Bézier with control points
`P0 = a.waypoint`, `P1 = a.pointTwo`, `P2 = b.pointOne`, `P3 = b.waypoint`.
A closed spline also joins the last waypoint back to the first.

**Closed vs open (Confirmed):** `state != 0` means closed/filled (`3` in samples). `state == 0` means an open polyline such as a cart path, stream, or object path. Older files may use `isClosed` / `isFilled` booleans, and the reference tool checks those as fallbacks.

When `hasCurves` is `false`, handles usually coincide with neighbouring anchors or are colinear (the "Designer Smooth" feature rewrites them to create curves).

---

## 4. `CourseDescription` — top-level map

~140 top-level keys. They are grouped by role below.

### 4.1 Identity and save info

| Key | Type | Notes |
|---|---|---|
| `name` | string | Course display name |
| `_id` | string | e.g. `offlineSaveilsphs4wqxbe`; shared with Metadata (`<id>-Meta`) and Thumbnail (`<id>-Thumb`) |
| `version` | int | Save schema (49) |
| `timeStamp` | int | Unix epoch **milliseconds** |
| `type` | string | `"course"` |
| `seed`, `courseSeed`, `terrainSeed` | int | Procedural generator seeds |

### 4.2 Behaviour flags (compat switches)

`useV4Height`, `useWaypointHeight`, `useSrgbBrushes`, `useOldFairwayEdges`, `useV19DetailPlacement`, `useV20ClusterRotation`, `useV21PathBrush`, `useV21HeavyRough`, `blurHeight`, `useV28FairwaySeed`, `useV30Rough`, `useV31SplatControlMasking`, `useV43AltSurfaceSplineScale`, `useV44IgnoreBunkerWaterLevelClipping`, `useV46HeightFieldDiscretization`, `useV47OverlaySplineScale`, `useV48ThemeAssetPack`, `legacyTerrain`.

These are booleans that switch the game between legacy and current generator behaviour. **We must round-trip them untouched.**

### 4.3 Environment

| Key | Notes |
|---|---|
| `theme` (int), `backdrop` (int) | Theme and backdrop enums (samples: themes 8, 10, 50; backdrops 0, 3) |
| `timeOfDay` (0–1), `sunOrientation`, `sunInclination` (degrees) | Lighting |
| `weather` (int), `weather3` {…} | Cloud layers (`cloudLayer1/2`: `typeName`, `cloudiness`, `scale`, `offset`), `highCloudLayer1..3`, `haze`, `fogCeiling`, `fogDensity`, `rain` |
| `cartPathTexture`, `teeTexture`, `flagSet`, `teeType`, `teeColour`, `teeColours[5]`, `showFurniture` | Cosmetic enums |

### 4.4 Playing conditions

`greenSpeed`, `greenFirmness`, `fairwaySpeed`, `fairwayFirmness`, `roughSpeed`, `roughFirmness`, `heavyRoughSpeed`, `heavyRoughFirmness`, `bunkerSpeed`, `bunkerFirmness`, `terrainFirmness`. All are normalized floats; **`-1.0` means "use the game default"**.

### 4.5 Procedural generator parameters

These are used when the designer auto-generates a course. They are usually inert on hand-built courses, but must be preserved:

- Noise blocks `{period, scale, persistence, octaves}`: `greenNoise`, `fairwayNoise`, `roughNoise`, `unplayableNoise`, `terrainNoise`, `perturbationNoise`, `fairwayEdge2`. Edge blocks `{period, scale}`: `roughEdge`, `fairwayEdge`, `greenEdge`.
- Radii and shape: `fairwayRadius`, `fairwayStart`, `fairwayEnd`, `greenRadius`, `greenPoints`, `greenVariance`, `teeRadius`, `teeHeight`, `teeMarkerWidth`, `roughRadius`, `heavyRoughRadius`, `roughTerrainFalloff`, `greenFringeRadius`, `fairwayNoiseScale`.
- Mowing: `GreenMowTiling`, `GreenMowIntensity`, `FairwayMowTiling`, `FairwayMowIntensity`, `grassWeatheringEdgeBlend`.
- Hazards and density: `hazardDepth`, `hazardThreshold`, `hazardRadius`, `hazardFairwayPeriod`, `hazardGreenCount`, `hazardFairwayCount`, `treeOptions.density`, `detailDensity`, `rockDensity`, `plantDensity`, `treeDensityScale`, `treeDensityMap[]`.
- Layout generator: `holeCount`, `par3Count`, `par4Count`, `par5Count`, `holeSeparation`, `waypointHeight`, `courseIterations`, `minimumDoglegPositon` (sic, typo is in the game key), `maximumDoglegPosition`, `maximumDoglegAngle`, `allowFairwayCrowd`, `flattenFairways`, `flattenGreens`, `waterLevel`, `waterAmount`, `hillsAmount`, `hillsHeight`.
- Bunker shaping: `bunkerSettings2` = `{highResStamping, regularBunkerDepressionSettings{baseDepth, lipStart, lipDepth, lipSteepness}, potBunkerDepressionSettings{…}, flatFloor, stampDepth}`, `bunkerBrushMaskFairwayGreen`, `bunkerSplineMaskFairwayGreen`.

### 4.6 Surface material table (Inferred)

`surfaces2[15]` and `secondarySurfaces[15]` are arrays of `{name?, blend, maskWater}`. A present `name` overrides the material for that slot; an absent `name` means the theme default. Slot meanings inferred from Jungle Plot 2, where all slots are named:

| Slot | Example material | Likely role |
|---|---|---|
| 0 | `Sand50_StAndrews`, `Sand46_Pinehurst` | Bunker sand |
| 1 | `Concrete01` | Cart path |
| 2 | `Cobbles06` | Path / hardscape |
| 3–5 | — | (unnamed in samples) |
| 6 | `Green13` | Green |
| 7 | `Fringe13` | Fringe |
| 8 | `Fairway27_EastLakeGC` | Fairway |
| 9 | `LightRough27_EastLakeGC` | Light rough |
| 10 | `HeavyRough18_AtlanticBeachCC` | Heavy rough |
| 11–14 | `Splat0_…` to `Splat3_…` | Terrain splat textures 0–3 |

`surfaces[9]` is a legacy array (all `null`).

### 4.7 Holes — `holes3[]` (Confirmed)

The 2K25 hole list. 2K23 used `holes2` and 2K21 used `holes`.

```json
{
  "waypoints": [ {x,y,z}, {x,y,z}, {x,y,z} ],   // route: [0]=route start (usually the back tee) … [-1]=green centre
  "tees":  [ {"position": {x, y, z}} , … ],    // 1–5 tee markers, ordered FRONT → BACK
  "pins":  [ {"position": {x, y, z}, "description": 0}, … ],  // 1–4 pins, RELATIVE to waypoints[-1]
  "par": 4,
  "creatorDefinedPar": -1,                     // -1 = auto; otherwise the designer override
  "name": "",
  "flagOffset": {"x": 0.0, "y": 0.0},
  "greenRadius": -1.0, "teeRadius": -1.0, "fairwayRadius": -1.0,
  "fairwayStart": -1.0, "fairwayEnd": -1.0, "fairwayNoiseScale": -1.0,
  "roughRadius": -1.0, "heavyRoughRadius": -1.0,
  "hazardGreenCount": -1.0, "hazardFairwayCount": -1.0, "hazardFairwayPeriod": -1.0,
  "teeHeight": -1.0, "greenSeed": -1, "fairwaySeed": -1, "teeTexture": -1
}
```

Key rules:

- `waypoints[0]` is the **route start**. In 53 of 54 sample holes it equals the back (last) tee, but Shinnecock hole 18 is an exception: its route starts 10–43 m from every tee, so don't assume the two coincide. `waypoints[-1]` is the **green centre / pin anchor**. Intermediate waypoints are dogleg aim points (1 in all samples, giving 3 waypoints total).
- **Pin coordinates are offsets from `waypoints[-1]`** (the reference tool reads `anchor + pin.position` and writes `candidate − anchor`). A single `(0, 0)` pin means "at the green centre".
- `y` is `0.0` on route, tee and pin points. The game projects them onto the terrain.
- Per-hole numeric overrides use `-1` / `-1.0` for "inherit the course-level value".

### 4.8 Terrain — `height[]` (Confirmed structure; brush-type semantics partly Inferred)

An **ordered edit log** of terrain stamps. The game replays it top-to-bottom to build the heightfield, so **order matters** and appending is the safe way to add edits.

| `tool` | Meaning |
|---|---|
| `0` | **Absolute** height stamp. `value` = target elevation (m). |
| `1` | **Relative / local** stamp: raise, lower or level. `value` = local offset; `0.0` with type 9 = "flatten at 0.0 ft". |

Observed `type` (brush shape id) usage across the samples:

| type | tool 0 (n) | tool 1 (n) | Notes |
|---|---|---|---|
| 8–18 | common | some | Small to medium shaped brushes |
| 9 | 8 | 1 | **Level/flatten brush** — the reference tool uses it for tee levelling (tool 1, value 0.0) |
| 10 | 17 | – | **Round smooth stamp** — the reference tool uses it for cart-path levelling (tool 0, absolute) |
| 23, 24, 45 | few | few | Tiny detail brushes (scale 1–6 m) |
| 39, 40 | – | 365 | Large soft relative brushes (hill shaping) |
| 42, 49, 53, 54, 55 | many | many | Large area brushes (scale up to 1,000 m); 54/55 dominate |
| **72** | – | – | **Fine terrain lattice**: a grid of absolute stamps produced by LiDAR/heightmap import tools ("TGC Tool"). **Not present in any of our samples.** |

`scale.x` ranges from 1.1 m to 1,000 m, and `rotation.y` is used on most shaped brushes.

`terrainHeight[]` (empty) is a legacy raw-heightfield slot.

### 4.9 Surfaces

#### 4.9.1 `surfaceSplines2[]` — painted surface areas and paths

Spline (§3.3) plus:

| Key | Notes |
|---|---|
| `surface` | Surface id (table below) |
| `secondarySurface`, `secondaryWidth` | Edge/collar surface and its width (`-1` = none) |
| `opacity` | 1.0 in samples |
| `bunkerOverrideParameters` | Optional per-bunker `{baseDepth, lipStart, lipDepth, lipSteepness, flatFloor}`. `-1` = inherit |

Surface ids (Confirmed from the reference tool's classifier, with sample counts):

| id | Meaning | Closed? | Notes |
|---|---|---|---|
| 0 | **Bunker** | closed | secondary often 3 (rough lip) |
| 1 | **Green** (and some tee pads) | closed | secondary 4 is common; the tool decides tee vs green by which hole anchor lies inside |
| 2 | **Fairway** | closed | Imported tee pads are often surface 2 or 3 |
| 3 | **Rough** | closed | |
| 4 | **Heavy rough** | closed or open | Also the `surfaceCategory` used for heavy-rough paint brushes |
| 7 | **Cart path** | open (`state 0`) | Classified as a cart path when `width >= 1.9` and not filled |
| 8, 16, 17 | Unknown (path/waste-area variants) | open/closed | Observed in Jungle Plot 2 and Delta Test |
| 10 | **Water** | closed = pond/lake (`width <= 0.25`); open = stream (`width >= 1.0`) | |

#### 4.9.2 `surfaceBrushes2[]` — painted surface stamps

Brush (§3.2) plus `surfaceCategory`. `value` is the paint opacity (0–1).

| surfaceCategory | Observed types | Notes |
|---|---|---|
| 0–4 | 8, 11, 14–22, 42, 48, 49, 59 | Same ids as spline surfaces (bunker, green, fairway, rough, heavy rough) |
| 7 | 38 | Cart-path paint |
| 14–17 | 9, 12, 13, 17, 18, 42, 48, 49, 53, 55 | Unknown (likely theme splat / waste layers) |
| **18** | 13, 18, 49, 54, 55 (reference tool uses **10, 48, 49, 50**) | **Terrain weathering** paint (value 0.02–0.6) |

`surfaceBrushes[]` (empty) is the legacy array.

### 4.10 Map-edit layers (Confirmed)

Each layer is `{ "brushes": [Brush], "splines": [Spline], "IsEmpty": bool }`:

| Key | Purpose |
|---|---|
| `clearTrees` | Remove procedurally scattered trees |
| `clearDetail` | Remove grass/detail scatter |
| `outOfBounds2` | OB boundaries |
| `crowdMap2` | Gallery/crowd zones |
| `AllMapEdits[4]` | Array of the same layer shape. Contents mirror `clearDetail` / `clearTrees` (likely an aggregate the game rebuilds) |

`water[]`, `costMap[]`, `distanceMarkers[]` and `walkingDestinations[]` are empty in all samples.

### 4.11 Placed objects — `placedObjects4[]` (Confirmed)

A list of `{Key, Value}` pairs (a serialized C# dictionary keyed by asset path):

```json
{
  "Key":   {"path": "Assets/Foliage/SpeedTree8/Trees/Oak_LiveMoss/Prefabs/Oak_LiveMoss_904"},
  "Value": {
    "items":    [ {"position": {x, "-Infinity", z}, "rotation": {x, y, z}, "scale": {x, y, z}, "metadata": "<json string>"?} ],
    "clusters": [],
    "splines":  [],
    "objectPaths2": [ ObjectPath ],
    "IsEmpty": false
  }
}
```

- Asset path families seen: `Assets/CourseProps/{Equipment,Signs,Furniture,Buildings,Seasonal,ModularPacks}`, `Assets/Foliage/SpeedTree` and `SpeedTree8`, `Assets/CourseGen/{Detail,Bundle}`.
- `items[].metadata` (optional) is a **JSON-encoded string**. For example, `CameraPlaceholder` stores broadcast camera settings (`shotIndex`, `cameraIndex`, `BeautyShotType…` flags).
- `placedObjects3[]` (empty) is the legacy array.

**ObjectPath** (objects laid along a spline, e.g. fences, walls, **and native water**):

```json
{
  "path": Spline,
  "height": 0.0,
  "spacingRule": 3, "flexibilityRule": 1, "heightRule": 0,
  "spacing": 4.0,
  "disableHeightOffset": false, "disableSurfaceTexture": false,
  "isWater": false
}
```

**Native water** is an ObjectPath under the asset key `Assets/CourseGen/Detail/Walls/GeneratedRiverSetupPrefab`. The reference tool creates one like this:

```json
{
  "path": {"surface": 0, "secondarySurface": -1, "secondaryWidth": -1.0, "opacity": 1.0,
           "waypoints": [...], "width": 3.0, "hasCurves": true, "state": 2, "absoluteY": true},
  "height": <water level m>,
  "spacingRule": 0, "flexibilityRule": 0, "heightRule": 1, "spacing": 0.25,
  "disableHeightOffset": false, "disableSurfaceTexture": false,
  "heightOffset": -2.0, "edgeHeightOffset": 0.5,
  "water": {"flowDirection": 1.0, "flowSpeed": 0.0},
  "isWater": true
}
```

---

## 5. `CourseMetadata` (Confirmed)

This is the browse/lobby summary. **It duplicates data from `CourseDescription`, and nothing keeps it in sync automatically.** The reference tool never rewrites it, so its output files have stale metadata after tees or pins change. Our app should regenerate it on save.

| Key | Derivation (verified against samples) |
|---|---|
| `_id` | `CourseDescription._id + "-Meta"`; `version: 2`, `type: "metadata"` |
| `name`, `author` (`s<steamId64>`), `authorName` | Identity |
| `courseId` | `CourseDescription._id` |
| `holes`, `par`, `par3s`, `par4s`, `par5s`, `pars[]` | From `holes3[].par` |
| `length` | **Sum over holes of the polyline length of `holes3[].waypoints`** (metres, back-tee route). Verified to 0.1 m on all 3 samples |
| `teeSetInfos[]` | One per tee set: `{color, holeLengths[]}`. `color` order matches `teeColours` (`[4,0,3,2]`). Set *i* uses `tees[i]` of each hole (front → back); holes with fewer tees reuse their tee. Length = `|tee − waypoints[1]| + route length from waypoints[1] to waypoints[-1]` (i.e. the tee replaces `waypoints[0]`). Verified to 0.1 mm on all 54 holes × 4 sets |
| `pinSets` | Maximum pins per hole |
| ⚠ `pinPositions[hole][]` | Holds **absolute TEE positions** (equal to `holes3[].tees[].position`) — the key names are swapped in the game data |
| ⚠ `teePositions[hole][]` | Holds **absolute PIN positions** (`waypoints[-1] + pin offset`) |
| `greenSpeed`, `greenFirmness`, `terrainFirmness`, `timeOfDay`, `weather` | Mirrors of description conditions |
| `courseTheme`, `courseBackdrop`, `courseVersion` | Mirrors of `theme`, `backdrop`, `version` |
| `surfaces2` | Copy of the description material table |
| `PlayoffHolesSuddenDeath` (`[17, 9]`), `PlayoffHolesAggregate`, `PlayoffHoleFront9/Back9/FullCourse` (`-1`) | Playoff config (0-based hole indices) |
| Ratings / plays / awards / `favourites` / `challenges` / `scratchRating` / `slopeRating` / `bogeyRating` / `suggestionWeight` / `newFlag` / `newExpiry` / `isCareerValid` / `isHBCourse` / `isRealCourse` / `difficulty` | Online/lobby stats; zeros/false for offline saves |
| `timeStamp` | epoch ms |

## 6. `Thumbnail` (Confirmed)

```json
{"image": "<base64 JPEG (JFIF)>", "timeStamp": 1785977393737,
 "_id": "<courseId>-Thumb", "version": 1, "type": "thumbnail"}
```

The JPEG is an overhead render of about 35–55 KB.

---

## 7. Writing rules for our app

1. **Preserve unknown keys.** Deserialize into a model that keeps unrecognized properties (e.g. `JsonExtensionData`) so the ~140 keys we don't edit round-trip exactly.
2. **Append, don't reorder**, in `height[]` and `surfaceBrushes2[]`. These are replayed in order.
3. **Keep `"-Infinity"` as a string** in `position.y`.
4. **Don't round everything.** The reference tool rounds *every* float in the document to 3 decimals on save (`parse_float=round(x,3)`). That is lossy for rotations and handles. Only round values we generate.
5. **Regenerate `CourseMetadata`** (length, pars, tee sets, pin/tee positions with the swapped keys) whenever holes, tees or pins change. The thumbnail can be kept.
6. **Track our own edits** in a private top-level key (the reference tool uses `malificaCourseFinisher`; the game ignores unknown keys). Record `{startIndex, count, sha256 signature}` for each generated tail so a re-run can remove exactly what we added and nothing the user added by hand.
7. **Never overwrite the source file.** Default the output to `<name> - <suffix>.course`.
8. Gzip at compression level 6 or higher, UTF-16 LE with BOM, compact JSON.
