using System;
using System.IO;

namespace ScreenBreak
{
    /// <summary>Tiny size-capped error log in %APPDATA%\ScreenBreak\error.log. Never throws.</summary>
    internal static class Log
    {
        private const long MaxBytes = 256 * 1024;

        public static void Error(string context, Exception ex)
        {
            try
            {
                string path = Path.Combine(Settings.DataDir, "error.log");
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes) info.Delete();
                File.AppendAllText(path, DateTime.Now.ToString("s") + " " + context + ": " + ex + "\r\n\r\n");
            }
            catch (Exception) { }
        }
    }
}
