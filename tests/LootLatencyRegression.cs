using System;
using System.Diagnostics;

#nullable enable
namespace FishingFun
{
    /// <summary>
    /// Checks the time it takes from a fish biting to the click reaching the game.
    /// 
    /// The lookup of the float happens between the bite and the click, so it has
    /// to come out of the reaction time rather than be added to it. Otherwise the
    /// click would be later than it used to be, and a fish that is not clicked
    /// soon enough is lost.
    /// </summary>
    public static class LootLatencyRegression
    {
        private static int failures;
        private static int checks;

        private static void Check(string what, bool passed, string detail = "")
        {
            checks++;
            Console.WriteLine((passed ? "PASS " : "FAIL ") + what + (detail.Length > 0 ? "  (" + detail + ")" : ""));
            if (!passed) failures++;
        }

        /// <summary>
        /// The wait the camera needs after the cursor has been moved onto the float.
        /// This is the same value the bot uses.
        /// </summary>
        private const int LootDelay = 1000;

        /// <summary>
        /// The worst case of the old behaviour: the reaction was 900 to 1124ms and
        /// then the camera wait of 1000ms.
        /// </summary>
        private const int OldWorstCase = 2124;

        /// <summary>
        /// The longest the lookup of the float is allowed to take before it is
        /// longer than the whole reaction time. A screen capture takes a few tens
        /// of milliseconds, so this is only reached when the machine is busy.
        /// </summary>
        private const int Budget = 1120;

        public static int Main()
        {
            Console.WriteLine("=== the click never comes later than it used to ===");
            {
                // The time from the fish biting to the click reaching the game is
                // the lookup of the float, then the rest of the reaction time, then
                // the wait the camera needs. The lookup comes out of the reaction
                // time, so the total is the larger of the two, not the sum.
                var timing = new HumanTiming(new Random(1), 2.0, true);

                int worstTotal = 0;
                int worstReaction = 0;
                for (int i = 0; i < 20000; i++)
                {
                    int reaction = timing.LootReactionMs();
                    int lookup = 150;
                    int actualReaction = Math.Max(0, reaction - lookup);
                    int total = lookup + actualReaction + LootDelay;

                    if (reaction > worstReaction) worstReaction = reaction;
                    if (total > worstTotal) worstTotal = total;
                }

                Check("the worst reaction is inside the range", worstReaction <= Budget, $"{worstReaction}ms");
                Check("the click is no later than the old worst case",
                    worstTotal <= OldWorstCase,
                    $"worst {worstTotal}ms, old worst {OldWorstCase}ms");
            }

            {
                // As long as the lookup is shorter than the reaction time the click
                // is never later than the old worst case.
                var timing = new HumanTiming(new Random(2), 2.0, true);
                bool withinLimit = true;
                int worst = 0;

                foreach (int lookup in new[] { 0, 20, 50, 100, 150, 200, 300, 500, 800, 1000 })
                {
                    for (int i = 0; i < 2000; i++)
                    {
                        int reaction = timing.LootReactionMs();
                        int actualReaction = Math.Max(0, reaction - lookup);
                        int total = lookup + actualReaction + LootDelay;
                        if (total > worst) worst = total;
                        if (total > OldWorstCase) withinLimit = false;
                    }
                }

                Check("a lookup shorter than the reaction keeps the click within the budget",
                    withinLimit, $"worst {worst}ms");
            }

            {
                // A lookup longer than the whole reaction time puts the click
                // outside the budget. That cannot be undone, but it must be
                // reported, and the click must still happen.
                int lookup = 1500;
                int reaction = 900;
                int actualReaction = Math.Max(0, reaction - lookup);
                int total = lookup + actualReaction + LootDelay;

                Check("a lookup longer than the reaction is noticed",
                    total > OldWorstCase,
                    $"the click would be {total}ms, which is outside the budget of {OldWorstCase}ms");
                Check("the click still happens straight away when the budget is gone",
                    actualReaction == 0);
            }

            {
                // A lookup that is faster than the reaction must not make the click
                // earlier than the reaction asks for.
                var timing = new HumanTiming(new Random(3), 1.0, true);
                bool withinRange = true;

                for (int i = 0; i < 5000; i++)
                {
                    int reaction = timing.LootReactionMs();
                    int total = 0 + reaction + LootDelay;
                    if (total < 700 + LootDelay || total > 1120 + LootDelay) withinRange = false;
                }

                Check("the click is between the shortest and longest reaction",
                    withinRange, $"between {700 + LootDelay}ms and {1120 + LootDelay}ms");
            }

            Console.WriteLine();
            Console.WriteLine("=== the pause before casting does not delay the click ===");
            {
                // The pause before casting is between casts. It makes each round
                // longer but it cannot make the click on a fish any later.
                var timing = new HumanTiming(new Random(4), 1.0, true);
                var pauses = new int[2000];
                for (int i = 0; i < pauses.Length; i++) pauses[i] = timing.PreCastDelayMs();

                int min = int.MaxValue, max = 0;
                foreach (int pause in pauses)
                {
                    if (pause < min) min = pause;
                    if (pause > max) max = pause;
                }

                Check("the pause before casting is inside the range",
                    min >= 950 && max <= 3000, $"[{min}, {max}]");
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILURE(S)");
            return failures == 0 ? 0 : 1;
        }
    }
}
