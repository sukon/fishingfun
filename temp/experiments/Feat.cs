using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

// Feature extraction + evaluation harness for bobber detection.
// Mode "dump"  -> writes features+labels CSV for offline training
// Mode "eval"  -> loads embedded logistic-regression weights and scores
unsafe class Feat
{
    static Random rng = new Random(1234);

    // ---------- scene synthesis (frames of a bobbing bobber) ----------
    static void PaintBackground(Graphics g, int w, int h, int evening)
    {
        // Realistic WoW water: deep blue in daylight, shifting red/orange at dusk.
        // The bobber's red feather contrasts strongly against deep blue water,
        // and poorly against a red dusk sky - which is exactly the real failure mode.
        for (int y = 0; y < h; y++)
        {
            float t = (float)y / h;
            int r, gg, b;
            if (evening == 0)
            {
                r = (int)(20 + 60 * t); gg = (int)(45 + 55 * t); b = (int)(90 + 40 * t);
            }
            else if (evening == 1)
            {
                r = (int)(60 + 110 * t); gg = (int)(55 + 45 * t); b = (int)(95 - 30 * t);
            }
            else if (evening == 2)
            {
                // Dusk: sky red at the top, water still blue-ish in the lower half.
                r = (int)(150 + 100 * (1 - t)); gg = (int)(70 + 25 * (1 - t)); b = (int)(60 + 60 * t);
            }
            else
            {
                r = (int)(60 + 40 * t); gg = (int)(25 + 15 * t); b = (int)(45 - 10 * t);
            }
            using (var br = new SolidBrush(Color.FromArgb(Math.Min(r, 255), Math.Min(gg, 255), Math.Max(b, 0))))
                g.FillRectangle(br, 0, y, w, 1);
        }
    }

    static void PaintRedTerrain(Graphics g, int w, int h, int severity)
    {
        int blobs = severity * 6;
        for (int i = 0; i < blobs; i++)
        {
            int bx = rng.Next(0, w), by = rng.Next(0, h);
            int bw = 30 + rng.Next(0, 90), bh = 20 + rng.Next(0, 60);
            int tint = rng.Next(3);
            Color col = tint == 0 ? Color.FromArgb(150, 40, 40)
                      : tint == 1 ? Color.FromArgb(190, 70, 55)
                                  : Color.FromArgb(120, 35, 60);
            using (var br = new SolidBrush(col)) g.FillEllipse(br, bx, by, bw, bh);
        }
    }

    static void PaintBobber(Graphics g, int cx, int cy, bool blue, int dy)
    {
        for (int y = 0; y < 14; y++)
            for (int x = 0; x < 8; x++)
            {
                double dx = (x - 3.5) / 4.0, ddy = (y - 6.5 + dy) / 7.0;
                if (dx * dx + ddy * ddy > 1.0) continue;
                int shade = 200 + rng.Next(0, 55);
                g.FillRectangle(new SolidBrush(blue ? Color.FromArgb(40, shade, 190) : Color.FromArgb(shade, 30, 30)), cx + x, cy + y, 1, 1);
            }
    }

    // ---------- candidate extraction via LOCAL CONTRAST ----------
    // Absolute colour thresholds break down at dusk; local contrast against a
    // coarse background estimate does not, because the bobber is a small object
    // that differs from whatever is behind it.
    class Cand { public int X, Y; public double R, G, B, Contrast, Sat, Hue; public double BgR, BgG, BgB; }

    static double contrastThreshold = 40;

