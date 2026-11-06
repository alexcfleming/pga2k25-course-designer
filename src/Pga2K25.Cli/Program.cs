using System.Globalization;
using Pga2K25.CourseCard;
using Pga2K25.CourseFormat;
using Pga2K25.Estimate;
using Pga2K25.GreensBook;
using Pga2K25.Maps;
using Pga2K25.Readiness;
using Pga2K25.TerrainMap;
using Pga2K25.YardageBook;

const string Usage = """
    pga2k25 info <file.course>
    pga2k25 greensbook <file.course> [-o <dir>] [--studies] [--pdf] [--no-sheet] [--mono]
                       [--holes 1,2,3] [--hardness 0.85] [--blur 1.0] [--credit "NAME"]
    pga2k25 yardage <file.course> [-o <dir>] [--holes 1,2] [--png] [--no-pdf]
                    [--verbose] [--credit "NAME"]
      default output: one PDF workbook, a portrait 4.25 x 11 in page per hole
      --png       also write a 1275 x 3300 px PNG per hole
    pga2k25 terrain <file.course> [-o <dir>] [--full-plot|--buildable] [--margin 150] [--interval 5] [--index 5]
                    [--shading relief|slope|none] [--ppm 4] [--cell 1.0] [--grid 100] [--no-features]
                    [--hardness 0.85] [--large-hardness 0.3] [--smooth 0] [--sea-level <m>] [--no-water]
                    [--water-footprints] [--pdf [--print-dpi 250]] [--holes 7,12|all [--sheet-grid 50] [--sheet-side 75] [--no-pdf] [--no-png]]
      --holes     instead, one 8.5 x 11 in terrain sketch sheet per hole, oriented like the
                  yardage book (PNGs at 300 dpi + one PDF): a strip 75 yd past the route's
                  widest point each side (--sheet-side 75), the hole design notes beside it
                    [--no-summits] [--credit "NAME"]
                    [--planting | --planting-layers trees,bushes,grass,flowers,walls] [--trees-one-colour]
      --planting  draw what was planted in the designer (trees, bushes, grass, flowers, walls);
                  hole sheets leave flowers off. Vegetation the game adds by itself isn't shown.
      one north-up PNG of the plot: contours, relief or slope shading, lettered grid and
      the routing as sticks and dots ("<course> - Terrain Map.png")
      --interval  contour interval in feet (default: smallest of 1/2/5/10… that fits)
      --ppm       output pixels per metre of ground (default 4)
      --pdf       also write "<course> - Terrain Map.pdf", the same sheet with vector contours and
                  text; the page is the PNG at --print-dpi (default 250)
    pga2k25 sketches <file.course> <scan.pdf|.jpg|.png>... [-o <dir>] [--sheets <built Hole Sheets folder>]
      imports scanned hole sheets with pencil sketching: finds each page's hole by matching it
      against the course's sheets and writes "<course> - Sketches/" (pencil PNG, straightened
      scan and sketches.json, tied to world coordinates) for the Studio's terrain map
    pga2k25 hole-notes <file.course>
        Prints the hazards and green notes the hole data card writes from the course file.
    pga2k25 cards <file.course> [-o <dir>] [--config course-card.json]
                  [--formats png,pdf,svg] [--dpi 300] [--wind 225] [--ocean WEST|none] [--force]
      compass rose, hole data card, course data card, green size card (when the course has
      drawn greens) and Wind Card (when Place.json has a wind rose) from the course file;
      --wind is used only when there is no wind rose (the rose's prevailing wind always wins);
      ERRORs stop the build unless --force, which draws the cards marked DRAFT with the first
      error (errors are still reported); Studio always builds that way
    pga2k25 estimate <file.course> [-o <dir>] [--holes 3,7|all] [--cell 1.0]
      construction estimate: cut/fill against an assumed starting ground (play splines are
      manufactured, everything else natural), hole and course-wide quantities with provenance,
      the roll-up to a total budget, low/likely/high and a fidelity sweep. Writes the report
      ("<course> - Estimate.pdf" + page PNGs) and line items ("<course> - Estimate.json/.csv");
      per-course choices from <dir>/Estimate.json
    pga2k25 readiness <file.course> [--tee-shot 300-350] [--drive 360] [--limits limits.json] [--holes] [--json report.json]
      publish-readiness checks the course file can answer: scorecard and same-par runs, drivable
      par 4s and the par-5 test (needs --drive, the power-profile test drive in yds), green size
      and landing width against the median, elevation and green relief, corridors, green speed,
      fringe and lighting. Yardages from the back tees. --limits overrides any ReadinessLimits
      value; --holes adds the per-hole table; --json writes the whole report
    pga2k25 calibrate <file.course> <captures.json> [--hardness 0.85] [--blur 1.0]
    pga2k25 scorecard <file.course> [-o <dir>] [--theme vintage|modern|illustrated|plansheet|modernillustrated|both|all] [--landscape|--portrait]
                      [--north-up] [--waste] [--raw-bunkers] [--no-paths]
                      [--pdf] [--credit "NAME"] [--subtitle "TEXT"]

      yardage, cards and scorecard read tee names, hole names and hand-written hazards and
      green notes from Holes.json in the output folder (the Studio's Holes card)

      default output: one PNG page with every green ("<course> - Greens Book.png")
      --studies   also write a high-res study PNG per green (3300 x 2550 px)
      --pdf       also write a PDF (sheet page + one page per green)
      --mono      black-and-white linework, no slope colour

      calibrate compares modelled elevations with in-game spot labels read off green
      screenshots (see samples/calibration/cassimbo/captures.json for the format)

    pga2k25 holesim <file.course> --hole 8 [--tee T4|4] [--pin 1] [--player power|normal]
                    [--samples 2000] [--seed 7] [--treat 15=native,16=bunker] [--png map.png] [--json run.json]
      hole design simulator, one hole: the read-back (par, yards, aim marker, widths, green,
      pins, hazards near the line, unknown surfaces) and a geometry-only tee-shot sweep over
      aims right (+) / left (-) of the game's aim marker, valued with strokes gained against
      the shipped baseline. Uses the shipped assumption defaults (mostly PLACEHOLDER); the
      Studio section asks you to confirm each one. --treat says how unknown surface ids play
      (native, heavy, rough, fairway, bunker, scrub, ignore)
      --green builds the putting map for every pin (expected putts from each spot on the green, from a
      putting model fitted to the baseline's green rows); --green-png file.png also draws the chosen pin's map
      --approach fits the simulated golfer to the baseline's rows off the green (cached in AppData) and
      plays 600 shots at the green: --from 153[,side] (yds to the pin, yds right of the line) or
      --from tee (a par 3), --aim best|pin|centre|right,long or --aim-distance yds; --approach-png
      file.png draws where they finish
    pga2k25 holesim-terrain <file.course> --holes 11,3,4 [--smooth 8] [--readings 10] [--fit-length 25]
      terrain study for the hole design simulator, on a course whose distance markers carry the
      game's ground height (Sangoma): the brush replay and the replay smoothed outside greens,
      each alone and with a correction fitted to the markers nearest the suggested MEASURE spots
      and tested on every other marker; gate band and fairway slope spread per candidate

    """;

