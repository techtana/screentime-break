using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ScreenBreak
{
    /// <summary>
    /// Deliberately hard-to-see "skip" button. Needs two clicks within 5 seconds so it
    /// can't be hit by accident while working behind the overlay. Never takes focus.
    /// </summary>
    internal class BypassForm : Form
    {
        public event EventHandler Confirmed;

        private bool armed;
        private DateTime armedAt;
        private Color back = Color.FromArgb(63, 94, 75), fore = Color.FromArgb(82, 118, 96);

        public BypassForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Opacity = 0.35;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public void SetColors(Color background, Color text)
        {
            if (background == back && text == fore) return;
            back = background;
            fore = text;
            Invalidate();
        }

        /// <summary>Shows the button in the bottom-right of <paramref name="host"/>.</summary>
        public void ShowIn(Rectangle host)
        {
            if (armed && (DateTime.Now - armedAt).TotalSeconds > 5) Disarm();
            int h = Math.Max(22, host.Height / 45);
            int w = armed ? h * 8 : (int)(h * 2.4);
            int x = host.Right - w - h, y = host.Bottom - h * 2;
            if (!Visible) Show();
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, x, y, w, h, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        public void HideButton()
        {
            if (Visible) Hide();
            Disarm();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (armed && (DateTime.Now - armedAt).TotalSeconds <= 5)
            {
                Disarm();
                HideButton();
                if (Confirmed != null) Confirmed(this, EventArgs.Empty);
                return;
            }
            armed = true;
            armedAt = DateTime.Now;
            Opacity = 0.85;
            Invalidate();
        }

        private void Disarm()
        {
            if (!armed) return;
            armed = false;
            Opacity = 0.35;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(back);
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            string text = armed ? "click again to skip (counted)" : "skip";
            using (var font = new Font("Segoe UI", ClientSize.Height * 0.5f, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(armed ? Color.White : fore))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(text, font, brush, ClientRectangle, sf);
            }
        }
    }
}
