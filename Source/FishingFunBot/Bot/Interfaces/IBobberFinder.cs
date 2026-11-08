using System.Drawing;

namespace FishingFun
{
    public interface IBobberFinder
    {
        // Return a timestamped observation even when the float is not visible.
        // Size is in original capture pixels; Position is in screen pixels.
        BobberObservation Find();

        void Reset();
    }
}
