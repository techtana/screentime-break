using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ScreenBreak
{
    internal enum OverlayStyle { Calm, Night, Alert, Angry, Corner }

    /// <summary>
    /// A borderless, always-on-top, semi-transparent window that ignores the mouse:
    /// clicks and keystrokes go to whatever is behind it.
    /// </summary>
    internal class OverlayForm : Form
    {
        private OverlayStyle style;
        private string title = "", big = "", sub = "";
        private Image background;
        private Bitmap cache;

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 0.5; // must stay < 1 so the window remains layered (click-through)
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW |
                              Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCHITTEST)
            {
                m.Result = (IntPtr)Native.HTTRANSPARENT;
                return;
            }
            base.WndProc(ref m);
        }

        public void SetContent(OverlayStyle newStyle, string newTitle, string newBig, string newSub, Image newBackground)
        {
            if (newStyle != style || newBackground != background) DropCache();
            else if (newTitle == title && newBig == big && newSub == sub) return;
            style = newStyle;
            background = newBackground;
            title = newTitle ?? "";
            big = newBig ?? "";
            sub = newSub ?? "";
            Invalidate();
        }

        public void SetOpacity(double value)
        {
            value = Math.Round(Math.Max(0.02, Math.Min(0.99, value)), 2);
            if (Math.Abs(Opacity - value) > 0.001) Opacity = value;
        }

        /// <summary>Moves/resizes the window and re-asserts topmost (games like to push themselves on top).</summary>
        public void Place(Rectangle r)
        {
            if (!Visible) Show();
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, r.X, r.Y, r.Width, r.Height,
                                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        public static void GetPalette(OverlayStyle style, out Color top, out Color bottom, out Color text)
        {
            switch (style)
            {
                case OverlayStyle.Night:
                    top = Color.FromArgb(11, 18, 48); bottom = Color.FromArgb(31, 43, 87); text = Color.FromArgb(230, 233, 245); break;
                case OverlayStyle.Alert:
                    top = Color.FromArgb(90, 42, 0); bottom = Color.FromArgb(166, 82, 0); text = Color.FromArgb(255, 243, 224); break;
                case OverlayStyle.Angry:
                    top = Color.FromArgb(92, 0, 0); bottom = Color.FromArgb(193, 18, 31); text = Color.White; break;
                case OverlayStyle.Corner:
                    top = Color.FromArgb(30, 43, 38); bottom = Color.FromArgb(45, 65, 54); text = Color.FromArgb(243, 235, 211); break;
                default:
                    // Muted sage green with warm off-white text: low contrast glare, easy on the eyes.
                    top = Color.FromArgb(34, 54, 44); bottom = Color.FromArgb(63, 94, 75); text = Color.FromArgb(243, 235, 211); break;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle r = ClientRectangle;
            if (r.Width < 4 || r.Height < 4) return;
            if (cache == null || cache.Size != r.Size) BuildCache(r.Size);

            Graphics g = e.Graphics;
            g.DrawImageUnscaled(cache, 0, 0);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            Color top, bottom, fg;
            GetPalette(style, out top, out bottom, out fg);
            float W = r.Width, H = r.Height;

            if (style == OverlayStyle.Corner)
            {
                DrawText(g, title, H * 0.17f, FontStyle.Regular, fg, new RectangleF(0, H * 0.04f, W, H * 0.26f));
                DrawText(g, big, H * 0.38f, FontStyle.Bold, fg, new RectangleF(0, H * 0.26f, W, H * 0.48f));
                DrawText(g, sub, H * 0.13f, FontStyle.Regular, fg, new RectangleF(0, H * 0.72f, W, H * 0.24f));
                return;
            }

            float y = 0.18f;
            if (style == OverlayStyle.Angry)
            {
                DrawAngryFace(g, new RectangleF(0, H * 0.05f, W, H * 0.25f));
                y = 0.31f;
            }
            float mx = W * 0.06f;
            DrawText(g, title, H * 0.055f, FontStyle.Bold, fg, new RectangleF(mx, H * y, W - 2 * mx, H * 0.11f));
            DrawText(g, big, H * 0.17f, FontStyle.Bold, fg, new RectangleF(mx, H * (y + 0.1f), W - 2 * mx, H * 0.26f));
            DrawText(g, sub, H * 0.028f, FontStyle.Regular, fg, new RectangleF(mx, H * (y + 0.37f), W - 2 * mx, H * (0.95f - y - 0.37f)));
        }

        private void BuildCache(Size size)
        {
            DropCache();
            cache = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(cache))
            {
                var rect = new Rectangle(Point.Empty, size);
                if (style == OverlayStyle.Calm && background != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    DrawCover(g, background, rect);
                    using (var shade = new SolidBrush(Color.FromArgb(110, 0, 0, 0))) g.FillRectangle(shade, rect);
                }
                else
                {
                    Color top, bottom, fg;
                    GetPalette(style, out top, out bottom, out fg);
                    using (var brush = new LinearGradientBrush(new Rectangle(0, -1, size.Width, size.Height + 2), top, bottom, 90f))
                        g.FillRectangle(brush, rect);
                }
            }
        }

        private static void DrawCover(Graphics g, Image img, Rectangle dest)
        {
            float scale = Math.Max((float)dest.Width / img.Width, (float)dest.Height / img.Height);
            float w = img.Width * scale, h = img.Height * scale;
            g.DrawImage(img, dest.X + (dest.Width - w) / 2, dest.Y + (dest.Height - h) / 2, w, h);
        }

        private static void DrawText(Graphics g, string text, float px, FontStyle fs, Color color, RectangleF rect)
        {
            if (string.IsNullOrEmpty(text) || px < 1 || rect.Height < 1) return;
            using (var font = new Font("Segoe UI", px, fs, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord })
            {
                g.DrawString(text, font, brush, rect, sf);
            }
        }

        private static void DrawAngryFace(Graphics g, RectangleF area)
        {
            float d = Math.Min(area.Width, area.Height);
            float x = area.X + (area.Width - d) / 2, y = area.Y + (area.Height - d) / 2;
            Func<float, float, PointF> P = (px, py) => new PointF(x + d * px, y + d * py);
            Color ink = Color.FromArgb(70, 8, 8);

            using (var face = new SolidBrush(Color.FromArgb(255, 200, 61)))
                g.FillEllipse(face, x, y, d, d);
            using (var outline = new Pen(ink, d * 0.03f))
                g.DrawEllipse(outline, x, y, d, d);
            using (var eye = new SolidBrush(ink))
            {
                g.FillEllipse(eye, x + d * 0.29f, y + d * 0.42f, d * 0.1f, d * 0.12f);
                g.FillEllipse(eye, x + d * 0.61f, y + d * 0.42f, d * 0.1f, d * 0.12f);
            }
            using (var brow = new Pen(ink, d * 0.06f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(brow, P(0.20f, 0.28f), P(0.44f, 0.38f));
                g.DrawLine(brow, P(0.80f, 0.28f), P(0.56f, 0.38f));
                g.DrawArc(brow, x + d * 0.30f, y + d * 0.66f, d * 0.40f, d * 0.24f, 200, 140);
            }
        }

        private void DropCache()
        {
            if (cache != null) { cache.Dispose(); cache = null; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) DropCache();
            base.Dispose(disposing);
        }
    }
}
