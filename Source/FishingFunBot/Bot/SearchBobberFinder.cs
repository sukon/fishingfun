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

        private static ILog logger = LogManager.GetLogger("Fishbot");

        private Point previousLocation;

        private Bitmap bitmap = new Bitmap(1, 1);

        public event EventHandler<BobberBitmapEvent> BitmapEvent;

        public SearchBobberFinder(IPixelClassifier pixelClassifier)
        {
            this.pixelClassifier = pixelClassifier;
            this.detector = new BobberDetector { Classifier = pixelClassifier };
            BitmapEvent += (s, e) => { };
        }

        public void Reset()
        {
            this.previousLocation = Point.Empty;
        }

        public Point Find()
        {
            this.bitmap = WowScreen.GetBitmap();

            var stopwatch = Stopwatch.StartNew();

            // The detector reads the frame through LockBits instead of Bitmap.GetPixel,
            // which is around ten times faster and keeps the detection rate high enough
            // to notice the bobber moving.
            Point? previous = this.previousLocation == Point.Empty ? (Point?)null : this.previousLocation;
            var detection = this.detector.Find(this.bitmap, previous);

            stopwatch.Stop();

            // Show the user which pixels matched, the same as the previous implementation
            // did, so the colour settings screen still makes sense.
            this.detector.HighlightMatches(this.bitmap, detection.Matches, Color.Red);

            Point found = detection.Candidate?.Centre ?? Point.Empty;

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
                logger.Info($"Bobber found at {found.X},{found.Y} in {stopwatch.ElapsedMilliseconds}ms.");
            }

            this.previousLocation = found;

            BitmapEvent?.Invoke(this, new BobberBitmapEvent { Point = new Point(found.X, found.Y), Bitmap = this.bitmap });

            this.bitmap.Dispose();

            return found == Point.Empty ? Point.Empty : WowScreen.GetScreenPositionFromBitmapPosition(found);
        }
    }
}
