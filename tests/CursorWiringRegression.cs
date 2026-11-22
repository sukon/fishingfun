using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

#nullable enable
namespace FishingFun
{
    /// <summary>
    /// Checks that the bot is wired to the cursor.
    ///
    /// The bot puts the cursor on the float when the float is first seen and gives
    /// it back when it stops. Those two calls are in the bot, not in the code the
    /// other cursor tests cover, so without this they could be removed and every
    /// suite would still be green.
    ///
    /// The wait before the click is checked in the source, because it only happens
    /// when a game is running and there is no game here to measure it in.
    /// </summary>
    public static class CursorWiringRegression
    {
        private static int failures;
        private static int checks;

        private static void Check(string what, bool passed, string detail = "")
        {
            checks++;
            Console.WriteLine((passed ? "PASS " : "FAIL ") + what + (detail.Length > 0 ? "  (" + detail + ")" : ""));
            if (!passed) failures++;
        }

        public static int Main(string[] args)
        {
            Console.WriteLine("=== the bot puts the cursor on the float and gives it back ===");
            CheckTheBotTakesTheCursorOver();

            Console.WriteLine();
            Console.WriteLine("=== the wait before the click is the random one ===");
            CheckTheClickWaitIsRandom(args);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILURE(S)");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Runs the bot until it has seen the float, and checks that it took the
        /// cursor over then and gave it back when it was stopped.
        ///
        /// The bot applies the lure before it looks for the float, and that takes
        /// fifteen seconds, so this is the slow test of the suite. The timings are
        /// switched off, so that the bot does not also wait before it casts.
        /// </summary>
        private static void CheckTheBotTakesTheCursorOver()
        {
            HumanTiming.Configure(false, 1.0, 1);

            // The cursor is put back where it was at the end, so that the test
            // cannot leave the mouse somewhere else.
            var original = System.Windows.Forms.Cursor.Position;
            var floatPosition = original == Point.Empty ? new Point(1000, 500) : original;

            var bot = new FishingBot(new FakeBobberFinder(floatPosition), new FakeBiteWatcher(),
                ConsoleKey.F1, new List<ConsoleKey>());

            var botThread = new Thread(() => bot.Start()) { IsBackground = true };
            botThread.Start();

            try
            {
                Check("the user is not holding a mouse button",
                    !WowProcess.IsMouseButtonHeldDownByUser());

                // The bot only takes the cursor over once it has seen the float.
                bool takenOver = WaitFor(() => WowProcess.RememberedUserCursorPosition.HasValue, 60000);
                Check("the bot takes the cursor over when the float is seen", takenOver);

                Check("the user is not holding a mouse button",
                    !WowProcess.IsMouseButtonHeldDownByUser());

                bot.Stop();

                // The place the cursor was left at is only ever cleared by the
                // restore, so this says that the bot called it.
                Check("the bot gives the cursor back when it stops",
                    !WowProcess.RememberedUserCursorPosition.HasValue);
            }
            finally
            {
                // The bot is stopped and its thread is joined, so that nothing is
                // still running when the test ends.
                bot.Stop();
                botThread.Join(30000);

                System.Windows.Forms.Cursor.Position = original;
            }
        }

        /// <summary>
        /// Waits for the given thing to happen, and says whether it happened
        /// before the time was up.
        /// </summary>
        private static bool WaitFor(Func<bool> condition, int timeoutMilliseconds)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
            {
                if (condition()) return true;
                Thread.Sleep(25);
            }

            return condition();
        }

        /// <summary>
        /// Checks that the wait before the click is the random one, so that the
        /// click does not always land at the same moment after the fish bites.
        ///
        /// The wait is only made when a game is running, so there is nothing to
        /// measure here. The call is looked for in the source instead, and the
        /// comments are left out so that a call in a comment cannot pass for one.
        /// </summary>
        private static void CheckTheClickWaitIsRandom(string[] args)
        {
            string? root = args.Length > 0 ? args[0] : null;
            if (string.IsNullOrEmpty(root))
            {
                Check("the repository root is given to the test", false);
                return;
            }

            string path = Path.Combine(root!, "Source", "FishingFunBot", "Platform", "WowProcess.cs");
            if (!File.Exists(path))
            {
                Check("the file that sends the click is there", false, path);
                return;
            }

            string code = CodeWithoutComments(File.ReadAllLines(path));

            Check("the wait before the click is the random one",
                Regex.IsMatch(code, @"HumanTiming\s*\.\s*Current\s*\.\s*LootDelayMs\s*\(\s*LootDelay\s*\)"));

            Check("the wait before the click is not a fixed time",
                !Regex.IsMatch(code, @"Thread\s*\.\s*Sleep\s*\(\s*(WowProcess\s*\.\s*)?LootDelay\s*\)"));
        }

        /// <summary>
        /// The source with the comments taken out, so that a call that is only
        /// mentioned in a comment is not counted as one.
        /// </summary>
        private static string CodeWithoutComments(IEnumerable<string> lines)
        {
            var code = new StringBuilder();
            bool insideBlockComment = false;

            foreach (string line in lines)
            {
                string trimmed = line.Trim();

                if (insideBlockComment)
                {
                    int end = trimmed.IndexOf("*/", StringComparison.Ordinal);
                    if (end < 0) continue;
                    trimmed = trimmed.Substring(end + 2);
                    insideBlockComment = false;
                }

                if (trimmed.StartsWith("/*", StringComparison.Ordinal))
                {
                    int end = trimmed.IndexOf("*/", 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        insideBlockComment = true;
                        continue;
                    }
                    trimmed = trimmed.Substring(end + 2);
                }

                int comment = trimmed.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0) trimmed = trimmed.Substring(0, comment);

                code.AppendLine(trimmed);
            }

            return code.ToString();
        }

        /// <summary>
        /// A finder that always sees the float in the same place, so that the bot
        /// puts the cursor on it as soon as it looks.
        /// </summary>
        private sealed class FakeBobberFinder : IBobberFinder
        {
            private readonly Point floatPosition;

            public FakeBobberFinder(Point floatPosition)
            {
                this.floatPosition = floatPosition;
            }

            public BobberObservation Find()
            {
                // The real finder takes a few milliseconds to look at the screen,
                // and the bot looks at it again straight away. The same is done
                // here, so that the test does not keep a core busy.
                Thread.Sleep(5);

                return new BobberObservation(floatPosition, new Size(12, 12), 100, BobberObservation.NowMilliseconds);
            }

            public void Reset()
            {
            }
        }

        /// <summary>
        /// A watcher that never sees a bite, so that the bot keeps looking for the
        /// float until the test stops it.
        /// </summary>
        private sealed class FakeBiteWatcher : IBiteWatcher
        {
            public Action<FishingEvent> FishingEventHandler { get; set; } = (e) => { };

            public void Reset(BobberObservation initialObservation)
            {
            }

            public bool IsBite(BobberObservation observation)
            {
                return false;
            }
        }
    }
}
