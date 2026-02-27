using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace RFIDTrackBin
{
    public static class AppLogger
    {
        private static readonly string LogFilePath =
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "app_log.txt");

        public static void Log(string message)
        {
            try
            {
                var logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}\n";
                File.AppendAllText(LogFilePath, logMessage);
            }
            catch
            {
                // Evitamos que un error de logging afecte la app
            }
        }

        public static void LogError(Exception ex)
        {
            Log($"[ERROR] {ex.Message}\nSTACKTRACE:\n{ex.StackTrace}");
        }

        public static string GetLogFilePath() => LogFilePath;
    }
}