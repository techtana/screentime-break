using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ScreenBreak
{
    /// <summary>"Take a break now" button shown on the reminder popups. Never takes focus.</summary>
    internal class BreakNowForm : Form, IPopupButton
    {
        private const string Caption = "Take a break now";

        public event EventHandler Clicked;

        private Color back = Color.FromArgb(243, 235, 211), fore = Color.FromArgb(34, 54, 44);

        public BreakNowForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Opacity = 0.9;
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

        /// <summary>
        /// Bottom-center of a large popup. A small corner popup has no room, so the button
        /// goes just outside it, on the side facing the middle of the screen.
        /// </summary>
        public void ShowIn(Rectangle host)
        {
            bool small = host.Height < 300;
            int h = small ? Math.Max(26, host.Height / 4) : Math.Max(30, host.Height / 26);
            int w = (int)(h * 5.2);
            int x = host.X + (host.Width - w) / 2, y;
            if (small)
            {
                Rectangle wa = Screen.FromRectangle(host).WorkingArea;
                int gap = h / 3;
                y = host.Top - wa.Top > wa.Bottom - host.Bottom ? host.Top - h - gap : host.Bottom + gap;
            }
            else y = host.Bottom - h * 2;
            if (!Visible) Show();
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, x, y, w, h, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        public void HideButton()
        {
            if (Visible) Hide();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Opacity = 1;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Opacity = 0.9;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && Clicked != null) Clicked(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(back);
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using (var font = new Font("Segoe UI", ClientSize.Height * 0.42f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(fore))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(Caption, font, brush, ClientRectangle, sf);
            }
        }
    }
}
