using System;
using System.Globalization;
using FishingFun;

class BobberReplay
{
    static int Main(string[] args)
    {
        try
        {
            var watcher = new PositionBiteWatcher(5);
            long firstBite = -1;
            long recordedBite = -1;
            int frames = 0;
            foreach (var entry in BobberTraceRecorder.Read(args[0]))
            {
                frames++;
                if (entry.RecordedDecision == "reset") watcher.Reset(entry.Observation);
                else if (watcher.IsBite(entry.Observation) && firstBite < 0)
                    firstBite = entry.Observation.TimestampMilliseconds;
                if (entry.RecordedDecision == "bite" && recordedBite < 0)
                    recordedBite = entry.Observation.TimestampMilliseconds;
            }
            Console.WriteLine("Frames: " + frames);
            Console.WriteLine("Replay first bite (ms, -1 means none): " + firstBite);
            Console.WriteLine("Recorded first decision (ms, not a human label): " + recordedBite);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "Final calibration: ready={0}, height={1:F1}px, threshold={2:F1}px",
                watcher.IsCalibrated, watcher.CalibratedHeight, watcher.ThresholdPixels));
            if (args.Length > 1)
            {
                long expected = long.Parse(args[1], CultureInfo.InvariantCulture);
                if (firstBite != expected)
                {
                    Console.Error.WriteLine("FAIL expected first bite at " + expected + "ms");
                    return 1;
                }
                Console.WriteLine("PASS expected decision");
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
            return 2;
        }
    }
}
