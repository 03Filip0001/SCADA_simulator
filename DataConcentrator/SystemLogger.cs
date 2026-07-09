using System;
using System.IO;

namespace DataConcentrator
{
    public static class SystemLogger
    {
        private static readonly object syncRoot = new object();

        private static string LogPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "system.log");

        public static void Log(string message) => Write("INFO", message);

        public static void LogWarning(string message) => Write("WARNING", message);

        public static void LogError(string message) => Write("ERROR", message);

        public static void LogError(string message, Exception exception)
        {
            if (exception == null)
            {
                Write("ERROR", message);
                return;
            }

            Write("ERROR", $"{message} Exception: {exception.GetType().Name}: {exception.Message}");
        }

        private static void Write(string level, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                lock (syncRoot)
                {
                    var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level.PadRight(8)}{message}{Environment.NewLine}";
                    File.AppendAllText(LogPath, line);
                }
            }
            catch
            {
                // Logging must never crash the application, even if the log
                // file is locked, the disk is full, or access is denied.
            }
        }
    }
}
