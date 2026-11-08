using System;
using System.Drawing;

namespace FishingFun
{
    public interface IBiteWatcher
    {
        // Reset once per cast. Observations must use the same coordinate system
        // and a monotonic millisecond timeline for both live detection and replay.
        void Reset(BobberObservation initialObservation);

        bool IsBite(BobberObservation observation);

        Action<FishingEvent> FishingEventHandler { set; get; }
    }
}
