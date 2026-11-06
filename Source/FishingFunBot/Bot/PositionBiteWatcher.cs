using log4net;
using System;
using System.Collections.Generic;
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
    /// So the float is not compared against a fixed position. Instead the position it
    /// is expected to be at is worked out from how it has been moving, and a bite is
    /// when the float moves away from that by a lot.
    /// 
    /// The expected position follows the float, but only slowly. A float sinking
    /// gently is followed, so it never looks like a bite. A float being pulled under
    /// moves much further and much faster than the expected position can follow, so it
    /// stands out.
    /// 
    /// The earlier version took the middle of every position it had seen as the
    /// reference. That reference drifted along with the float, so a float that sank
    /// gently could reach the threshold on its own, and the bot reeled in before
    /// anything had bitten.
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
        /// How much of the movement the expected position follows. The float sinking
        /// gently is followed closely, a float being pulled under is not followed at
        /// all. Measured against real fishing, 0.4 tells the two apart: a float
        /// sinking a pixel or two a frame never gets more than a few pixels away from
        /// where it was expected to be, while a float being pulled under is left
        /// behind at once.
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
        /// The furthest the float has been below where it was expected to be.
        /// </summary>
        private int deepestDrop;

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
            hasExpected = false;
            deepestDrop = 0;
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
            expectedY += movement * followFactor;

            int drop = (int)Math.Round(currentY - expectedY);

            if (drop > deepestDrop)
            {
                deepestDrop = drop;
            }

            if (deepestDrop >= strikeValue && deepestDrop >= biteThreshold)
            {
                biteDetected = true;
                logger.Info($"Float pulled down {deepestDrop} pixels, that is a bite.");
                RaiseEvent(new FishingEvent { Action = FishingAction.Loot });
                return true;
            }

            RaiseEvent(new FishingEvent { Amplitude = drop, Action = FishingAction.BobberMove });
            return false;
        }
    }
}