if (args.Length < 2) { Console.Error.WriteLine(Usage); return 2; }

try
{
    switch (args[0])
    {
        case "info": return Info(args[1]);
        case "greensbook": return GreensBook(args[1], args[2..]);
        case "scorecard": return Scorecard(args[1], args[2..]);
        case "yardage": return Yardage(args[1], args[2..]);
        case "terrain": return Terrain(args[1], args[2..]);
        case "cards": return Cards(args[1], args[2..]);
        case "hole-notes": return HoleNotesCommand(args[1]);
        case "sketches": return Sketches(args[1], args[2..]);
        case "estimate": return Estimate(args[1], args[2..]);
        case "readiness": return Readiness(args[1], args[2..]);
        case "calibrate" when args.Length >= 3: return Calibrate(args[1], args[2], args[3..]);
        case "holesim": return HoleSimCommand(args[1], args[2..]);
        case "holesim-terrain": return HoleSimTerrainCommand(args[1], args[2..]);
        default: Console.Error.WriteLine(Usage); return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(Environment.GetEnvironmentVariable("PGA2K25_DEBUG") is null ? $"error: {ex.Message}" : ex.ToString());
    return 1;
}

static int HoleSimTerrainCommand(string path, string[] rest)
{
    var inv = CultureInfo.InvariantCulture;
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    List<int>? holes = null; double smooth = 8, fit = 25; int readings = 10;
    for (int i = 0; i < rest.Length; i++)
    {
        string Next() => i + 1 < rest.Length ? rest[++i] : throw new ArgumentException($"{rest[i]} needs a value");
        switch (rest[i])
        {
            case "--holes": holes = Next().Split(',').Select(s => int.Parse(s, inv)).ToList(); break;
            case "--smooth": smooth = double.Parse(Next(), inv); break;
            case "--readings": readings = int.Parse(Next(), inv); break;
            case "--fit-length": fit = double.Parse(Next(), inv); break;
            default: throw new ArgumentException($"unknown option {rest[i]}");
        }
    }
    var file = CourseFile.Load(path);
    var course = file.ReadModel();
    if (course.DistanceMarkers.Count == 0) throw new ArgumentException("this course has no distance markers with in-game heights to compare against");
    holes ??= course.Holes.Select(h => h.Number).Take(3).ToList();
    Console.WriteLine($"{course.Name}: terrain candidates against {course.DistanceMarkers.Count:#,0} distance markers (the game's ground height)");
    Console.WriteLine($"Fitted rows: a correction fitted to {readings} markers nearest the suggested MEASURE spots, tested on the markers it didn't see.");
    Console.WriteLine();
    Console.WriteLine("hole  candidate                                                   n   RMS in  range in   pct  band          fairway slope p50/p90/p99   >10%");
    foreach (var r in Pga2K25.HoleSim.TerrainStudy.Run(course, file.Description, holes, smooth, readings, fit))
        Console.WriteLine($"{r.Hole,4}  {r.Candidate,-58} {r.Readings,4}  {r.RmsInches,7:0.0}  {r.RangeInches,8:0}  {r.Percent,4:0}%  {r.Band,-12}  {r.SlopeP50,5:0.0} / {r.SlopeP90,5:0.0} / {r.SlopeP99,5:0.0}%   {r.SteepShare,4:P0}");
    return 0;
}

static int HoleSimCommand(string path, string[] rest)
{
    var inv = CultureInfo.InvariantCulture;
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    int hole = 0, pin = 1; string? tee = null, png = null, json = null, greenPng = null; string player = "power"; bool green = false;
    bool approach = false; double fromYards = 150, fromSide = 0; string aimArg = "best"; double? aimDistance = null; string? approachPng = null; bool fromTee = false;
    var settings = new Pga2K25.HoleSim.RunSettings();
    var treat = new Dictionary<int, Pga2K25.HoleSim.Ground?>();
    for (int i = 0; i < rest.Length; i++)
    {
        string Next() => i + 1 < rest.Length ? rest[++i] : throw new ArgumentException($"{rest[i]} needs a value");
        switch (rest[i])
        {
            case "--hole": hole = int.Parse(Next(), inv); break;
            case "--tee": tee = Next(); break;
            case "--pin": pin = int.Parse(Next(), inv); break;
            case "--player": player = Next().ToLowerInvariant(); break;
            case "--samples": settings = settings with { SamplesPerAim = int.Parse(Next(), inv) }; break;
            case "--seed": settings = settings with { Seed = int.Parse(Next(), inv) }; break;
            case "--png": png = Next(); break;
            case "--json": json = Next(); break;
            case "--green": green = true; break;
            case "--green-png": green = true; greenPng = Next(); break;
            case "--approach": approach = true; break;
            case "--from":
                {
                    var v = Next().ToLowerInvariant();
                    if (v == "tee") { fromTee = true; break; }
                    var parts = v.Split(',');
                    fromYards = double.Parse(parts[0], inv); if (parts.Length > 1) fromSide = double.Parse(parts[1], inv);
                    break;
                }
            case "--aim": aimArg = Next().ToLowerInvariant(); break;
            case "--aim-distance": aimDistance = double.Parse(Next(), inv); break;
            case "--approach-png": approach = true; approachPng = Next(); break;
            case "--treat":
                foreach (var kv in Next().Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = kv.Split('=');
                    treat[int.Parse(parts[0], inv)] = parts[1].ToLowerInvariant() switch
                    {
                        "ignore" => null, "native" => Pga2K25.HoleSim.Ground.Native, "heavy" => Pga2K25.HoleSim.Ground.HeavyRough,
                        "rough" => Pga2K25.HoleSim.Ground.Rough, "fairway" => Pga2K25.HoleSim.Ground.Fairway,
                        "bunker" => Pga2K25.HoleSim.Ground.Bunker, "scrub" => Pga2K25.HoleSim.Ground.Scrub,
                        _ => throw new ArgumentException($"--treat: unknown ground '{parts[1]}'"),
                    };
                }
                break;
            default: throw new ArgumentException($"unknown option {rest[i]}");
        }
    }
    if (hole <= 0) throw new ArgumentException("--hole is required (one hole at a time)");

    var stamp = Pga2K25.HoleSim.FileStamp.Read(path);
    var file = CourseFile.Load(path);
    var course = file.ReadModel();
    var first = Pga2K25.HoleSim.HoleModelBuilder.Build(course, file.Description, hole, new() { Treatments = treat }, stamp);
    int? teeIndex = tee is null ? null
        : int.TryParse(tee, inv, out var n) ? n - 1
        : first.Tees.FirstOrDefault(t => t.Name.Equals(tee, StringComparison.OrdinalIgnoreCase))?.Index
          ?? throw new ArgumentException($"no tee '{tee}' (tees: {string.Join(", ", first.Tees.Select(t => t.Name))})");
    var m = teeIndex is null ? first : Pga2K25.HoleSim.HoleModelBuilder.Build(course, file.Description, hole, new() { Treatments = treat, TeeIndex = teeIndex }, stamp);

    Console.WriteLine($"{m.CourseName} · hole {m.Number} · {m.TeeLabel} · pin {pin}");
    Console.WriteLine($"File saved {stamp.ModifiedUtc.ToLocalTime():d MMM yyyy HH:mm} · sha256 {stamp.ShortHash}");
    Console.WriteLine();
    foreach (var r in Pga2K25.HoleSim.ReadBack.Rows(m))
        Console.WriteLine($"  {r.Item,-22} {r.Value}  [{Pga2K25.HoleSim.AssumptionRow.Tag(r.Source)}]");
    foreach (var u in m.UnknownSurfaces)
        Console.WriteLine($"  {"Unknown surface",-22} {u.Text}: plays as {(m.Treatments.TryGetValue(u.SurfaceId, out var g) ? g?.ToString() ?? "ignored" : "native (default)")}");

    var sheet = Pga2K25.HoleSim.AssumptionSheet.Load();
    var baseline = Pga2K25.HoleSim.TableBaseline.LoadShipped(sheet.BaselineId);
    var run = Pga2K25.HoleSim.GeometryRun.Run(m, sheet.Profile(player), pin, sheet.GroundRules(Pga2K25.HoleSim.GreenPhysics.RollingFriction(Pga2K25.HoleSim.AssumptionSheet.Stimp(course.GreenSpeed) ?? 10)), sheet.Lies(), baseline, settings);
    Console.WriteLine();
    Console.WriteLine($"Geometry-only tee shots, {run.Player.Name} ({run.Player.Carry:0} carry, SD {run.Player.CarrySd:0} / {run.Player.SideSd:0}), {settings.SamplesPerAim:#,0} per aim, seed {settings.Seed}");
    Console.WriteLine($"E(tee, {m.Pins[Math.Clamp(pin - 1, 0, m.Pins.Count - 1)].Frame.Length:0} yds) = {run.TeeExpected:0.000} [{run.TeeCoverage}]");
    Console.WriteLine("   aim  fairway  rough  native  bunker  scrub  yds left   SG tee shot      exp. strokes");
    foreach (var a in run.Aims)
    {
        double rough = a.Share(Pga2K25.HoleSim.Ground.Rough) + a.Share(Pga2K25.HoleSim.Ground.HeavyRough);
        Console.WriteLine($"  {a.Aim,4:+0;-0;0}  {a.Share(Pga2K25.HoleSim.Ground.Fairway),7:P0}  {rough,5:P0}  {a.Share(Pga2K25.HoleSim.Ground.Native),6:P0}  {a.Share(Pga2K25.HoleSim.Ground.Bunker),6:P0}  {a.Share(Pga2K25.HoleSim.Ground.Scrub),5:P0}  {a.MeanYardsLeft,8:0}   {a.MeanSg,+7:+0.000;-0.000} ±{a.SgCi95:0.000}   {a.ExpectedStrokes:0.000}{(a == run.Best ? "  best" : "")}");
    }
    foreach (var note in run.Notes) Console.WriteLine("  " + note);
    Console.WriteLine($"  Placeholders in use: {sheet.Placeholders.Count} of {sheet.Rows.Count} assumption rows.");

    if (green)
    {
        double stimp = Pga2K25.HoleSim.AssumptionSheet.Stimp(course.GreenSpeed) ?? 10;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var terr = Pga2K25.HoleSim.ReplayTerrain.Smoothed(course, m, sheet["terrain.smoothing"].Number);
        var surface = Pga2K25.HoleSim.GreenSurface.Build(m, terr);
        var putting = Pga2K25.HoleSim.PuttingModel.Create(stimp, baseline, sheet.Putting());
        var (p50, p90, steep) = surface.SlopeStats(putting.Friction, putting.Settings.StaticFactor);
        Console.WriteLine();
        Console.WriteLine($"Green: {surface.SquareYards:0} sq yd, Stimp {stimp:0.0} (friction {putting.Friction:0.0000}), slope p50 {p50:0.0}% p90 {p90:0.0}%, {steep:P0} too steep to hold a stopped ball");
        Console.WriteLine($"Putter fitted to {baseline.Id}: aim SD {putting.Skill.AimSdDegrees:0.0} deg, pace SD {putting.Skill.PaceSd:P0}; " +
            string.Join(", ", putting.Skill.Fit.Select(f => $"{f.Feet:0} ft {f.Model:0.00} (anchor {f.Baseline:0.00})")));
        var maps = new List<Pga2K25.HoleSim.PuttingMap>();
        foreach (var pn in m.Pins)
        {
            var map = Pga2K25.HoleSim.PuttingMap.Build(surface, putting, pn.Number, pn.Frame);
            var (above, below) = map.AboveBelow(surface);
            maps.Add(map);
            Console.WriteLine($"  pin {pn.Number} ({pn.Where}): average {map.Average:0.00} putts, {map.HardShare():P0} of the green needs 2.3+, 15-30 ft above {above:0.00} / below {below:0.00}, cup slope {surface.Slope(pn.Frame).Length * 100:0.0}%");
        }
        Console.WriteLine($"  ({sw.Elapsed.TotalSeconds:0.0} s)");
        if (greenPng is not null)
        {
            var sel = maps[Math.Clamp(pin - 1, 0, maps.Count - 1)];
            File.WriteAllBytes(greenPng, Pga2K25.HoleSim.HoleMapRenderer.RenderGreenPng(m, surface, sel, sel.Pin, putting.Friction * putting.Settings.StaticFactor * 100));
            Console.WriteLine($"Wrote {greenPng}");
        }
    }
    if (approach)
    {
        double stimp = Pga2K25.HoleSim.AssumptionSheet.Stimp(course.GreenSpeed) ?? 10;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var terr = Pga2K25.HoleSim.ReplayTerrain.Smoothed(course, m, sheet["terrain.smoothing"].Number);
        var surface = Pga2K25.HoleSim.GreenSurface.Build(m, terr);
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CourseDesignerStudio", "holesim");
        var (ctx, fit) = Pga2K25.HoleSim.ApproachSetup.Build(m, surface, null, sheet, baseline, stimp, cache);
        Console.WriteLine();
        Console.WriteLine($"Golfer fitted to {fit.BaselineId}: RMS {fit.RmsError:0.00} strokes over {fit.Rows.Count} anchors (worst {fit.MaxError:0.00}), {fit.Evaluations} tries");
        foreach (var r in fit.Rows) Console.WriteLine($"  {r.Lie,-9} {r.Yards,4:0} yds  anchor {r.Anchor:0.00}  golfer {r.Model:0.00}  {r.Error,5:+0.00;-0.00}");
        var gp = fit.Golfer;
        Console.WriteLine($"  spread {gp.DistPct:P1} long / {gp.SidePct:P1} side of the shot (+{gp.LongGrowth:0.00} per 100 yds past 150), floor {gp.FloorYds:0.0} yds; rough ×{gp.RoughMult:0.00}, sand ×{gp.SandMult:0.00}, recovery ×{gp.RecoveryMult:0.00}, tee ×{gp.TeeMult:0.00}; reach {gp.MaxCarry:0} yds, recovery {gp.RecoveryCarry:0}");
        var pinAt = m.Pins[Math.Clamp(pin - 1, 0, m.Pins.Count - 1)].Frame;
        var from = fromTee ? new Pga2K25.HoleSim.FramePoint(0, 0) : Pga2K25.HoleSim.ApproachSetup.SpotOnRoute(m, pin, fromYards, fromSide);
        Pga2K25.HoleSim.FramePoint aimAt;
        IReadOnlyList<Pga2K25.HoleSim.AimOption>? sweep = null;
        if (aimDistance is { } ad)
        {
            var dir = (pinAt - from) * (1 / (pinAt - from).Length); aimAt = from + dir * ad;
        }
        else if (aimArg == "pin") aimAt = pinAt;
        else if (aimArg is "centre" or "center") aimAt = m.GreenFrame;
        else if (aimArg == "best") { sweep = Pga2K25.HoleSim.ApproachSim.Sweep(ctx, from, pin, fromTee: fromTee); aimAt = sweep[0].Aim; }
        else { var xy = aimArg.Split(','); aimAt = pinAt + new Pga2K25.HoleSim.FramePoint(double.Parse(xy[0], inv), double.Parse(xy[1], inv)); }
        var res = Pga2K25.HoleSim.ApproachSim.Run(ctx, from, aimAt, pin, fromTee: fromTee);
        var shotDir = (pinAt - from) * (1 / (pinAt - from).Length);
        double AimLong() => (aimAt - pinAt).Lat * shotDir.Lat + (aimAt - pinAt).Along * shotDir.Along;
        double AimSide() => (aimAt - pinAt).Lat * shotDir.Along - (aimAt - pinAt).Along * shotDir.Lat;
        Console.WriteLine();
        Console.WriteLine($"Approach from {(fromTee ? "the tee" : $"{res.Yards:0} yds ({Pga2K25.HoleSim.Grounds.Name(res.Lie).ToLowerInvariant()})")} to pin {pin}, plays like {res.PlaysLike:0}, {res.Samples} shots, aim {AimSide():+0;-0;0} right / {AimLong():+0;-0;0} long of the pin");
        Console.WriteLine($"  green {res.GreenHit:P0}, proximity {res.ProximityFt:0} ft on the green; misses short {res.Short:P0}, long {res.Long:P0}, left {res.Left:P0}, right {res.Right:P0}; bunker {res.Share(Pga2K25.HoleSim.Ground.Bunker):P0}, water {res.Share(Pga2K25.HoleSim.Ground.Water):P0}");
        Console.WriteLine($"  expected strokes {res.Expected:0.000} ±{res.SgCi95:0.000}; " + (res.StrokesGained is { } sg ? $"SG for the shot {sg:+0.000;-0.000} against E(start) {res.Expected + sg:0.00}" : $"start outside the baseline's rows ({res.StartCoverage})"));
        if (sweep is not null)
        {
            Console.WriteLine("  best aims (yds right / long of the pin):");
            foreach (var o in sweep.Take(5)) Console.WriteLine($"    {o.Offset.Lat,3:+0;-0;0} / {o.Offset.Along,3:+0;-0;0}   {o.Expected:0.000}   green {o.GreenHit:P0}");
            var atPin = sweep.First(o => o.Offset.Length < 1e-6);
            Console.WriteLine($"    at the pin: {atPin.Expected:0.000} (the best aim saves {atPin.Expected - sweep[0].Expected:0.000})");
        }
        Console.WriteLine($"  ({sw.Elapsed.TotalSeconds:0.0} s)");
        if (approachPng is not null)
        {
            var pts = res.Shots.Select(s => (s.Finish, SkiaSharp.SKColor.Parse("#1F1D1A"))).ToList();
            var rings = new List<(Pga2K25.HoleSim.FramePoint, SkiaSharp.SKColor)> { (from, SkiaSharp.SKColor.Parse("#E06A3A")), (aimAt, SkiaSharp.SKColor.Parse("#3A6EA5")) };
            File.WriteAllBytes(approachPng, Pga2K25.HoleSim.HoleMapRenderer.RenderPng(m, new() { SelectedPin = pin, Overlay = new(pts, rings) }));
            Console.WriteLine($"Wrote {approachPng}");
        }
    }
    if (png is not null)
    {
        var best = run.Best;
        var colours = new[] { SkiaSharp.SKColor.Parse("#E06A3A"), SkiaSharp.SKColor.Parse("#1F1D1A"), SkiaSharp.SKColor.Parse("#3A6EA5") };
        var picks = new[] { best.Aim - 20, best.Aim, best.Aim + 20 }.Select(x => run.Aims.FirstOrDefault(a => Math.Abs(a.Aim - x) < 1e-6)).ToList();
        var chosen = picks.Select((a, j) => (Aim: a, Colour: colours[j])).Where(t => t.Aim is not null).ToList();
        var pts = chosen.SelectMany(t => t.Aim!.Samples.Select(s => (s.Finish, t.Colour))).ToList();
        var rings = chosen.Select(t => (Ring(m, t.Aim!.Aim, run.Player.Carry), t.Colour)).ToList();
        File.WriteAllBytes(png, Pga2K25.HoleSim.HoleMapRenderer.RenderPng(m, new() { SelectedPin = pin, Overlay = new(pts, rings) }));
        Console.WriteLine($"Wrote {png}");
    }
    if (json is not null)
    {
        File.WriteAllText(json, System.Text.Json.JsonSerializer.Serialize(new
        {
            course = m.CourseName, hole = m.Number, tee = m.Tee.Name, pin, stamp = new { stamp.ModifiedUtc, stamp.Sha256 },
            readBack = Pga2K25.HoleSim.ReadBack.Rows(m), run.BaselineId, run.TeeExpected, run.Notes, settings,
            aims = run.Aims.Select(a => new { a.Aim, a.ShareByGround, a.MeanYardsLeft, a.MedianYardsLeft, a.MeanSg, a.SgCi95, a.ExpectedStrokes, a.OutsideTableShare }),
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Wrote {json}");
    }
    return 0;

    static Pga2K25.HoleSim.FramePoint Ring(Pga2K25.HoleSim.HoleModel m, double aim, double carry)
    {
        var d = m.AimFrame * (1 / m.AimFrame.Length); var r = new Pga2K25.HoleSim.FramePoint(d.Along, -d.Lat);
        return d * carry + r * aim;
    }
}

static int Readiness(string path, string[] rest)
{
    var inv = CultureInfo.InvariantCulture;
    var limits = new ReadinessLimits();
    double? drive = null;
    (double Min, double Max)? tee = null;
    string? json = null;
    bool table = false;
    var jsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
    for (int i = 0; i < rest.Length; i++)
    {
        string Next() => i + 1 < rest.Length ? rest[++i] : throw new ArgumentException($"{rest[i]} needs a value");
        switch (rest[i])
        {
            case "--drive": drive = double.Parse(Next(), inv); break;
            case "--tee-shot":
                var parts = Next().Split('-');
                tee = (double.Parse(parts[0], inv), double.Parse(parts[^1], inv));
                break;
            case "--limits": limits = System.Text.Json.JsonSerializer.Deserialize<ReadinessLimits>(File.ReadAllText(Next()), jsonOptions) ?? limits; break;
            case "--json": json = Next(); break;
            case "--holes": table = true; break;
            default: throw new ArgumentException($"unknown option {rest[i]}");
        }
    }
    if (drive is not null) limits = limits with { TestDriveYards = drive };
    if (tee is { } t) limits = limits with { DesignDriveMinYards = t.Min, DesignDriveMaxYards = t.Max };

    var course = CourseFile.Load(path).ReadModel();
    var report = ReadinessChecker.Run(course, limits);
    Console.WriteLine($"{report.CourseName}: publish readiness from the course file (back tees" +
        (limits.TestDriveYards is double d ? $", {d:0}-yd test drive)" : ", no test drive entered)"));
    foreach (var c in report.Checks.OrderByDescending(c => c.Severity))
    {
        Console.WriteLine($"  {c.Severity.ToString().ToUpperInvariant(),-8} {c.Title}");
        Console.WriteLine($"           {c.Detail}");
        Console.WriteLine($"           {c.Rule}");
    }
    int open = report.Open.Count();
    Console.WriteLine($"{report.Checks.Count - open} of {report.Checks.Count} checks pass; {open} to fix, mark deliberate or confirm in game.");

    if (table)
    {
        Console.WriteLine();
        Console.WriteLine($"Hole Par  Yds Strt  Green sq ft      Widths at {limits.DesignDriveMinYards:0}/{limits.DesignDriveMidYards:0}/{limits.DesignDriveMaxYards:0}  Start Steady  Tee>green  Relief  Nearest other green  Landing-zone hazards");
        foreach (var h in report.Holes)
        {
            string green = h.GreenEstimated ? "not drawn" : string.Format(inv, "{0,6:N0} {1,5:0.00}x", h.GreenSquareFeet, h.GreenRatio ?? 0);
            string widths = string.Join("/", h.Stations.Select(s => s.WidthYards is double w ? w.ToString("0", inv) : "-"));
            string near = h.NearGreenYards is double ny ? string.Format(inv, "{0,4:0} yds ({1})", ny, h.NearGreenOf == 0 ? "practice" : $"hole {h.NearGreenOf}") : "";
            string relief = h.GreenReliefFeet is double r ? string.Format(inv, "{0:0.0} ft", r) : "";
            string start = h.FairwayStartYards is double fs ? fs.ToString("0", inv) + (h.FairwayStartOverrideYards is not null ? "!" : "") : "";
            string steady = h.WidthSteadiness is double sd ? sd.ToString("0.0", inv) + "x" : "";
            string hz = string.Join("; ", h.LandingHazards.Select(ReadinessChecker.Describe));
            Console.WriteLine(string.Format(inv, "{0,4} {1,3} {2,4} {3,4}  {4,-16} {5,-22} {6,5} {7,6} {8,7:+0;-0;0} ft {9,7}  {10,-20} {11}",
                h.Number, h.Par, h.Yards, h.StraightYards, green, widths, start, steady, h.ChangeFeet, relief, near, hz));
        }
        Console.WriteLine("  Strt: straight-line yards from the back tee. Start: where the main fairway starts (60 = 60 or less). Steady: wide over narrow past 200 yds.");
    }

    if (json is not null)
    {
        File.WriteAllText(json, System.Text.Json.JsonSerializer.Serialize(report, jsonOptions));
        Console.WriteLine($"Report: {json}");
    }
    return report.Checks.Any(c => c.Severity == Pga2K25.Readiness.Severity.High) ? 1 : 0;
}

static int Estimate(string path, string[] rest)
{
    var options = new EstimateOptions();
    List<int>? holes = null;
    string outDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
    for (int i = 0; i < rest.Length; i++)
    {
        string Next() => i + 1 < rest.Length ? rest[++i] : throw new ArgumentException($"{rest[i]} needs a value");
        switch (rest[i])
        {
            case "-o": case "--out": outDir = Next(); break;
            case "--holes":
                var h = Next();
                holes = h.Equals("all", StringComparison.OrdinalIgnoreCase) ? null : h.Split(',').Select(int.Parse).ToList();
                break;
            case "--cell": options = options with { Terrain = options.Terrain with { CellMetres = double.Parse(Next(), CultureInfo.InvariantCulture) } }; break;
            default: throw new ArgumentException($"unknown option {rest[i]}");
        }
    }
    var result = EstimateBuilder.Build(path, outDir, holes, options, new SyncProgress(Console.WriteLine));
    foreach (var e in result.Holes)
    {
        Console.WriteLine($"Hole {e.Hole.Number} (par {e.Hole.Par})");
        foreach (var q in e.Quantities)
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-28} {1,12:N1} {2,-6} {3,-9} {4}", q.Item, q.Qty, q.Unit, q.Provenance, q.Basis));
        foreach (var l in e.Lines)
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  ${0,-27} {1,12} {2}", l.Quantity.Item,
                l.Cost is { } c ? c.ToString("N0", CultureInfo.InvariantCulture) : "not priced", l.IsPlaceholder ? "PLACEHOLDER" : l.PriceStatus));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  Hole-direct total ${0:N0}: {1:P0} on placeholder prices; by quantity {2}",
            e.Total, e.PlaceholderShare, string.Join(", ", e.ShareByProvenance.Select(p => $"{p.Key.ToString().ToLowerInvariant()} {p.Value:P0}"))));
    }
    var s = result.Summary;
    var inv = CultureInfo.InvariantCulture;
    Console.WriteLine();
    Console.WriteLine(result.Partial ? $"Summary for {s.Holes} hole(s) only (partial)" : "Summary");
    foreach (var l in s.CourseWideLines)
        Console.WriteLine(string.Format(inv, "  {0,-40} {1,14:N0} {2}", l.Quantity.Item, l.Cost ?? 0, l.IsPlaceholder ? "PLACEHOLDER" : l.PriceStatus));
    void Line(string label, double v) => Console.WriteLine(string.Format(inv, "  {0,-40} {1,14:N0}", label, v));
    Line("Hole-direct", s.HoleDirect);
    Line("Course-wide", s.CourseWide);
    Line("Hard cost", s.Hard);
    Line("Regional adjustment", s.RegionalAdjustment);
    Line("Escalation to construction midpoint", s.Escalation);
    Line("Contingency", s.Contingency);
    Line("Construction budget", s.Construction);
    Line("Soft costs (design, field rep, permits, owner)", s.Soft);
    Line("TOTAL PROJECT BUDGET (ex. land & buildings)", s.Total);
    Console.WriteLine(string.Format(inv, "  Construction per hole ${0:N0} ({1}, band ${2:N0}-${3:N0}); per acre ${4:N0} over {5:0.0} acres of {6}",
        s.ConstructionPerHole, s.BenchmarkCheck, s.BenchmarkLow, s.BenchmarkHigh, s.ConstructionPerAcre, s.Acres, s.AcresBasis));
    Console.WriteLine(string.Format(inv, "  Hard cost on placeholder prices {0:P0}; by quantity {1}", s.PlaceholderShare,
        string.Join(", ", s.ShareByProvenance.Select(p => $"{p.Key.ToString().ToLowerInvariant()} {p.Value:P0}"))));
    Console.WriteLine();
    Console.WriteLine("Scenarios (total project budget)");
    foreach (var (name, run) in result.Scenarios)
        Console.WriteLine(string.Format(inv, "  {0,-8} {1,14:N0}   construction {2,14:N0}   cut {3,9:N0} / fill {4,9:N0} cu yd",
            name, run.Summary.Total, run.Summary.Construction, run.CutCuYd, run.FillCuYd));
    Console.WriteLine("Fidelity sweep (likely prices; offset added to every zone's natural fidelity)");
    foreach (var p in result.Sweep)
        Console.WriteLine(string.Format(inv, "  {0,6:+0.00;-0.00;0.00}   total {1,14:N0}   earthwork {2,10:N0}   cut {3,9:N0} / fill {4,9:N0} cu yd",
            p.FidelityOffset, p.Total, p.Earthwork, p.CutCuYd, p.FillCuYd));
    return 0;
}

