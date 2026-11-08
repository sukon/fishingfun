using System;
using System.Drawing;
using FishingFun;

// Logging is irrelevant to position detection; no game or packages are needed.
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

    private static void CheckNoBite(int[] positions, string name)
    {
        var watcher = new PositionBiteWatcher(5);
        watcher.Reset(new Point(100, positions[0]));
        bool triggered = false;
        foreach (int y in positions)
            triggered |= watcher.IsBite(new Point(100, y));
        Check(!triggered, name);
    }

    static int Main()
    {
        // Replays of the two premature strikes in myapp.log on 2026-11-08.
        // Bitmap Y values are sufficient: the screen offset is constant per cast.
        CheckNoBite(new[] { 240, 240, 240, 243, 245, 247, 247, 246,
            245, 243, 242, 241, 241, 242, 244, 246, 247, 247, 247,
            246, 244, 243, 241, 240, 240, 241, 244, 247, 250 },
            "21:19:42-21:19:48 logged drift does not reel in");
        CheckNoBite(new[] { 353, 353, 354, 357, 361 },
            "21:19:54-21:19:55 logged drift does not reel in");
        CheckNoBite(new[] { 100, 103, 106, 109, 112, 115, 118, 121 },
            "steady three-pixel drift does not reel in");
        CheckNoBite(new[] { 100, 104, 108, 112, 116, 120, 124, 128 },
            "steady four-pixel drift does not reel in");

        var watcher = new PositionBiteWatcher(5);
        watcher.Reset(new Point(100, 100));
        watcher.IsBite(new Point(100, 100));
        Check(watcher.IsBite(new Point(100, 107)), "seven-pixel bite triggers reel-in");

        watcher.Reset(new Point(100, 100));
        Check(watcher.IsBite(new Point(100, 107)), "bite on first frame after reset triggers reel-in");

        watcher.Reset(new Point(100, 100));
        bool driftTriggered = false;
        for (int y = 100; y <= 300; y += 2)
            driftTriggered |= watcher.IsBite(new Point(100, y));
        Check(!driftTriggered, "gentle two-pixel drift never triggers reel-in");
        Check(watcher.IsBite(new Point(100, 307)), "bite after drift triggers reel-in");
        Check(watcher.IsBite(new Point(100, 300)), "detected bite remains latched");

        watcher.Reset(new Point(100, 100));
        Check(!watcher.IsBite(new Point(100, 100)), "reset clears detected bite");
        Check(!watcher.IsBite(new Point(100, 90)), "upward movement is not a bite");
        return failures == 0 ? 0 : 1;
    }
}
