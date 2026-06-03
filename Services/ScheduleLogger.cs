using System;
using System.IO;

namespace HistorianSyncTool.Services
{
    /// <summary>
    /// Appends scheduled-run results to a monthly rolling text file in
    /// <c>{exe folder}/logs/schedule-YYYY-MM.log</c>. Used so unattended runs leave
    /// an audit trail that survives an app restart. UI-thread safe (just writes bytes).
    /// </summary>
    public static class ScheduleLogger
    {
        private static readonly object Gate = new object();

        public static string LogDirectory =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

        public static string CurrentLogPath =>
            Path.Combine(LogDirectory, $"schedule-{DateTime.Now:yyyy-MM}.log");

        public static void Append(string line)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(LogDirectory);
                    File.AppendAllText(
                        CurrentLogPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]  {line}{Environment.NewLine}");
                }
            }
            catch
            {
                // Logging failures must never crash the app — swallow.
            }
        }
    }
}