static int Info(string path)
{
    var file = CourseFile.Load(path);
    var c = file.ReadModel();
    Console.WriteLine($"{c.Name}  ({c.Version}, schema v{c.SchemaVersion})");
    Console.WriteLine($"holes {c.Holes.Count}, par {c.Holes.Sum(h => h.Par)}, length {c.Holes.Sum(h => h.RouteLength):0.0} m");
    Console.WriteLine($"height stamps {c.HeightStamps.Count}, surface splines {c.SurfaceSplines.Count}");
    foreach (var h in c.Holes)
        Console.WriteLine($"  hole {h.Number,2}  par {h.Par}  {h.RouteLength,6:0.0} m  tees {h.Tees.Count}  pins {h.Pins.Count}");
    return 0;
}

static int GreensBook(string path, string[] rest)
{
    var options = new GreensBookOptions();
    var terrain = options.Terrain;
    string outDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
    var outputs = new GreensBookOutputs();
    List<int>? holes = null;

    for (int i = 0; i < rest.Length; i++)
    {
        string Next() => i + 1 < rest.Length ? rest[++i] : throw new ArgumentException($"{rest[i]} needs a value");
        double NextDouble() => double.Parse(Next(), CultureInfo.InvariantCulture);
        switch (rest[i])
        {
            case "-o": case "--out": outDir = Next(); break;
            case "--studies": outputs = outputs with { GreenStudies = true }; break;
            case "--pdf": outputs = outputs with { Pdf = true }; break;
            case "--no-sheet": outputs = outputs with { SheetPng = false }; break;
            case "--mono": options = options with { ColourSlopes = false }; break;
            case "--holes": holes = Next().Split(',').Select(int.Parse).ToList(); break;
            case "--hardness": terrain = terrain with { Hardness = NextDouble() }; break;
            case "--blur": terrain = terrain with { BlurSigmaMetres = NextDouble() }; break;
            case "--credit": options = options with { Credit = Next() }; break;
            default: throw new ArgumentException($"unknown option {rest[i]}");
        }
    }
    options = options with { Terrain = terrain };

    GreensBookBuilder.Build(path, outDir, options, outputs, holes, new SyncProgress(Console.WriteLine));
    return 0;
}

