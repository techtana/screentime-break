using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ScreenBreak
{
    /// <summary>
    /// Append-only list of UTC timestamps persisted to a text file, keeping only a
    /// rolling window. Used for active-minute samples and for skip (bypass) events.
    /// </summary>
    internal class TimeLog
    {
        private readonly string path;
        private readonly TimeSpan keep;
        private readonly List<DateTime> items = new List<DateTime>();

        public TimeLog(string path, TimeSpan keep)
        {
            this.path = path;
            this.keep = keep;
            try
            {
                if (File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        long ticks;
                        if (long.TryParse(line.Trim(), out ticks) && ticks > 0 && ticks < DateTime.MaxValue.Ticks)
                            items.Add(new DateTime(ticks, DateTimeKind.Utc));
                    }
                }
            }
            catch (IOException) { }
            items.Sort();
            Compact();
        }

        public void Add(DateTime utc)
        {
            items.Add(utc);
            try { File.AppendAllText(path, utc.Ticks + "\r\n"); }
            catch (IOException) { }
            if (items.Count % 500 == 0) Compact();
        }

        /// <summary>Adds a sample unless one already exists for the same minute.</summary>
        public void AddMinute(DateTime utc)
        {
            long minute = utc.Ticks / TimeSpan.TicksPerMinute;
            if (items.Count > 0 && items[items.Count - 1].Ticks / TimeSpan.TicksPerMinute == minute) return;
            Add(utc);
        }

        public int CountSince(DateTime utc)
        {
            int n = 0;
            for (int i = items.Count - 1; i >= 0 && items[i] >= utc; i--) n++;
            return n;
        }

        private void Compact()
        {
            DateTime cutoff = DateTime.UtcNow - keep;
            items.RemoveAll(t => t < cutoff);
            try { File.WriteAllLines(path, items.Select(t => t.Ticks.ToString()).ToArray()); }
            catch (IOException) { }
        }
    }
}
