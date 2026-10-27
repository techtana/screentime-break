using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;
using Microsoft.Win32;

namespace ScreenBreak
{
    /// <summary>Tray icon plus the once-a-second scheduler that drives every popup.</summary>
    internal class TrayApp : ApplicationContext
    {
        private enum Phase { Working, Ramp, Break, Bedtime }

        private const int AngrySeconds = 20;
        private const int LimitAlertSeconds = 60;

        private Settings s;
        private Image background;
        private readonly NotifyIcon tray;
        private readonly Timer timer;
        private readonly ToolStripMenuItem statusItem, usageItem;
        private SettingsForm settingsForm;
        private readonly StatusForm statusPanel = new StatusForm();
        private readonly RegisteredWaitHandle showWait;

        private readonly OverlaySet main = new OverlaySet();
        private readonly OverlaySet alert = new OverlaySet();
        private readonly OverlaySet corner = new OverlaySet();
        private readonly ButtonSet<BypassForm> bypass;
        private readonly ButtonSet<BreakNowForm> mainBreakNow, alertBreakNow, cornerBreakNow;
        private OverlayStyle alertStyle;

        private readonly TimeLog usage;   // one sample per active minute, 24 h window
        private readonly TimeLog skips;   // one entry per skip, 24 h window
        private readonly History history; // per-day totals for the History tab

        private Phase phase = Phase.Working;
        private double workSec, rampSec, breakSec;
        private bool preview;
        private double savedWorkSec;
        private DateTime lastTick = DateTime.Now;
        private DateTime angryUntil, limitUntil, lastLimitAlert = DateTime.MinValue, snoozeUntil = DateTime.MinValue;
        private int angryCount;
        private bool away, locked;
        private DateTime lockedAt;