static int Calibrate(string coursePath, string capturesPath, string[] rest)
{
    var terrain = new TerrainModelOptions();
    for (int i = 0; i < rest.Length; i++)
    {
        double Next() => double.Parse(i + 1 < rest.Length ? rest[++i] : throw new ArgumentException($"{rest[i]} needs a value"), CultureInfo.InvariantCulture);
        switch (rest[i])
        {
            case "--hardness": terrain = terrain with { Hardness = Next() }; break;
            case "--blur": terrain = terrain with { BlurSigmaMetres = Next() }; break;
            default: throw new ArgumentException($"unknown option {rest[i]}");
        }
    }
    var course = CourseFile.Load(coursePath).ReadModel();
    var options = new GreensBookOptions { Terrain = terrain };
    var results = Calibration.Evaluate(course, Calibration.LoadCaptures(capturesPath), options);

    Console.WriteLine($"{course.Name}: hardness {terrain.Hardness:0.00}, blur {terrain.BlurSigmaMetres:0.00} m");
    foreach (var r in results)
    {
        string band = r.PercentOfRange < 10 ? "trustworthy" : r.PercentOfRange <= 18 ? "indicative" : "CHECK";
        Console.WriteLine($"Hole {r.Hole,2}: pin fit {r.PinFitMetres:0.00} m, {r.MetresPerPixel * 100:0.00} cm/px, " +
                          $"spot RMS {r.RmsInches:0.0} in = {r.PercentOfRange:0}% of {r.RangeInches:0} in  [{band}]");
        foreach (var s in r.Spots)
            Console.WriteLine($"   label {GreenAnalyzer.FormatFeetInches(s.Label.Inches),6}  model {GreenAnalyzer.FormatFeetInches(s.ModelInches),6}" +
                              $"  edge {s.EdgeDistance,5:0.0} m{(s.Used ? "" : "  (not scored: edge/outside)")}");
    }
    var scored = results.Where(r => double.IsFinite(r.PercentOfRange)).ToList();
    if (scored.Count > 0)
        Console.WriteLine($"Mean {scored.Average(r => r.PercentOfRange):0.0}% of range, worst {scored.Max(r => r.PercentOfRange):0.0}%.");
    return 0;
}

