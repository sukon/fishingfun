using System;
using System.Drawing;
using System.IO;
using System.Linq;
using FishingFun;

// Exercise the production observation/decision seam without a game or NuGet restore.
namespace log4net
{
    public interface ILog { void Info(object message); }
    public static class LogManager
    {
        private class SilentLog : ILog { public void Info(object message) { } }
        public static ILog GetLogger(string name) { return new SilentLog(); }
    }
}

class BiteWatcherRegression
{
    private static int failures;
    private static void Check(bool condition, string name)
    {
        Console.WriteLine((condition ? "PASS " : "FAIL ") + name);
        if (!condition) failures++;
    }

    private static BobberObservation At(long time, int y = 100, int height = 30, int x = 100)
    {
        return new BobberObservation(new Point(x, y), new Size(height, height), height * height, time);
    }

    private static PositionBiteWatcher Ready(int height = 30)
    {
        var watcher = new PositionBiteWatcher(5);
        watcher.Reset(At(0, height: height));
        for (int i = 1; i <= 5; i++) watcher.IsBite(At(i * 200, height: height));
        return watcher;
    }

    private static void CheckNoBite(int[] positions, string name)
    {
        var watcher = Ready();
        bool triggered = false;
        for (int i = 0; i < positions.Length; i++)
            triggered |= watcher.IsBite(At(1200 + i * 200, positions[i]));
        Check(!triggered, name);
    }

    private static long Replay(string path)
    {
        var watcher = new PositionBiteWatcher(5);
        long firstBite = -1;
        foreach (var entry in BobberTraceRecorder.Read(path))
        {
            if (entry.RecordedDecision == "reset") watcher.Reset(entry.Observation);
            else if (watcher.IsBite(entry.Observation) && firstBite < 0)
                firstBite = entry.Observation.TimestampMilliseconds;
        }
        return firstBite;
    }

