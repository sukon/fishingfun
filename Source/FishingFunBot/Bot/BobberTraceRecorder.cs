using log4net;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

#nullable enable
namespace FishingFun
{
    /// <summary>Small per-cast observation recordings; recording failures never stop fishing.</summary>
    public sealed class BobberTraceRecorder : IDisposable
    {
        private static readonly ILog logger = LogManager.GetLogger("Fishbot");
        public const string Header = "elapsed_ms,x,y,width,height,area,decision";
        private StreamWriter? writer;
        private readonly long startedAt;
        public string Path { get; }

        private BobberTraceRecorder(string path, long startedAt)
        {
            Path = path;
            this.startedAt = startedAt;
            writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            try
            {
                writer.WriteLine(Header);
                // The recording is only made when it is asked for, so it is written as it
                // happens. That keeps the file readable while it is still open, which is
                // what the replay tool expects.
                writer.AutoFlush = true;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public static BobberTraceRecorder? TryCreate(long startedAt, string? directory = null)
        {
            // Recording writes a file for every cast, so it is only done when it is
            // asked for. The bot itself does not need it.
            if (!BotConfig.RecordFloatTraces)
            {
                return null;
            }

            try
            {
                directory = directory ?? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp", "bite-traces");
                Directory.CreateDirectory(directory);
                string filename = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
                    + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".csv";
                var recorder = new BobberTraceRecorder(System.IO.Path.Combine(directory, filename), startedAt);
                logger.Info("Float observation trace: " + recorder.Path);
                return recorder;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                logger.Info("Float observation recording unavailable: " + error.Message);
                return null;
            }
        }

        public void Record(BobberObservation observation, string decision)
        {
            try
            {
                writer?.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5},{6}",
                    observation.TimestampMilliseconds - startedAt, observation.Position.X, observation.Position.Y,
                    observation.Size.Width, observation.Size.Height, observation.Area, decision));
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                logger.Info("Float observation recording stopped: " + error.Message);
                Dispose();
            }
        }

        public static IEnumerable<BobberTraceEntry> Read(string path)
        {
            using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            if (reader.ReadLine() != Header) throw new FormatException("Unrecognized observation trace header.");
            string? line;
            long previousTime = -1;
            bool first = true;
            while ((line = reader.ReadLine()) != null)
            {
                var fields = line.Split(',');
                if (fields.Length != 7) throw new FormatException("Expected seven trace columns.");
                long time = long.Parse(fields[0], CultureInfo.InvariantCulture);
                int x = int.Parse(fields[1], CultureInfo.InvariantCulture);
                int y = int.Parse(fields[2], CultureInfo.InvariantCulture);
                int width = int.Parse(fields[3], CultureInfo.InvariantCulture);
                int height = int.Parse(fields[4], CultureInfo.InvariantCulture);
                int area = int.Parse(fields[5], CultureInfo.InvariantCulture);
                string decision = fields[6];
                if (time < 0 || time < previousTime ||
                    (first ? decision != "reset" : decision != "watch" && decision != "bite"))
                    throw new FormatException("Invalid observation time or decision.");
                previousTime = time;
                first = false;
                yield return new BobberTraceEntry(new BobberObservation(new System.Drawing.Point(x, y),
                    new System.Drawing.Size(width, height), area, time), decision);
            }
            if (first) throw new FormatException("Observation trace contains no samples.");
        }

        public void Dispose()
        {
            var stream = writer;
            writer = null;
            try
            {
                // Flush before closing, so that the last rows are on disk.
                stream?.Flush();
                stream?.Dispose();
            }
            catch (IOException error) { logger.Info("Float observation recording close failed: " + error.Message); }
        }
    }

    public sealed class BobberTraceEntry
    {
        public BobberObservation Observation { get; }
        public string RecordedDecision { get; }
        public BobberTraceEntry(BobberObservation observation, string recordedDecision)
        {
            Observation = observation;
            RecordedDecision = recordedDecision;
        }
    }
}