    static List<Cand> Extract(Bitmap bmp, int prevY, out bool[,] contrastMask)
    {
        int w = bmp.Width, h = bmp.Height;
        var pix = new byte[w * h * 3];
        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        byte* p0 = (byte*)data.Scan0;
        for (int y = 0; y < h; y++)
        {
            byte* row = p0 + y * data.Stride;
            for (int x = 0; x < w; x++)
            {
                pix[(y * w + x) * 3 + 0] = row[x * 3 + 2]; // R
                pix[(y * w + x) * 3 + 1] = row[x * 3 + 1]; // G
                pix[(y * w + x) * 3 + 2] = row[x * 3];     // B
            }
        }
        bmp.UnlockBits(data);

        // coarse background estimate: 24x24 block mean, per channel
        int BS = 24;
        var blockMean = new double[(w + BS - 1) / BS, (h + BS - 1) / BS];
        var blockMeanR = new double[(w + BS - 1) / BS, (h + BS - 1) / BS];
        var blockMeanG = new double[(w + BS - 1) / BS, (h + BS - 1) / BS];
        var blockMeanB = new double[(w + BS - 1) / BS, (h + BS - 1) / BS];
        for (int by = 0; by < blockMean.GetLength(1); by++)
            for (int bx = 0; bx < blockMean.GetLength(0); bx++)
            {
                double sr = 0, sg = 0, sb = 0; int n = 0;
                for (int y = by * BS; y < Math.Min((by + 1) * BS, h); y++)
                    for (int x = bx * BS; x < Math.Min((bx + 1) * BS, w); x++)
                    {
                        sr += pix[(y * w + x) * 3]; sg += pix[(y * w + x) * 3 + 1]; sb += pix[(y * w + x) * 3 + 2]; n++;
                    }
                blockMeanR[bx, by] = n > 0 ? sr / n : 0;
                blockMeanG[bx, by] = n > 0 ? sg / n : 0;
                blockMeanB[bx, by] = n > 0 ? sb / n : 0;
                blockMean[bx, by] = n > 0 ? (sr + sg + sb) / (3.0 * n) : 0;
            }

        var cands = new List<Cand>();
        contrastMask = new bool[w, h];

        // Adaptive threshold. A fixed cut-off is wrong in both directions: too high and a
        // low-contrast bobber (overcast, blue feather on pale water) is never even a
        // candidate; too low and every terrain edge floods the candidate list.
        // Compute the contrast distribution over the whole frame and keep only pixels
        // that are genuine outliers against their local background.
        var contrastHist = new int[512];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte r = pix[(y * w + x) * 3], g = pix[(y * w + x) * 3 + 1], b = pix[(y * w + x) * 3 + 2];
                double bm = blockMean[x / BS, y / BS];
                int c = (int)Math.Min(511, Math.Abs(r + g + b - 3 * bm));
                contrastHist[c]++;
            }