        public TrayApp(bool startWithPreview, EventWaitHandle showSignal)
        {
            s = Settings.Load();
            usage = new TimeLog(Path.Combine(Settings.DataDir, "usage.log"), TimeSpan.FromHours(24));
            skips = new TimeLog(Path.Combine(Settings.DataDir, "skips.log"), TimeSpan.FromHours(24));
            history = new History(Path.Combine(Settings.DataDir, "history.csv"), usage, skips);
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

            tray = new NotifyIcon { Icon = AppIcon.Load(SystemInformation.SmallIconSize), ContextMenuStrip = menu, Visible = true, Text = "ScreenBreak" };
            tray.MouseClick += (sender, e) => { if (e.Button == MouseButtons.Left) statusPanel.Toggle(Snapshot()); };

            statusPanel.BreakNowClicked += delegate { StartBreakNow(); };
            statusPanel.SettingsClicked += delegate { ShowSettings(); };
            statusPanel.ExitClicked += delegate { ExitThread(); };
            // Launching ScreenBreak again (e.g. from the Start Menu) signals this copy to show the panel.
            IntPtr panelHandle = statusPanel.Handle; // create the handle now so BeginInvoke works
            showWait = ThreadPool.RegisterWaitForSingleObject(showSignal, delegate
            {
                try { statusPanel.BeginInvoke((MethodInvoker)delegate { statusPanel.ShowPanel(Snapshot()); }); }
                catch (InvalidOperationException) { } // shutting down
            }, null, Timeout.Infinite, false);

            bypass = new ButtonSet<BypassForm>(delegate { var b = new BypassForm(); b.Confirmed += OnBypass; return b; });
            mainBreakNow = NewBreakNowButtons();
            alertBreakNow = NewBreakNowButtons();
            cornerBreakNow = NewBreakNowButtons();
            SystemEvents.SessionSwitch += OnSessionSwitch;
            if (s.FirstRun && !startWithPreview)
            {
                // Adding a startup app changes the user's Windows experience, so ask first.
                // A hidden topmost owner keeps the question from opening behind other windows.
                using (var owner = new Form { TopMost = true, ShowInTaskbar = false })
                {
                    owner.CreateControl();
                    s.StartWithWindows = MessageBox.Show(owner,
                        "Start ScreenBreak automatically when you sign in to Windows?\n\nYou can change this later in Settings.",
                        "ScreenBreak", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                }
                try { s.Save(); }
                catch (Exception ex) { Log.Error("Saving settings", ex); }
            }
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

            // idle: time since the last input. awayIdle: the same, except it stays 0 while a video
            // or full-screen app is playing, so watching without touching anything isn't "away".
            double idle = InputMonitor.IdleSeconds(s.AwayCountsController);
            bool resumedFromSleep = dt > 120;
            if (resumedFromSleep) idle = Math.Max(idle, dt);
            bool lockedAway = locked && s.AwayWhenLocked;
            if (lockedAway) idle = Math.Max(idle, (now - lockedAt).TotalSeconds);
            double awayIdle = idle;
            if (s.NotAwayWhileWatching && !lockedAway && !resumedFromSleep && idle >= 30 && Native.IsWatching()) awayIdle = 0;

            bool wasAway = away;
            away = resumedFromSleep || lockedAway || awayIdle >= s.AwayMinutes * 60.0;
            if (!away && usage.AddMinute(DateTime.UtcNow)) history.AddMinute(now);

            UpdateBedtime(now);
            if (phase != Phase.Bedtime) UpdateBreakCycle(dt, idle, awayIdle, wasAway);
            UpdateAlerts(now);
            UpdateBypassButton();
            UpdateBreakNowButtons();

            // Bottom to top: break popup, alerts, bedtime corner, buttons.
            main.BringToTop();
            alert.BringToTop();
            corner.BringToTop();
            mainBreakNow.BringToTop();
            alertBreakNow.BringToTop();
            cornerBreakNow.BringToTop();
            bypass.BringToTop();
            if (statusPanel.Visible)
            {
                Native.BringToTop(statusPanel.Handle);
                statusPanel.UpdateStatus(Snapshot());
            }

            RefreshStatus();
        }

        // ---- break cycle -------------------------------------------------------

        private void UpdateBreakCycle(double dt, double idle, double awayIdle, bool wasAway)
        {
            double needed = BreakTotal;
            switch (phase)
            {
                case Phase.Working:
                    if (awayIdle >= s.BreakMinutes * 60.0)
                    {
                        // Being away for a full break length counts as a natural break.
                        if (workSec > 0) history.AddAwayBreak(DateTime.Now);
                        workSec = 0;
                    }
                    else if (away)
                    {
                        // The countdown pauses while you're away. It only knows you're away once the
                        // "away" time has passed, so give back the time counted while you were already gone.
                        if (!wasAway) workSec = Math.Max(0, workSec - Math.Max(0, awayIdle - dt));
                    }
                    else workSec += dt;
                    if (workSec >= s.WorkMinutes * 60.0) StartRamp();
                    break;

                case Phase.Ramp:
                    rampSec += dt;
                    if (!preview && awayIdle >= needed) { history.AddBreak(DateTime.Now); FinishBreak(); return; }
                    if (rampSec >= RampTotal) { phase = Phase.Break; breakSec = 0; }
                    break;

                case Phase.Break:
                    if (preview || !s.BreakNeedsIdle || idle >= 2) breakSec += dt;
                    if (!preview && awayIdle >= needed) breakSec = needed;
                    if (breakSec >= needed)
                    {
                        bool wasPreview = preview;
                        if (!wasPreview) history.AddBreak(DateTime.Now);
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

            TimeSpan bed = s.BedtimeOfDay;
            TimeSpan wake = s.WakeTimeOfDay;
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
            if (!show) { bypass.Hide(); return; }

            Color top, bottom, text;
            OverlayForm.GetPalette(phase == Phase.Bedtime ? OverlayStyle.Night : OverlayStyle.Calm, out top, out bottom, out text);
            if (phase != Phase.Bedtime && background != null) bottom = Color.FromArgb(28, 28, 28);
            bypass.ShowIn(main.Rects, bottom, Color.FromArgb(Math.Min(255, bottom.R + 22), Math.Min(255, bottom.G + 22), Math.Min(255, bottom.B + 22)));
        }

        private ButtonSet<BreakNowForm> NewBreakNowButtons()
        {
            return new ButtonSet<BreakNowForm>(delegate { var b = new BreakNowForm(); b.Clicked += OnBreakNowClicked; return b; });
        }

        /// <summary>Every reminder popup gets a "Take a break now" button, until a break or bedtime starts.</summary>
        private void UpdateBreakNowButtons()
        {
            bool canBreak = phase == Phase.Working || phase == Phase.Ramp;
            ShowBreakNow(mainBreakNow, main, phase == Phase.Ramp, OverlayStyle.Calm);
            ShowBreakNow(alertBreakNow, alert, canBreak, alertStyle);
            ShowBreakNow(cornerBreakNow, corner, canBreak, OverlayStyle.Corner);
        }

        private static void ShowBreakNow(ButtonSet<BreakNowForm> buttons, OverlaySet popup, bool show, OverlayStyle style)
        {
            if (!show || !popup.Visible) { buttons.Hide(); return; }
            Color top, bottom, text;
            OverlayForm.GetPalette(style, out top, out bottom, out text);
            buttons.ShowIn(popup.Rects, text, top);
        }

        private void OnBreakNowClicked(object sender, EventArgs e)
        {
            // Taking the break answers the alert, so close it.
            angryUntil = limitUntil = DateTime.MinValue;
            alert.Hide();
            StartBreakNow();
            mainBreakNow.Hide();
            alertBreakNow.Hide();
            cornerBreakNow.Hide();
        }

        private void OnBypass(object sender, EventArgs e)
        {
            bypass.Hide();
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
            history.AddSkip(now);
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
                alertStyle = OverlayStyle.Angry;
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
                    alertStyle = OverlayStyle.Alert;
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
                default: status = (away ? "Paused (away): next break in " : "Next break in ") + Fmt(s.WorkMinutes * 60.0 - workSec); break;
            }
            int minutes = usage.CountSince(DateTime.UtcNow.AddHours(-24));
            int recentSkips = skips.CountSince(DateTime.UtcNow.AddHours(-2));
            statusItem.Text = status;
            usageItem.Text = "Screen time (24 h): " + FmtHours(minutes) + "   Skips (2 h): " + recentSkips;

            string tip = "ScreenBreak - " + status + "\n24 h: " + FmtHours(minutes);
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            if (tray.Text != tip) tray.Text = tip;
        }

        private StatusSnapshot Snapshot()
        {
            var st = new StatusSnapshot();
            switch (phase)
            {
                case Phase.Ramp:
                    st.Headline = "Break starts in";
                    st.Big = Fmt(RampTotal - rampSec);
                    st.Progress = RampTotal <= 0 ? 1 : rampSec / RampTotal;
                    break;
                case Phase.Break:
                    st.Headline = "On a break, time left";
                    st.Big = Fmt(BreakTotal - breakSec);
                    st.Progress = breakSec / BreakTotal;
                    break;
                case Phase.Bedtime:
                    st.Headline = "It's bedtime, until " + s.WakeTime;
                    st.Big = DateTime.Now.ToString("HH:mm");
                    break;
                default:
                    st.Headline = away ? "Paused while you're away. Next break in" : "Next break in";
                    st.Big = Fmt(s.WorkMinutes * 60.0 - workSec);
                    st.Progress = workSec / (s.WorkMinutes * 60.0);
                    break;
            }
            st.CanBreakNow = phase == Phase.Working;

            int minutes = usage.CountSince(DateTime.UtcNow.AddHours(-24));
            st.ScreenTime = FmtHours(minutes) + (s.DailyLimitEnabled ? " of " + s.DailyLimitHours + "h" : "");
            st.Skips = skips.CountSince(DateTime.UtcNow.AddHours(-2)).ToString();
            st.Days = history.LastDays(7);
            st.LimitMinutes = s.DailyLimitEnabled ? s.DailyLimitHours * 60 : 0;

            if (!s.BedtimeEnabled) st.Bedtime = "Off";
            else if (phase == Phase.Bedtime) st.Bedtime = "Now";
            else
            {
                DateTime now = DateTime.Now;
                DateTime next = now.Date + s.BedtimeOfDay;
                if (next <= now) next = next.AddDays(1);
                TimeSpan left = next - now;
                st.Bedtime = s.Bedtime + " (in " + (int)left.TotalHours + "h " + left.Minutes.ToString("00") + "m)";
            }
            return st;
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock) { locked = true; lockedAt = DateTime.Now; }
            else if (e.Reason == SessionSwitchReason.SessionUnlock) locked = false;
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
            if (!s.UseCustomBackground || !Settings.IsSafeBackground(s.BackgroundImage)) return;
            try
            {
                // Copy into memory so the file isn't locked, and cap the size: no screen
                // needs more than 4K, and a huge image would waste a lot of memory.
                using (var stream = new MemoryStream(File.ReadAllBytes(s.BackgroundImage)))
                using (Image img = Image.FromStream(stream, false, true))
                {
                    const int maxSide = 3840;
                    double scale = Math.Min(1.0, (double)maxSide / Math.Max(img.Width, img.Height));
                    background = new Bitmap(img, Math.Max(1, (int)(img.Width * scale)), Math.Max(1, (int)(img.Height * scale)));
                }
            }
            catch (Exception ex)
            {
                Log.Error("Loading background", ex);
                background = null;
            }
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
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            showWait.Unregister(null);
            statusPanel.Dispose();
            main.Hide();
            alert.Hide();
            corner.Hide();
            bypass.Dispose();
            mainBreakNow.Dispose();
            alertBreakNow.Dispose();
            cornerBreakNow.Dispose();
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
    }
}
