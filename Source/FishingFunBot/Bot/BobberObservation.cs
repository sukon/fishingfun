using System;
using System.Diagnostics;
using System.Drawing;

namespace FishingFun
{
    /// <summary>
    /// One frame: screen position, original capture size and area, and monotonic time.
    /// The capture is cropped, not resized. Empty position means not visible; empty
    /// size means the finder cannot measure a shape.
    /// </summary>
    public sealed class BobberObservation
    {
        public Point Position { get; }
        public Size Size { get; }
        public int Area { get; }
        public long TimestampMilliseconds { get; }
        public bool IsVisible => Position != Point.Empty;

        public BobberObservation(Point position, Size size, int area, long timestampMilliseconds)
        {
            if (size.Width < 0 || size.Height < 0 || area < 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            Position = position;
            Size = size;
            Area = area;
            TimestampMilliseconds = timestampMilliseconds;
        }

        public static long NowMilliseconds => (long)(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);

        public static BobberObservation Missing(long timestampMilliseconds)
        {
            return new BobberObservation(Point.Empty, Size.Empty, 0, timestampMilliseconds);
        }
    }
}