        // find the 99.5th percentile of contrast as an upper noise bound
        long total = 0; for (int i = 0; i < 512; i++) total += contrastHist[i];
        long cum = 0; int p995 = 0;
        for (int i = 0; i < 512; i++) { cum += contrastHist[i]; if (cum >= total * 995 / 1000) { p995 = i; break; } }
        // keep a floor so a perfectly flat frame still yields nothing, and a ceiling
        // so a noisy frame does not admit everything
        double adaptive = Math.Max(18, Math.Min(p995 * 0.35, 90));
        contrastThreshold = adaptive;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte r = pix[(y * w + x) * 3], g = pix[(y * w + x) * 3 + 1], b = pix[(y * w + x) * 3 + 2];
                double bm = blockMean[x / BS, y / BS];
                double bmR = blockMeanR[x / BS, y / BS], bmG = blockMeanG[x / BS, y / BS], bmB = blockMeanB[x / BS, y / BS];
                double contrast = Math.Abs(r + g + b - 3 * bm);
                // Adaptive threshold: a fixed cut-off either misses low-contrast bobbers
                // (overcast) or floods the candidate list on noisy terrain. Scale the
                // threshold with the local contrast spread of the block instead.
                if (contrast < contrastThreshold) continue;
                contrastMask[x, y] = true;
                double max = Math.Max(r, Math.Max(g, b));
                double min = Math.Min(r, Math.Min(g, b));
                double sat = max <= 0 ? 0 : (max - min) / max;
                double hue = 0;
                if (max != min)
                {
                    if (max == r) hue = 60 * (((g - b) / (max - min)) % 6);
                    else if (max == g) hue = 60 * ((b - r) / (max - min) + 2);
                    else hue = 60 * ((r - g) / (max - min) + 4);
                    if (hue < 0) hue += 360;
                }
                cands.Add(new Cand { X = x, Y = y, R = r, G = g, B = b, Contrast = contrast, Sat = sat, Hue = hue,
                                    BgR = bmR, BgG = bmG, BgB = bmB });
            }
        return cands;
    }

    // ---------- blob grouping of candidates ----------
    class Blob { public List<Cand> P = new List<Cand>(); public int MinX, MinY, MaxX, MaxY; }

    static List<Blob> Group(List<Cand> cands, int w, int h)
    {
        var map = new Dictionary<int, int>();
        for (int i = 0; i < cands.Count; i++) map[cands[i].Y * w + cands[i].X] = i;
        var seen = new bool[cands.Count];
        var blobs = new List<Blob>();
        var stack = new Stack<int>();
        for (int i = 0; i < cands.Count; i++)
        {
            if (seen[i]) continue;
            var blob = new Blob { MinX = int.MaxValue, MinY = int.MaxValue, MaxX = int.MinValue, MaxY = int.MinValue };
            stack.Clear(); stack.Push(i); seen[i] = true;
            while (stack.Count > 0)
            {
                int ci = stack.Pop();
                Cand c = cands[ci];
                blob.P.Add(c);
                if (c.X < blob.MinX) blob.MinX = c.X; if (c.X > blob.MaxX) blob.MaxX = c.X;
                if (c.Y < blob.MinY) blob.MinY = c.Y; if (c.Y > blob.MaxY) blob.MaxY = c.Y;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int nx = c.X + dx, ny = c.Y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int ni;
                        if (!map.TryGetValue(ny * w + nx, out ni) || seen[ni]) continue;
                        seen[ni] = true; stack.Push(ni);
                    }
            }
            blobs.Add(blob);
        }
        return blobs;
    }

    // ---------- feature vector ----------
    // Key design: features are RELATIVE to the local background, not absolute RGB.
    // A red feather on deep blue water and a blue feather on orange lava both look
    // like "a compact blob that differs from its surroundings", so a model keyed on
    // relative properties generalises across lighting and feather colour.
    static double[] Featurize(Blob b, double bgR, double bgG, double bgB)
    {
        int area = b.P.Count;
        int w = b.MaxX - b.MinX + 1, h = b.MaxY - b.MinY + 1;
        double mr = 0, mg = 0, mb = 0, mcon = 0, msat = 0;
        foreach (var c in b.P) { mr += c.R; mg += c.G; mb += c.B; mcon += c.Contrast; msat += c.Sat; }
        mr /= area; mg /= area; mb /= area; mcon /= area; msat /= area;

        // relative colour vs local background
        double dr = mr - bgR, dg = mg - bgG, db = mb - bgB;
        double dmag = Math.Sqrt(dr * dr + dg * dg + db * db) / 255.0;

        // which channel dominates the difference (red-dominant or blue-dominant)
        double absmax = Math.Max(Math.Abs(dr), Math.Max(Math.Abs(dg), Math.Abs(db)));
        double domR = absmax > 0 ? Math.Abs(dr) / absmax : 0;
        double domB = absmax > 0 ? Math.Abs(db) / absmax : 0;

        double fill = (double)area / (w * h);
        double aspect = (double)w / h;
        double compact = area / (4.0 * Math.PI * (w * h) / 4.0);   // ~1 for a disc
        return new double[] {
            dmag, domR, domB,
            mcon / 255.0, msat,
            area / 100.0, w / 20.0, h / 20.0, fill, aspect, compact,
            Math.Log(area + 1) / 5.0
        };
    }

    static int FEATN = 12;

    // ---------- scene with ground truth ----------
    class Scene
    {
        public string Name; public int Evening, Terrain; public bool Blue; public Point Truth; public Bitmap Bmp;
    }

    static Scene MakeScene(string name, int evening, int terrain, bool blue, int jitterX, int jitterY, int bobDy, bool withBobber = true)
    {
        int w = 960, h = 540;
        var truth = new Point(w / 2 + jitterX, h / 2 + jitterY + bobDy);
        var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp))
        {
            PaintBackground(g, w, h, evening);
            if (terrain > 0) PaintRedTerrain(g, w, h, terrain);
            if (withBobber) PaintBobber(g, truth.X, truth.Y, blue, 0);
        }
        return new Scene { Name = name, Evening = evening, Terrain = terrain, Blue = blue, Truth = truth, Bmp = bmp };
    }

    static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "eval";
        var scenes = new (string name, int eve, int terr, bool blue)[]
        {
            ("day-no-terrain",      0, 0, false),
            ("day-light-terrain",   0, 1, false),
            ("day-heavy-terrain",   0, 3, false),
            ("overcast-no-terrain", 1, 0, false),
            ("overcast-light-terrain",1,1,false),
            ("dusk-no-terrain",     2, 0, false),
            ("dusk-light-terrain",  2, 1, false),
            ("dusk-heavy-terrain",  2, 3, false),
            ("night-heavy-terrain", 3, 3, false),
            ("lava-blue-feather",   2, 3, true),
            ("NEG-day-no-bobber",   0, 0, false),
            ("NEG-dusk-no-bobber",  2, 0, false),
            ("NEG-dusk-heavy-terrain",2,3,false),
            ("NEG-night-terrain",  3, 3, false),
        };

        if (mode == "dump")
        {
            var sb = new StringBuilder();
            for (int si = 0; si < scenes.Length; si++)
            {
                var s = scenes[si];
                for (int trial = 0; trial < 60; trial++)
                {
                    int jx = rng.Next(-25, 26), jy = rng.Next(-25, 26);
                    bool neg = s.name.StartsWith("NEG-");
                    var sc = MakeScene(s.name, s.eve, s.terr, s.blue, jx, jy, 0, !neg);
                    bool[,] mask;
                    var cands = Extract(sc.Bmp, 0, out mask);
                    var blobs = Group(cands, sc.Bmp.Width, sc.Bmp.Height);
                    foreach (var b in blobs)
                    {
                        if (b.P.Count < 8) continue;
                        var f = Featurize(b, b.P[0].BgR, b.P[0].BgG, b.P[0].BgB);
                        double cx = b.P.Average(c => c.X), cy = b.P.Average(c => c.Y);
                        int d = Math.Max((int)Math.Abs(cx - sc.Truth.X), (int)Math.Abs(cy - sc.Truth.Y));
                        int label = (!neg && d <= 15 && b.P.Count >= 25) ? 1 : 0;
                        sb.Append(string.Join(",", Array.ConvertAll(f, v => v.ToString("F5", CultureInfo.InvariantCulture))));
                        sb.Append(",").Append(label).Append(",").Append(si).Append("\n");
                    }
                    sc.Bmp.Dispose();
                }
            }
            File.WriteAllText("train.csv", sb.ToString());
            Console.WriteLine("wrote train.csv, rows=" + sb.ToString().Split('\n').Length);
            return;
        }

        // eval mode: load weights from weights.txt
        double[] wts = null, mu = null, sdv = null; double bias = 0;
        if (File.Exists("weights.txt") && File.Exists("scaler.txt"))
        {
            var parts = File.ReadAllText("weights.txt").Trim().Split(',');
            wts = new double[parts.Length - 1];
            for (int i = 0; i < wts.Length; i++) wts[i] = double.Parse(parts[i], CultureInfo.InvariantCulture);
            bias = double.Parse(parts[parts.Length - 1], CultureInfo.InvariantCulture);

            var sc = File.ReadAllText("scaler.txt").Trim().Split('|');
            var mp = sc[0].Split(','); var sp = sc[1].Split(',');
            mu = new double[mp.Length]; sdv = new double[sp.Length];
            for (int i = 0; i < mu.Length; i++) mu[i] = double.Parse(mp[i], CultureInfo.InvariantCulture);
            for (int i = 0; i < sdv.Length; i++) sdv[i] = double.Parse(sp[i], CultureInfo.InvariantCulture);
        }

        Console.WriteLine("scene                        proposed(local-contrast + learned ranker)");
        Console.WriteLine(new string('-', 72));
        int totHit = 0, totMiss = 0, totFP = 0, totScene = 0;
        foreach (var s in scenes)
        {
            int hit = 0, miss = 0, fp = 0, total = 0;
            for (int trial = 0; trial < 20; trial++)
            {
                int jx = rng.Next(-25, 26), jy = rng.Next(-25, 26);
                bool neg = s.name.StartsWith("NEG-");
                var sc = MakeScene(s.name, s.eve, s.terr, s.blue, jx, jy, 0, !neg);
                bool[,] mask;
                var cands = Extract(sc.Bmp, 0, out mask);
                var blobs = Group(cands, sc.Bmp.Width, sc.Bmp.Height);
                total++;

                Blob bestB = null; double bestP = -1;
                foreach (var b in blobs)
                {
                    if (b.P.Count < 8) continue;
                    var f = Featurize(b, b.P[0].BgR, b.P[0].BgG, b.P[0].BgB);
                    double p;
                    if (wts == null) p = b.P.Count; // fallback heuristic
                    else
                    {
                        double z = bias;
                        for (int i = 0; i < f.Length; i++) z += wts[i] * ((f[i] - mu[i]) / sdv[i]);
                        p = 1.0 / (1.0 + Math.Exp(-z));
                    }
                    if (p > bestP) { bestP = p; bestB = b; }
                }
                if (neg)
                {
                    // negative scene: any confident detection is a false positive
                    if (bestB != null && bestP > 0.5) fp++;
                    else hit++;   // correctly reported nothing
                }
                else if (bestB != null && bestP > 0.5)
                {
                    double cx = bestB.P.Average(c => c.X), cy = bestB.P.Average(c => c.Y);
                    int d = Math.Max((int)Math.Abs(cx - sc.Truth.X), (int)Math.Abs(cy - sc.Truth.Y));
                    if (d <= 15) hit++; else fp++;
                }
                else miss++;
                sc.Bmp.Dispose();
            }
            totHit += hit; totMiss += miss; totFP += fp; totScene += total;
            Console.WriteLine($"{s.name,-30} hit {hit}/{total}, miss {miss}, false {fp}");
        }
        Console.WriteLine();
        Console.WriteLine($"TOTAL hit {totHit}/{totScene}, miss {totMiss}, false-positive {totFP}");
    }
}