static int Yardage(string path, string[] rest)
{
    var options = new YardageOptions();
    string outDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
    List<int>? holes = null;
    var outputs = new YardageBookOutputs();
    bool verbose = false;
    for (int i = 0; i < rest.Length; i++)
    {
        string Next() => i + 1 < rest.Length ? rest[++i] : throw new ArgumentException($"{rest[i]} needs a value");
        switch (rest[i])
        {
            case "-o": case "--out": outDir = Next(); break;
            case "--holes": holes = Next().Split(',').Select(int.Parse).ToList(); break;
            case "--verbose": verbose = true; break;
            case "--png": outputs = outputs with { Pngs = true }; break;
            case "--no-pdf": outputs = outputs with { Pdf = false }; break;
            case "--pdf": break;   // default; accepted for compatibility
            case "--credit": options = options with { Credit = Next() }; break;
            default: throw new ArgumentException($"unknown option {rest[i]}");
        }
    }
    if (!outputs.Pdf && !outputs.Pngs) throw new ArgumentException("--no-pdf needs --png, or there is nothing to write");
    YardageBookBuilder.Build(path, outDir, options, outputs, holes, new SyncProgress(Console.WriteLine), verbose);
    return 0;
}

static int Sketches(string path, string[] rest)
{
    string outDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
    var scans = new List<string>();
    string? sheets = null;
    for (int i = 0; i < rest.Length; i++)
    {
        if (rest[i] is "-o" or "--out" && i + 1 < rest.Length) outDir = rest[++i];
        else if (rest[i] == "--sheets" && i + 1 < rest.Length) sheets = rest[++i];
        else scans.Add(rest[i]);
    }
    if (scans.Count == 0) throw new ArgumentException("give at least one scan (PDF or image)");
    var course = CourseFile.Load(path).ReadModel();
    string stem = string.Concat((string.IsNullOrWhiteSpace(course.Name) ? Path.GetFileNameWithoutExtension(path) : course.Name)
        .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
    var log = new Progress<string>(Console.WriteLine);
    // The Hole sheets build's PNGs, when beside the output, are usually what was printed.
    string built = sheets ?? Path.Combine(outDir, $"{stem} - Hole Sheets");
    var results = HoleSketches.Import(path, scans, HoleSketches.FolderFor(outDir, stem), new HoleSheetOptions(), log,
        Directory.Exists(built) ? built : null);
    foreach (var r in results) Console.WriteLine($"{Path.GetFileName(r.Source)} p{r.Page}: {r.Message}");
    return 0;
}

static int Terrain(string path, string[] rest)
{
    var options = new TerrainMapOptions();
    var sheet = new HoleSheetOptions();
    string? holes = null;
    string? regionShape = null; int regionSeed = 1; double regionKm = 25;
    string outDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
    for (int i = 0; i < rest.Length; i++)
    {
        string Next() => i + 1 < rest.Length ? rest[++i] : throw new ArgumentException($"{rest[i]} needs a value");
        double NextDouble() => double.Parse(Next(), CultureInfo.InvariantCulture);
        switch (rest[i])
        {
            case "-o": case "--out": outDir = Next(); break;
            case "--holes": holes = Next(); break;
            case "--sheet-grid": sheet = sheet with { GridYards = NextDouble() }; break;
            case "--sheet-side": sheet = sheet with { SideYards = NextDouble() }; break;
            case "--no-pdf": sheet = sheet with { Pdf = false }; break;
            case "--no-png": sheet = sheet with { Png = false }; break;
            case "--full-plot": options = options with { Extent = MapExtent.FullPlot }; break;
            case "--buildable": options = options with { Extent = MapExtent.Buildable }; break;
            case "--margin": options = options with { MarginMetres = NextDouble() }; break;
            case "--interval": options = options with { ContourIntervalFeet = NextDouble() }; break;
            case "--index": options = options with { IndexEvery = int.Parse(Next(), CultureInfo.InvariantCulture) }; break;
            case "--shading":
                options = options with
                {
                    Shading = Next().ToLowerInvariant() switch
                    {
                        "relief" => ReliefShading.Relief,
                        "slope" => ReliefShading.Slope,
                        "none" => ReliefShading.None,
                        var t => throw new ArgumentException($"unknown shading {t}"),
                    },
                };
                break;
            case "--ppm": options = options with { PixelsPerMetre = NextDouble() }; break;
            case "--cell": options = options with { Terrain = options.Terrain with { CellMetres = NextDouble() } }; break;
            case "--hardness": options = options with { Terrain = options.Terrain with { Hardness = NextDouble() } }; break;
            case "--large-hardness": options = options with { Terrain = options.Terrain with { LargeBrushHardness = NextDouble() } }; break;
            case "--smooth": options = options with { SmoothMetres = NextDouble() }; break;
            case "--grid": options = options with { GridYards = NextDouble() }; break;
            case "--no-features": options = options with { ShowFeatures = false }; break;
            case "--no-summits": options = options with { ShowSummits = false }; break;
            case "--sea-level": options = options with { SeaLevelMetres = NextDouble() }; break;
            case "--no-water": options = options with { ShowWater = false }; break;
            case "--water-footprints": options = options with { ShowWaterFootprints = true }; break;
            case "--no-drawn-green-sites": options = options with { ShowDrawnGreenSites = false }; break;
            case "--credit": options = options with { Credit = Next() }; break;
            case "--pdf": options = options with { Pdf = true }; break;
            case "--print-dpi": options = options with { PrintDpi = NextDouble() }; break;
            case "--planting": options = options with { Planting = PlantingLayers.All }; break;
            case "--site-inset": options = options with { SiteInset = true }; break;
            case "--region": regionShape = Next(); break;
            case "--seed": regionSeed = int.Parse(Next()); break;
            case "--region-km": regionKm = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
            case "--planting-layers": options = options with { Planting = ParseLayers(Next()) }; break;
            case "--trees-one-colour": options = options with { TreesByType = false }; break;
            default: throw new ArgumentException($"unknown option {rest[i]}");
        }
    }
    if (regionShape is not null) options = options with { Region = CliRegion(path, regionShape, regionSeed, regionKm) };
    if (holes is null)
    {
        TerrainMapBuilder.Build(path, outDir, options, new SyncProgress(Console.WriteLine));
        return 0;
    }
    // Per-hole sketch sheets; the plot map's --interval would be too coarse here, so it applies only if given.
    var numbers = holes.Equals("all", StringComparison.OrdinalIgnoreCase) ? null : holes.Split(',').Select(int.Parse).ToList();
    // The notes column prints the hole design notes saved by the Studio's Readiness page beside the outputs.
    var notes = Pga2K25.Readiness.SheetNotes.Load(outDir, path);
    HoleSheetBuilder.Build(path, outDir, sheet with { Map = options, DesignNotes = notes }, numbers, new SyncProgress(Console.WriteLine));
    return 0;
}

/// <summary>"trees,bushes,grass,flowers,walls" (any of them) as planting layers.</summary>
static PlantingLayers ParseLayers(string list) =>
    list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Aggregate(PlantingLayers.None, (l, name) => l | name.ToLowerInvariant() switch
    {
        "trees" => PlantingLayers.Trees,
        "bushes" => PlantingLayers.Bushes,
        "grass" => PlantingLayers.Grass,
        "flowers" => PlantingLayers.Flowers,
        "walls" => PlantingLayers.Walls,
        "all" => PlantingLayers.All,
        var t => throw new ArgumentException($"unknown planting layer {t} (trees, bushes, grass, flowers, walls)"),
    });

static int Cards(string path, string[] rest)
{
    var config = new CourseCardConfig();
    string outDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
    bool force = false;
    for (int i = 0; i < rest.Length; i++)
    {
        string Next() => i + 1 < rest.Length ? rest[++i] : throw new ArgumentException($"{rest[i]} needs a value");
        switch (rest[i])
        {
            case "-o": case "--out": outDir = Next(); break;
            case "--config": config = CourseCardConfig.Load(Next()) with { }; break;
            case "--formats": config = config with { Formats = Next().Split(',') }; break;
            case "--dpi": config = config with { Dpi = double.Parse(Next(), CultureInfo.InvariantCulture) }; break;
            case "--wind": config = config with { WindFromDegrees = int.Parse(Next(), CultureInfo.InvariantCulture) }; break;
            case "--no-auto-notes": config = config with { AutoHoleNotes = false }; break;
            case "--ocean": config = config with { OceanSide = Next() }; break;
            case "--force": force = true; break;
            default: throw new ArgumentException($"unknown option {rest[i]}");
        }
    }
    CourseCardBuilder.Build(path, outDir, config, new SyncProgress(Console.WriteLine), force);
    return 0;
}

static int Scorecard(string path, string[] rest)
{
    var options = new ScorecardMapOptions();
    string? regionShape = null; int regionSeed = 1; double regionKm = 25;
    IEnumerable<MapThemeKind> themes = Enum.GetValues<MapThemeKind>();
    string outDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
    bool pdf = false;
    for (int i = 0; i < rest.Length; i++)
    {
        string Next() => i + 1 < rest.Length ? rest[++i] : throw new ArgumentException($"{rest[i]} needs a value");
        switch (rest[i])
        {
            case "-o": case "--out": outDir = Next(); break;
            case "--theme":
                themes = Next().ToLowerInvariant() switch
                {
                    "vintage" => [MapThemeKind.Vintage],
                    "modern" => [MapThemeKind.Modern],
                    "illustrated" => [MapThemeKind.Illustrated],
                    "plansheet" or "plan-sheet" or "plan" => [MapThemeKind.PlanSheet],
                    "modernillustrated" or "modern-illustrated" => [MapThemeKind.ModernIllustrated],
                    "both" => [MapThemeKind.Vintage, MapThemeKind.Modern],
                    "all" => Enum.GetValues<MapThemeKind>(),
                    var t => throw new ArgumentException($"unknown theme {t}"),
                };
                break;
            case "--landscape": options = options with { Orientation = PageOrientation.Landscape }; break;
            case "--portrait": options = options with { Orientation = PageOrientation.Portrait }; break;
            case "--north-up": options = options with { AutoRotate = false }; break;
            case "--waste": options = options with { ShowWaste = true }; break;
            case "--raw-bunkers": options = options with { MergeBunkers = false }; break;
            case "--bunker-merge": options = options with { BunkerMergeMetres = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture) }; break;
            case "--no-paths": options = options with { ShowPaths = false }; break;
            case "--pdf": pdf = true; break;
            case "--credit": options = options with { Credit = Next() }; break;
            case "--subtitle": options = options with { Subtitle = Next() }; break;
            case "--location": options = options with { Location = Next() }; break;
            case "--region": regionShape = Next(); break;
            case "--seed": regionSeed = int.Parse(Next()); break;
            case "--region-km": regionKm = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
            default: throw new ArgumentException($"unknown option {rest[i]}");
        }
    }
    if (regionShape is not null) options = options with { Watermark = CliRegion(path, regionShape, regionSeed, regionKm) };
    ScorecardMapBuilder.Build(path, outDir, options, themes, pdf, log: new SyncProgress(Console.WriteLine));
    return 0;
}

static int HoleNotesCommand(string path)
{
    var course = CourseFile.Load(path).ReadModel();
    foreach (var n in Pga2K25.CourseCard.HoleNotes.For(course, new Progress<string>(Console.Error.WriteLine)))
    {
        Console.WriteLine($"{n.Hole,2}  {string.Join(" · ", n.Hazards)}");
        Console.WriteLine($"    {n.GreenNotes}");
    }
    return 0;
}

// A generated region from the command line, turned to the plot's surf-prop sea side.
static Region CliRegion(string path, string shape, int seed, double km) =>
    RegionGenerator.Generate(RegionGenerator.Parse(shape), seed, OceanFinder.SeaBearing(CourseFile.Load(path).ReadModel()), km);

sealed class SyncProgress(Action<string> write) : IProgress<string>
{
    public void Report(string value) => write(value);
}
