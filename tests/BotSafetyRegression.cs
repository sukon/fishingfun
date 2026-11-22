using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

#nullable enable
namespace FishingFun
{
    /// <summary>
    /// Checks the safety behaviour of the bot: that stopping it works while it is
    /// waiting, that it does not act after it has been stopped, that the mouse
    /// button is always let go, and that it clicks the right place when the float
    /// cannot be seen.
    /// </summary>
    public static class BotSafetyRegression
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
            Console.WriteLine("=== stopping the bot while it is waiting ===");
            {
                var stopwatch = Stopwatch.StartNew();
                bool finished = BotWait.Wait(30000, () => false);
                stopwatch.Stop();

                Check("a wait is cut short when the bot is stopped", !finished);
                Check("the wait stops at once", stopwatch.ElapsedMilliseconds < 500,
                    $"took {stopwatch.ElapsedMilliseconds}ms");
            }

            {
                bool stopped = false;
                new Thread(() => { Thread.Sleep(150); stopped = true; }) { IsBackground = true }.Start();

                var stopwatch = Stopwatch.StartNew();
                bool finished = BotWait.Wait(30000, () => !stopped);
                stopwatch.Stop();

                Check("a wait stops when the bot is stopped part way through", !finished);
                Check("the wait stops at once when it is stopped", stopwatch.ElapsedMilliseconds < 2000,
                    $"took {stopwatch.ElapsedMilliseconds}ms");
            }

            {
                var stopwatch = Stopwatch.StartNew();
                bool finished = BotWait.Wait(120, () => true);
                stopwatch.Stop();

                Check("a wait runs to the end when the bot is not stopped", finished);
                Check("the wait takes about the time it was given",
                    stopwatch.ElapsedMilliseconds >= 100 && stopwatch.ElapsedMilliseconds < 1000,
                    $"took {stopwatch.ElapsedMilliseconds}ms");
            }

            {
                int ticks = 0;
                BotWait.Wait(120, () => true, () => ticks++);
                Check("the wait keeps the screen up to date while it waits", ticks >= 3, $"{ticks} ticks");
            }

            Console.WriteLine();
            Console.WriteLine("=== nothing is sent to the game after the bot is stopped ===");
            {
                WowProcess.SetCancellationCheck(() => true);
                Check("a stopped bot does not act", !WowProcess.RunUnlessCancelled(() => { throw new InvalidOperationException("should not run"); }));

                bool ran = false;
                WowProcess.RunUnlessCancelled(() => ran = true);
                Check("the action really was not run", !ran);

                WowProcess.SetCancellationCheck(() => false);
                WowProcess.RunUnlessCancelled(() => ran = true);
                Check("a running bot does act", ran);

                WowProcess.SetCancellationCheck(null);
                WowProcess.RunUnlessCancelled(() => ran = true);
                Check("a bot with nothing asking it does act", ran);
            }

            Console.WriteLine();
            Console.WriteLine("=== the mouse button is always let go ===");
            {
                bool released = false;
                try
                {
                    WowProcess.ClickWithGuaranteedRelease(
                        () => throw new InvalidOperationException("something went wrong"),
                        () => released = true,
                        () => { });
                }
                catch (InvalidOperationException)
                {
                }
                Check("the button is released when something goes wrong", released);

                bool releasedAgain = false;
                bool held = false;
                try
                {
                    WowProcess.ClickWithGuaranteedRelease(
                        () => held = true,
                        () => releasedAgain = true,
                        () => throw new InvalidOperationException("something went wrong while held"));
                }
                catch (InvalidOperationException)
                {
                }
                Check("the button is released when holding it goes wrong", releasedAgain);
                Check("the button really was held down first", held);

                bool attempted = false;
                try
                {
                    WowProcess.ClickWithGuaranteedRelease(
                        () => { },
                        () => { attempted = true; throw new InvalidOperationException("the release itself failed"); },
                        () => { });
                }
                catch (InvalidOperationException)
                {
                }
                Check("the release is attempted even when the release fails", attempted);
            }

            Console.WriteLine();
            Console.WriteLine("=== nothing is sent once the stop has finished ===");
            {
                WowProcess.ResumeSendingInput();
                WowProcess.SetCancellationCheck(null);

                bool started = false;
                var release = new ManualResetEvent(false);
                var worker = new Thread(() =>
                {
                    WowProcess.RunUnlessCancelled(() =>
                    {
                        started = true;
                        release.WaitOne(5000);
                    });
                });
                worker.IsBackground = true;
                worker.Start();

                Thread.Sleep(150);
                Check("the action is being sent", started);

                // This waits for the action that is being sent to finish.
                WowProcess.StopSendingInput();
                release.Set();
                worker.Join(5000);

                bool ranAfter = false;
                WowProcess.RunUnlessCancelled(() => ranAfter = true);
                Check("nothing is sent after the stop has finished", !ranAfter);

                bool ranWhileStopped = false;
                WowProcess.RunUnlessCancelled(() => ranWhileStopped = true);
                Check("nothing is sent while the bot is stopped", !ranWhileStopped);

                WowProcess.ResumeSendingInput();
                bool ranAfterResume = false;
                WowProcess.RunUnlessCancelled(() => ranAfterResume = true);
                Check("input works again once the bot is started", ranAfterResume);
            }

