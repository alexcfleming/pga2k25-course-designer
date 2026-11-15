namespace Pga2K25.TerrainMap;

/// <summary>A traced hand-drawn line in straightened-frame pixels; <see cref="Construction"/> for shot lines and marks.</summary>
public sealed record TracedLine(IReadOnlyList<(float X, float Y)> Points, bool Construction);

/// <summary>
/// Turns the pencil on a straightened sheet into lines. Strokes are grown from confident pixels into fainter ones
/// (so a stroke that fades stays whole), thinned to centre lines, traced, joined through junctions into long strokes
/// along the straightest way on, bridged across short breaks (where a line crossed the printed route or text) and
/// smoothed.
///
/// Each stroke is then judged whole: an edge (tee, fairway, hazard, green) or construction. Construction is a
/// ruler-straight stroke (shot lines from the tee through lettered aim points to the green), or, when the sheet is
/// drawn in two weights, a light stroke that is straight or short (aim dots, letters, hatch ticks) and not part of a
/// cluster of curving line (a lightly drawn scalloped bunker). Two weights means the strokes' darkness splits cleanly
/// with the light group the smaller; a sheet drawn in one weight only loses its straight strokes.
/// Sizes are for a frame straightened at <see cref="SheetScan.PixelsPerPoint"/> (3 px per point).
/// </summary>
public static class SketchTracer
{
    const float Strong = 0.45f, Weak = 0.2f;
    const int MinStroke = 25, BridgePixels = 18, TinyPiece = 12, StraightPixels = 60, CurvingPixels = 30, CurvingCluster = 120,
        MinFirmPixels = 15, StubPixels = 8;

    /// <summary>A stroke this much warmer (red over blue) than neutral is printed contour brown, not graphite or pen.</summary>
    public const double BrownWarmth = 15;
    /// <summary>… and this much warmer than the paper.</summary>
    public const double WarmerThanPaper = 4;
    /// <summary>The hairline / hand split is taken when it explains this much of the spread of ink darkness (two peaks).</summary>
    public const double TwoPeaks = 0.65;
    /// <summary>… and the lighter ink is in this share of the sheet's 40 pt tiles.</summary>
    public const double HairlineSpread = 0.5;
    /// <summary>Darkness from which ink is black pen whatever its tint (index contours scan at 150–230).</summary>
    public const int BlackInk = 235;
    /// <summary>Ink this much greener than it is red or blue is the printed green site ring.</summary>
    public const double GreenInk = 6;

    /// <summary>
    /// With <paramref name="scanRgba"/> (the straightened scan's pixels), strokes are also judged by their ink, for a
    /// sheet whose print today's render can't explain (the ground was reshaped after printing): brown strokes and
    /// strokes no darker than the sheet's mass of hairline contours are print, and are dropped.
    /// </summary>
    public static List<TracedLine> Trace(float[] score, byte[] scanNormalised, int w, int h, out double? faintBelow, byte[]? scanRgba = null,
        (int X0, int Y0, int X1, int Y1)? strip = null)
    {
        var mask = Hysteresis(score, w, h);
        if (scanRgba is not null) KeepHandInk(mask, scanNormalised, scanRgba, w, h, strip ?? (0, 0, w - 1, h - 1));
        Close(mask, w, h);
        Thin(mask, w, h);
        var edges = TracePaths(mask, w, h);

        // How dark each piece is on the scan: 255 − the darkest pixel within 1 px, median along the piece.
        var dark = new byte[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int min = 255;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int xx = Math.Clamp(x + dx, 0, w - 1), yy = Math.Clamp(y + dy, 0, h - 1);
                min = Math.Min(min, scanNormalised[yy * w + xx]);
            }
            dark[y * w + x] = (byte)(255 - min);
        }
        var darkness = edges.Select(e =>
        {
            var d = e.Pixels.Select(i => (int)dark[i]).OrderBy(v => v).ToList();
            return (double)d[d.Count / 2];
        }).ToArray();

