using System;
using System.Drawing;

#nullable enable
namespace FishingFun
{
    /// <summary>
    /// Retains a known location across short occlusions. A new small or sparse
    /// candidate must remain consistent for three frames before becoming a track.
    /// </summary>
    public sealed class BobberTracker
    {
        private readonly BobberDetector detector;
        private BobberCandidate? tracked;
        private long lastSeenAt;
        private BobberCandidate? tentative;
        private long tentativeAt;
        private int tentativeFrames;

        public BobberTracker(BobberDetector detector) { this.detector = detector; }

        public void Reset()
        {
            tracked = null;
            tentative = null;
            tentativeFrames = 0;
        }

        public BobberDetectionResult Find(Bitmap bitmap, long timestampMilliseconds)
        {
            if (tracked != null && timestampMilliseconds - lastSeenAt > 2000) tracked = null;
            Point? previous = tracked == null ? (Point?)null : tracked.Centre;
            var result = detector.Find(bitmap, previous);
            var candidate = result.Candidate;
            if (candidate == null && tracked == null && result.TentativeCandidate != null)
            {
                candidate = result.TentativeCandidate;
                bool consistent = tentative != null && timestampMilliseconds > tentativeAt &&
                    timestampMilliseconds - tentativeAt <= 1000 &&
                    Math.Abs(candidate.Centre.X - tentative.Centre.X) <= 20 &&
                    Math.Abs(candidate.Centre.Y - tentative.Centre.Y) <= 20 &&
                    candidate.Area >= tentative.Area * 0.5 && candidate.Area <= tentative.Area * 2;
                tentativeFrames = consistent ? tentativeFrames + 1 : 1;
                tentative = candidate;
                tentativeAt = timestampMilliseconds;
                candidate = tentativeFrames >= 3 ? candidate : null;
                result.Candidate = candidate;
            }
            else
            {
                tentative = null;
                tentativeFrames = 0;
            }
            if (candidate != null)
            {
                tracked = candidate;
                lastSeenAt = timestampMilliseconds;
            }
            return result;
        }
    }
}
