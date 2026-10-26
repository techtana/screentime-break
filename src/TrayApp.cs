using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScreenBreak
{
    /// <summary>Tray icon plus the once-a-second scheduler that drives every popup.</summary>
    internal class TrayApp : ApplicationContext
    {
        private enum Phase { Working, Ramp, Break, Bedtime }

        private const int IdleForUsageSeconds = 300;   // no input for 5 min = not "using" the PC
        private const int AngrySeconds = 20;
        private const int LimitAlertSeconds = 60;

        private Settings s;
        private Image background;
        private readonly NotifyIcon tray;
        private readonly Timer timer;
        private readonly ToolStripMenuItem statusItem, usageItem;
        private SettingsForm settingsForm;

        private readonly OverlaySet main = new OverlaySet();
        private readonly OverlaySet alert = new OverlaySet();
        private readonly OverlaySet corner = new OverlaySet();
        private readonly BypassForm bypass = new BypassForm();

        private readonly TimeLog usage;   // one sample per active minute, 24 h window
        private readonly TimeLog skips;   // one entry per skip, 24 h window

        private Phase phase = Phase.Working;
        private double workSec, rampSec, breakSec;
        private bool preview;
        private double savedWorkSec;
        private DateTime lastTick = DateTime.Now;
        private DateTime angryUntil, limitUntil, lastLimitAlert = DateTime.MinValue, snoozeUntil = DateTime.MinValue;
        private int angryCount;

        public TrayApp(bool startWithPreview)
        {
            s = Settings.Load();
            usage = new TimeLog(Path.Combine(Settings.DataDir, "usage.log"), TimeSpan.FromHours(24));
            skips = new TimeLog(Path.Combine(Settings.DataDir, "skips.log"), TimeSpan.FromHours(24));
            LoadBackground();

            statusItem = new ToolStripMenuItem { Enabled = false };
            usageItem = new ToolStripMenuItem { Enabled = false };
            var menu = new ContextMenuStrip();
            menu.Items.Add(statusItem);
            menu.Items.Add(usageItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Take a break now", null, delegate { StartBreakNow(); });
            menu.Items.Add("Preview popup (35 s)", null, delegate { StartPreview(); });
            menu.Items.Add("Settings...", null, delegate { ShowSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitThread(); });
            menu.Opening += delegate { RefreshStatus(); };

            tray = new NotifyIcon { Icon = MakeIcon(), ContextMenuStrip = menu, Visible = true, Text = "ScreenBreak" };
            tray.DoubleClick += delegate { ShowSettings(); };

            bypass.Confirmed += OnBypass;
            ApplyAutostart();

            timer = new Timer { Interval = 1000 };
            timer.Tick += OnTick;
            timer.Start();

            if (startWithPreview) { StartPreview(); return; }
            tray.ShowBalloonTip(4000, "ScreenBreak is running",
                "Next break in " + s.WorkMinutes + " min. Right-click the tray icon for settings.", ToolTipIcon.Info);
        }

        private double RampTotal { get { return preview ? 20 : s.RampMinutes * 60.0; } }
        private double BreakTotal { get { return preview ? 15 : s.BreakMinutes * 60.0; } }

        private void OnTick(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            double dt = Math.Max(0, (now - lastTick).TotalSeconds);
            lastTick = now;

            double idle = InputMonitor.IdleSeconds();
            bool resumedFromSleep = dt > 120;
            if (resumedFromSleep) idle = Math.Max(idle, dt);
            else if (idle < IdleForUsageSeconds) usage.AddMinute(DateTime.UtcNow);

            UpdateBedtime(now);
            if (phase != Phase.Bedtime) UpdateBreakCycle(dt, idle);
            UpdateAlerts(now);
            UpdateBypassButton();

            // Bottom to top: break popup, alerts, bedtime corner, skip button.
            main.BringToTop();
            alert.BringToTop();
            corner.BringToTop();
            if (bypass.Visible) Native.BringToTop(bypass.Handle);

            RefreshStatus();
        }

        // ---- break cycle -------------------------------------------------------

        private void UpdateBreakCycle(double dt, double idle)
        {
            double needed = BreakTotal;
            switch (phase)
            {
                case Phase.Working:
                    // Being away for a full break length counts as a natural break.
                    if (idle >= s.BreakMinutes * 60.0) workSec = 0;
                    else workSec += dt;
                    if (workSec >= s.WorkMinutes * 60.0) StartRamp();
                    break;

                case Phase.Ramp:
                    rampSec += dt;
                    if (!preview && idle >= needed) { FinishBreak(); return; }
                    if (rampSec >= RampTotal) { phase = Phase.Break; breakSec = 0; }
                    break;

                case Phase.Break:
                    if (preview || !s.BreakNeedsIdle || idle >= 2) breakSec += dt;
                    if (!preview && idle >= needed) breakSec = needed;
                    if (breakSec >= needed)
                    {
                        bool wasPreview = preview;
                        FinishBreak();
                        if (!wasPreview) tray.ShowBalloonTip(3000, "Break complete", "Nice! Next break in " + s.WorkMinutes + " min.", ToolTipIcon.Info);
                        return;
                    }
                    break;
            }
            RenderBreakPopup(idle);
        }

        private void RenderBreakPopup(double idle)
        {
            double maxOpacity = s.MaxOpacityPercent / 100.0;
            if (phase == Phase.Ramp)
            {
                double t = RampTotal <= 0 ? 1 : Math.Min(1, rampSec / RampTotal);
                double opacity = Lerp(s.StartOpacityPercent / 100.0, maxOpacity, t);
                double coverage = Lerp(s.StartCoveragePercent / 100.0, 1, t);
                main.Update(OverlayStyle.Calm, "Screen break coming up", Fmt(RampTotal - rampSec),
                    "Time to save your work and wrap up.\nYou can still click and type in the apps behind this.\n" +
                    "Break length: " + Fmt(BreakTotal),
                    opacity, coverage, background);
            }
            else if (phase == Phase.Break)
            {
                bool paused = !preview && s.BreakNeedsIdle && idle < 2;
                main.Update(OverlayStyle.Calm, "Screen break", Fmt(BreakTotal - breakSec),
                    paused ? "Paused while you're using the PC. Step away and the countdown continues."
                           : "Rest your eyes: look at something far away, stretch, drink some water.\nThis closes by itself when the break is done.",
                    maxOpacity, 1, background);
            }
            else main.Hide();
        }

        private void StartRamp()
        {
            phase = Phase.Ramp;
            rampSec = 0;
        }

        private void StartBreakNow()
        {
            if (phase == Phase.Bedtime) return;
            if (preview) EndPreview();
            phase = Phase.Break;
            breakSec = 0;
        }

        private void StartPreview()
        {
            if (phase == Phase.Bedtime) return;
            if (!preview) savedWorkSec = phase == Phase.Working ? workSec : 0;
            preview = true;
            phase = Phase.Ramp;
            rampSec = 0;
        }

        private void EndPreview()
        {
            preview = false;
            workSec = savedWorkSec;
        }

        private void FinishBreak()
        {
            main.Hide();
            phase = Phase.Working;
            if (preview) EndPreview();
            else workSec = 0;
        }

        // ---- bedtime -----------------------------------------------------------

        private void UpdateBedtime(DateTime now)
        {
            if (!s.BedtimeEnabled)
            {
                corner.Hide();
                if (phase == Phase.Bedtime) EndBedtime();
                return;
            }

            TimeSpan bed = Settings.ParseTime(s.Bedtime, new TimeSpan(22, 30, 0));
            TimeSpan wake = Settings.ParseTime(s.WakeTime, new TimeSpan(7, 0, 0));
            bool inWindow = InWindow(now.TimeOfDay, bed, wake);

            if (inWindow && now >= snoozeUntil)
            {
                if (phase != Phase.Bedtime)
                {
                    if (preview) EndPreview();
                    main.Hide();
                    phase = Phase.Bedtime;
                }
                main.Update(OverlayStyle.Night, "It's bedtime", now.ToString("HH:mm"),
                    "Save your work and shut down. This stays up until " + s.WakeTime + ".\n" +
                    "You can still use the apps behind it to wrap up.",
                    s.MaxOpacityPercent / 100.0, 1, null);
            }
            else if (phase == Phase.Bedtime) EndBedtime();

            if (!inWindow)
            {
                DateTime next = now.Date + bed;
                if (next <= now) next = next.AddDays(1);
                TimeSpan left = next - now;
                if (left.TotalMinutes <= s.BedtimeWarnMinutes)
                    corner.UpdateCorner("Bedtime in", Fmt(left.TotalSeconds), "at " + s.Bedtime, s.BedtimeCorner, 0.88);
                else corner.Hide();
            }
            else if (now < snoozeUntil)
                corner.UpdateCorner("Snoozed bedtime", Fmt((snoozeUntil - now).TotalSeconds), "you're past bedtime", s.BedtimeCorner, 0.92);
            else corner.Hide();
        }

        private void EndBedtime()
        {
            main.Hide();
            phase = Phase.Working;
            workSec = 0;
        }

        private static bool InWindow(TimeSpan t, TimeSpan start, TimeSpan end)
        {
            if (start == end) return false;
            return start < end ? t >= start && t < end : t >= start || t < end;
        }

        // ---- skip button / alerts ---------------------------------------------

        private void UpdateBypassButton()
        {
            bool show = s.BypassEnabled && main.Visible && phase != Phase.Working;
            if (!show) { bypass.HideButton(); return; }

            Color top, bottom, text;
            OverlayForm.GetPalette(phase == Phase.Bedtime ? OverlayStyle.Night : OverlayStyle.Calm, out top, out bottom, out text);
            if (phase != Phase.Bedtime && background != null) bottom = Color.FromArgb(28, 28, 28);
            bypass.SetColors(bottom, Color.FromArgb(Math.Min(255, bottom.R + 22), Math.Min(255, bottom.G + 22), Math.Min(255, bottom.B + 22)));
            bypass.ShowIn(main.PrimaryRect);
        }

        private void OnBypass(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            if (phase == Phase.Bedtime)
            {
                snoozeUntil = now.AddMinutes(s.BedtimeSnoozeMinutes);
                EndBedtime();
            }
            else if (phase == Phase.Ramp || phase == Phase.Break)
            {
                bool wasPreview = preview;
                FinishBreak();
                if (wasPreview) return;
            }
            else return;

            skips.Add(DateTime.UtcNow);
            int recent = skips.CountSince(DateTime.UtcNow.AddHours(-2));
            if (recent > 1)
            {
                angryCount = recent;
                angryUntil = now.AddSeconds(AngrySeconds);
            }
        }

        private void UpdateAlerts(DateTime now)
        {
            if (now < angryUntil)
            {
                alert.Update(OverlayStyle.Angry, "I'm very NOT happy.", angryCount + " skips",
                    "You've skipped " + angryCount + " breaks in the last 2 hours.\nYour eyes, back and brain deserve better.",
                    0.9, 0.75, null);
                return;
            }

            if (s.DailyLimitEnabled)
            {
                int minutes = usage.CountSince(DateTime.UtcNow.AddHours(-24));
                if (minutes >= s.DailyLimitHours * 60 && phase == Phase.Working &&
                    (now - lastLimitAlert).TotalMinutes >= s.DailyLimitRepeatMinutes)
                {
                    lastLimitAlert = now;
                    limitUntil = now.AddSeconds(LimitAlertSeconds);
                }
                if (now < limitUntil)
                {
                    alert.Update(OverlayStyle.Alert, s.DailyLimitHours + "-hour screen limit reached", FmtHours(minutes),
                        "That's your screen time in the last 24 hours. Time to wind down.\n" +
                        "This reminder repeats every " + s.DailyLimitRepeatMinutes + " min.",
                        0.85, 0.6, null);
                    return;
                }
            }
            alert.Hide();
        }

        // ---- tray / settings ---------------------------------------------------

        private void RefreshStatus()
        {
            string status;
            switch (phase)
            {
                case Phase.Ramp: status = "Break starts in " + Fmt(RampTotal - rampSec); break;
                case Phase.Break: status = "On a break: " + Fmt(BreakTotal - breakSec) + " left"; break;
                case Phase.Bedtime: status = "Bedtime"; break;
                default: status = "Next break in " + Fmt(s.WorkMinutes * 60.0 - workSec); break;
            }
            int minutes = usage.CountSince(DateTime.UtcNow.AddHours(-24));
            int recentSkips = skips.CountSince(DateTime.UtcNow.AddHours(-2));
            statusItem.Text = status;
            usageItem.Text = "Screen time (24 h): " + FmtHours(minutes) + "   Skips (2 h): " + recentSkips;

            string tip = "ScreenBreak - " + status + "\n24 h: " + FmtHours(minutes);
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            if (tray.Text != tip) tray.Text = tip;
        }

        private void ShowSettings()
        {
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                settingsForm.Activate();
                return;
            }
            settingsForm = new SettingsForm(s);
            settingsForm.Applied += OnSettingsApplied;
            settingsForm.Show();
            settingsForm.Activate();
        }

        private void OnSettingsApplied(Settings updated, bool runPreview)
        {
            s = updated;
            try { s.Save(); }
            catch (Exception ex) { MessageBox.Show("Couldn't save settings:\n" + ex.Message, "ScreenBreak"); }
            LoadBackground();
            ApplyAutostart();
            CleanupOldBackgrounds();
            if (runPreview) StartPreview();
        }

        private void LoadBackground()
        {
            if (background != null) { background.Dispose(); background = null; }
            if (!s.UseCustomBackground || !File.Exists(s.BackgroundImage)) return;
            try
            {
                // Copy into memory so the file isn't locked.
                using (var stream = new MemoryStream(File.ReadAllBytes(s.BackgroundImage)))
                using (Image img = Image.FromStream(stream))
                    background = new Bitmap(img);
            }
            catch (Exception) { background = null; }
        }

        private void CleanupOldBackgrounds()
        {
            try
            {
                foreach (string f in Directory.GetFiles(Settings.DataDir, "background-*"))
                    if (!string.Equals(f, s.BackgroundImage, StringComparison.OrdinalIgnoreCase)) File.Delete(f);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void ApplyAutostart()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (s.StartWithWindows) key.SetValue("ScreenBreak", "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue("ScreenBreak", false);
                }
            }
            catch (Exception) { }
        }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            main.Hide();
            alert.Hide();
            corner.Hide();
            bypass.Dispose();
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Close();
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }

        // ---- helpers -----------------------------------------------------------

        private static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * t;
        }

        private static string Fmt(double seconds)
        {
            TimeSpan ts = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, seconds)));
            return ts.TotalHours >= 1
                ? string.Format("{0}:{1:00}:{2:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds)
                : string.Format("{0:00}:{1:00}", ts.Minutes, ts.Seconds);
        }

        private static string FmtHours(int minutes)
        {
            return string.Format("{0}h {1:00}m", minutes / 60, minutes % 60);
        }

        private static Icon MakeIcon()
        {
            using (Stream ico = typeof(TrayApp).Assembly.GetManifestResourceStream("ScreenBreak.ico"))
                if (ico != null) return new Icon(ico, SystemInformation.SmallIconSize);

            // Fallback when built without the icon resource.
            using (var bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var fill = new SolidBrush(Color.FromArgb(63, 125, 92))) g.FillEllipse(fill, 2, 2, 28, 28);
                    using (var pen = new Pen(Color.FromArgb(243, 235, 211), 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawEllipse(pen, 3, 3, 26, 26);
                        g.DrawLine(pen, 16, 16, 16, 8);
                        g.DrawLine(pen, 16, 16, 22, 19);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
