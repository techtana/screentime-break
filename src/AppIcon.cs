using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace ScreenBreak
{
    /// <summary>The green clock icon, shared by the tray icon and the app's windows.</summary>
    internal static class AppIcon
    {
        /// <summary>The icon at the size closest to <paramref name="size"/>. It keeps every size
        /// from the .ico, so a window can still pick its own small and large versions.</summary>
        public static Icon Load(Size size)
        {
            using (Stream ico = typeof(AppIcon).Assembly.GetManifestResourceStream("ScreenBreak.ico"))
                if (ico != null) return new Icon(ico, size);

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
