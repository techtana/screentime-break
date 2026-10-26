using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ScreenBreak
{
    internal class SettingsForm : Form
    {
        /// <summary>Raised with the edited settings; <c>true</c> in the bool means "also run a preview".</summary>
        public event Action<Settings, bool> Applied;

        private readonly Settings s;
        private readonly TableLayoutPanel table;
        private readonly ToolTip tips = new ToolTip { AutoPopDelay = 15000 };

        private NumericUpDown work, ramp, startOpacity, maxOpacity, startCoverage, breakLen;
        private NumericUpDown limitHours, limitRepeat, bedWarn, bedSnooze;
        private CheckBox needIdle, customBg, bypass, limitOn, bedOn, autostart;
        private DateTimePicker bedTime, wakeTime;
        private ComboBox corner;
        private Label bgLabel;
        private string bgPath;

        public SettingsForm(Settings current)
        {
            s = current.Clone();
            bgPath = s.BackgroundImage;

            Text = "ScreenBreak settings";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(14);

            table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Location = new Point(14, 8) };
            Controls.Add(table);
            Build();
        }

        private void Build()
        {
            Header("Break reminder");
            Row("Screen time before the popup (min)", work = Num(1, 600, s.WorkMinutes),
                "How long you can use the PC before the break popup starts appearing.");
            Row("Popup ramp-up time (min)", ramp = Num(0, 240, s.RampMinutes),
                "Over this time the popup grows from the starting size to full screen and fades in. Then the break starts.");
            Row("Starting opacity (%)", startOpacity = Num(5, 95, s.StartOpacityPercent), null);
            Row("Final opacity (%)", maxOpacity = Num(5, 98, s.MaxOpacityPercent),
                "Below 100% so you can still see and finish things behind it.");
            Row("Starting size (% of screen)", startCoverage = Num(10, 100, s.StartCoveragePercent), null);
            Row("Break length (min)", breakLen = Num(1, 120, s.BreakMinutes),
                "The popup closes by itself when the break is complete. Being away from the PC this long also counts as a break.");
            Full(needIdle = Check("Only count break time while I'm away from keyboard, mouse and controller", s.BreakNeedsIdle));

            Header("Popup background");
            Full(customBg = Check("Use my own image (default: soft green that's easy on the eyes)", s.UseCustomBackground));
            var bgRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var browse = new Button { Text = "Choose image...", AutoSize = true };
            browse.Click += OnBrowse;
            bgLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 8, 0, 0), MaximumSize = new Size(280, 0) };
            bgLabel.Text = File.Exists(bgPath) ? Path.GetFileName(bgPath) : "(no image)";
            bgRow.Controls.Add(browse);
            bgRow.Controls.Add(bgLabel);
            Row("Image", bgRow, null);

            Header("Skipping");
            Full(bypass = Check("Show a hard-to-see Skip button (skips are counted; 2+ in 2 hours = red alert)", s.BypassEnabled));

            Header("Daily limit");
            Full(limitOn = Check("Alert when screen time in the last 24 hours reaches the limit", s.DailyLimitEnabled));
            Row("Limit (hours per 24 h)", limitHours = Num(1, 24, s.DailyLimitHours), null);
            Row("Repeat the alert every (min)", limitRepeat = Num(5, 240, s.DailyLimitRepeatMinutes), null);

            Header("Bedtime");
            Full(bedOn = Check("Enforce bedtime", s.BedtimeEnabled));
            Row("Bedtime", bedTime = Time(s.Bedtime), null);
            Row("Wake-up time", wakeTime = Time(s.WakeTime),
                "Between bedtime and wake-up time a full-screen bedtime popup stays up.");
            Row("Show countdown before bedtime (min)", bedWarn = Num(5, 480, s.BedtimeWarnMinutes), null);
            corner = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
            corner.Items.AddRange(new object[] { "Top left", "Top right", "Bottom left", "Bottom right" });
            corner.SelectedIndex = Math.Max(0, Array.IndexOf(Settings.Corners, s.BedtimeCorner));
            Row("Countdown corner", corner, null);
            Row("Snooze length when skipped (min)", bedSnooze = Num(1, 120, s.BedtimeSnoozeMinutes), null);

            Header("General");
            Full(autostart = Check("Start ScreenBreak when I sign in to Windows", s.StartWithWindows));

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 16, 0, 0) };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            var save = new Button { Text = "Save", AutoSize = true };
            var preview = new Button { Text = "Apply && preview", AutoSize = true };
            save.Click += delegate { Apply(false); Close(); };
            preview.Click += delegate { Apply(true); };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);
            buttons.Controls.Add(preview);
            Full(buttons);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private void Apply(bool runPreview)
        {
            s.WorkMinutes = (int)work.Value;
            s.RampMinutes = (int)ramp.Value;
            s.StartOpacityPercent = (int)Math.Min(startOpacity.Value, maxOpacity.Value);
            s.MaxOpacityPercent = (int)Math.Max(startOpacity.Value, maxOpacity.Value);
            s.StartCoveragePercent = (int)startCoverage.Value;
            s.BreakMinutes = (int)breakLen.Value;
            s.BreakNeedsIdle = needIdle.Checked;
            s.UseCustomBackground = customBg.Checked && Settings.IsSafeBackground(bgPath);
            s.BackgroundImage = bgPath ?? "";
            s.BypassEnabled = bypass.Checked;
            s.DailyLimitEnabled = limitOn.Checked;
            s.DailyLimitHours = (int)limitHours.Value;
            s.DailyLimitRepeatMinutes = (int)limitRepeat.Value;
            s.BedtimeEnabled = bedOn.Checked;
            s.Bedtime = bedTime.Value.ToString("HH:mm");
            s.WakeTime = wakeTime.Value.ToString("HH:mm");
            s.BedtimeWarnMinutes = (int)bedWarn.Value;
            s.BedtimeCorner = Settings.Corners[Math.Max(0, corner.SelectedIndex)];
            s.BedtimeSnoozeMinutes = (int)bedSnooze.Value;
            s.StartWithWindows = autostart.Checked;
            if (Applied != null) Applied(s.Clone(), runPreview);
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All files|*.*", Title = "Choose a popup background" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    // Validate it's a reasonably sized image, then keep a private copy so moving the original doesn't break it.
                    if (new FileInfo(dlg.FileName).Length > Settings.MaxImageBytes)
                        throw new InvalidDataException("The image is larger than 20 MB.");
                    using (var stream = File.OpenRead(dlg.FileName))
                    using (Image.FromStream(stream, false, true)) { }
                    string dest = Path.Combine(Settings.DataDir, "background-" + DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetExtension(dlg.FileName));
                    File.Copy(dlg.FileName, dest, true);
                    bgPath = dest;
                    bgLabel.Text = Path.GetFileName(dlg.FileName);
                    customBg.Checked = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Couldn't use that image:\n" + ex.Message, "ScreenBreak", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void Header(string text)
        {
            var l = new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 14, 0, 4) };
            Full(l);
        }

        private void Row(string label, Control c, string tip)
        {
            var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(16, 6, 16, 6) };
            c.Anchor = AnchorStyles.Left;
            table.Controls.Add(l, 0, table.RowCount);
            table.Controls.Add(c, 1, table.RowCount);
            table.RowCount++;
            if (tip != null) { tips.SetToolTip(l, tip); tips.SetToolTip(c, tip); }
        }

        private void Full(Control c)
        {
            table.Controls.Add(c, 0, table.RowCount);
            table.SetColumnSpan(c, 2);
            table.RowCount++;
        }

        private static NumericUpDown Num(int min, int max, int value)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), Width = 80 };
        }

        private CheckBox Check(string text, bool value)
        {
            return new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(16, 4, 0, 4) };
        }

        private static DateTimePicker Time(string hhmm)
        {
            return new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "HH:mm",
                ShowUpDown = true,
                Width = 80,
                Value = DateTime.Today + Settings.ParseTime(hhmm, TimeSpan.Zero)
            };
        }
    }
}
