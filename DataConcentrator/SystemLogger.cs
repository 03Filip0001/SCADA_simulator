using System;
using System.IO;

namespace DataConcentrator
{
    public static class SystemLogger
    {
        private static readonly object syncRoot = new object();

        private static string LogPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "system.log");

        public static void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            lock (syncRoot)
            {
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}{Environment.NewLine}");
            }
        }

        public static void LogError(string message, Exception exception)
        {
            Log($"{message} Error: {exception?.Message}");
        }
    }
}
