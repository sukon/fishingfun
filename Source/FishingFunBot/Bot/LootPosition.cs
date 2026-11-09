using System.Drawing;

#nullable enable
namespace FishingFun
{
    /// <summary>
    /// Decides where the float should be clicked.
    /// 
    /// The float is looked for again just before the click, because it may have
    /// moved while the bot was reacting. The place it is now is used only when it
    /// is close to the place it was last seen, because something else being found
    /// means the search has locked onto the wrong thing.
    /// 
    /// When the float cannot be seen at all the place it was last seen is used.
    /// A float that has gone under the water cannot be seen, but it is still the
    /// thing that has to be clicked, and clicking where it was is what a person
    /// would do.
    /// </summary>
    public static class LootPosition
    {
        /// <summary>
        /// How close the float has to be to the place it was last seen for the new
        /// place to be used.
        /// </summary>
        public const int MaximumDistance = 60;

        /// <summary>
        /// Picks the place to click.
        /// </summary>
        /// <param name="lastKnown">Where the float was last seen.</param>
        /// <param name="freshPosition">Where it is now, or null when it cannot be seen.</param>
        public static Point Choose(Point lastKnown, Point? freshPosition)
        {
            if (freshPosition.HasValue && IsCloseTo(freshPosition.Value, lastKnown, MaximumDistance))
            {
                return freshPosition.Value;
            }

            return lastKnown;
        }

        /// <summary>
        /// True when the two places are close enough to be the same float.
        /// </summary>
        public static bool IsCloseTo(Point first, Point second, int distance)
        {
            int dx = first.X - second.X;
            int dy = first.Y - second.Y;
            return (dx * dx) + (dy * dy) <= distance * distance;
        }
    }
}
