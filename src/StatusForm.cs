using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace ScreenBreak
{
    internal class StatusSnapshot
    {
        public string Headline = "", Big = "", ScreenTime = "", Skips = "", Bedtime = "";
        public double Progress = -1; // 0..1, or < 0 to hide the bar
        public bool CanBreakNow = true;
        public DayStats[] Days = new DayStats[0]; // History tab, oldest first, ending today
        public int LimitMinutes;                  // daily limit line on the chart, 0 = off
    }

    /// <summary>Small status panel that pops up above the tray icon. Closes when it loses focus.</summary>
    internal class StatusForm : Form
    {
        public event EventHandler BreakNowClicked, SettingsClicked, ExitClicked;

        private static readonly Color Bg = Color.FromArgb(30, 43, 38);
        private static readonly Color Fg = Color.FromArgb(243, 235, 211);
        private static readonly Color Dim = Color.FromArgb(168, 184, 170);
        private static readonly Color Track = Color.FromArgb(52, 72, 62);
        private static readonly Color Accent = Color.FromArgb(132, 196, 152);
        private static readonly Color ButtonBg = Color.FromArgb(48, 68, 58);
        private static readonly Color OverLimit = Color.FromArgb(224, 160, 96);

        private readonly Label headline, big, screenTime, skips, bedtime;
        private readonly Label today, average, breaksTaken, skipsWeek;
        private readonly Label nowTab, historyTab;
        private readonly TableLayoutPanel nowPage, historyPage, dayTable;
        private readonly Panel bar, chart;
        private readonly Button breakNow;
        private double progress = -1;
        private DateTime hiddenAt = DateTime.MinValue;
        private Rectangle workArea, screenBounds;
        private DayStats[] days = new DayStats[0];
        private int limitMinutes;
        private string chartKey = "";

        public StatusForm()
        {
            Text = "ScreenBreak";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = true;
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font("Segoe UI", 9.75f);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(18, 14, 18, 16);

            int width = Font.Height * 19;
            var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Location = new Point(18, 14), Margin = new Padding(0) };
            Controls.Add(root);

            var header = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Width = width, Margin = new Padding(0, 0, 0, 10) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width * 0.5f));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width * 0.5f));
            header.Controls.Add(MakeLabel("ScreenBreak", 9f, FontStyle.Bold, Dim, new Padding(0, 3, 0, 0)), 0, 0);
            var tabs = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
            tabs.Controls.Add(nowTab = MakeTab("Now"));
            tabs.Controls.Add(historyTab = MakeTab("History"));
            header.Controls.Add(tabs, 1, 0);
            root.Controls.Add(header);

            // ---- Now tab
            nowPage = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Margin = new Padding(0) };
            nowPage.Controls.Add(headline = MakeLabel("", 9.75f, FontStyle.Regular, Dim, new Padding(0)));
            nowPage.Controls.Add(big = MakeLabel("", 28f, FontStyle.Bold, Fg, new Padding(0, 0, 0, 6)));

            bar = new Panel { Width = width, Height = Math.Max(4, Font.Height / 4), Margin = new Padding(0, 0, 0, 14) };
            bar.Paint += PaintBar;
            nowPage.Controls.Add(bar);

            TableLayoutPanel details = MakeDetails(width);
            screenTime = AddDetail(details, "Screen time (24 h)");
            skips = AddDetail(details, "Skips (last 2 h)");
            bedtime = AddDetail(details, "Bedtime");
            nowPage.Controls.Add(details);
            root.Controls.Add(nowPage);

            // ---- History tab
            historyPage = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Margin = new Padding(0), Visible = false };
            TableLayoutPanel summary = MakeDetails(width);
            summary.Margin = new Padding(0, 0, 0, 10);
            today = AddDetail(summary, "Screen time today");
            average = AddDetail(summary, "Average per day (7 d)");
            breaksTaken = AddDetail(summary, "Breaks taken (7 d)");
            skipsWeek = AddDetail(summary, "Skips (7 d)");
            historyPage.Controls.Add(summary);

            chart = new Panel { Width = width, Height = Font.Height * 6, Margin = new Padding(0, 0, 0, 12) };
            chart.Paint += PaintChart;
            historyPage.Controls.Add(chart);

            dayTable = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, Width = width, Margin = new Padding(0, 0, 0, 14) };
            foreach (float w in new[] { 0.36f, 0.26f, 0.2f, 0.18f }) dayTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width * w));
            string[] heads = { "Day", "Screen time", "Breaks", "Skips" };
            for (int c = 0; c < 4; c++) dayTable.Controls.Add(MakeCell(heads[c], Dim, c > 0), c, 0);
            for (int r = 1; r <= 7; r++)
                for (int c = 0; c < 4; c++) dayTable.Controls.Add(MakeCell("", Fg, c > 0), c, r);
            historyPage.Controls.Add(dayTable);
            root.Controls.Add(historyPage);

            nowTab.Click += delegate { SelectTab(false); };
            historyTab.Click += delegate { SelectTab(true); };
            SelectTab(false);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            breakNow = MakeButton("Take a break now");
            Button settings = MakeButton("Settings");
            Button exit = MakeButton("Exit");
            breakNow.Click += delegate { Raise(BreakNowClicked); };
            settings.Click += delegate { Raise(SettingsClicked); };
            exit.Click += delegate { Raise(ExitClicked); };
            buttons.Controls.Add(breakNow);
            buttons.Controls.Add(settings);
            buttons.Controls.Add(exit);
            root.Controls.Add(buttons);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW; // keep it out of Alt+Tab
                cp.ClassStyle |= 0x20000;              // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
        }

        /// <summary>Tray click: open, or close if already open. A click on the icon while open
        /// first deactivates (hides) the panel, so a re-open right after a hide is ignored.</summary>
        public void Toggle(StatusSnapshot status)
        {
            if (Visible) { HidePanel(); return; }
            if ((DateTime.Now - hiddenAt).TotalMilliseconds < 400) return;
            ShowPanel(status);
        }

        public void ShowPanel(StatusSnapshot status)
        {
            UpdateStatus(status);
            PerformLayout();
            Screen screen = Screen.FromPoint(Cursor.Position);
            workArea = screen.WorkingArea;
            screenBounds = screen.Bounds;
            Reposition();
            Show();
            Activate();
        }

        public void HidePanel()
        {
            if (!Visible) return;
            hiddenAt = DateTime.Now;
            Hide();
        }

        /// <summary>Sit next to the taskbar, wherever it is docked. Also runs when switching tabs
        /// changes the panel's size, so it stays in its corner.</summary>
        private void Reposition()
        {
            if (workArea.IsEmpty) return;
            int gap = Font.Height / 2 + 4;
            int x = workArea.Right - Width - gap, y = workArea.Bottom - Height - gap;
            if (workArea.Left > screenBounds.Left) x = workArea.Left + gap;
            if (workArea.Top > screenBounds.Top) y = workArea.Top + gap;
            Location = new Point(x, y);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Reposition();
        }

        private void SelectTab(bool history)
        {
            SuspendLayout();
            nowPage.Visible = !history;
            historyPage.Visible = history;
            nowTab.Tag = !history;
            historyTab.Tag = history;
            nowTab.ForeColor = history ? Dim : Fg;
            historyTab.ForeColor = history ? Fg : Dim;
            nowTab.Invalidate();
            historyTab.Invalidate();
            ResumeLayout(true);
        }

        public void UpdateStatus(StatusSnapshot st)
        {
            SetText(headline, st.Headline);
            SetText(big, st.Big);
            SetText(screenTime, st.ScreenTime);
            SetText(skips, st.Skips);
            SetText(bedtime, st.Bedtime);
            breakNow.Enabled = st.CanBreakNow;
            bar.Visible = st.Progress >= 0;
            if (Math.Abs(st.Progress - progress) > 0.001)
            {
                progress = st.Progress;
                bar.Invalidate();
            }
            UpdateHistory(st);
        }

        private void UpdateHistory(StatusSnapshot st)
        {
            days = st.Days;
            limitMinutes = st.LimitMinutes;
            if (days.Length == 0) return;

            DayStats[] used = days.Where(d => d.Minutes > 0).ToArray();
            int breaks = days.Sum(d => d.Breaks + d.AwayBreaks), awayBreaks = days.Sum(d => d.AwayBreaks);
            SetText(today, FmtHours(days[days.Length - 1].Minutes));
            SetText(average, used.Length == 0 ? "-" : FmtHours((int)Math.Round(used.Average(d => d.Minutes))));
            SetText(breaksTaken, awayBreaks > 0 ? breaks + " (" + awayBreaks + " away)" : breaks.ToString());
            SetText(skipsWeek, days.Sum(d => d.Skips).ToString());

            // Newest day first.
            for (int r = 1; r <= 7; r++)
            {
                int i = days.Length - r;
                DayStats d = i >= 0 ? days[i] : null;
                SetText((Label)dayTable.GetControlFromPosition(0, r), d == null ? "" : DayName(d.Date));
                SetText((Label)dayTable.GetControlFromPosition(1, r), d == null ? "" : FmtHours(d.Minutes));
                SetText((Label)dayTable.GetControlFromPosition(2, r), d == null ? "" : (d.Breaks + d.AwayBreaks).ToString());
                SetText((Label)dayTable.GetControlFromPosition(3, r), d == null ? "" : d.Skips.ToString());
            }

            string key = limitMinutes + ":" + string.Join(",", days.Select(d => d.Date.Day + "=" + d.Minutes));
            if (key != chartKey)
            {
                chartKey = key;
                chart.Invalidate();
            }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            HidePanel();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) HidePanel();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Alt+F4 just hides it; the app keeps running in the tray.
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HidePanel();
            }
            base.OnFormClosing(e);
        }

        private void Raise(EventHandler handler)
        {
            HidePanel();
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void PaintBar(object sender, PaintEventArgs e)
        {
            Rectangle r = bar.ClientRectangle;
            using (var track = new SolidBrush(Track)) e.Graphics.FillRectangle(track, r);
            int filled = (int)(r.Width * Math.Max(0, Math.Min(1, progress)));
            using (var fill = new SolidBrush(Accent)) e.Graphics.FillRectangle(fill, 0, 0, filled, r.Height);
        }

        /// <summary>Bar chart of screen time per day, with the daily limit as a dashed line.</summary>
        private void PaintChart(object sender, PaintEventArgs e)
        {
            if (days.Length == 0) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = chart.ClientRectangle;

            using (var small = new Font("Segoe UI", 8f))
            using (var dim = new SolidBrush(Dim))
            using (var fg = new SolidBrush(Fg))
            using (var center = new StringFormat { Alignment = StringAlignment.Center })
            {
                int labelH = small.Height;
                int top = labelH + 2, bottom = r.Height - labelH - 4;
                int plotH = Math.Max(1, bottom - top);
                int scale = Math.Max(60, Math.Max(limitMinutes, days.Max(d => d.Minutes)));
                float slot = (float)r.Width / days.Length;
                float barW = slot * 0.56f;

                using (var axis = new Pen(Track)) g.DrawLine(axis, 0, bottom, r.Width, bottom);
                for (int i = 0; i < days.Length; i++)
                {
                    DayStats d = days[i];
                    bool isToday = i == days.Length - 1;
                    float cx = slot * i + slot / 2;
                    int h = (int)Math.Round(plotH * (double)d.Minutes / scale);
                    if (d.Minutes > 0) h = Math.Max(2, h);
                    Color c = limitMinutes > 0 && d.Minutes >= limitMinutes ? OverLimit : Accent;
                    if (!isToday) c = Color.FromArgb(170, c);
                    using (var fill = new SolidBrush(c)) g.FillRectangle(fill, cx - barW / 2, bottom - h, barW, h);

                    if (d.Minutes > 0)
                        g.DrawString(ShortHours(d.Minutes), small, isToday ? fg : dim, cx, bottom - h - labelH - 1, center);
                    string day = isToday ? "Today" : d.Date.ToString("ddd", CultureInfo.CurrentCulture);
                    g.DrawString(day, small, isToday ? fg : dim, cx, bottom + 3, center);
                }

                if (limitMinutes > 0)
                {
                    int y = bottom - (int)Math.Round(plotH * (double)limitMinutes / scale);
                    using (var pen = new Pen(Color.FromArgb(150, OverLimit)) { DashStyle = DashStyle.Dash })
                        g.DrawLine(pen, 0, y, r.Width, y);
                }
            }
        }

        private static string DayName(DateTime date)
        {
            if (date == DateTime.Today) return "Today";
            if (date == DateTime.Today.AddDays(-1)) return "Yesterday";
            return date.ToString("ddd d MMM", CultureInfo.CurrentCulture);
        }

        private static string FmtHours(int minutes)
        {
            return string.Format("{0}h {1:00}m", minutes / 60, minutes % 60);
        }

        private static string ShortHours(int minutes)
        {
            return minutes < 60 ? minutes + "m" : (minutes / 60.0).ToString("0.#", CultureInfo.CurrentCulture) + "h";
        }

        private static void SetText(Label l, string text)
        {
            if (l.Text != text) l.Text = text;
        }

        private static Label MakeLabel(string text, float size, FontStyle style, Color color, Padding margin)
        {
            return new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", size, style), ForeColor = color, Margin = margin };
        }

        /// <summary>A clickable tab title; the selected one (Tag = true) is underlined.</summary>
        private static Label MakeTab(string text)
        {
            var tab = new Label
            {
                Text = text,
                AutoSize = true,
                Cursor = Cursors.Hand,
                ForeColor = Dim,
                Padding = new Padding(4, 2, 4, 6),
                Margin = new Padding(6, 0, 0, 0)
            };
            tab.Paint += (sender, e) =>
            {
                if (!(tab.Tag is bool) || !(bool)tab.Tag) return;
                using (var line = new SolidBrush(Accent)) e.Graphics.FillRectangle(line, 4, tab.Height - 3, tab.Width - 8, 2);
            };
            return tab;
        }

        private static TableLayoutPanel MakeDetails(int width)
        {
            var details = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Width = width, Margin = new Padding(0, 0, 0, 14) };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width * 0.55f));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width * 0.45f));
            return details;
        }

        private static Label MakeCell(string text, Color color, bool right)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = color, Anchor = right ? AnchorStyles.Right : AnchorStyles.Left, Margin = new Padding(0, 2, 0, 2) };
        }

        private Label AddDetail(TableLayoutPanel table, string caption)
        {
            var name = new Label { Text = caption, AutoSize = true, ForeColor = Dim, Margin = new Padding(0, 3, 0, 3) };
            var value = new Label { AutoSize = true, ForeColor = Fg, Anchor = AnchorStyles.Right, Margin = new Padding(0, 3, 0, 3) };
            table.Controls.Add(name, 0, table.RowCount);
            table.Controls.Add(value, 1, table.RowCount);
            table.RowCount++;
            return value;
        }

        private static Button MakeButton(string text)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                BackColor = ButtonBg,
                ForeColor = Fg,
                Margin = new Padding(0, 0, 8, 0),
                Padding = new Padding(6, 2, 6, 2),
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(78, 104, 90);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(62, 88, 74);
            return b;
        }
    }
}
