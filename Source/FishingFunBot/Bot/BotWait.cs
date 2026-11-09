using System;
using System.Diagnostics;
using System.Threading;

#nullable enable
namespace FishingFun
{
    /// <summary>
    /// Waits for a time, but stops straight away when the bot is stopped.
    /// 
    /// The waits the bot does between looking at the screen and acting on it are part
    /// of how long it takes to click the float. A wait that cannot be cut short
    /// makes the stop button slow to work, which is not what the user expects.
    /// </summary>
    public static class BotWait
    {
        /// <summary>
        /// Waits for the given number of milliseconds.
        /// 
        /// Returns true when the whole time was waited out, and false when the wait
        /// was cut short because the bot was stopped.
        /// 
        /// The wait is done in short slices and only the time that is left is slept
        /// each time, so that stopping the bot is noticed quickly.
        /// </summary>
        public static bool Wait(int milliseconds, Func<bool>? stillEnabled, Action? onTick = null)
        {
            if (milliseconds <= 0) return true;
            if (stillEnabled == null) return true;

            var stopwatch = Stopwatch.StartNew();
            while (stillEnabled() && stopwatch.ElapsedMilliseconds < milliseconds)
            {
                onTick?.Invoke();

                int remaining = milliseconds - (int)stopwatch.ElapsedMilliseconds;
                Thread.Sleep(Math.Max(1, Math.Min(25, remaining)));
            }

            return stopwatch.ElapsedMilliseconds >= milliseconds;
        }
    }
}
