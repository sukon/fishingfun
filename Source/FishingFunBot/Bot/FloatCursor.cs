using System.Drawing;

#nullable enable
namespace FishingFun
{
    /// <summary>
    /// Decides when the cursor is put onto the float.
    /// 
    /// A person puts the cursor on the float once the float has landed and then
    /// leaves it there. The float drifts while they wait, but they do not follow
    /// it. The place it ends up in is put right before the click.
    /// 
    /// This class holds that rule. It does not touch the mouse itself, so that
    /// the rule can be checked without a game running.
    /// </summary>
    public class FloatCursor
    {
        private Point? placedOn;

        /// <summary>
        /// True when the cursor has already been put on the float for this cast.
        /// </summary>
        public bool Placed => placedOn.HasValue;

        /// <summary>
        /// The place the cursor was put on.
        /// </summary>
        public Point? PlacedOn => placedOn;

        /// <summary>
        /// Puts the cursor on the float the first time the float is seen.
        /// 
        /// Returns false when it has already been done for this cast. It also
        /// returns false when the user is holding a mouse button down, and in
        /// that case the float is not marked as placed, so that the cursor can
        /// be put on it on a later frame.
        /// </summary>
        public bool Place(Point floatPosition, bool userHoldsMouseButton)
        {
            if (placedOn.HasValue || userHoldsMouseButton) return false;
            placedOn = floatPosition;
            return true;
        }
    }
}
