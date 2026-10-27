using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScreenBreak
{
    /// <summary>One overlay window per monitor, rebuilt automatically when displays change.</summary>
    internal class OverlaySet
    {
        private static int displayVersion;

        static OverlaySet()
        {
            SystemEvents.DisplaySettingsChanged += delegate { displayVersion++; };
        }

        private readonly List<OverlayForm> forms = new List<OverlayForm>();
        private readonly List<Rectangle> rects = new List<Rectangle>();
        private Screen[] screens = new Screen[0];
        private int builtVersion = -1;

        public bool Visible { get { return forms.Count > 0; } }

        /// <summary>Rectangle of the overlay on each monitor (where its buttons go).</summary>
        public IList<Rectangle> Rects { get { return rects; } }

        private void Ensure()
        {
            if (forms.Count > 0 && builtVersion == displayVersion) return;
            Hide();
            builtVersion = displayVersion;
            screens = Screen.AllScreens;
            foreach (Screen s in screens)
            {
                forms.Add(new OverlayForm());
                rects.Add(Rectangle.Empty);
            }
        }

        /// <summary>Centered overlay covering <paramref name="coverage"/> (0..1) of each screen's area.</summary>
        public void Update(OverlayStyle style, string title, string big, string sub, double opacity, double coverage, Image bg)
        {
            Ensure();
            coverage = Math.Round(Math.Max(0.05, Math.Min(1, coverage)) / 0.005) * 0.005; // fewer resizes
            double k = Math.Sqrt(coverage);
            for (int i = 0; i < forms.Count; i++)
            {
                Rectangle b = screens[i].Bounds;
                int w = (int)Math.Round(b.Width * k), h = (int)Math.Round(b.Height * k);
                var r = new Rectangle(b.X + (b.Width - w) / 2, b.Y + (b.Height - h) / 2, w, h);
                Apply(i, r, style, title, big, sub, opacity, bg);
            }
        }

        /// <summary>Small fixed-size panel in a corner of each screen.</summary>
        public void UpdateCorner(string title, string big, string sub, string corner, double opacity)
        {
            Ensure();
            for (int i = 0; i < forms.Count; i++)
            {
                Rectangle wa = screens[i].WorkingArea;
                int h = Math.Max(110, (int)(screens[i].Bounds.Height * 0.11));
                int w = (int)(h * 2.6);
                int m = h / 5;
                int x = corner.EndsWith("Left") ? wa.Left + m : wa.Right - w - m;
                int y = corner.StartsWith("Top") ? wa.Top + m : wa.Bottom - h - m;
                Apply(i, new Rectangle(x, y, w, h), OverlayStyle.Corner, title, big, sub, opacity, null);
            }
        }

        private void Apply(int i, Rectangle r, OverlayStyle style, string title, string big, string sub, double opacity, Image bg)
        {
            forms[i].SetContent(style, title, big, sub, bg);
            forms[i].SetOpacity(opacity);
            forms[i].Place(r);
            rects[i] = r;
        }

        public void BringToTop()
        {
            foreach (OverlayForm f in forms) Native.BringToTop(f.Handle);
        }

        public void Hide()
        {
            foreach (OverlayForm f in forms)
            {
                f.Close();
                f.Dispose();
            }
            forms.Clear();
            rects.Clear();
        }
    }
}
