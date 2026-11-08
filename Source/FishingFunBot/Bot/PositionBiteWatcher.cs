using log4net;
using System;
using System.Collections.Generic;

#nullable enable
namespace FishingFun
{
    /// <summary>
    /// Learns the visible size and ordinary motion of each cast before watching for
    /// a sharp downward movement. Live detection and replay use the same observations.
    /// </summary>
    public class PositionBiteWatcher : IBiteWatcher
    {
        private static readonly ILog logger = LogManager.GetLogger("Fishbot");
        private const int CalibrationMilliseconds = 1000;
        private const int MinimumCalibrationSamples = 6;
        private const int MaximumObservationGapMilliseconds = 2000;
        private const int ContinuousObservationMilliseconds = 1000;
        private const int MotionHistoryLength = 30;
        private const double ReferenceIntervalMilliseconds = 200;
        private readonly int maximumThreshold;
        private readonly double followFactor;
        private readonly List<double> heights = new List<double>();
        private readonly List<double> normalSpeeds = new List<double>();
        private BobberObservation? previous;
        private long calibrationStart;
        private long lastTimestamp;
        private int calibrationSamples;
        private double expectedY;
        private bool biteDetected;

        public bool IsCalibrated { get; private set; }
        public double CalibratedHeight { get; private set; }
        public double ThresholdPixels { get; private set; }
        public Action<FishingEvent> FishingEventHandler { get; set; } = e => { };

        public PositionBiteWatcher(int strikeValue)
            : this(strikeValue, biteThreshold: 6, followFactor: 0.4) { }

        public PositionBiteWatcher(int strikeValue, int biteThreshold, double followFactor)
        {
            if (strikeValue <= 0 || biteThreshold <= 0)
                throw new ArgumentOutOfRangeException(nameof(strikeValue));
            if (double.IsNaN(followFactor) || followFactor <= 0 || followFactor > 1)
                throw new ArgumentOutOfRangeException(nameof(followFactor));
            maximumThreshold = Math.Max(strikeValue, biteThreshold);
            this.followFactor = followFactor;
            ThresholdPixels = maximumThreshold;
        }

        public void Reset(BobberObservation initialObservation)
        {
            biteDetected = false;
            BeginCalibration(initialObservation);
            FishingEventHandler?.Invoke(new FishingEvent { Action = FishingAction.Reset });
        }

        private void BeginCalibration(BobberObservation observation)
        {
            IsCalibrated = false;
            CalibratedHeight = 0;
            ThresholdPixels = maximumThreshold;
            heights.Clear();
            normalSpeeds.Clear();
            calibrationStart = observation.TimestampMilliseconds;
            lastTimestamp = observation.TimestampMilliseconds;
            previous = observation.IsVisible ? observation : null;
            calibrationSamples = observation.IsVisible ? 1 : 0;
            expectedY = observation.Position.Y;
            AddHeight(observation);
        }

        public bool IsBite(BobberObservation observation)
        {
            if (biteDetected) return true;
            if (observation.TimestampMilliseconds <= lastTimestamp) return false;
            lastTimestamp = observation.TimestampMilliseconds;
            if (!observation.IsVisible) return false;

            var last = previous;
            if (last == null)
            {
                BeginCalibration(observation);
                return false;
            }

            long elapsed = observation.TimestampMilliseconds - last.TimestampMilliseconds;
            int horizontalMovement = Math.Abs(observation.Position.X - last.Position.X);
            int maximumHorizontalMovement = Math.Max(12, last.Size.Width * 2);
            if (elapsed > MaximumObservationGapMilliseconds || horizontalMovement > maximumHorizontalMovement)
            {
                // A long gap or a jump to a different object cannot establish a bite.
                BeginCalibration(observation);
                return false;
            }

            double frameDrop = observation.Position.Y - last.Position.Y;
            double speed = Math.Abs(frameDrop) * 1000.0 / elapsed;
            previous = observation;

            if (IsCalibrated && elapsed > ContinuousObservationMilliseconds)
            {
                // Keep this cast's learned appearance and motion through a brief
                // dropout. Reappearance itself is not evidence of a sharp drop.
                expectedY = observation.Position.Y;
                return false;
            }

            if (!IsCalibrated)
            {
                calibrationSamples++;
                AddHeight(observation);
                AddNormalSpeed(speed);
                expectedY = observation.Position.Y;
                if (calibrationSamples >= MinimumCalibrationSamples &&
                    observation.TimestampMilliseconds - calibrationStart >= CalibrationMilliseconds)
                {
                    CalibratedHeight = heights.Count == 0 ? 0 : Median(heights);
                    // Keep the working six-pixel setting for large floats. Smaller
                    // floats use 20% of their height, with a two-pixel noise floor.
                    // Freeze size here so submersion cannot lower the threshold.
                    ThresholdPixels = CalibratedHeight > 0
                        ? Math.Max(2, Math.Min(maximumThreshold, CalibratedHeight * 0.20))
                        : maximumThreshold;
                    IsCalibrated = true;
                    logger.Info($"Float calibrated: height {CalibratedHeight:F1}px, drop threshold {ThresholdPixels:F1}px, normal speed limit {NormalSpeedLimit():F1}px/s.");
                }
                return false;
            }

            double drop = observation.Position.Y - expectedY;
            // Ordinary motion is measured in pixels/second, so slower capture or
            // short occlusion cannot turn normal drift into an abrupt movement.
            double normalMovement = NormalSpeedLimit() * elapsed / 1000.0 + 1;
            if (frameDrop >= ThresholdPixels && drop >= ThresholdPixels && frameDrop > normalMovement)
            {
                biteDetected = true;
                logger.Info($"Float bite: drop {frameDrop:F1}px in {elapsed}ms, reference drop {drop:F1}px, threshold {ThresholdPixels:F1}px, normal movement limit {normalMovement:F1}px.");
                FishingEventHandler?.Invoke(new FishingEvent { Action = FishingAction.Loot });
                return true;
            }

            double follow = 1 - Math.Pow(1 - followFactor, elapsed / ReferenceIntervalMilliseconds);
            expectedY += drop * follow;
            AddNormalSpeed(speed);
            FishingEventHandler?.Invoke(new FishingEvent
            {
                Amplitude = (int)Math.Round(drop), Action = FishingAction.BobberMove
            });
            return false;
        }

        private void AddHeight(BobberObservation observation)
        {
            if (observation.IsVisible && observation.Size.Height > 0)
                heights.Add(observation.Size.Height);
        }

        private void AddNormalSpeed(double speed)
        {
            if (normalSpeeds.Count == MotionHistoryLength) normalSpeeds.RemoveAt(0);
            normalSpeeds.Add(speed);
        }

        private double NormalSpeedLimit()
        {
            if (normalSpeeds.Count == 0) return 0;
            double median = Median(normalSpeeds);
            var deviations = normalSpeeds.ConvertAll(value => Math.Abs(value - median));
            return median + 3 * Median(deviations);
        }

        private static double Median(List<double> values)
        {
            var sorted = values.ToArray();
            Array.Sort(sorted);
            int middle = sorted.Length / 2;
            return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
        }
    }
}