    static int Main(string[] args)
    {
        var imageDetector = new BobberDetector();
        var imageTracker = new BobberTracker(imageDetector);
        var imageWatcher = new PositionBiteWatcher(5);
        bool imageBite = false;
        bool splashFound = false;
        bool normalTriggered = false;
        for (int frame = 37; frame <= 44; frame++)
        {
            using (var bitmap = new Bitmap(Path.Combine(args[0], "images", "splash-" + frame + ".png")))
            {
                long time = (frame - 37) * 500;
                var result = imageTracker.Find(bitmap, time);
                var candidate = result.Candidate;
                var observation = candidate == null ? BobberObservation.Missing(time) :
                    new BobberObservation(candidate.Centre, new Size(candidate.Width, candidate.Height), candidate.Area, time);
                if (frame == 37) imageWatcher.Reset(observation);
                else
                {
                    bool decision = imageWatcher.IsBite(observation);
                    if (frame < 43) normalTriggered |= decision;
                    else imageBite |= decision;
                }
                if (frame == 43) splashFound = candidate != null;
            }
        }
        Check(!normalTriggered, "actual pre-splash images do not reel in");
        Check(splashFound, "actual splash image keeps tracking the float");
        Check(imageBite, "actual image sequence triggers reel-in on the splash");

        using (var stable = new Bitmap(Path.Combine(args[0], "images", "splash-42.png")))
        using (var splash = new Bitmap(Path.Combine(args[0], "images", "splash-43.png")))
        using (var empty = new Bitmap(stable.Width, stable.Height))
        {
            imageTracker.Reset();
            Check(imageTracker.Find(splash, 0).Candidate == null &&
                imageTracker.Find(splash, 200).Candidate == null &&
                imageTracker.Find(splash, 400).Candidate != null,
                "sparse acquisition requires three consistent real images");
            imageTracker.Reset();
            imageTracker.Find(stable, 0);
            Check(imageTracker.Find(empty, 200).Candidate == null &&
                imageTracker.Find(splash, 400).Candidate != null,
                "one missed frame retains the tracked sparse float");
            Check(imageTracker.Find(splash, 2600).Candidate == null,
                "expired track requires confirmation again");
        }

        using (var small = new Bitmap(1280, 720))
        {
            using (var graphics = Graphics.FromImage(small))
            {
                graphics.Clear(Color.FromArgb(20, 40, 60));
                graphics.FillRectangle(Brushes.Red, 640, 360, 3, 6);
            }
            imageTracker.Reset();
            Check(imageTracker.Find(small, 0).Candidate == null &&
                imageTracker.Find(small, 200).Candidate == null &&
                imageTracker.Find(small, 400).Candidate?.Area == 18,
                "small candidate below frame-relative area limit is acquired after confirmation");
        }

        using (var line = new Bitmap(1280, 720))
        {
            using (var graphics = Graphics.FromImage(line))
            {
                graphics.Clear(Color.FromArgb(20, 40, 60));
                graphics.FillRectangle(Brushes.Red, 640, 360, 30, 1);
            }
            imageTracker.Reset();
            bool found = false;
            for (int i = 0; i < 5; i++) found |= imageTracker.Find(line, i * 200).Candidate != null;
            Check(!found, "a thin background line is still rejected");
        }

        var watcher = new PositionBiteWatcher(5);
        watcher.Reset(At(0));
        Check(!watcher.IsBite(At(200, 107)), "initial landing movement does not reel in");
        Check(!watcher.IsCalibrated, "one observation is insufficient calibration");

        foreach (int height in new[] { 10, 20, 30, 60 })
        {
            watcher = Ready(height);
            double expectedThreshold = Math.Max(2, Math.Min(6, height * 0.2));
            Check(watcher.IsCalibrated && watcher.ThresholdPixels == expectedThreshold,
                "per-cast threshold for height " + height);
            int drop = (int)expectedThreshold + 1;
            int lootEvents = 0;
            watcher.FishingEventHandler = e => { if (e.Action == FishingAction.Loot) lootEvents++; };
            Check(watcher.IsBite(At(1200, 100 + drop, height)), "bite for height " + height);
            Check(watcher.IsBite(At(1400, height: height)) && lootEvents == 1,
                "one loot event and latched decision for height " + height);
        }

        CheckNoBite(new[] { 100, 103, 106, 109, 112, 115, 118, 121 }, "three-pixel drift does not reel in");
        CheckNoBite(new[] { 100, 104, 108, 112, 116, 120, 124, 128 }, "four-pixel drift does not reel in");
        CheckNoBite(Enumerable.Range(0, 100).Select(i => 100 + i * 2).ToArray(), "two-pixel drift does not reel in");

        watcher = Ready(10);
        bool triggered = false;
        for (int i = 0; i < 40; i++) triggered |= watcher.IsBite(At(1200 + i * 200, 100 + i % 2, 10));
        Check(!triggered, "one-pixel jitter on a small float does not reel in");

        watcher = new PositionBiteWatcher(5);
        watcher.Reset(At(0, height: 10));
        for (int i = 1; i <= 5; i++) watcher.IsBite(At(i * 200, 100 + i * 2, 10));
        triggered = false;
        for (int i = 6; i <= 20; i++) triggered |= watcher.IsBite(At(i * 200, 100 + i * 2, 10));
        Check(!triggered, "calibrated two-pixel drift on a small float does not reel in");
        Check(watcher.IsBite(At(4200, 146, 10)), "sharp bite above a small float's normal motion");

        foreach (int interval in new[] { 100, 200, 400 })
        {
            watcher = new PositionBiteWatcher(5);
            watcher.Reset(At(0, height: 10));
            triggered = false;
            for (int time = interval; time <= 6000; time += interval)
                triggered |= watcher.IsBite(At(time, 100 + time / 100, 10));
            Check(watcher.IsCalibrated && !triggered, "same normal speed at " + interval + "ms sampling");
            Check(watcher.IsBite(At(6000 + interval, 170, 10)), "bite at " + interval + "ms sampling");
        }

        watcher = Ready();
        Check(!watcher.IsBite(BobberObservation.Missing(1200)), "missing float alone is not a bite");
        Check(!watcher.IsBite(At(2400, 104)) && watcher.IsCalibrated,
            "brief dropout retains learned size and motion without striking on reappearance");
        watcher = Ready();
        Check(!watcher.IsBite(At(3200, 120)) && !watcher.IsCalibrated,
            "long observation gap requires calibration again");

        watcher = new PositionBiteWatcher(5);
        watcher.Reset(At(0, height: 10));
        for (int i = 1; i <= 5; i++) watcher.IsBite(At(i * 200, 100 + i * 2, 10));
        watcher.IsBite(BobberObservation.Missing(1200));
        watcher.IsBite(BobberObservation.Missing(1400));
        Check(!watcher.IsBite(At(1600, 116, 10)), "short occlusion does not turn gradual drift into a bite");

        watcher = Ready();
        Check(!watcher.IsBite(At(900, 150)) && !watcher.IsBite(At(1000, 150)), "stale and duplicate observations ignored");
        Check(watcher.IsBite(At(1200, 107)), "stale data does not corrupt the following bite");

        watcher = Ready();
        Check(!watcher.IsBite(At(1200, 130, x: 200)) && !watcher.IsCalibrated,
            "jump to another object does not reel in");
        watcher = Ready();
        Check(!watcher.IsBite(At(1200, 103, height: 5)) && watcher.ThresholdPixels == 6,
            "submersion cannot lower the calibrated size threshold");

        watcher = Ready();
        watcher.Reset(At(2000, height: 10));
        Check(!watcher.IsCalibrated, "each cast clears previous calibration");
        for (int i = 1; i <= 5; i++) watcher.IsBite(At(2000 + i * 200, height: 10));
        Check(watcher.ThresholdPixels == 2, "next cast learns a different float size");

        watcher = new PositionBiteWatcher(5);
        watcher.Reset(At(0));
        for (int i = 1; i <= 5; i++) watcher.IsBite(At(i * 200, height: i == 2 ? 200 : 30));
        Check(watcher.CalibratedHeight == 30, "median size rejects a calibration outlier");

        watcher = Ready(0);
        Check(watcher.ThresholdPixels == 6 && watcher.IsBite(At(1200, 107, height: 0)),
            "unknown size retains the working six-pixel threshold");

        foreach (string fixture in Directory.GetFiles(args[0], "*.csv"))
            Check(Replay(fixture) == -1, "real logged no-bite replay " + Path.GetFileName(fixture));

        var initial = At(5000, height: 10);
        using (var recorder = BobberTraceRecorder.TryCreate(5000, args[1]))
        {
            Check(recorder != null, "trace recorder opens under temp");
            if (recorder != null)
            {
                recorder.Record(initial, "reset");
                for (int i = 1; i <= 5; i++) recorder.Record(At(5000 + i * 200, height: 10), "watch");
                recorder.Record(At(6200, 103, 10), "bite");
                Check(Replay(recorder.Path) == 1200, "recorded size/time sequence replays through production watcher");
                var last = BobberTraceRecorder.Read(recorder.Path).Last();
                Check(last.Observation.Size.Height == 10 && last.Observation.Area == 100 &&
                    last.RecordedDecision == "bite", "recording preserves size, area, time and original decision");
            }
        }
        Check(BobberTraceRecorder.TryCreate(0, args[2]) == null,
            "unwritable recording location does not stop detection");
        return failures == 0 ? 0 : 1;
    }
}
