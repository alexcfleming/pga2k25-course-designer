# Testing

The suite is one xUnit project, `tests/Pga2K25.Tests`. Every test is labelled with an **area** and, when it takes a few seconds or more, **Speed=Slow**, so you can run the part that matches the risk of a change.

## How to run it

```powershell
tools\test.ps1                 # fast: 324 tests, about 10 s of testing (plus the build)
tools\test.ps1 area HoleSim    # one area, slow tests included
tools\test.ps1 slow            # only the slow end-to-end tests
tools\test.ps1 full            # everything: before a commit, a push or a release
```

Or with `dotnet test` directly: `--filter "Speed!=Slow"`, `--filter "Area=Products"`, `--filter "Speed=Slow"`.

| Tier | When | What it covers |
|---|---|---|
| **Fast** | every change, as you work | Course decoding, strokes gained, routing, estimates, the Studio view models, assumption sheets, the session record, the readiness checks and the simulator's shot model: the logic, on small inputs. |
| **Area** | after a change in that area | All of an area's tests, slow ones included: `Core`, `HoleSim`, `Products`, `Readiness`, `Water`. |
| **Full** | before committing or pushing to main, and before a release | Everything, including whole-product builds (cards, maps, books), whole-course water traces and the private-sample checks. |

Areas:

- **Core:** course format, calibration, the Studio, sample flattening, green outlines.
- **HoleSim:** the hole design simulator and strokes gained.
- **Products:** course cards, scorecard maps, yardage and greens books, terrain map, sketches, hole notes, plantings, wind, estimates, routing test.
- **Readiness:** publish readiness and hole sheets.
- **Water:** water tracing and water in play (Luke's course, Sangoma).

## What's slow and why

The slow tests are the end-to-end ones: a whole-course water trace (Sangoma's takes most of a minute), building every card, map or book for a course, and the simulator's play-outs on Pilolcura. They're kept because they catch the regressions the fast tests can't: product output, water in play and real-course figures. They run in the full suite.

## Keeping it quick

- **Build expensive things once per run.** `Samples` (in the test project) holds course models, the Cassimbo readiness measuring, traced water maps and the Pilolcura simulator setup, shared by every test class. They're read-only: a test that changes a course (a not-water mark, say) loads its own copy with `Samples.Fresh`.
- **Long test classes are split into nested classes**, which xUnit runs in parallel with the rest (for example `SangomaTests.NotWaterMarks`). A nested class can use its outer class's shared samples.
- **A test builds only what it checks.** Card tests that don't look at the hole notes turn off `AutoHoleNotes` and draw at low DPI.
- **Label a new test slow** (`[Trait("Speed", "Slow")]`) if it takes 3 s or more, and give a new class its `[Trait("Area", "...")]`. A test that only looks fast because another test built its shared sample first still belongs in the slow tier: run `tools\test.ps1 fast` and check nothing in it takes seconds.

## Timings (6 Nov 2026, 16 cores)

| | Before | After |
|---|---|---|
| Full suite, wall clock (with the build) | 4 m 13 s | 2 m 18 s |
| Sum of all test times | 2,187 s | 1,377 s |
| Readiness tests (one class, in sequence) | 248 s | 26 s |
| Fast tier | — | 280 tests, about 10 s |

What's left in the full run is mostly Sangoma's whole-course water traces (each one is needed: different settings, or a course with its own not-water mark), which now run side by side with everything else.

The biggest single win was in the product, not the tests: `ReadinessChecker` now skips fairway polygons a point can't be in (a bounding-box check before each containment test), which took a Cassimbo readiness run from 17 s to 0.7 s with byte-identical reports on three courses. Studio's Readiness page and the readiness record get the same speed-up. `ReadinessChecker.Prepare` also measures a course once for runs with different limits.
