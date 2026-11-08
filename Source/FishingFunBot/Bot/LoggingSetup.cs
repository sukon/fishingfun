using System;
using System.IO;
using System.Linq;
using System.Reflection;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository;
using log4net.Repository.Hierarchy;

#nullable enable
namespace FishingFun
{
    /// <summary>
    /// Sets log4net up from <see cref="BotConfig"/>.
    /// 
    /// The file appender is added by the program rather than by log4net.config, so that
    /// the log file is never created when file logging is switched off. log4net creates
    /// the file as soon as it is configured, not when something is first written, so
    /// removing the appender afterwards would still leave an empty file behind.
    /// 
    /// The console appender is always added. The user interface adds itself to the
    /// logger separately, so everything still appears on screen even when the file
    /// appender is keeping only warnings and errors.
    /// </summary>
    public static class LoggingSetup
    {
        /// <summary>
        /// The name of the appender that writes the log file.
        /// </summary>
        public const string FileAppenderName = "file";

        /// <summary>
        /// Configures logging and returns the configured repository.
        /// </summary>
        public static Hierarchy Configure()
        {
            BotConfig.Load();

            var hierarchy = (Hierarchy)LogManager.GetRepository();

            var consoleAppender = new ConsoleAppender
            {
                Name = "console",
                Layout = new PatternLayout("%date: %message%newline")
            };
            consoleAppender.ActivateOptions();
            hierarchy.Root.AddAppender(consoleAppender);

            if (BotConfig.LogToFile)
            {
                var fileAppender = CreateFileAppender();
                if (fileAppender != null)
                {
                    hierarchy.Root.AddAppender(fileAppender);
                }
            }

            hierarchy.Root.Level = Level.All;
            hierarchy.Configured = true;

            return hierarchy;
        }

        /// <summary>
        /// Builds the appender that writes the log file, or returns null when the
        /// settings ask for something that cannot be used.
        /// </summary>
        private static RollingFileAppender? CreateFileAppender()
        {
            string path;
            try
            {
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "myapp.log");
            }
            catch (Exception)
            {
                return null;
            }

            var appender = new RollingFileAppender
            {
                Name = FileAppenderName,
                File = path,
                AppendToFile = true,
                RollingStyle = RollingFileAppender.RollingMode.Size,
                MaxSizeRollBackups = BotConfig.LogFileBackups,
                MaxFileSize = (long)BotConfig.LogFileMaxSizeMb * 1024 * 1024,
                StaticLogFileName = true,
                ImmediateFlush = BotConfig.LogFileImmediateFlush,
                Threshold = FindLevel(BotConfig.LogFileThreshold) ?? Level.Warn,
                Layout = new PatternLayout("%date %message%newline")
            };

            try
            {
                appender.ActivateOptions();
            }
            catch (Exception)
            {
                // The directory may not be writable. The console output still works.
                return null;
            }

            return appender;
        }

        /// <summary>
        /// Finds the level with the given name, for example WARN or Debug. Returns null
        /// when there is no such level, so that a wrong setting falls back to the
        /// default rather than stopping the program.
        /// </summary>
        public static Level? FindLevel(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            foreach (var field in typeof(Level).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(Level))
                {
                    continue;
                }

                if (field.GetValue(null) is Level level &&
                    string.Equals(level.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return level;
                }
            }

            return null;
        }
    }
}