        // Pieces meet at junction pixels; the stubs thinning leaves where a line steps diagonally are too short to
        // judge, so they take the darkness of the pieces around them.
        var incident = new Dictionary<int, List<int>>();
        for (int k = 0; k < edges.Count; k++)
            foreach (int node in edges[k].Nodes())
                (incident.TryGetValue(node, out var list) ? list : incident[node] = []).Add(k);
        bool Tiny(int k) => edges[k].Pixels.Count < TinyPiece;
        for (int k = 0; k < edges.Count; k++)
        {
            if (!Tiny(k)) continue;
            double sum = 0, weight = 0;
            foreach (int node in edges[k].Nodes())
                foreach (int o in incident[node])
                    if (o != k && !Tiny(o)) { sum += darkness[o] * edges[o].Pixels.Count; weight += edges[o].Pixels.Count; }
            if (weight > 0) darkness[k] = sum / weight;
        }

        faintBelow = FaintThreshold(Enumerable.Range(0, edges.Count).Where(k => !Tiny(k))
            .Select(k => (darkness[k], (double)edges[k].Pixels.Count)).ToList());
        double? t = faintBelow;

        // Strokes first, judged whole: an outline stays one long curving stroke however lightly parts of it are
        // drawn, and a shot line crossing it carries straight on as its own stroke.
        // Bridged before they are judged, so an edge broken into short arcs (light scalloped bunkers) is judged whole.
        var strokes = Bridge(Chain(edges, incident, w), w).Select(p => new PathPixels(p, false, false)).ToList();
        // Strokes that touch form a cluster; how much curving line a cluster holds tells a scalloped bunker
        // (many short light arcs) from an aim dot or hatch ticks on a shot line.
        var cluster = Clusters(mask, w, h);
        var curving = new Dictionary<int, int>();
        bool Curving(PathPixels c) => Chord(c.Pixels, w) < 0.85 * c.Pixels.Count && !Straight(c.Pixels, w, StraightPixels);
        foreach (var c in strokes)
            if (Curving(c)) curving[cluster[c.Pixels[0]]] = curving.GetValueOrDefault(cluster[c.Pixels[0]]) + c.Pixels.Count;

        // Where printed lines crossed a pencil line, stubs of them are left on it.
        strokes.RemoveAll(c => c.Pixels.Count < StubPixels);
        var firm = new List<PathPixels>();
        var faint = new List<PathPixels>();
        foreach (var c in strokes)
        {
            var d = c.Pixels.Select(i => (int)dark[i]).OrderBy(v => v).ToList();
            bool light = t is not null && d[d.Count / 2] < t;
            int length = c.Pixels.Count;
            bool straightish = Chord(c.Pixels, w) >= 0.85 * length;
            bool inCurvingCluster = curving.GetValueOrDefault(cluster[c.Pixels[0]]) >= CurvingCluster;
            // Shot lines are ruler-straight however firmly drawn; light straight marks are shot lines too, and
            // light short ones aim dots, letters and hatch ticks unless they are part of a curving outline.
            // Everything else is an edge.
            if (Straight(c.Pixels, w, StraightPixels) || (light && length >= MinFirmPixels && straightish && length >= CurvingPixels)
                || (light && length < CurvingPixels && !inCurvingCluster)) faint.Add(c);
            else if (length >= MinFirmPixels || inCurvingCluster) firm.Add(c);
        }

