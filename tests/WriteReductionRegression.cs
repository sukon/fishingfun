using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using log4net.Appender;
using log4net.Core;

// Checks that the settings which decide how much is written to disk behave.
//
//  - switching file logging off must not create the file at all
//  - the threshold only applies to the file: everything still reaches the screen
//  - a normal exit keeps the last lines
//  - the log file rolls over
//  - the environment wins over the settings file, and a bad value is ignored
class WriteReductionRegression
{
    static int failures = 0;

    static void Check(string name, bool condition, string detail = "")
    {
        Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? "  " + detail : "")}");
        if (!condition) failures++;
    }

    // An appender that keeps everything it is given, standing in for the user
    // interface, which shows every message on screen.
    class CollectingAppender : AppenderSkeleton
    {
        public List<LoggingEvent> Received = new List<LoggingEvent>();
        protected override void Append(LoggingEvent loggingEvent)
        {
            Received.Add(loggingEvent);
        }
    }

    static string WorkDirectory
    {
        get { return AppDomain.CurrentDomain.BaseDirectory; }
    }

    static string LogPath => Path.Combine(WorkDirectory, "myapp.log");

    static void RemoveLog()
    {
        if (File.Exists(LogPath)) File.Delete(LogPath);
    }

    static string ReadLog()
    {
        if (!File.Exists(LogPath)) return "";
        using var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }

    static void Reset()
    {
        RemoveLog();
        var hierarchy = (log4net.Repository.Hierarchy.Hierarchy)log4net.LogManager.GetRepository();
        hierarchy.Root.RemoveAllAppenders();
        hierarchy.ResetConfiguration();
    }

    static void Main()
    {
        string configPath = Path.Combine(WorkDirectory, "fishingfun.config");
        if (File.Exists(configPath)) File.Delete(configPath);

        Console.WriteLine("=== file logging switched off creates no file ===");
        {
            Reset();
            Environment.SetEnvironmentVariable("FISHINGFUN_LOG_TO_FILE", "0");
            FishingFun.LoggingSetup.Configure();
            var log = log4net.LogManager.GetLogger("Fishbot");
            log.Info("this must not be written to disk");
            log.Error("nor must this");
            log4net.LogManager.Shutdown();

            Check("no log file is created", !File.Exists(LogPath));
            Environment.SetEnvironmentVariable("FISHINGFUN_LOG_TO_FILE", null);
        }

        Console.WriteLine();
        Console.WriteLine("=== the threshold applies to the file only ===");
        {
            Reset();
            var collecting = new CollectingAppender { Name = "screen" };
            collecting.ActivateOptions();
            ((log4net.Repository.Hierarchy.Hierarchy)log4net.LogManager.GetRepository()).Root.AddAppender(collecting);

            FishingFun.LoggingSetup.Configure();
            var log = log4net.LogManager.GetLogger("Fishbot");
            log.Info("an ordinary message");
            log.Error("a problem");
            log4net.LogManager.Shutdown();

            string content = ReadLog();
            Check("the info message is not on disk", !content.Contains("an ordinary message"));
            Check("the error message is on disk", content.Contains("a problem"));
            Check("the screen still gets the info message",
                collecting.Received.Any(e => e.RenderedMessage.Contains("an ordinary message")));
        }

        Console.WriteLine();
        Console.WriteLine("=== a normal exit keeps the last lines ===");
        {
            Reset();
            FishingFun.LoggingSetup.Configure();
            var log = log4net.LogManager.GetLogger("Fishbot");
            for (int i = 0; i < 20; i++) log.Error("last line " + i);
            log4net.LogManager.Shutdown();

            string content = ReadLog();
            Check("the final line is on disk", content.Contains("last line 19"), "(20 error lines expected)");
        }

        Console.WriteLine();
        Console.WriteLine("=== the log file rolls over ===");
        {
            Reset();
            Environment.SetEnvironmentVariable("FISHINGFUN_LOG_FILE_MAX_SIZE_MB", "1");
            Environment.SetEnvironmentVariable("FISHINGFUN_LOG_FILE_BACKUPS", "2");
            FishingFun.LoggingSetup.Configure();
            var log = log4net.LogManager.GetLogger("Fishbot");
            // A megabyte of log lines is more than the limit, so it has to roll over.
            for (int i = 0; i < 40000; i++) log.Error("a line that is long enough to fill the file up quickly " + i);
            log4net.LogManager.Shutdown();

            string[] files = Directory.GetFiles(WorkDirectory, "myapp.log*");
            Check("the log rolled over", files.Length > 1, "(" + files.Length + " files)");
            Environment.SetEnvironmentVariable("FISHINGFUN_LOG_FILE_MAX_SIZE_MB", null);
            Environment.SetEnvironmentVariable("FISHINGFUN_LOG_FILE_BACKUPS", null);
            foreach (string f in files) { try { File.Delete(f); } catch { } }
        }

        Console.WriteLine();
        Console.WriteLine("=== the settings file is read ===");
        {
            Reset();
            File.WriteAllText(configPath, "# a comment\nrecord_float_traces = true\nlog_file_threshold = ERROR\n");
            FishingFun.BotConfig.Load();
            Check("record_float_traces is read", FishingFun.BotConfig.RecordFloatTraces);
            Check("log_file_threshold is read", FishingFun.BotConfig.LogFileThreshold == "ERROR");
        }

        Console.WriteLine();
        Console.WriteLine("=== the environment wins over the settings file ===");
        {
            Environment.SetEnvironmentVariable("FISHINGFUN_RECORD_FLOAT_TRACES", "0");
            FishingFun.BotConfig.Load();
            Check("the environment overrides the file", !FishingFun.BotConfig.RecordFloatTraces);
            Environment.SetEnvironmentVariable("FISHINGFUN_RECORD_FLOAT_TRACES", null);
        }

        Console.WriteLine();
        Console.WriteLine("=== a bad value is ignored ===");
        {
            File.WriteAllText(configPath, "log_file_max_size_mb = not a number\nlog_file_backups = -5\nlog_file_threshold = NOPE\n");
            FishingFun.BotConfig.Load();
            Check("a bad size keeps the default", FishingFun.BotConfig.LogFileMaxSizeMb == 10);
            Check("a negative count keeps the default", FishingFun.BotConfig.LogFileBackups == 5);
            Check("an unknown level is reported", FishingFun.LoggingSetup.FindLevel("NOPE") == null);
            File.Delete(configPath);
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILURE(S)");
        Environment.Exit(failures == 0 ? 0 : 1);
    }
}
