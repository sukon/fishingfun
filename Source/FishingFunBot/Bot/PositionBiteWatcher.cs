using log4net;
using System;
using System.Collections.Generic;
using System.Drawing;

#nullable enable
namespace FishingFun
{
    public class PositionBiteWatcher : IBiteWatcher
    {
        private static ILog logger = LogManager.GetLogger("Fishbot");

        // yPositions have only unique points 
        private List<int> yPositions = new List<int>();
       
        // yPositionsAll have all recorded positions which could be used for a different kind of clasifier 
        private List<int> amplitudes = new List<int>();
        private int strikeValue;
        private int amplitudeTreashHold = 5;
        private int yDiff;
        private TimedAction? timer;

        public Action<FishingEvent> FishingEventHandler { set; get; } = (e)=> { };

        public PositionBiteWatcher(int strikeValue)
        {
            this.strikeValue = strikeValue;
        }

        public void RaiseEvent(FishingEvent ev)
        {
            FishingEventHandler?.Invoke(ev);
        }

        public void Reset(Point InitialBobberPosition)
        {
            RaiseEvent(new FishingEvent { Action = FishingAction.Reset });

            yPositions = new List<int>();
            amplitudes = new List<int>();
            yPositions.Add(InitialBobberPosition.Y);
            timer = new TimedAction((a) =>
            {
                amplitudes.Add(yDiff);
                RaiseEvent(new FishingEvent { Amplitude = yDiff, Action = FishingAction.BobberMove });
            }, 400, 25);
        }


        /* We noticed that the main clasifier doesn't detect sometimes very subtle changes. 
         * However there is different pattern which can be observed. Consider this amplitude:
         * 1 0 1 2 1 0 -3 4 5 4 3
         * ^^ As u can see there is change of 7 in amplitude, however they are not negative thus the original 
         * Classifier doens't detect them. 
         *          
         */            
        private bool simplePositiveClasifier()
        {

            // First run
            if (amplitudes.Count < 3) return false;

            int currentAmpltitude = amplitudes[amplitudes.Count - 1];
            int lastAmpltitude = amplitudes[amplitudes.Count - 2];
            int last2Ampltitude = amplitudes[amplitudes.Count - 3];

            // This means that we are in positive amplitude 
            if (lastAmpltitude < currentAmpltitude)
            {
                // System.Console.WriteLine($"Checking c:{currentAmpltitude} - l:{lastAmpltitude} = {currentAmpltitude - lastAmpltitude} > {amplitudeTreashHold}");
                if (currentAmpltitude - lastAmpltitude >= amplitudeTreashHold)
                {
                    logger.Info("Detected using positive threshold  clasifier");
                    return true;
                }
                //System.Console.WriteLine($"Checking c:{currentAmpltitude} - l2:{last2Ampltitude} = {currentAmpltitude - last2Ampltitude} > {amplitudeTreashHold}");
                if (currentAmpltitude - last2Ampltitude >= amplitudeTreashHold) {
                    logger.Info("Detected using positive threshold  clasifier");
                    return true;
                }
            }
          
            return false;
        }

        public bool IsBite(Point currentBobberPosition)
        {

            bool positiveTreasholdReached = simplePositiveClasifier();
 
            if (!yPositions.Contains(currentBobberPosition.Y))
            {
                yPositions.Add(currentBobberPosition.Y);
                yPositions.Sort();
            }

            yDiff = yPositions[(int)((((double)yPositions.Count) + 0.5) / 2)] - currentBobberPosition.Y;
            bool thresholdReached = yDiff <= -strikeValue;

            if (timer != null)
            {
                timer.ExecuteIfDue();
            }

            if (thresholdReached || positiveTreasholdReached)
            {
                RaiseEvent(new FishingEvent { Action = FishingAction.Loot });
                if (timer != null)
                {
                    timer.ExecuteNow();
                }
                return true;
            }

            return false;
        }
    }
}