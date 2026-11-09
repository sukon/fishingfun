using FishingFun;
using log4net;
using System;
using System.Collections.Generic;
using System.IO;

namespace Powershell
{
    class Program
    {
        private static void Main(string[] args)
        {
            LoggingSetup.Configure();

            var log = LogManager.GetLogger("Fishbot");

            // The same settings the window version uses, so that the two fish the same
            // way. The window version writes them when they are changed.
            var settings = FishingFunSettings.Load();

            log.Info("Settings: " + settings.Describe());

            int strikeValue = 5;

            var pixelClassifier = new PixelClassifier();
            pixelClassifier.SetConfiguration(WowProcess.IsWowClassic());
            settings.ApplyTo(pixelClassifier);

            log.Info($"Colour rule in use: multiplier {pixelClassifier.ColourMultiplier}, " +
                     $"closeness multiplier {pixelClassifier.ColourClosenessMultiplier}");

            var bobberFinder = new SearchBobberFinder(pixelClassifier);
            var biteWatcher = new PositionBiteWatcher(strikeValue);

            var bot = new FishingBot(bobberFinder, biteWatcher, settings.CastKey, settings.TenMinuteKeys);
            bot.FishingEventHandler += (b, e) => log.Info(e);

            WowProcess.PressKey(ConsoleKey.Spacebar);
            System.Threading.Thread.Sleep(1500);

            bot.Start();
        }
    }
}
