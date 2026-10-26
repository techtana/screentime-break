// Regenerates assets\ScreenBreak.ico (16/24/32/48 px DIB frames + 256 px PNG frame).
// Build and run: see build.ps1 -Icon
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class IconGen
{
    private static void Main(string[] args)
    {
        int[] sizes = { 16, 24, 32, 48, 256 };
        var frames = new List<byte[]>();
        foreach (int s in sizes)
        {
            using (Bitmap bmp = Draw(s))
                frames.Add(s == 256 ? Png(bmp) : Dib(bmp));
        }

        using (var w = new BinaryWriter(File.Create(args[0])))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] % 256)); w.Write((byte)(sizes[i] % 256));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(frames[i].Length); w.Write(offset);
                offset += frames[i].Length;
            }
            foreach (byte[] f in frames) w.Write(f);
        }
    }

    // Same design as the tray icon: sage disc, cream ring, clock hands.
    private static Bitmap Draw(int s)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = s / 32f;
            using (var fill = new SolidBrush(Color.FromArgb(63, 125, 92))) g.FillEllipse(fill, 1 * k, 1 * k, 30 * k, 30 * k);
            using (var pen = new Pen(Color.FromArgb(243, 235, 211), Math.Max(1.2f, 2.5f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawEllipse(pen, 3 * k, 3 * k, 26 * k, 26 * k);
                g.DrawLine(pen, 16 * k, 16 * k, 16 * k, 8 * k);
                g.DrawLine(pen, 16 * k, 16 * k, 22 * k, 19 * k);
            }
        }
        return bmp;
    }

    private static byte[] Png(Bitmap bmp)
    {
        using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    private static byte[] Dib(Bitmap bmp)
    {
        int s = bmp.Width;
        int maskStride = ((s + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32);
            w.Write(0); w.Write(s * s * 4 + maskStride * s); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = s - 1; y >= 0; y--)
                for (int x = 0; x < s; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A);
                }
            w.Write(new byte[maskStride * s]); // alpha channel does the masking
            return ms.ToArray();
        }
    }
}
