using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable
namespace FishingFun
{
    /// <summary>
    /// Checks that the human timings stay inside their ranges, that they are shaped
    /// the way they are meant to be, and that switching them off gives back the
    /// behaviour the bot had before.
    /// </summary>
    public static class HumanTimingRegression
    {
        private static int failures;
        private static int checks;

        private static void Check(string what, bool passed, string detail = "")
        {
            checks++;
            if (passed)
            {
                Console.WriteLine("PASS " + what + (detail.Length > 0 ? "  (" + detail + ")" : ""));
            }
            else
            {
                failures++;
                Console.WriteLine("FAIL " + what + (detail.Length > 0 ? "  (" + detail + ")" : ""));
            }
        }

        public static int Main()
        {
            Console.WriteLine("=== the human timings stay inside their ranges ===");
            {
                var timing = new HumanTiming(new Random(1), 1.0, true);

                var preCast = Sample(timing.PreCastDelayMs, 4000);
                Check("the pause before casting stays in range",
                    preCast.All(v => v >= 950 && v <= 3000),
                    $"[{preCast.Min()}, {preCast.Max()}]");

                var reaction = Sample(timing.LootReactionMs, 4000);
                Check("the reaction time stays in range",
                    reaction.All(v => v >= 700 && v <= 1120),
                    $"[{reaction.Min()}, {reaction.Max()}]");

                var hold = Sample(timing.KeyHoldMs, 4000);
                Check("the key hold stays in range",
                    hold.All(v => v >= 45 && v <= 180),
                    $"[{hold.Min()}, {hold.Max()}]");
            }

            Console.WriteLine();
            Console.WriteLine("=== the click on the float is not slower than it used to be ===");
            {
                // The old behaviour was a 900 to 1125ms wait and then the 1000ms the
                // camera needs. Anything longer than that loses fish.
                var timing = new HumanTiming(new Random(2), 2.0, true);
                var reaction = Sample(timing.LootReactionMs, 8000);
                int worstTotal = reaction.Max() + 1000;
                Check("the worst case is no slower than the old behaviour",
                    worstTotal <= 2124,
                    $"worst {worstTotal}ms, old worst 2124ms");

                var strongest = Sample(timing.PreCastDelayMs, 4000);
                Check("a strong setting cannot push the pause past its range",
                    strongest.All(v => v >= 950 && v <= 3000),
                    $"[{strongest.Min()}, {strongest.Max()}]");
            }

            Console.WriteLine();
            Console.WriteLine("=== the timings are not flat ===");
            {
                var timing = new HumanTiming(new Random(3), 1.0, true);
                var reaction = Sample(timing.LootReactionMs, 8000);
                double median = Median(reaction);
                double mean = reaction.Average();
                Check("most reaction times are shorter than the average",
                    median < mean,
                    $"median {median:F0}ms, mean {mean:F0}ms");

                var preCast = Sample(timing.PreCastDelayMs, 8000);
                Check("most pauses before casting are shorter than the average",
                    Median(preCast) < preCast.Average(),
                    $"median {Median(preCast):F0}ms, mean {preCast.Average():F0}ms");

                Check("the reaction times are spread out",
                    reaction.Distinct().Count() > 50,
                    $"{reaction.Distinct().Count()} different values");
            }

            Console.WriteLine();
            Console.WriteLine("=== the values do not pile up at the ends ===");
            {
                // A strong setting used to push the values out of the range, and
                // they were then cut off at the ends. Most of the values were then
                // the same number, which looks nothing like a person.
                var strong = new HumanTiming(new Random(9), 2.0, true);
                var values = Sample(strong.PreCastDelayMs, 8000);

                int atMinimum = values.Count(v => v == 950);
                int atMaximum = values.Count(v => v == 3000);
                Check("a strong setting does not pile the values up at the ends",
                    atMinimum < values.Count / 20 && atMaximum < values.Count / 20,
                    $"{atMinimum} at the low end, {atMaximum} at the high end, out of {values.Count}");

                var quarters = new int[4];
                foreach (int value in values)
                {
                    int quarter = (value - 950) * 4 / (3000 - 950);
                    quarters[Math.Max(0, Math.Min(3, quarter))]++;
                }
                Check("the values are spread over the whole range",
                    quarters.All(count => count > values.Count / 20),
                    string.Join(", ", quarters));

                var reaction = Sample(strong.LootReactionMs, 8000);
                int reactionAtMaximum = reaction.Count(v => v == 1120);
                Check("a strong setting cannot make the click later than the old worst",
                    reaction.Max() <= 1124,
                    $"worst {reaction.Max()}ms");

                var normal = new HumanTiming(new Random(10), 1.0, true);
                var normalValues = Sample(normal.PreCastDelayMs, 8000);
                Check("the normal setting keeps the whole range",
                    normalValues.Min() >= 950 && normalValues.Max() <= 3000,
                    $"[{normalValues.Min()}, {normalValues.Max()}]");
            }

            Console.WriteLine();
            Console.WriteLine("=== switching it off gives back the old behaviour ===");
            {
                var timing = new HumanTiming(new Random(4), 1.0, false);

                Check("there is no pause before casting when it is off",
                    Sample(timing.PreCastDelayMs, 500).All(v => v == 0));

                var reaction = Sample(timing.LootReactionMs, 8000);
                Check("the reaction time is the old 900 to 1125ms",
                    reaction.All(v => v >= 900 && v <= 1124),
                    $"[{reaction.Min()}, {reaction.Max()}]");
                Check("the old reaction time is still spread out",
                    reaction.Distinct().Count() > 50,
                    $"{reaction.Distinct().Count()} different values");

                var hold = Sample(timing.KeyHoldMs, 8000);
                Check("the key hold is the old 50 to 125ms",
                    hold.All(v => v >= 50 && v <= 124),
                    $"[{hold.Min()}, {hold.Max()}]");
            }

            Console.WriteLine();
            Console.WriteLine("=== a bad strength is handled ===");
            {
                Check("a strength that is not a number keeps the default",
                    new HumanTiming(new Random(5), double.NaN, true).Jitter == 1.0);
                Check("a strength that is infinite keeps the default",
                    new HumanTiming(new Random(5), double.PositiveInfinity, true).Jitter == 1.0);
                Check("a strength that is too big is brought back",
                    new HumanTiming(new Random(5), 99.0, true).Jitter == 2.0);
                Check("a strength that is too small is brought back",
                    new HumanTiming(new Random(5), -5.0, true).Jitter == 0.0);
            }

            Console.WriteLine();
            Console.WriteLine("=== the same seed gives the same sequence ===");
            {
                var first = new HumanTiming(new Random(7), 1.0, true);
                var second = new HumanTiming(new Random(7), 1.0, true);

                var firstValues = Enumerable.Range(0, 50).Select(_ => first.LootReactionMs()).ToList();
                var secondValues = Enumerable.Range(0, 50).Select(_ => second.LootReactionMs()).ToList();

                Check("the same seed gives the same timings", firstValues.SequenceEqual(secondValues));

                var other = new HumanTiming(new Random(8), 1.0, true);
                var otherValues = Enumerable.Range(0, 50).Select(_ => other.LootReactionMs()).ToList();
                Check("a different seed gives a different sequence", !firstValues.SequenceEqual(otherValues));
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILURE(S)");
            return failures == 0 ? 0 : 1;
        }

        private static List<int> Sample(Func<int> method, int count)
        {
            var values = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                values.Add(method());
            }
            return values;
        }

        private static double Median(List<int> values)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            int middle = sorted.Length / 2;
            return sorted.Length % 2 == 0
                ? (sorted[middle - 1] + sorted[middle]) / 2.0
                : sorted[middle];
        }
    }
}
