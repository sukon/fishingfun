using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Diagnostic tool for the bobber detector.
//
//   DiagBobber.exe [outDir] [seconds] [intervalMs] [imageFile]
//
// OutDir      where the images and the report are written (default ".")
// seconds     how long to keep capturing (default 20)
// intervalMs  time between captures (default 500)
// imageFile   analyse this image instead of capturing the screen. Use this when the
//             game runs on another machine and only a screenshot is available. The
//             image is treated as the game window.
//
// Run it on the machine where the game is actually running, with the float in the
// water and the character standing still.

class Program
{
    static void Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : ".";
        double seconds = args.Length > 1 ? double.Parse(args[1]) : 20;
        int intervalMs = args.Length > 2 ? int.Parse(args[2]) : 500;
        string? imageFile = args.Length > 3 ? args[3] : null;

        Directory.CreateDirectory(outDir);

        var asm = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "FishingFunBot.dll"));
        var cls = asm.GetType("FishingFun.BobberDetector")!;
        var ctype = asm.GetType("FishingFun.PixelClassifier")!;

        // Off line mode: analyse a screenshot instead of capturing the screen.
        if (imageFile != null)
        {
            Console.WriteLine($"Analyzing {imageFile} instead of capturing the screen.");
            using (var shot = new Bitmap(imageFile))
            {
                var bounds = new Rectangle(0, 0, shot.Width, shot.Height);
                string shotReport = Analyse(cls, ctype, bounds, shot, outDir, "screenshot", out string shotDetail);
                Console.WriteLine(shotReport);
                Console.WriteLine(shotDetail);
                File.WriteAllText(Path.Combine(outDir, "diag-report.txt"), shotReport + Environment.NewLine + shotDetail);
                Console.WriteLine();
                Console.WriteLine($"Report written to {Path.Combine(outDir, "diag-report.txt")}");
            }
            return;
        }

        Console.WriteLine("=== WoW processes ===");
        var procs = Process.GetProcesses();
        Process? wow = null;
        foreach (var p in procs)
        {
            var name = p.ProcessName;
            if (name.Equals("Wow", StringComparison.OrdinalIgnoreCase)
                || name.Equals("WowClassic", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Wow-64", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  FOUND {name} pid={p.Id} title='{SafeTitle(p)}'");
                if (wow == null) wow = p;
            }
        }
        if (wow == null)
        {
            Console.WriteLine("  No Wow process with a name the bot recognises.");
            Console.WriteLine("  The bot looks for: Wow, WowClassic, Wow-64");
            Console.WriteLine("  Without it the bot captures an empty image and can never find");
            Console.WriteLine("  the float. Run this tool where the game runs, or pass a");
            Console.WriteLine("  screenshot as the fourth argument.");
            Console.WriteLine();
            Console.WriteLine("  Windows on this machine:");
            foreach (var p in procs)
            {
                string t;
                try { t = p.MainWindowTitle; } catch { continue; }
                if (!string.IsNullOrEmpty(t)) Console.WriteLine($"    {p.ProcessName,-22} '{t}'");
            }
            return;
        }

        var handle = wow.MainWindowHandle;
        var report = new List<string>();
        var lines = new List<string>();

        report.Add($"Diagnostic run at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.Add($"Game window: '{SafeTitle(wow)}' handle={handle}");
        report.Add($"Capture every {intervalMs}ms for {seconds:0}s");
        report.Add("");

        string? firstFound = null;
        string? bestFrame = null;
        int bestMatches = -1;
        int foundCount = 0;
        int frameNo = 0;

        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalSeconds < seconds)
        {
            frameNo++;
            var bounds = GetWindowRectSafe(handle);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                lines.Add($"frame {frameNo,3}  the game window has no size");
                Thread.Sleep(intervalMs);
                continue;
            }

            using (var shot = new Bitmap(bounds.Width, bounds.Height))
            {
                using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

                string verdict = Analyse(cls, ctype, bounds, shot, outDir, "frame" + frameNo, out string detail);

                bool found = verdict.Contains("FOUND");
                int matchCount = ParseMatchCount(detail);
                lines.Add($"frame {frameNo,3}  {verdict}  {detail}");

                if (found)
                {
                    foundCount++;
                    if (firstFound == null) firstFound = Path.Combine(outDir, "capture-found.png");
                }
                if (matchCount > bestMatches)
                {
                    bestMatches = matchCount;
                    bestFrame = Path.Combine(outDir, "capture-most-matches.png");
                }
            }
            Thread.Sleep(intervalMs);
        }

        report.Add($"Captured {frameNo} frames.");
        report.Add($"Frames where the float was found: {foundCount}");
        report.Add("");
        report.Add("Per frame:");
        report.AddRange(lines);
        report.Add("");
        if (firstFound != null) report.Add($"A frame where the float was found: {firstFound}");
        if (bestFrame != null) report.Add($"The frame with the most matching pixels: {bestFrame}");
        report.Add("");
        report.AddRange(LimitsText(cls));

        var text = string.Join("\n", report);
        File.WriteAllText(Path.Combine(outDir, "diag-report.txt"), text);
        Console.WriteLine(text);
        Console.WriteLine();
        Console.WriteLine($"Report written to {Path.Combine(outDir, "diag-report.txt")}");
    }

    /// <summary>
    /// Runs the detector over one frame and describes what happened.
    /// </summary>
    static string Analyse(Type cls, Type ctype, Rectangle bounds, Bitmap shot, string outDir, string label, out string detail)
    {
        var det = Activator.CreateInstance(cls)!;
        cls.GetProperty("Classifier")!.SetValue(det, Activator.CreateInstance(ctype)!);

        // Exactly what WowScreen.GetBitmap() does: a bitmap of half the window size,
        // filled from the central quarter of the window at 1:1.
        var capture = new Bitmap(bounds.Width / 2, bounds.Height / 2);
        using (var g = Graphics.FromImage(capture))
            g.DrawImage(shot, new Rectangle(0, 0, bounds.Width / 2, bounds.Height / 2),
                        new Rectangle(bounds.Width / 4, bounds.Height / 4, bounds.Width / 2, bounds.Height / 2), GraphicsUnit.Pixel);

        var res = cls.GetMethod("Find")!.Invoke(det, new object[] { capture, null })!;
        var resT = res.GetType();
        var matches = (List<Point>)resT.GetProperty("Matches")!.GetValue(res)!;
        bool fallback = (bool)resT.GetProperty("UsedFallback")!.GetValue(res)!;
        var candidate = resT.GetProperty("Candidate")!.GetValue(res);

        var groupM = cls.GetMethod("GroupIntoCandidates", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var cands = (System.Collections.IEnumerable)groupM.Invoke(det, new object[] { matches, capture.Width, capture.Height })!;
        var list = new List<(int area, int w, int h, double fill, double sol, int cx, int cy)>();
        foreach (var c in cands)
        {
            var t = c.GetType();
            list.Add(((int)t.GetProperty("Area")!.GetValue(c)!, (int)t.GetProperty("Width")!.GetValue(c)!, (int)t.GetProperty("Height")!.GetValue(c)!,
                (double)t.GetProperty("FillRatio")!.GetValue(c)!, (double)t.GetProperty("Solidity")!.GetValue(c)!,
                ((Point)t.GetProperty("Centre")!.GetValue(c)!).X, ((Point)t.GetProperty("Centre")!.GetValue(c)!).Y));
        }
        list.Sort((a, b) => b.area.CompareTo(a.area));

        string verdict;
        detail = "";

        if (candidate != null)
        {
            var t = candidate.GetType();
            var ctr = (Point)t.GetProperty("Centre")!.GetValue(candidate)!;
            verdict = $"FOUND at ({ctr.X},{ctr.Y}) area={t.GetProperty("Area")!.GetValue(candidate)} " +
                      $"box={t.GetProperty("Width")!.GetValue(candidate)}x{t.GetProperty("Height")!.GetValue(candidate)}";
            SaveCapture(capture, matches, outDir, "capture-found.png");
        }
        else
        {
            verdict = "not found";
            if (list.Count > 0)
            {
                var c = list[0];
                var why = WhyRejected(cls, det, c, capture.Width, capture.Height);
                detail = "matches " + matches.Count + " fallback " + (fallback ? "yes" : "no") +
                         " | best candidate area=" + c.area + " box=" + c.w + "x" + c.h +
                         (why.Count > 0 ? " rejected: " + string.Join(", ", why) : " (accepted but not the best)") +
                         " | " + list.Count + " candidates";
            }
            else
            {
                detail = "matches " + matches.Count + " fallback " + (fallback ? "yes" : "no") + " | no pixels matched at all";
            }
        }

        detail = detail.TrimStart('|', ' ');
        SaveCapture(capture, matches, outDir, "capture-most-matches.png");

        // Read the size before disposing, a disposed bitmap cannot be measured.
        string sizeText = $"capture {capture.Width}x{capture.Height}";
        capture.Dispose();

        return $"{sizeText} {verdict}";
    }

    static List<string> WhyRejected(Type cls, object det, (int area, int w, int h, double fill, double sol, int cx, int cy) c, int fw, int fh)
    {
        int maxArea = (int)(fw * fh * (double)cls.GetProperty("MaxAreaFraction")!.GetValue(det)!);
        int minArea = (int)(fw * fh * (double)cls.GetProperty("MinAreaFraction")!.GetValue(det)!);
        int maxW = (int)(fw * (double)cls.GetProperty("MaxWidthFraction")!.GetValue(det)!);
        int maxH = (int)(fh * (double)cls.GetProperty("MaxHeightFraction")!.GetValue(det)!);
        double minFill = (double)cls.GetProperty("MinFillRatio")!.GetValue(det)!;
        double minSol = (double)cls.GetProperty("MinSolidity")!.GetValue(det)!;
        int maxCentre = (int)(fw * (double)cls.GetProperty("MaxCentreDistanceFraction")!.GetValue(det)!);
        int dist = Math.Max(Math.Abs(c.cx - fw / 2), Math.Abs(c.cy - fh / 2));

        var why = new List<string>();
        if (c.area < minArea) why.Add($"area {c.area} < {minArea}");
        if (c.area > maxArea) why.Add($"area {c.area} > {maxArea}");
        if (c.w > maxW) why.Add($"width {c.w} > {maxW}");
        if (c.h > maxH) why.Add($"height {c.h} > {maxH}");
        if (c.fill < minFill) why.Add($"fill {c.fill:F2} < {minFill}");
        if (c.sol < minSol) why.Add($"solidity {c.sol:F2} < {minSol}");
        if (dist > maxCentre) why.Add($"centre distance {dist} > {maxCentre}");
        return why;
    }

    static int ParseMatchCount(string detail)
    {
        var i = detail.IndexOf("matches ");
        if (i < 0) return 0;
        var rest = detail.Substring(i + 8);
        var j = 0;
        while (j < rest.Length && char.IsDigit(rest[j])) j++;
        return int.Parse(rest.Substring(0, j));
    }

    static List<string> LimitsText(Type cls)
    {
        var d = Activator.CreateInstance(cls)!;
        var l = new List<string> { "Limits in force:" };
        l.Add($"  area between {(double)cls.GetProperty("MinAreaFraction")!.GetValue(d)!} and {(double)cls.GetProperty("MaxAreaFraction")!.GetValue(d)!} of the frame");
        l.Add($"  width up to {(double)cls.GetProperty("MaxWidthFraction")!.GetValue(d)!} of the frame width, height up to {(double)cls.GetProperty("MaxHeightFraction")!.GetValue(d)!} of the frame height");
        l.Add($"  fill at least {(double)cls.GetProperty("MinFillRatio")!.GetValue(d)!}, solidity at least {(double)cls.GetProperty("MinSolidity")!.GetValue(d)!}");
        l.Add($"  centre distance at most {(double)cls.GetProperty("MaxCentreDistanceFraction")!.GetValue(d)!} of the frame width");
        return l;
    }

    static void SaveCapture(Bitmap capture, List<Point> matches, string outDir, string name)
    {
        var copy = new Bitmap(capture);
        using (var g = Graphics.FromImage(copy))
        {
            foreach (var p in matches)
            {
                if (p.X < 0 || p.Y < 0 || p.X >= copy.Width || p.Y >= copy.Height) continue;
                copy.SetPixel(p.X, p.Y, Color.Red);
            }
        }
        try { copy.Save(Path.Combine(outDir, name), ImageFormat.Png); } catch { }
        copy.Dispose();
    }

    static Rectangle GetWindowRectSafe(IntPtr handle)
    {
        try
        {
            RECT r;
            Native.GetWindowRect(handle, out r);
            return new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }
        catch { return Rectangle.Empty; }
    }

    static string SafeTitle(Process p)
    {
        try { return p.MainWindowTitle; } catch { return "<none>"; }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    static class Native
    {
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    }
}
