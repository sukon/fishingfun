using log4net;
using System;
using System.Diagnostics;
using System.Drawing;

#nullable enable
namespace FishingFun
{
    public class SearchBobberFinder : IBobberFinder, IImageProvider
    {
        private readonly IPixelClassifier pixelClassifier;
        private readonly BobberDetector detector;
        private readonly BobberTracker tracker;

        private static ILog logger = LogManager.GetLogger("Fishbot");

        private Bitmap bitmap = new Bitmap(1, 1);

        public event EventHandler<BobberBitmapEvent> BitmapEvent;

        public SearchBobberFinder(IPixelClassifier pixelClassifier)
        {
            this.pixelClassifier = pixelClassifier;
            this.detector = new BobberDetector { Classifier = pixelClassifier };
            this.tracker = new BobberTracker(this.detector);
            BitmapEvent += (s, e) => { };
        }

        public void Reset()
        {
            this.tracker.Reset();
        }

        public BobberObservation Find()
        {
            this.bitmap = WowScreen.GetBitmap();
            long capturedAt = BobberObservation.NowMilliseconds;

            var stopwatch = Stopwatch.StartNew();

            // The detector reads the frame through LockBits instead of Bitmap.GetPixel,
            // which is around ten times faster and keeps the detection rate high enough
            // to notice the bobber moving.
            var detection = this.tracker.Find(this.bitmap, capturedAt);

            stopwatch.Stop();

            // Show the user which pixels matched, the same as the previous implementation
            // did, so the colour settings screen still makes sense.
            this.detector.HighlightMatches(this.bitmap, detection.Matches, Color.Red);

            Point found = detection.Candidate?.Centre ?? Point.Empty;
            var candidate = detection.Candidate;
            var observation = candidate == null
                ? BobberObservation.Missing(capturedAt)
                : new BobberObservation(WowScreen.GetScreenPositionFromBitmapPosition(found),
                    new Size(candidate.Width, candidate.Height), candidate.Area, capturedAt);

            if (detection.UsedFallback)
            {
                logger.Info("The colour rule matched too much of the screen to be useful, so the bobber is being looked for by how it differs from the water behind it.");
            }

            if (found == Point.Empty)
            {
                logger.Info("No bobber found in this frame.");
            }
            else
            {
                logger.Info($"Bobber found at {found.X},{found.Y}, size {observation.Size.Width}x{observation.Size.Height}, area {observation.Area}, in {stopwatch.ElapsedMilliseconds}ms.");
            }

            BitmapEvent?.Invoke(this, new BobberBitmapEvent { Point = new Point(found.X, found.Y), Bitmap = this.bitmap });

            this.bitmap.Dispose();

            return observation;
        }
    }
}
