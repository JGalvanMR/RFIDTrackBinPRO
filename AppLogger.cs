using System;
using System.IO;

namespace RFIDTrackBin
{
    public static class AppLogger
    {
        private static readonly string LogFilePath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "app_log.txt");

        // FIX AL-1: _logLock garantiza thread-safety sobre File.AppendAllText.
        // Log() es invocado concurrentemente desde: UI thread, callbacks del SDK
        // RFID (hilo nativo del hardware) y múltiples Task.Run de fondo.
        // File.AppendAllText NO es thread-safe — sin este lock dos hilos pueden
        // corromper el archivo o lanzar IOException silenciosa (el catch la
        // tragaba, haciendo el problema invisible en producción).
        private static readonly object _logLock = new object();

        public static void Log(string message)
        {
            try
            {
                var logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}\n";
                lock (_logLock)
                {
                    File.AppendAllText(LogFilePath, logMessage);
                }
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