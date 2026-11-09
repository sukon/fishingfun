using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

#nullable enable
namespace FishingFun
{
    /// <summary>
    /// The settings that both front ends use: which key casts the float, how the colour
    /// rule is tuned, and which keys run the macro that keeps the bait applied.
    /// 
    /// The user interface can change all of them, so they are kept in a file that the
    /// console front end reads as well. Otherwise the two would drift apart and the
    /// console version would fish differently from the window version without it being
    /// obvious why.
    /// </summary>
    public class FishingFunSettings
    {
        /// <summary>
        /// The file the settings are kept in, next to the program.
        /// </summary>
        public const string FileName = "fishingfun.settings";

        /// <summary>
        /// The older file that only held the cast key. It is still read, so that a setup
        /// made before the settings file existed keeps working.
        /// </summary>
        private const string OldCastKeyFileName = "keybind.txt";

        /// <summary>
        /// The key that casts the float. In the game this is the fishing button on the
        /// action bar.
        /// </summary>
        public ConsoleKey CastKey { get; set; } = ConsoleKey.D5;

        /// <summary>
        /// How strongly a pixel has to be red for the colour rule to accept it.
        /// </summary>
        public double ColourMultiplier { get; set; } = 0.5;

        /// <summary>
        /// How close green and blue have to be to each other for the colour rule to
        /// accept a pixel.
        /// </summary>
        public double ColourClosenessMultiplier { get; set; } = 2.0;

        /// <summary>
        /// The keys that run the macro which keeps the bait applied. They are pressed
        /// every ten minutes.
        /// </summary>
        public List<ConsoleKey> TenMinuteKeys { get; set; } = new List<ConsoleKey> { ConsoleKey.D5, ConsoleKey.D6 };

        /// <summary>
        /// The settings both front ends use. There is only one set of them, so that a
        /// change made in one front end is seen by the other.
        /// </summary>
        public static FishingFunSettings Current { get; } = new FishingFunSettings();

        /// <summary>
        /// Puts the settings back to their defaults. Used by the tests.
        /// </summary>
        public static void ResetForTests()
        {
            Current.CastKey = ConsoleKey.D5;
            Current.ColourMultiplier = 0.5;
            Current.ColourClosenessMultiplier = 2.0;
            Current.TenMinuteKeys = new List<ConsoleKey> { ConsoleKey.D5, ConsoleKey.D6 };
        }

        /// <summary>
        /// Reads the settings. A missing or unreadable file leaves the defaults in place,
        /// so a problem with the file cannot stop the program from starting.
        /// </summary>
        public static FishingFunSettings Load()
        {
            var settings = Current;

            settings.LoadFromOldCastKeyFile();

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
            if (!File.Exists(path))
            {
                return settings;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (IOException)
            {
                return settings;
            }
            catch (UnauthorizedAccessException)
            {
                return settings;
            }

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int equals = line.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, equals).Trim().ToLowerInvariant();
                string value = line.Substring(equals + 1).Trim();

                switch (key)
                {
                    case "cast_key":
                        if (Enum.TryParse(value, ignoreCase: true, out ConsoleKey castKey))
                        {
                            settings.CastKey = castKey;
                        }
                        break;
                    case "colour_multiplier":
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double multiplier) && multiplier > 0)
                        {
                            settings.ColourMultiplier = multiplier;
                        }
                        break;
                    case "colour_closeness_multiplier":
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double closeness) && closeness > 0)
                        {
                            settings.ColourClosenessMultiplier = closeness;
                        }
                        break;
                    case "ten_minute_keys":
                        var keys = new List<ConsoleKey>();
                        foreach (string part in value.Split(','))
                        {
                            string trimmed = part.Trim();
                            if (trimmed.Length > 0 && Enum.TryParse(trimmed, ignoreCase: true, out ConsoleKey parsedKey))
                            {
                                keys.Add(parsedKey);
                            }
                        }
                        settings.TenMinuteKeys = keys;
                        break;
                }
            }

            return settings;
        }

        /// <summary>
        /// Reads the cast key from the file the older versions used.
        /// </summary>
        private void LoadFromOldCastKeyFile()
        {
            try
            {
                if (!File.Exists(OldCastKeyFileName))
                {
                    return;
                }

                string contents = File.ReadAllText(OldCastKeyFileName).Trim();
                if (int.TryParse(contents, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                    && Enum.IsDefined(typeof(ConsoleKey), value))
                {
                    CastKey = (ConsoleKey)value;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// Writes the settings, so that the other front end picks them up.
        /// </summary>
        public void Save()
        {
            try
            {
                var lines = new List<string>
                {
                    "# Fishing Fun settings, written by the program.",
                    $"cast_key = {CastKey}",
                    $"colour_multiplier = {ColourMultiplier.ToString("0.###", CultureInfo.InvariantCulture)}",
                    $"colour_closeness_multiplier = {ColourClosenessMultiplier.ToString("0.###", CultureInfo.InvariantCulture)}",
                    "ten_minute_keys = " + string.Join(",", TenMinuteKeys),
                };

                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
                File.WriteAllLines(path, lines);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// Puts the colour settings into the classifier.
        /// </summary>
        public void ApplyTo(IPixelClassifier classifier)
        {
            classifier.ColourMultiplier = ColourMultiplier;
            classifier.ColourClosenessMultiplier = ColourClosenessMultiplier;
        }

        /// <summary>
        /// A description of the settings, so that a log shows what the program was
        /// actually using.
        /// </summary>
        public string Describe()
        {
            return $"cast key {CastKey}, colour multiplier {ColourMultiplier:0.###}, " +
                   $"colour closeness multiplier {ColourClosenessMultiplier:0.###}, " +
                   $"ten minute keys [{string.Join(",", TenMinuteKeys)}]";
        }
    }
}