        var result = new List<TracedLine>();
        foreach (var c in firm)
            result.Add(new TracedLine(Smooth(c.Pixels, w), false));
        foreach (var c in faint)
            result.Add(new TracedLine(Smooth(c.Pixels, w), true));
        return result;
    }

    /// <summary>
    /// How much of what a pencil score holds is contour brown (0–1): near nothing when the sheet it was scored
    /// against explains the print, a good part when the ground has been reshaped since the sheet was printed.
    /// </summary>
    public static double BrownShare(float[] score, byte[] scanNormalised, byte[] scanRgba, int w, int h)
    {
        var mask = Hysteresis(score, w, h);
        int before = mask.Count(m => m);
        if (before == 0) return 0;
        var kept = (bool[])mask.Clone();
        KeepHandInk(kept, scanNormalised, scanRgba, w, h, (0, 0, w - 1, h - 1), brownOnly: true);
        return 1 - (double)kept.Count(m => m) / before;
    }

    /// <summary>
    /// Takes printed ink out of the mask by what it is made of. Each pixel's ink is read at its core, the darkest
    /// pixel within 2 px (what is left of a printed line can be only its edge, and a pen line's edge takes colour
    /// from what it crosses), with its warmth (red over blue) averaged over the 3 × 3 there. Brown ink is index
    /// contour and green ink the green site's ring. Ink no darker than the sheet's mass of hairlines (1 ft contours scan pale and nearly grey) is
    /// contour too, when hairlines are a good part of what is there. What is left smaller than a stroke goes.
    /// </summary>
    static void KeepHandInk(bool[] mask, byte[] norm, byte[] rgba, int w, int h, (int X0, int Y0, int X1, int Y1) strip, bool brownOnly = false)
    {
        // Darkness is taken over the ground around it (13 × 13), so hairlines bunched on a steep slope or lying on
        // the relief shading don't add up to a pencil line's.
        var around = new float[mask.Length];
        for (int i = 0; i < around.Length; i++) around[i] = 255 - norm[i];
        around = SheetScan.BoxBlur(around, w, h, 6);
        var coreDark = new byte[mask.Length];
        var brown = new bool[mask.Length];
        // The paper's own warmth (a sheet printed with its tinted ground scans cream; a scanner may whiten it).
        var tint = new long[511];
        for (int i = 0; i < norm.Length; i += 5) if (norm[i] > 240) tint[rgba[4 * i] - rgba[4 * i + 2] + 255]++;
        long paperCount = tint.Sum(), run = 0;
        int paperWarm = 0;
        for (int v = 0; v < tint.Length && paperCount > 0; v++) { run += tint[v]; if (2 * run >= paperCount) { paperWarm = v - 255; break; } }
        double brownFrom = Math.Max(BrownWarmth, paperWarm + WarmerThanPaper);
        var hist = new long[256];
        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i]) continue;
            int x = i % w, y = i / w, core = i;
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int xx = x + dx, yy = y + dy;
                if (xx >= 0 && yy >= 0 && xx < w && yy < h && norm[yy * w + xx] < norm[core]) core = yy * w + xx;
            }
            int cx = core % w, cy = core / w, sum = 0, n = 0, green = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int xx = cx + dx, yy = cy + dy;
                if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                int j = 4 * (yy * w + xx);
                sum += rgba[j] - rgba[j + 2]; n++;
                green += rgba[j + 1] - Math.Max(rgba[j], rgba[j + 2]);
            }
            // Darkness from 1 px only, so a hairline doesn't borrow a pen line's where they cross.
            int near = norm[i];
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int xx = x + dx, yy = y + dy;
                if (xx >= 0 && yy >= 0 && xx < w && yy < h) near = Math.Min(near, norm[yy * w + xx]);
            }
            coreDark[i] = (byte)Math.Clamp(255 - near - around[i], 0, 255);
            // Ink as dark as black pen is never contour brown, even drawn along a contour.
            brown[i] = ((double)sum / n > brownFrom && 255 - norm[core] < BlackInk) || (double)green / n > GreenInk;
            if (!brown[i]) hist[coreDark[i]]++;
        }
        // Hairlines and hand ink are two peaks; Otsu's split between them, taken only when both are really there
        // (means 45 apart, each at least a tenth of the ink, the split explaining most of the spread).
        double total = 0, sumAll = 0;
        for (int v = 0; v < 256; v++) { total += hist[v]; sumAll += (double)v * hist[v]; }
        double mean = total > 0 ? sumAll / total : 0, variance = 0;
        for (int v = 0; v < 256; v++) variance += hist[v] * (v - mean) * (v - mean);
        double best = 0, lightCut = 0, wa = 0, sa = 0;
        for (int v = 0; v < 255 && total > 0; v++)
        {
            wa += hist[v]; sa += (double)v * hist[v];
            double wb = total - wa;
            if (wa < 0.1 * total || wb < 0.1 * total) continue;
            double ma = sa / wa, mb = (sumAll - sa) / wb, between = wa * wb * (ma - mb) * (ma - mb);
            if (between > best && mb - ma >= 45) { best = between; lightCut = v + 1; }
        }
        double split = variance > 0 ? best / total / variance : 0;
        if (split < TwoPeaks) lightCut = 0;
        if (lightCut > 0)
        {
            // Hairlines cover the sheet; a light pencil drawing under darker marks doesn't. The lighter ink must be
            // in most 40 pt tiles of the map strip.
            const int tile = 120;
            int tw = (w + tile - 1) / tile, th = (h + tile - 1) / tile;
            var light = new int[tw * th];
            for (int i = 0; i < mask.Length; i++)
                if (mask[i] && !brown[i] && coreDark[i] < lightCut) light[i / w / tile * tw + i % w / tile]++;
            int tiles = (strip.X1 / tile - strip.X0 / tile + 1) * (strip.Y1 / tile - strip.Y0 / tile + 1);
            if (light.Count(n => n >= 20) < HairlineSpread * tiles) lightCut = 0;
        }
        if (brownOnly)
        {
            for (int i = 0; i < mask.Length; i++) if (mask[i] && brown[i]) mask[i] = false;
            return;
        }
        for (int i = 0; i < mask.Length; i++) if (mask[i] && (brown[i] || coreDark[i] < lightCut)) mask[i] = false;

        var seen = new bool[mask.Length];
        var stack = new Stack<int>();
        var group = new List<int>();
        for (int start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || seen[start]) continue;
            group.Clear();
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                group.Add(i);
                int x = i % w, y = i / w;
                foreach (var (dx, dy) in Around)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int q = ny * w + nx;
                    if (mask[q] && !seen[q]) { seen[q] = true; stack.Push(q); }
                }
            }
            if (group.Count < MinStroke) foreach (int i in group) mask[i] = false;
        }
    }

    /// <summary>Connected groups of the thinned lines (8-neighbour), as a label per pixel.</summary>
    static int[] Clusters(bool[] m, int w, int h)
    {
        var label = new int[m.Length];
        var stack = new Stack<int>();
        int next = 0;
        for (int start = 0; start < m.Length; start++)
        {
            if (!m[start] || label[start] != 0) continue;
            label[start] = ++next;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int i = stack.Pop(), x = i % w, y = i / w;
                foreach (var (dx, dy) in Around)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int n = ny * w + nx;
                    if (m[n] && label[n] == 0) { label[n] = next; stack.Push(n); }
                }
            }
        }
        return label;
    }

    static double Chord(List<int> px, int w)
    {
        double dx = px[^1] % w - px[0] % w, dy = px[^1] / w - px[0] / w;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>At least <paramref name="minChord"/> long and never more than 3 px (or 3 %, freehand) off its chord.</summary>
    static bool Straight(List<int> px, int w, double minChord)
    {
        double ax = px[0] % w, ay = px[0] / w, bx = px[^1] % w, by = px[^1] / w;
        double dx = bx - ax, dy = by - ay, chord = Math.Sqrt(dx * dx + dy * dy);
        if (chord < minChord) return false;
        double limit = Math.Max(3, 0.03 * chord);
        foreach (int i in px)
            if (Math.Abs((i % w - ax) * dy - (i / w - ay) * dx) / chord > limit) return false;
        return true;
    }

    /// <summary>
    /// Joins pieces into long strokes: from each unused piece, carry on through junctions along the piece that turns
    /// least (within 70°), at both ends. A stroke's end is free (open to bridging) when no other piece meets it there.
    /// </summary>
    static List<PathPixels> Chain(List<PathPixels> edges, Dictionary<int, List<int>> incident, int w)
    {
        var used = new bool[edges.Count];
        var chains = new List<PathPixels>();
        (double X, double Y) Dir(List<int> px, bool atEnd)
        {
            int n = Math.Min(8, px.Count - 1);
            int a = atEnd ? px[^1] : px[0], b = atEnd ? px[^(n + 1)] : px[n];
            double dx = a % w - b % w, dy = a / w - b / w, len = Math.Sqrt(dx * dx + dy * dy);
            return len < 1e-6 ? (0, 0) : (dx / len, dy / len);
        }
        int Others(int node, int except) => incident.TryGetValue(node, out var l) ? l.Count(o => o != except) : 0;

        for (int start = 0; start < edges.Count; start++)
        {
            if (used[start]) continue;
            used[start] = true;
            var px = new List<int>(edges[start].Pixels);
            bool closed = edges[start].Pixels.Count > 2 && edges[start].Pixels[0] == edges[start].Pixels[^1];
            int lastEdge = start, firstEdge = start;
            // Extend forwards from the end, then backwards from the start.
            for (int side = 0; side < 2 && !closed; side++)
            {
                while (true)
                {
                    int node = px[^1];
                    if (!incident.TryGetValue(node, out var around)) break;
                    var inDir = Dir(px, true);
                    int best = -1; double bestCos = Math.Cos(70 * Math.PI / 180);
                    bool bestReversed = false;
                    foreach (int o in around)
                    {
                        if (used[o]) continue;
                        var op = edges[o].Pixels;
                        bool reversed = op[0] != node;
                        if (reversed && op[^1] != node) continue;
                        var oriented = reversed ? Enumerable.Reverse(op).ToList() : op;
                        var outDir = Dir(oriented, false);
                        double cos = -(inDir.X * outDir.X + inDir.Y * outDir.Y);   // outDir points back towards the node
                        if (cos > bestCos) { bestCos = cos; best = o; bestReversed = reversed; }
                    }
                    if (best < 0) break;
                    used[best] = true;
                    var next = bestReversed ? Enumerable.Reverse(edges[best].Pixels).ToList() : edges[best].Pixels;
                    px.AddRange(next.Skip(1));
                    if (side == 0) lastEdge = best; else firstEdge = best;
                    if (px[^1] == px[0]) { closed = true; break; }
                }
                px.Reverse();
            }
            // Reversed twice, the line runs as the first piece did: px[0] ends firstEdge, px[^1] ends lastEdge.
            bool startFree = !closed && Others(px[0], firstEdge) == 0;
            bool endFree = !closed && Others(px[^1], lastEdge) == 0;
            chains.Add(new PathPixels(px, startFree, endFree));
        }
        return chains;
    }

    /// <summary>
    /// Otsu's split of line darkness (weighted by length), taken only when it is clean: the groups well apart
    /// (between-group variance at least 60 % of the total, means 50 apart), the faint group really faint and
    /// the smaller. Otherwise null: one weight, keep everything.
    /// </summary>
    static double? FaintThreshold(List<(double Dark, double Weight)> lines)
    {
        lines = lines.Where(l => l.Weight >= 8).ToList();
        double total = lines.Sum(l => l.Weight);
        if (lines.Count < 4 || total <= 0) return null;
        double mean = lines.Sum(l => l.Dark * l.Weight) / total;
        double variance = lines.Sum(l => l.Weight * (l.Dark - mean) * (l.Dark - mean)) / total;
        if (variance <= 0) return null;
        (double Between, double T, double Ma, double Mb, double Share) best = default;
        for (int t = 40; t < 220; t += 2)
        {
            double wa = 0, sa = 0, wb = 0, sb = 0;
            foreach (var (d, wt) in lines)
                if (d < t) { wa += wt; sa += d * wt; } else { wb += wt; sb += d * wt; }
            if (wa <= 0 || wb <= 0) continue;
            double ma = sa / wa, mb = sb / wb;
            double between = wa * wb * (ma - mb) * (ma - mb) / (total * total);
            if (between > best.Between) best = (between, t, ma, mb, wa / total);
        }
        bool clean = best.Between / variance >= 0.6 && best.Mb - best.Ma >= 50 && best.Ma <= 95 && best.Share <= 0.6;
        return clean ? best.T : null;
    }

    // ───────────────────────────── mask ─────────────────────────────

    static readonly (int Dx, int Dy)[] Around = [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

    /// <summary>Pixels above <see cref="Weak"/> in groups that reach <see cref="Strong"/> somewhere, and are not specks.</summary>
    static bool[] Hysteresis(float[] s, int w, int h)
    {
        var mask = new bool[w * h];
        var seen = new bool[w * h];
        var stack = new Stack<int>();
        var group = new List<int>();
        for (int start = 0; start < s.Length; start++)
        {
            if (seen[start] || s[start] <= Weak) continue;
            group.Clear();
            bool strong = false;
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                group.Add(i);
                if (s[i] > Strong) strong = true;
                int x = i % w, y = i / w;
                foreach (var (dx, dy) in Around)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int n = ny * w + nx;
                    if (!seen[n] && s[n] > Weak) { seen[n] = true; stack.Push(n); }
                }
            }
            if (strong && group.Count >= MinStroke) foreach (int i in group) mask[i] = true;
        }
        return mask;
    }

    /// <summary>Morphological closing with a 3 × 3 square: fills pinholes and one-pixel breaks.</summary>
    static void Close(bool[] m, int w, int h)
    {
        var grown = new bool[m.Length];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            if (!m[y * w + x]) continue;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < w && ny < h) grown[ny * w + nx] = true;
            }
        }
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            bool all = true;
            for (int dy = -1; dy <= 1 && all; dy++)
            for (int dx = -1; dx <= 1 && all; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || !grown[ny * w + nx]) all = false;
            }
            m[y * w + x] = all;
        }
    }

    /// <summary>Zhang–Suen thinning to one-pixel centre lines.</summary>
    static void Thin(bool[] m, int w, int h)
    {
        var candidates = new List<int>();
        for (int y = 1; y < h - 1; y++)
        for (int x = 1; x < w - 1; x++)
            if (m[y * w + x]) candidates.Add(y * w + x);
        for (int y = 0; y < h; y++) { m[y * w] = false; m[y * w + w - 1] = false; }
        for (int x = 0; x < w; x++) { m[x] = false; m[(h - 1) * w + x] = false; }

        var remove = new List<int>();
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int pass = 0; pass < 2; pass++)
            {
                remove.Clear();
                foreach (int i in candidates)
                {
                    if (!m[i]) continue;
                    // p2..p9 clockwise from north.
                    bool p2 = m[i - w], p3 = m[i - w + 1], p4 = m[i + 1], p5 = m[i + w + 1],
                         p6 = m[i + w], p7 = m[i + w - 1], p8 = m[i - 1], p9 = m[i - w - 1];
                    int b = (p2 ? 1 : 0) + (p3 ? 1 : 0) + (p4 ? 1 : 0) + (p5 ? 1 : 0) + (p6 ? 1 : 0) + (p7 ? 1 : 0) + (p8 ? 1 : 0) + (p9 ? 1 : 0);
                    if (b < 2 || b > 6) continue;
                    int a = (!p2 && p3 ? 1 : 0) + (!p3 && p4 ? 1 : 0) + (!p4 && p5 ? 1 : 0) + (!p5 && p6 ? 1 : 0)
                          + (!p6 && p7 ? 1 : 0) + (!p7 && p8 ? 1 : 0) + (!p8 && p9 ? 1 : 0) + (!p9 && p2 ? 1 : 0);
                    if (a != 1) continue;
                    if (pass == 0 ? (p2 && p4 && p6) || (p4 && p6 && p8) : (p2 && p4 && p8) || (p2 && p6 && p8)) continue;
                    remove.Add(i);
                }
                foreach (int i in remove) m[i] = false;
                if (remove.Count > 0) changed = true;
            }
            candidates.RemoveAll(i => !m[i]);
        }
    }

    // ───────────────────────────── tracing ─────────────────────────────

    /// <summary>A traced centre line: its pixels in order, and whether each end is free (not at a junction).</summary>
    sealed class PathPixels(List<int> pixels, bool startFree, bool endFree, bool startNode = false, bool endNode = false)
    {
        public List<int> Pixels { get; } = pixels;
        public bool StartFree { get; set; } = startFree;
        public bool EndFree { get; set; } = endFree;
        /// <summary>Whether the ends are junction or end pixels (not mid-line), where pieces meet.</summary>
        public bool StartNode { get; } = startNode;
        public bool EndNode { get; } = endNode;

        public IEnumerable<int> Nodes()
        {
            if (StartNode) yield return Pixels[0];
            if (EndNode && (!StartNode || Pixels[^1] != Pixels[0])) yield return Pixels[^1];
        }
    }

    static List<PathPixels> TracePaths(bool[] m, int w, int h)
    {
        int Degree(int i)
        {
            int x = i % w, y = i / w, n = 0;
            foreach (var (dx, dy) in Around)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < w && ny < h && m[ny * w + nx]) n++;
            }
            return n;
        }
        IEnumerable<int> Next(int i)
        {
            int x = i % w, y = i / w;
            foreach (var (dx, dy) in Around)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < w && ny < h && m[ny * w + nx]) yield return ny * w + nx;
            }
        }

        var deg = new Dictionary<int, int>();
        for (int i = 0; i < m.Length; i++) if (m[i]) deg[i] = Degree(i);
        var used = new HashSet<long>();   // walked steps a → b
        var visited = new HashSet<int>();
        var paths = new List<PathPixels>();

        List<int> Walk(int from, int first)
        {
            var path = new List<int> { from, first };
            int prev = from, cur = first;
            used.Add((long)from << 32 | (uint)first);
            while (deg[cur] == 2)
            {
                int next = -1;
                foreach (int q in Next(cur))
                    if (q != prev && !used.Contains((long)cur << 32 | (uint)q) && (path.Count < 3 || q != path[^3])) { next = q; break; }
                if (next < 0) break;
                used.Add((long)cur << 32 | (uint)next);
                prev = cur; cur = next;
                path.Add(cur);
                if (cur == from) break;
            }
            used.Add((long)cur << 32 | (uint)prev);
            return path;
        }

        foreach (var (node, d) in deg)
        {
            if (d == 2) continue;
            if (d == 0) continue;   // single pixels: too small to be a line
            foreach (int f in Next(node))
            {
                if (used.Contains((long)node << 32 | (uint)f)) continue;
                var p = Walk(node, f);
                foreach (int i in p) visited.Add(i);
                paths.Add(new PathPixels(p, deg[p[0]] == 1, deg[p[^1]] == 1, deg[p[0]] != 2, deg[p[^1]] != 2));
            }
        }
        // Closed loops have no ends.
        foreach (var (start, d) in deg)
        {
            if (d != 2 || visited.Contains(start)) continue;
            int first = Next(start).First();
            var p = Walk(start, first);
            foreach (int i in p) visited.Add(i);
            paths.Add(new PathPixels(p, false, false));
        }
        return paths;
    }

    // ───────────────────────────── bridging and smoothing ─────────────────────────────

    /// <summary>
    /// Joins free line ends that point at each other across a short gap (the line broke where it crossed printed
    /// ink): within <see cref="BridgePixels"/>, each end's own direction within 60° of the gap.
    /// </summary>
    static List<List<int>> Bridge(List<PathPixels> paths, int w)
    {
        var lines = paths.Select(p => (Pixels: new List<int>(p.Pixels), p.StartFree, p.EndFree, Alive: true)).ToList();
        (double X, double Y) At(int i) => (i % w, i / w);
        // Outward direction at an end, from the last few pixels.
        (double X, double Y) Out(List<int> px, bool atEnd)
        {
            int n = Math.Min(8, px.Count - 1);
            var a = At(atEnd ? px[^1] : px[0]);
            var b = At(atEnd ? px[^(n + 1)] : px[n]);
            double dx = a.X - b.X, dy = a.Y - b.Y, len = Math.Sqrt(dx * dx + dy * dy);
            return len < 1e-6 ? (0, 0) : (dx / len, dy / len);
        }

        bool joined = true;
        while (joined)
        {
            joined = false;
            var ends = new List<(int Line, bool AtEnd, (double X, double Y) P, (double X, double Y) Dir)>();
            for (int k = 0; k < lines.Count; k++)
            {
                var l = lines[k];
                if (!l.Alive || l.Pixels.Count < 3) continue;
                if (l.StartFree) ends.Add((k, false, At(l.Pixels[0]), Out(l.Pixels, false)));
                if (l.EndFree) ends.Add((k, true, At(l.Pixels[^1]), Out(l.Pixels, true)));
            }
            var pairs = new List<(double D, int A, int B)>();
            for (int a = 0; a < ends.Count; a++)
            for (int b = a + 1; b < ends.Count; b++)
            {
                var ea = ends[a]; var eb = ends[b];
                if (ea.Line == eb.Line && lines[ea.Line].Pixels.Count < 30) continue;
                double dx = eb.P.X - ea.P.X, dy = eb.P.Y - ea.P.Y, d = Math.Sqrt(dx * dx + dy * dy);
                if (d > BridgePixels || d < 1e-6) continue;
                double ca = (ea.Dir.X * dx + ea.Dir.Y * dy) / d, cb = -(eb.Dir.X * dx + eb.Dir.Y * dy) / d;
                if (ca < 0.5 || cb < 0.5) continue;
                pairs.Add((d, a, b));
            }
            var taken = new HashSet<int>();
            foreach (var (_, a, b) in pairs.OrderBy(p => p.D))
            {
                if (taken.Contains(a) || taken.Contains(b)) continue;
                var ea = ends[a]; var eb = ends[b];
                if (!lines[ea.Line].Alive || !lines[eb.Line].Alive) continue;
                // Never merge a line already touched in this round; the next round picks it up.
                if (taken.Any(t => ends[t].Line == ea.Line || ends[t].Line == eb.Line)) continue;
                taken.Add(a); taken.Add(b);
                var la = lines[ea.Line];
                if (ea.Line == eb.Line)
                {
                    // Close the loop.
                    la.Pixels.Add(la.Pixels[0]);
                    lines[ea.Line] = (la.Pixels, false, false, true);
                    joined = true;
                    continue;
                }
                var lb = lines[eb.Line];
                // Orient a to end at the joint and b to start there.
                var pa = ea.AtEnd ? la.Pixels : Enumerable.Reverse(la.Pixels).ToList();
                bool aFar = ea.AtEnd ? la.StartFree : la.EndFree;
                var pb = eb.AtEnd ? Enumerable.Reverse(lb.Pixels).ToList() : lb.Pixels;
                bool bFar = eb.AtEnd ? lb.StartFree : lb.EndFree;
                pa.AddRange(pb);
                lines[ea.Line] = (pa, aFar, bFar, true);
                lines[eb.Line] = (lb.Pixels, false, false, false);
                joined = true;
            }
        }
        return lines.Where(l => l.Alive).Select(l => l.Pixels).ToList();
    }

    /// <summary>Douglas–Peucker to 0.7 px, then two rounds of Chaikin corner cutting (ends kept).</summary>
    static List<(float X, float Y)> Smooth(List<int> pixels, int w)
    {
        var pts = pixels.Select(i => ((double)(i % w), (double)(i / w))).ToList();
        bool closed = pixels.Count > 3 && pixels[0] == pixels[^1];
        var keep = new bool[pts.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, pts.Count - 1));
        while (stack.Count > 0)
        {
            var (a, z) = stack.Pop();
            double dx = pts[z].Item1 - pts[a].Item1, dy = pts[z].Item2 - pts[a].Item2, len = Math.Sqrt(dx * dx + dy * dy);
            int worst = -1; double worstD = 0.7;
            for (int i = a + 1; i < z; i++)
            {
                double ex = pts[i].Item1 - pts[a].Item1, ey = pts[i].Item2 - pts[a].Item2;
                double d = len < 1e-9 ? Math.Sqrt(ex * ex + ey * ey) : Math.Abs(ex * dy - ey * dx) / len;
                if (d > worstD) { worstD = d; worst = i; }
            }
            if (worst < 0) continue;
            keep[worst] = true;
            stack.Push((a, worst));
            stack.Push((worst, z));
        }
        var simple = pts.Where((_, i) => keep[i]).ToList();
        for (int round = 0; round < 2 && simple.Count > 2; round++)
        {
            var next = new List<(double, double)>();
            if (!closed) next.Add(simple[0]);
            for (int i = 0; i + 1 < simple.Count; i++)
            {
                var (ax, ay) = simple[i]; var (bx, by) = simple[i + 1];
                next.Add((0.75 * ax + 0.25 * bx, 0.75 * ay + 0.25 * by));
                next.Add((0.25 * ax + 0.75 * bx, 0.25 * ay + 0.75 * by));
            }
            if (closed) next.Add(next[0]);
            else next.Add(simple[^1]);
            simple = next;
        }
        return simple.Select(p => ((float)p.Item1, (float)p.Item2)).ToList();
    }
}
