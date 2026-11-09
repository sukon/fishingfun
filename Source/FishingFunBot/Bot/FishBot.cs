using log4net;
using log4net.Appender;
using log4net.Repository.Hierarchy;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace FishingFun
{
    public class FishingBot
    {
        public static ILog logger = LogManager.GetLogger("Fishbot");

        private ConsoleKey castKey;
        private ConsoleKey rodKey;
        private ConsoleKey lureKey;
        private ConsoleKey hsKey;
        private List<ConsoleKey> tenMinKey;
        private IBobberFinder bobberFinder;
        private IBiteWatcher biteWatcher;
        // The stop button is on another thread, so this has to be volatile for the
        // bot thread to see the change.
        private volatile bool isEnabled;

        // Set when the bot is asked to stop. This is separate from isEnabled because
        // the bot has not started yet before Start is called, and the key press
        // before that must still go through.
        private volatile bool stopRequested;
        private int maxFinishingMinutes = 45;
        private int lureTimer = 10;
        private Stopwatch totalTimeStopWatch = new Stopwatch();
        private Stopwatch stopwatch = new Stopwatch();
        private Stopwatch lureStopwatch = new Stopwatch();

        public ConsoleKey RodKey { get => rodKey; set => rodKey = value; }
        public ConsoleKey LureKey { get => lureKey; set => lureKey = value; }

        private static Random random = new Random();

        public event EventHandler<FishingEvent> FishingEventHandler;

        [DllImport("Powrprof.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        public static extern bool SetSuspendState(bool hiberate, bool forceCritical, bool disableWakeEvent);


        public FishingBot(IBobberFinder bobberFinder, IBiteWatcher biteWatcher, ConsoleKey castKey, List<ConsoleKey> tenMinKey)
        {
            this.bobberFinder = bobberFinder;
            this.biteWatcher = biteWatcher;
            this.castKey = castKey;
            this.tenMinKey = tenMinKey;

            this.rodKey = ConsoleKey.D2;
            this.lureKey = ConsoleKey.D3;
            this.hsKey = ConsoleKey.D9;

            // The input the bot sends to the game is checked against this, so that
            // an action which was queued up before the bot was stopped does not go
            // through afterwards.
            WowProcess.SetCancellationCheck(() => stopRequested);

            logger.Info("FishBot Created.");

            FishingEventHandler += (s, e) => { };
        }

        public void Start()
        {
            biteWatcher.FishingEventHandler = (e) => FishingEventHandler?.Invoke(this, e);

            stopRequested = false;
            isEnabled = true;
            WowProcess.ResumeSendingInput();
     
            // Enable total time stopwatch
            totalTimeStopWatch.Start();

            // Equip the rod
            WowProcess.PressKey(rodKey);

            // Enable lure stopwatch
            lureStopwatch.Start();
            Lure.applyLure(rodKey, lureKey);

            while (isEnabled)
            {
                try
                {                    
                    checkLureTimer();

                    // A person does not cast the instant the last fish is in the bag,
                    // and the game needs a moment to finish the last cast.
                    int preCastDelay = HumanTiming.Current.PreCastDelayMs();
                    if (preCastDelay > 0)
                    {
                        logger.Info($"Waiting {preCastDelay}ms before casting.");
                    }
                    WaitWhileEnabled(preCastDelay);

                    logger.Info($"Pressing key {castKey} to Cast.");

                    FishingEventHandler?.Invoke(this, new FishingEvent { Action = FishingAction.Cast });
                    WowProcess.PressKey(castKey);

                    Watch(2000);

                    WaitForBite();
                    checkForStopTimer();
                }
                catch (Exception e)
                {
                    logger.Error(e.ToString());
                    Thread.Sleep(2000);
                }
            }

            logger.Error("Bot has Stopped.");
        }


        public void checkForStopTimer()
        {
            TimeSpan ts = totalTimeStopWatch.Elapsed;
            int elapsedMinutes = (int)ts.TotalMinutes;

            logger.Info($"Elapsed {elapsedMinutes} out of {maxFinishingMinutes}mins.");

            if (elapsedMinutes > maxFinishingMinutes)
            {                               
                Stop();
                WowProcess.PressKey(hsKey);
                Thread.Sleep(20000);
                // Sleep computer
                SetSuspendState(false, true, true);

            }


        }

        public void checkLureTimer()
        {            

            TimeSpan ts = lureStopwatch.Elapsed;
            int elapsedMinutes = ts.Minutes;
            int elapsedSeconds = ts.Seconds;
            
            logger.Info($"Checking the lure timer.");

            if ((elapsedMinutes >= lureTimer && elapsedSeconds > 30) || elapsedMinutes > lureTimer)
            {
                Lure.applyLure(rodKey, lureKey);
                lureStopwatch.Restart();
            } else
            {
                logger.Info($"Lure still active for {lureTimer - elapsedMinutes} min");
            }


        }

        public void SetCastKey(ConsoleKey castKey)
        {
            this.castKey = castKey;
        }

        private void Watch(int milliseconds)
        {
            bobberFinder.Reset();
            stopwatch.Restart();
            while (stopwatch.ElapsedMilliseconds < milliseconds)
            {
                bobberFinder.Find();
            }
            stopwatch.Stop();
        }

        public void Stop()
        {
            stopRequested = true;
            isEnabled = false;

            // Nothing more is sent to the game once this returns. Anything that was
            // being sent is finished first, so that a button or a key is never left
            // held down.
            WowProcess.StopSendingInput();

            totalTimeStopWatch.Reset();
            logger.Error("Bot is Stopping...");
        }

        private void WaitForBite()
        {
            bobberFinder.Reset();

            var timedTask = new TimedAction((a) => { logger.Info("Fishing timed out!"); }, 25 * 1000, 25);
            var observation = FindBobber();
            while (isEnabled && !observation.IsVisible)
            {
                if (!timedTask.ExecuteIfDue()) return;
                observation = FindBobber();
            }
            if (!isEnabled) return;

            this.biteWatcher.Reset(observation);
            using var trace = BobberTraceRecorder.TryCreate(observation.TimestampMilliseconds);
            trace?.Record(observation, "reset");

            logger.Info("Bobber start position: " + observation.Position);

            // Wait for the bobber to move
            while (isEnabled)
            {
                observation = FindBobber();
                bool isBite = this.biteWatcher.IsBite(observation);
                trace?.Record(observation, isBite ? "bite" : "watch");

                if (isBite)
                {
                    Loot(observation.Position);
                    PressTenMinKey();
                    return;
                }
                if (!observation.IsVisible)
                {
                    // The float could not be seen in this frame. That happens while the
                    // water is in the way of it. It says nothing about a bite, so the
                    // bot keeps looking instead of giving up.
                    logger.Info("Float not visible in this frame, still looking.");
                }

                if (!timedTask.ExecuteIfDue()) { return; }
            }
        }

        private DateTime StartTime = DateTime.Now;


        private void PressTenMinKey()
        {
            if ((DateTime.Now - StartTime).TotalMinutes > 10 && tenMinKey.Count > 0)
            {
                StartTime = DateTime.Now;
                logger.Info($"Pressing key {tenMinKey} to run a macro.");

                FishingEventHandler?.Invoke(this, new FishingEvent { Action = FishingAction.Cast });

                foreach (var key in tenMinKey)
                {
                    WowProcess.PressKey(key);
                }
            }
        }

        private void Loot(Point bobberPosition)
        {
            // The pause between noticing the fish and moving the hand onto the float.
            // It is part of the time the fish is on the line, so it is kept short and
            // it stops straight away when the bot is stopped.
            //
            // The time taken to look the float up again comes out of this pause, so
            // that the click is never later than it used to be. That lookup is the
            // only thing here that can take a noticeable amount of time.
            var reactionWatch = Stopwatch.StartNew();

            var fresh = bobberFinder.Find();
            Point? freshPosition = fresh.IsVisible ? fresh.Position : (Point?)null;
            Point chosen = LootPosition.Choose(bobberPosition, freshPosition);
            if (chosen == bobberPosition && freshPosition.HasValue && chosen != freshPosition.Value)
            {
                // The search found something else. Clicking where the float was is
                // safer than clicking whatever was found.
                logger.Info("Something else was found instead of the float, clicking where the float was.");
            }
            else if (!freshPosition.HasValue)
            {
                logger.Info("The float could not be seen again, clicking where it was last seen.");
            }
            bobberPosition = chosen;

            int reaction = HumanTiming.Current.LootReactionMs();
            int remaining = reaction - (int)reactionWatch.ElapsedMilliseconds;
            if (remaining < 0) remaining = 0;

            if (remaining == 0 && reactionWatch.ElapsedMilliseconds > reaction)
            {
                // The lookup took longer than the whole reaction time, so the click
                // is later than the budget. There is nothing to be done about it,
                // but it is worth saying so.
                logger.Info($"Looking the float up again took {reactionWatch.ElapsedMilliseconds}ms, which is longer than the reaction time of {reaction}ms.");
            }

            if (!WaitWhileEnabled(remaining))
            {
                logger.Info("Bot was stopped before the float was clicked.");
                return;
            }

            logger.Info($"Reacted in {reaction}ms.");
            logger.Info($"Right clicking mouse to Loot at {bobberPosition}.");
            WowProcess.RightClickMouse(logger, bobberPosition);
            Sleep(1000 + random.Next(0, 125));
        }

        /// <summary>
        /// Waits for the given time, but stops straight away when the bot is stopped.
        /// 
        /// Returns true when the whole time was waited out and false when the bot was
        /// stopped part way through.
        /// </summary>
        private bool WaitWhileEnabled(int milliseconds)
        {
            return BotWait.Wait(milliseconds, () => isEnabled, FlushBuffers);
        }

        public static void Sleep(int ms)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            while (sw.Elapsed.TotalMilliseconds < ms)
            {
                FlushBuffers();
                //System.Windows.Application.Current?.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new ThreadStart(delegate { }));
                Thread.Sleep(100);
            }
        }

        public static void FlushBuffers()
        {
            ILog log = LogManager.GetLogger("Fishbot");
            var logger = log.Logger as Logger;
            if (logger != null)
            {
                foreach (IAppender appender in logger.Appenders)
                {
                    var buffered = appender as BufferingAppenderSkeleton;
                    if (buffered != null)
                    {
                        buffered.Flush();
                    }
                }
            }
        }

        /// <summary>
        /// Looks for the float once and gives up straight away if it cannot be seen.
        /// 
        /// The earlier version kept trying for five seconds. That meant the bot was
        /// blind for up to five seconds at a time, and a fish biting during that time
        /// was missed entirely. The caller decides whether to try again.
        /// </summary>
        private BobberObservation FindBobber()
        {
            return this.bobberFinder.Find();
        }
    }
}
