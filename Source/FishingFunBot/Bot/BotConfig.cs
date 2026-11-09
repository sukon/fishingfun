using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

#nullable enable
namespace FishingFun
{
    /// <summary>
    /// Settings that decide how much the bot writes to disk, and how human the
    /// bot's timing is.
    /// 
    /// The bot writes in two places. The log file gets a line for every message, and the
    /// float observation recording writes a row for every frame of every cast. Both are
    /// frequent enough to matter for a drive that is written to all day.
    /// 
    /// The defaults here are the quiet ones. The log file only keeps warnings and
    /// errors, so the per frame messages do not reach the disk at all, and the float
    /// observation recording is off until it is asked for. Nothing is lost from the
    /// screen, because the user interface shows every message as it happens.
    /// 
    /// The human timing settings decide how the bot spaces out its actions. They are
    /// on by default, and switching them off gives back the old random behaviour
    /// rather than a set of fixed values.
    /// 
    /// Settings are read from the environment first and from fishingfun.config next, so
    /// a machine can be set up without touching the installation:
    /// 
    ///   set FISHINGFUN_LOG_FILE_THRESHOLD=WARN
    ///   set FISHINGFUN_RECORD_FLOAT_TRACES=1
    ///   set FISHINGFUN_HUMAN_BEHAVIOUR=0
    /// 
    /// or in fishingfun.config, one setting per line:
    /// 
    ///   log_file_threshold=WARN
    ///   record_float_traces=false
    ///   human_behaviour=true
    ///   human_jitter=1.0
    /// </summary>
    public static class BotConfig
    {
        /// <summary>
        /// The name of the optional settings file, looked for next to the program.
        /// </summary>
        public const string FileName = "fishingfun.config";

        /// <summary>
        /// The default values, kept apart so that a setting which cannot be understood
        /// goes back to the default rather than to whatever it happened to be before.
        /// </summary>
        private const bool DefaultLogToFile = true;
        private const string DefaultLogFileThreshold = "WARN";
        private const bool DefaultLogFileImmediateFlush = true;
        private const int DefaultLogFileMaxSizeMb = 10;
        private const int DefaultLogFileBackups = 5;
        private const bool DefaultRecordFloatTraces = false;
        private const bool DefaultHumanBehaviour = true;
        private const double DefaultHumanJitter = 1.0;

        /// <summary>
        /// Whether messages are written to the log file at all. When this is false the
        /// file is never created, not even an empty one.
        /// </summary>
        public static bool LogToFile { get; set; } = DefaultLogToFile;

        /// <summary>
        /// The lowest level that reaches the log file. Messages below this are shown on
        /// screen but are not written to disk, which is what keeps the per frame
        /// messages from filling the drive.
        /// </summary>
        public static string LogFileThreshold { get; set; } = DefaultLogFileThreshold;

        /// <summary>
        /// Whether each log line is pushed to the disk as it happens.
        /// 
        /// Turning this off batches the writes, which is gentler on the drive, but the
        /// tail of the log is then only written when the program closes the file. A
        /// program that is killed rather than closed loses that last part. It is left on
        /// by default because the log is the main way of finding out what went wrong.
        /// </summary>
        public static bool LogFileImmediateFlush { get; set; } = DefaultLogFileImmediateFlush;

        /// <summary>
        /// How large the log file may grow before it is rolled over, in megabytes.
        /// </summary>
        public static int LogFileMaxSizeMb { get; set; } = DefaultLogFileMaxSizeMb;

        /// <summary>
        /// How many rolled over log files are kept.
        /// </summary>
        public static int LogFileBackups { get; set; } = DefaultLogFileBackups;

        /// <summary>
        /// Whether the float observation recording is written. It creates a file for
        /// every cast and adds a row for every frame, so it is off by default.
        /// </summary>
        public static bool RecordFloatTraces { get; set; } = DefaultRecordFloatTraces;

        /// <summary>
        /// Whether the bot spaces its actions out the way a person would.
        /// 
        /// When this is false the bot uses the random behaviour it had before, not a
        /// set of fixed times. Switching it off therefore does not change anything
        /// else about how the bot behaves.
        /// </summary>
        public static bool HumanBehaviour { get; set; } = DefaultHumanBehaviour;

        /// <summary>
        /// How strong the variation in the timings is, from 0 to 2.
        /// 
        /// Zero gives the middle of every range, one gives the range as it was
        /// designed and two gives twice the spread. A value outside the range is
        /// brought back inside it, so that the bot can never be made slower than it
        /// was in a way that loses fish.
        /// </summary>
        public static double HumanJitter { get; set; } = DefaultHumanJitter;

