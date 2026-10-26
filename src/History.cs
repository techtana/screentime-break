using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ScreenBreak
{
    internal class DayStats
    {
        public DateTime Date;           // local date
        public int Minutes;             // active screen minutes
        public int Breaks;              // break popups completed
        public int AwayBreaks;          // natural breaks: away from the PC for a full break length
        public int Skips;               // skipped breaks and bedtime snoozes
    }

    /// <summary>
    /// Per-day totals kept for the History tab, stored as CSV lines in %APPDATA%\ScreenBreak\history.csv:
    /// date,minutes,breaks,awayBreaks,skips. Only the last <see cref="KeepDays"/> days are kept.
    /// </summary>
    internal class History
    {
        public const int KeepDays = 90;

        private readonly string path;
        private readonly SortedDictionary<DateTime, DayStats> days = new SortedDictionary<DateTime, DayStats>();

        public History(string path, TimeLog usage, TimeLog skips)
        {
            this.path = path;
            bool exists = File.Exists(path);
            try
            {
                if (exists && new FileInfo(path).Length < 256 * 1024)
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        string[] p = line.Split(',');
                        DateTime date;
                        if (p.Length < 5 || !DateTime.TryParseExact(p[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) continue;
                        days[date] = new DayStats
                        {
                            Date = date,
                            Minutes = ParseCount(p[1], 24 * 60),
                            Breaks = ParseCount(p[2], 10000),
                            AwayBreaks = ParseCount(p[3], 10000),
                            Skips = ParseCount(p[4], 10000)
                        };
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            // First run with this version: start from what the 24 h logs already know.
            if (!exists)
            {
                foreach (DateTime t in usage.Items) Day(t.ToLocalTime()).Minutes++;
                foreach (DateTime t in skips.Items) Day(t.ToLocalTime()).Skips++;
                Save();
            }
        }

        public void AddMinute(DateTime local) { Day(local).Minutes++; Save(); }
        public void AddBreak(DateTime local) { Day(local).Breaks++; Save(); }
        public void AddAwayBreak(DateTime local) { Day(local).AwayBreaks++; Save(); }
        public void AddSkip(DateTime local) { Day(local).Skips++; Save(); }

        /// <summary>The last <paramref name="count"/> days ending today, oldest first, with empty days filled in.</summary>
        public DayStats[] LastDays(int count)
        {
            var result = new DayStats[count];
            DateTime today = DateTime.Today;
            for (int i = 0; i < count; i++)
            {
                DateTime d = today.AddDays(i - count + 1);
                DayStats found;
                result[i] = days.TryGetValue(d, out found) ? found : new DayStats { Date = d };
            }
            return result;
        }

        private DayStats Day(DateTime local)
        {
            DateTime d = local.Date;
            DayStats st;
            if (!days.TryGetValue(d, out st)) days[d] = st = new DayStats { Date = d };
            return st;
        }

        private void Save()
        {
            DateTime cutoff = DateTime.Today.AddDays(-KeepDays);
            foreach (DateTime old in days.Keys.Where(d => d < cutoff).ToList()) days.Remove(old);
            var sb = new StringBuilder();
            foreach (DayStats d in days.Values)
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd},{1},{2},{3},{4}\r\n", d.Date, d.Minutes, d.Breaks, d.AwayBreaks, d.Skips);
            try { File.WriteAllText(path, sb.ToString()); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static int ParseCount(string text, int max)
        {
            int v;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? Math.Max(0, Math.Min(max, v)) : 0;
        }
    }
}
