using System;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenBreak
{
    internal class StatusSnapshot
    {
        public string Headline = "", Big = "", ScreenTime = "", Skips = "", Bedtime = "";
        public double Progress = -1; // 0..1, or < 0 to hide the bar
        public bool CanBreakNow = true;
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

        private readonly Label headline, big, screenTime, skips, bedtime;
        private readonly Panel bar;
        private readonly Button breakNow;
        private double progress = -1;
        private DateTime hiddenAt = DateTime.MinValue;

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

            root.Controls.Add(MakeLabel("ScreenBreak", 9f, FontStyle.Bold, Dim, new Padding(0, 0, 0, 10)));
            root.Controls.Add(headline = MakeLabel("", 9.75f, FontStyle.Regular, Dim, new Padding(0)));
            root.Controls.Add(big = MakeLabel("", 28f, FontStyle.Bold, Fg, new Padding(0, 0, 0, 6)));

            bar = new Panel { Width = width, Height = Math.Max(4, Font.Height / 4), Margin = new Padding(0, 0, 0, 14) };
            bar.Paint += PaintBar;
            root.Controls.Add(bar);

            var details = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Width = width, Margin = new Padding(0, 0, 0, 14) };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width * 0.55f));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width * 0.45f));
            screenTime = AddDetail(details, "Screen time (24 h)");
            skips = AddDetail(details, "Skips (last 2 h)");
            bedtime = AddDetail(details, "Bedtime");
            root.Controls.Add(details);

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
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Rectangle full = Screen.FromPoint(Cursor.Position).Bounds;
            int gap = Font.Height / 2 + 4;
            // Sit next to the taskbar, wherever it is docked.
            int x = wa.Right - Width - gap, y = wa.Bottom - Height - gap;
            if (wa.Left > full.Left) x = wa.Left + gap;
            if (wa.Top > full.Top) y = wa.Top + gap;
            Location = new Point(x, y);
            Show();
            Activate();
        }

        public void HidePanel()
        {
            if (!Visible) return;
            hiddenAt = DateTime.Now;
            Hide();
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

        private static void SetText(Label l, string text)
        {
            if (l.Text != text) l.Text = text;
        }

        private static Label MakeLabel(string text, float size, FontStyle style, Color color, Padding margin)
        {
            return new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", size, style), ForeColor = color, Margin = margin };
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