        /// <summary>
        /// Puts every setting back to its default. Used by the tests.
        /// </summary>
        public static void Reset()
        {
            LogToFile = DefaultLogToFile;
            LogFileThreshold = DefaultLogFileThreshold;
            LogFileImmediateFlush = DefaultLogFileImmediateFlush;
            LogFileMaxSizeMb = DefaultLogFileMaxSizeMb;
            LogFileBackups = DefaultLogFileBackups;
            RecordFloatTraces = DefaultRecordFloatTraces;
            HumanBehaviour = DefaultHumanBehaviour;
            HumanJitter = DefaultHumanJitter;
        }

        /// <summary>
        /// Reads the settings. The environment wins over the file, and the file wins
        /// over the defaults, so a setting in the file can still be changed for one run
        /// without editing the file. A setting that cannot be understood is ignored and
        /// the value it had is kept, so a typo cannot stop the bot from starting.
        /// </summary>
        public static void Load()
        {
            // Start from the defaults so that a setting which cannot be understood
            // falls back to the default rather than to a value left over from an
            // earlier run.
            Reset();

            // The file is read first and the environment second, so that the
            // environment has the last word.
            LoadFromFile();
            LoadFromEnvironment();
        }

        private static void LoadFromEnvironment()
        {
            Apply("FISHINGFUN_LOG_TO_FILE", value => LogToFile = ParseBool(value, LogToFile));
            Apply("FISHINGFUN_LOG_FILE_THRESHOLD", value => LogFileThreshold = value);
            Apply("FISHINGFUN_LOG_FILE_IMMEDIATE_FLUSH", value => LogFileImmediateFlush = ParseBool(value, LogFileImmediateFlush));
            Apply("FISHINGFUN_LOG_FILE_MAX_SIZE_MB", value => LogFileMaxSizeMb = ParseInt(value, LogFileMaxSizeMb, 1));
            Apply("FISHINGFUN_LOG_FILE_BACKUPS", value => LogFileBackups = ParseInt(value, LogFileBackups, 0));
            Apply("FISHINGFUN_RECORD_FLOAT_TRACES", value => RecordFloatTraces = ParseBool(value, RecordFloatTraces));
            Apply("FISHINGFUN_HUMAN_BEHAVIOUR", value => HumanBehaviour = ParseBool(value, HumanBehaviour));
            Apply("FISHINGFUN_HUMAN_JITTER", value => HumanJitter = ParseDouble(value, HumanJitter, 0.0, 2.0));
        }

        private static void LoadFromFile()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
            if (!File.Exists(path))
            {
                return;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
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
                    case "log_to_file":
                        LogToFile = ParseBool(value, LogToFile);
                        break;
                    case "log_file_threshold":
                        LogFileThreshold = value;
                        break;
                    case "log_file_immediate_flush":
                        LogFileImmediateFlush = ParseBool(value, LogFileImmediateFlush);
                        break;
                    case "log_file_max_size_mb":
                        LogFileMaxSizeMb = ParseInt(value, LogFileMaxSizeMb, 1);
                        break;
                    case "log_file_backups":
                        LogFileBackups = ParseInt(value, LogFileBackups, 0);
                        break;
                    case "record_float_traces":
                        RecordFloatTraces = ParseBool(value, RecordFloatTraces);
                        break;
                    case "human_behaviour":
                        HumanBehaviour = ParseBool(value, HumanBehaviour);
                        break;
                    case "human_jitter":
                        HumanJitter = ParseDouble(value, HumanJitter, 0.0, 2.0);
                        break;
                }
            }
        }

        private static void Apply(string variable, Action<string> setter)
        {
            string value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(value))
            {
                setter(value.Trim());
            }
        }

        private static bool ParseBool(string value, bool fallback)
        {
            if (bool.TryParse(value, out bool parsed))
            {
                return parsed;
            }

            // Numbers are accepted too, because 1 and 0 are easier to type in some shells.
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            {
                return number != 0;
            }

            return fallback;
        }

        private static int ParseInt(string value, int fallback, int minimum)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed >= minimum)
            {
                return parsed;
            }

            return fallback;
        }

        /// <summary>
        /// Reads a number. A value that is not a number, or is not finite, is ignored
        /// and the value it had is kept. A value that is outside the range is brought
        /// back inside it.
        /// </summary>
        private static double ParseDouble(string value, double fallback, double minimum, double maximum)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                return fallback;
            }

            if (double.IsNaN(parsed) || double.IsInfinity(parsed))
            {
                return fallback;
            }

            return Math.Max(minimum, Math.Min(maximum, parsed));
        }
    }
}
