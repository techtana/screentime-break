using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace ScreenBreak
{
    /// <summary>User settings, stored as key=value lines in %APPDATA%\ScreenBreak\settings.ini.</summary>
    public class Settings
    {
        // Default values live in config\defaults.ini, which is embedded in the exe (see Defaults).

        // Break cycle
        public int WorkMinutes;
        public int RampMinutes;
        public int StartOpacityPercent;
        public int MaxOpacityPercent;
        public int StartCoveragePercent;
        public int BreakMinutes;
        public bool BreakNeedsIdle;

        // What counts as "away from the PC": the break countdown pauses and screen time isn't counted.
        public int AwayMinutes;
        public bool AwayCountsController;      // controller input means you're still there
        public bool AwayWhenLocked;            // locking the PC means away right away
        public bool NotAwayWhileWatching;      // a video or full-screen app keeps you "present"

        // Look
        public bool UseCustomBackground;
        public string BackgroundImage;

        // Skip button
        public bool BypassEnabled;

        // Rolling 24h limit
        public bool DailyLimitEnabled;
        public int DailyLimitHours;
        public int DailyLimitRepeatMinutes;

        // Bedtime
        public bool BedtimeEnabled;
        public string Bedtime;
        public string WakeTime;
        public int BedtimeWarnMinutes;
        public string BedtimeCorner;
        public int BedtimeSnoozeMinutes;

        public bool StartWithWindows;

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

        private static Settings defaults;

        /// <summary>The defaults from config\defaults.ini, embedded in the exe by build.ps1.</summary>
        public static Settings Defaults
        {
            get
            {
                if (defaults == null) defaults = LoadDefaults();
                return defaults;
            }
        }

        private static Settings LoadDefaults()
        {
            string[] lines;
            using (Stream res = typeof(Settings).Assembly.GetManifestResourceStream("defaults.ini"))
            {
                if (res == null) throw new InvalidOperationException("defaults.ini is not embedded in the exe. Build with build.ps1.");
                using (var reader = new StreamReader(res))
                    lines = reader.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            }
            var s = new Settings();
            var found = Read(s, lines);
            // A missing or unreadable default would silently become 0, false or null, so fail loudly instead.
            foreach (FieldInfo f in typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (!found.Contains(f.Name)) throw new InvalidOperationException("defaults.ini has no valid value for " + f.Name);
            TimeSpan t;
            if (!TryParseTime(s.Bedtime, out t) || !TryParseTime(s.WakeTime, out t) || Array.IndexOf(Corners, s.BedtimeCorner) < 0)
                throw new InvalidOperationException("defaults.ini has an invalid bedtime, wake-up time or corner");
            return s;
        }

        public static Settings Load()
        {
            Settings s = Defaults.Clone();
            s.FirstRun = !File.Exists(FilePath);
            try
            {
                // Anything bigger than a few KB isn't a settings file we wrote; ignore it.
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length < 64 * 1024)
                    Read(s, File.ReadAllLines(FilePath));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            s.Clamp();
            return s;
        }

        /// <summary>Applies key=value lines to <paramref name="s"/>, skipping unknown keys and bad values.
        /// Returns the names that were set.</summary>
        private static HashSet<string> Read(Settings s, string[] lines)
        {
            var found = new HashSet<string>();
            foreach (string line in lines)
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
                else continue;
                found.Add(f.Name);
            }
            return found;
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
            AwayMinutes = Clamp(AwayMinutes, 1, 60);
            DailyLimitHours = Clamp(DailyLimitHours, 1, 24);
            DailyLimitRepeatMinutes = Clamp(DailyLimitRepeatMinutes, 5, 240);
            BedtimeWarnMinutes = Clamp(BedtimeWarnMinutes, 5, 480);
            BedtimeSnoozeMinutes = Clamp(BedtimeSnoozeMinutes, 1, 120);
            Bedtime = BedtimeOfDay.ToString(@"hh\:mm");
            WakeTime = WakeTimeOfDay.ToString(@"hh\:mm");
            if (Array.IndexOf(Corners, BedtimeCorner) < 0) BedtimeCorner = Defaults.BedtimeCorner;
            if (!IsSafeBackground(BackgroundImage))
            {
                BackgroundImage = "";
                UseCustomBackground = false;
            }
        }

        /// <summary>Bedtime as a time of day, or the default bedtime if the text is invalid.</summary>
        public TimeSpan BedtimeOfDay { get { return ParseTime(Bedtime, ParseTime(Defaults.Bedtime, TimeSpan.Zero)); } }

        /// <summary>Wake-up time as a time of day, or the default wake-up time if the text is invalid.</summary>
        public TimeSpan WakeTimeOfDay { get { return ParseTime(WakeTime, ParseTime(Defaults.WakeTime, TimeSpan.Zero)); } }

        public static TimeSpan ParseTime(string text, TimeSpan fallback)
        {
            TimeSpan t;
            return TryParseTime(text, out t) ? t : fallback;
        }

        private static bool TryParseTime(string text, out TimeSpan time)
        {
            DateTime t;
            bool ok = DateTime.TryParseExact(text ?? "", new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out t);
            time = ok ? t.TimeOfDay : TimeSpan.Zero;
            return ok;
        }

        private static int Clamp(int v, int min, int max)
        {
            return Math.Max(min, Math.Min(max, v));
        }
    }
}