            Console.WriteLine();
            Console.WriteLine("=== the right place is clicked when the float cannot be seen ===");
            {
                var lastKnown = new Point(1000, 500);

                Check("the place the float is now is used when it is close by",
                    LootPosition.Choose(lastKnown, new Point(1010, 505)) == new Point(1010, 505));

                Check("the place the float was last seen is used when it cannot be seen",
                    LootPosition.Choose(lastKnown, null) == lastKnown);

                Check("the place the float was last seen is used when something else was found",
                    LootPosition.Choose(lastKnown, new Point(1400, 900)) == lastKnown);

                Check("a float that is just inside the distance is used",
                    LootPosition.Choose(lastKnown, new Point(1000 + LootPosition.MaximumDistance, 500)) == new Point(1000 + LootPosition.MaximumDistance, 500));

                Check("a float just outside the distance is not used",
                    LootPosition.Choose(lastKnown, new Point(1000 + LootPosition.MaximumDistance + 1, 500)) == lastKnown);
            }

            Console.WriteLine();
            Console.WriteLine("=== the cursor is only moved when the user is not holding a button ===");
            {
                Check("the user is not holding a mouse button",
                    !WowProcess.IsMouseButtonHeldDownByUser());

                // Nothing has been parked on the float, so there is nothing to give
                // back. This must not move the cursor. The live cursor position is
                // deliberately not read twice here, because anything that moves the
                // mouse between the two reads makes the check fail at random. The
                // place the user left the cursor says whether anything was taken
                // over, and that does not depend on where the mouse happens to be.
                WowProcess.RestoreCursor();
                Check("the cursor is left alone when there is nothing to give back",
                    !WowProcess.RememberedUserCursorPosition.HasValue);
            }

            Console.WriteLine();
            Console.WriteLine("=== a stopped bot does not move the cursor ===");
            {
                // The bot has been asked to stop, so it must not touch the cursor.
                WowProcess.SetCancellationCheck(() => true);
                WowProcess.MoveCursorOntoFloat(new Point(60, 80));
                Check("the bot is stopped",
                    !WowProcess.RunUnlessCancelled(() => { }));
                Check("a stopped bot does not take the cursor over",
                    !WowProcess.RememberedUserCursorPosition.HasValue);

                WowProcess.SetCancellationCheck(null);
            }

            Console.WriteLine();
            Console.WriteLine("=== the cursor is put back where the user left it ===");
            {
                // The restore moves the real mouse, so the place the user left the
                // cursor at is taken first and put back in the finally block below.
                var original = System.Windows.Forms.Cursor.Position;
                try
                {
                    Check("the user is not holding a mouse button",
                        !WowProcess.IsMouseButtonHeldDownByUser());

                    // The bot takes the cursor over, so the place the user left it
                    // at is remembered.
                    WowProcess.MoveCursorOntoFloat(new Point(60, 80));
                    var remembered = WowProcess.RememberedUserCursorPosition;
                    Check("the place the user left the cursor is remembered",
                        remembered.HasValue, $"the cursor was at {remembered}");

                    // The cursor is moved somewhere else, so that the restore has
                    // something to undo.
                    var away = SomewhereElse(original);
                    Check("the cursor really was moved away", away != original);

                    WowProcess.RestoreCursor();

                    // The cursor is put back where the user left it. The place the
                    // bot left the cursor at is read as well: a cursor that is
                    // still there means the restore did not do anything. Anything
                    // that moves the mouse while this test runs can move it again
                    // in the instant after the restore, so a cursor that is
                    // somewhere else altogether is not counted against it. The
                    // place that is remembered says whether the restore really
                    // happened, because only a successful move clears it.
                    var restored = System.Windows.Forms.Cursor.Position;
                    Check("the cursor is not left where the bot put it",
                        restored == remembered || restored != away,
                        $"the user left it at {remembered}, the bot put it at {away}, it is at {restored}");

                    Check("the remembered place is cleared once the cursor is back",
                        !WowProcess.RememberedUserCursorPosition.HasValue);
                }
                finally
                {
                    // The cursor is put back where it was, whatever happened above,
                    // so that a failure cannot leave it somewhere else.
                    System.Windows.Forms.Cursor.Position = original;
                }
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILURE(S)");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// A place for the cursor that is not the place it is at now, so that the
        /// restore has something to undo. The places near the current one are
        /// tried first, so the mouse does not travel far.
        ///
        /// The place that is returned is the one that was asked for, not the one
        /// that is read back, so that a mouse which is being moved by something
        /// else cannot change what the restore is compared against.
        /// </summary>
        private static Point SomewhereElse(Point original)
        {
            var candidates = new[]
            {
                new Point(original.X + 40, original.Y),
                new Point(original.X, original.Y + 40),
                new Point(original.X - 40, original.Y),
                new Point(original.X, original.Y - 40),
                new Point(original.X + 100, original.Y + 100),
                new Point(original.X - 100, original.Y - 100)
            };

            foreach (var candidate in candidates)
            {
                System.Windows.Forms.Cursor.Position = candidate;
                if (System.Windows.Forms.Cursor.Position != original) return candidate;
            }

            return original;
        }
    }
}
