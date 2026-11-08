using log4net;
using System;
using System.Drawing;

#nullable enable
namespace FishingFun
{
    /// <summary>
    /// Works out whether the float has been pulled under by a fish.
    /// 
    /// The float floats on the surface and drifts. When a fish bites it is pulled down
    /// sharply. The trouble is telling the two apart, because a float that has just
    /// landed keeps sinking gently, a pixel or two a frame, and that goes on for a
    /// while.
    /// 
    /// A bite must move down sharply since the last observation as well as fall
    /// below the moving reference. Distance from the reference alone is insufficient:
    /// ordinary three- or four-pixel drift can build up enough lag to cross it.
    /// </summary>
    public class PositionBiteWatcher : IBiteWatcher
    {
        private static ILog logger = LogManager.GetLogger("Fishbot");

        /// <summary>
        /// How far the float has to be pulled under before it counts as a bite, in pixels.
        /// </summary>
        private readonly int strikeValue;

        /// <summary>
        /// How far the float has to move away from where it was expected to be, in
        /// pixels, before it counts as a bite. This is what tells a float being pulled
        /// under apart from one that is drifting, because a drifting float is followed
        /// closely and never gets this far away.
        /// </summary>
        private readonly int biteThreshold;

        /// <summary>
        /// How much of each observed movement the expected position follows.
        /// A separate per-observation drop check prevents tracking lag from
        /// turning gradual motion into a bite.
        /// </summary>
        private readonly double followFactor;

        /// <summary>
        /// Where the float is expected to be, worked out from how it has been moving.
        /// </summary>
        private double expectedY;

        /// <summary>
        /// True once the float has been seen at least once, so there is something to
        /// work from.
        /// </summary>
        private bool hasExpected;

        /// <summary>
        /// The previous observed position, used to distinguish a sharp drop from
        /// distance accumulated through gradual drift.
        /// </summary>
        private int previousY;

        /// <summary>
        /// True once the float has been pulled far enough down to count as a bite.
        /// </summary>
        private bool biteDetected;

        public Action<FishingEvent> FishingEventHandler { set; get; } = (e) => { };

        public PositionBiteWatcher(int strikeValue)
            : this(strikeValue, biteThreshold: 6, followFactor: 0.4)
        {
        }

        public PositionBiteWatcher(int strikeValue, int biteThreshold, double followFactor)
        {
            this.strikeValue = strikeValue;
            this.biteThreshold = biteThreshold;
            this.followFactor = followFactor;
        }

        public void RaiseEvent(FishingEvent ev)
        {
            FishingEventHandler?.Invoke(ev);
        }

        /// <summary>
        /// Called when the float has been cast and found.
        /// </summary>
        public void Reset(Point InitialBobberPosition)
        {
            RaiseEvent(new FishingEvent { Action = FishingAction.Reset });

            expectedY = InitialBobberPosition.Y;
            hasExpected = true;
            previousY = InitialBobberPosition.Y;
            biteDetected = false;
        }

        /// <summary>
        /// Feeds in the position the float was seen at. Returns true when it looks like a
        /// fish has bitten.
        /// </summary>
        public bool IsBite(Point currentBobberPosition)
        {
            if (biteDetected)
            {
                return true;
            }

            int currentY = currentBobberPosition.Y;

            if (!hasExpected)
            {
                expectedY = currentY;
                previousY = currentY;
                hasExpected = true;
                return false;
            }

            // Where the float would be if it had kept moving the way it has been
            // moving. The expected position follows the float, but only part of the
            // way, so a float being pulled under cannot be followed.
            //
            // The y axis grows downwards, so a float that has been pulled under has a
            // larger y than where it was expected to be.
            double movement = currentY - expectedY;
            // Measure before following. Following first attenuates a seven-pixel
            // bite to four pixels and hides it below the detection threshold.
            int drop = (int)Math.Round(movement);
            int frameDrop = currentY - previousY;
            previousY = currentY;

            int threshold = Math.Max(strikeValue, biteThreshold);
            if (drop >= threshold && frameDrop >= threshold)
            {
                biteDetected = true;
                logger.Info($"Float pulled down {frameDrop} pixels since the last observation ({drop} below reference), that is a bite.");
                RaiseEvent(new FishingEvent { Action = FishingAction.Loot });
                return true;
            }

            expectedY += movement * followFactor;
            RaiseEvent(new FishingEvent { Amplitude = drop, Action = FishingAction.BobberMove });
            return false;
        }
    }
}
