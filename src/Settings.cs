using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace ScreenBreak
{
    /// <summary>User settings, stored as key=value lines in %APPDATA%\ScreenBreak\settings.ini.</summary>
    public class Settings
    {
        // Break cycle
        public int WorkMinutes = 60;
        public int RampMinutes = 30;
        public int StartOpacityPercent = 15;
        public int MaxOpacityPercent = 90;
        public int StartCoveragePercent = 30;
        public int BreakMinutes = 10;
        public bool BreakNeedsIdle = true;

        // Look
        public bool UseCustomBackground = false;
        public string BackgroundImage = "";

        // Skip button
        public bool BypassEnabled = true;

        // Rolling 24h limit
        public bool DailyLimitEnabled = true;
        public int DailyLimitHours = 4;
        public int DailyLimitRepeatMinutes = 30;

        // Bedtime
        public bool BedtimeEnabled = false;
        public string Bedtime = "22:30";
        public string WakeTime = "07:00";
        public int BedtimeWarnMinutes = 120;
        public string BedtimeCorner = "BottomRight";
        public int BedtimeSnoozeMinutes = 15;

        public bool StartWithWindows = false;

        /// <summary>True when no settings file existed yet (not saved: it's a property, not a field).</summary>
        public bool FirstRun { get; private set; }

        public static readonly string[] Corners = { "TopLeft", "TopRight", "BottomLeft", "BottomRight" };

        public static string DataDir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenBreak");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string FilePath { get { return Path.Combine(DataDir, "settings.ini"); } }

        public Settings Clone()
        {
            return (Settings)MemberwiseClone();
        }

        public const long MaxImageBytes = 20 * 1024 * 1024;

        /// <summary>
        /// Background images must be our own copy inside DataDir. This blocks a tampered
        /// settings.ini from pointing at a network path (which would leak the Windows login
        /// hash to that server) or at an arbitrary file.
        /// </summary>
        public static bool IsSafeBackground(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return false;
                string full = Path.GetFullPath(path);
                string dir = Path.GetFullPath(DataDir) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) return false;
                if (!Path.GetFileName(full).StartsWith("background-", StringComparison.OrdinalIgnoreCase)) return false;
                var info = new FileInfo(full);
                return info.Exists && info.Length <= MaxImageBytes && (info.Attributes & FileAttributes.ReparsePoint) == 0;
            }
            catch (Exception) { return false; }
        }

        public static Settings Load()
        {
            var s = new Settings();
            s.FirstRun = !File.Exists(FilePath);
            try
            {
                // Anything bigger than a few KB isn't a settings file we wrote; ignore it.
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length < 64 * 1024)
                {
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0 || line.StartsWith("#")) continue;
                        FieldInfo f = typeof(Settings).GetField(line.Substring(0, eq).Trim(), BindingFlags.Public | BindingFlags.Instance);
                        if (f == null) continue;
                        string v = line.Substring(eq + 1).Trim();
                        int i;
                        bool b;
                        if (f.FieldType == typeof(int) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) f.SetValue(s, i);
                        else if (f.FieldType == typeof(bool) && bool.TryParse(v, out b)) f.SetValue(s, b);
                        else if (f.FieldType == typeof(string)) f.SetValue(s, v);
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            s.Clamp();
            return s;
        }

        public void Save()
        {
            Clamp();
            var sb = new StringBuilder("# ScreenBreak settings\r\n");
            foreach (FieldInfo f in typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance))
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0}={1}\r\n", f.Name, f.GetValue(this));
            File.WriteAllText(FilePath, sb.ToString());
        }

        public void Clamp()
        {
            WorkMinutes = Clamp(WorkMinutes, 1, 600);
            RampMinutes = Clamp(RampMinutes, 0, 240);
            StartOpacityPercent = Clamp(StartOpacityPercent, 5, 95);
            MaxOpacityPercent = Clamp(MaxOpacityPercent, StartOpacityPercent, 98);
            StartCoveragePercent = Clamp(StartCoveragePercent, 10, 100);
            BreakMinutes = Clamp(BreakMinutes, 1, 120);
            DailyLimitHours = Clamp(DailyLimitHours, 1, 24);
            DailyLimitRepeatMinutes = Clamp(DailyLimitRepeatMinutes, 5, 240);
            BedtimeWarnMinutes = Clamp(BedtimeWarnMinutes, 5, 480);
            BedtimeSnoozeMinutes = Clamp(BedtimeSnoozeMinutes, 1, 120);
            Bedtime = ParseTime(Bedtime, new TimeSpan(22, 30, 0)).ToString(@"hh\:mm");
            WakeTime = ParseTime(WakeTime, new TimeSpan(7, 0, 0)).ToString(@"hh\:mm");
            if (Array.IndexOf(Corners, BedtimeCorner) < 0) BedtimeCorner = "BottomRight";
            if (!IsSafeBackground(BackgroundImage))
            {
                BackgroundImage = "";
                UseCustomBackground = false;
            }
        }

        public static TimeSpan ParseTime(string text, TimeSpan fallback)
        {
            DateTime t;
            if (DateTime.TryParseExact(text ?? "", new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                return t.TimeOfDay;
            return fallback;
        }

        private static int Clamp(int v, int min, int max)
        {
            return Math.Max(min, Math.Min(max, v));
        }
    }
}
