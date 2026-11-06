using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace FishingFun
{
    public static class Lure
    {
        public static ILog logger = LogManager.GetLogger("Fishbot");

        public static void applyLure(ConsoleKey rodKey, ConsoleKey lureKey)
        {
            logger.Info($"Applying lure with key {lureKey} on rod key {rodKey}.");
            WowProcess.PressKey(lureKey);
            Thread.Sleep(250);
            WowProcess.PressKey(rodKey);
            // Wait for the lure to be applied
            // We also need longer sleep for re-applying to be not too soon
            Thread.Sleep(15000);
        }

    }
}
