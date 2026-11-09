using System;

#nullable enable
namespace FishingFun
{
    /// <summary>
    /// Produces the timings a person would use rather than the fixed ones a program
    /// would use.
    /// 
    /// Every duration has a range and a shape. The shape is skewed towards the short
    /// end, because most people react quickly and a few are slow. A flat spread over
    /// the whole range would look less natural than the real thing, not more.
    /// 
    /// The ranges are chosen so that the time from a fish biting to the click on the
    /// float stays within what the bot used to take. Being slower than that loses
    /// fish, so the range for that step is not made wider than it was.
    /// 
    /// When the behaviour is switched off the old random behaviour is used, not a
    /// fixed value, so that switching it off does not change how the bot behaves in
    /// any other way.
    /// </summary>
    public class HumanTiming
    {
        /// <summary>
        /// The settings the running bot uses.
        /// </summary>
        public static HumanTiming Current { get; private set; } = new HumanTiming(new Random(), 1.0, true);

        /// <summary>
        /// Sets the settings the running bot uses.
        /// 
        /// A seed is only given by the tests and by diagnostics. It is not given in
        /// normal use, so that two runs of the bot do not follow the same sequence.
        /// </summary>
        public static void Configure(bool enabled, double jitter, int? seed = null)
        {
            Current = new HumanTiming(seed.HasValue ? new Random(seed.Value) : new Random(), jitter, enabled);
        }

        private readonly Random random;
        private readonly double jitter;

        /// <summary>
        /// Whether the human timings are used at all.
        /// </summary>
        public bool Enabled { get; }

        /// <summary>
        /// How strong the variation is. Zero gives the middle of every range, one
        /// gives the range as designed and two gives twice the spread, never going
        /// outside the range.
        /// </summary>
        public double Jitter => jitter;

        public HumanTiming(Random random, double jitter, bool enabled)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            this.jitter = ClampJitter(jitter);
            this.Enabled = enabled;
        }

        /// <summary>
        /// The pause before the float is cast again.
        /// 
        /// A person does not cast the instant the last fish is in the bag, and the
        /// game needs a moment to finish the last cast.
        /// </summary>
        public int PreCastDelayMs()
        {
            if (!Enabled) return 0;
            return Skewed(950, 3000, 1.6);
        }

        /// <summary>
        /// The time between noticing the fish and moving the hand onto the float.
        /// 
        /// This is the step that has to stay short. Together with the wait that the
        /// camera needs afterwards it decides how long it takes to click the float,
        /// and the fish is only on the line for a short while. The top of the range
        /// is chosen so that the click is never later than it used to be.
        /// </summary>
        public int LootReactionMs()
        {
            if (!Enabled) return 900 + random.Next(0, 225);
            return Skewed(700, 1120, 1.5);
        }

        /// <summary>
        /// How long a key is held down for.
        /// </summary>
        public int KeyHoldMs()
        {
            if (!Enabled) return 50 + random.Next(0, 75);
            return Skewed(45, 180, 1.2);
        }

        /// <summary>
        /// Picks a value from the range, with most of the values towards the short end.
        /// 
        /// The shape is a power of a uniform number. A shape above one pulls the
        /// values towards the low end and leaves a long tail towards the high end,
        /// which is how people behave.
        /// 
        /// The spread is scaled around the middle of the range, so that a stronger
        /// setting gives a wider spread. A value that ends up outside the range is
        /// folded back into it rather than being cut off, because cutting it off
        /// would pile the values up at the ends and make the timing look less
        /// natural than it was, not more.
        /// </summary>
        private int Skewed(int minimum, int maximum, double shape)
        {
            double unit = Math.Pow(random.NextDouble(), shape);
            double raw = minimum + (maximum - minimum) * unit;

            // The spread is scaled around the middle of the range, so that a strength
            // of zero gives the middle and a strength of two gives twice the spread.
            double centre = (minimum + maximum) / 2.0;
            double scaled = centre + (raw - centre) * jitter;

            // The value is folded back into the range. The range is what keeps the
            // bot from being slower than it used to be, so it is never left.
            double span = maximum - minimum;
            if (span <= 0) return minimum;

            while (scaled > maximum)
            {
                scaled = maximum - (scaled - maximum);
            }
            while (scaled < minimum)
            {
                scaled = minimum + (minimum - scaled);
            }

            return (int)Math.Round(scaled);
        }

        /// <summary>
        /// Keeps the strength within a range. A strength that is not a number, or is
        /// not finite, is treated as the default.
        /// </summary>
        private static double ClampJitter(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 1.0;
            return Math.Max(0.0, Math.Min(2.0, value));
        }
    }
}
