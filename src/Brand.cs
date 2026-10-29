using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Afterimage
{
    internal static class Brand
    {
        internal static readonly Color Accent = Color.FromArgb(255, 102, 124);
        internal static readonly Color Frost = Color.FromArgb(218, 226, 248);
        internal static void Draw(Graphics g, RectangleF bounds, Color? signal = null, float opacity = 1)
        {
            var saved = g.Save(); g.TranslateTransform(bounds.X, bounds.Y); g.ScaleTransform(bounds.Width / 32, bounds.Height / 32);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var back = IslandForm.Round(new RectangleF(3, 3, 20, 20), 6);
            using var backInk = new SolidBrush(Color.FromArgb((int)(80 * opacity), 149, 164, 204)); g.FillPath(backInk, back);
            using var mid = IslandForm.Round(new RectangleF(7, 7, 20, 20), 6);
            using var midInk = new SolidBrush(Color.FromArgb((int)(170 * opacity), 149, 164, 204)); g.FillPath(midInk, mid);
            using var front = IslandForm.Round(new RectangleF(11, 11, 18, 18), 5);
            using var light = new LinearGradientBrush(new RectangleF(11, 11, 18, 18), Color.FromArgb((int)(255 * opacity), Frost), Color.FromArgb((int)(255 * opacity), 173, 189, 224), 90); g.FillPath(light, front);
            using var dot = new SolidBrush(Color.FromArgb((int)(255 * opacity), signal ?? Accent)); g.FillEllipse(dot, 17, 17, 6, 6);
            g.Restore(saved);
        }
        internal static Icon Icon(Color? signal = null)
        {
            using var bitmap = new Bitmap(64, 64);
            using (var g = Graphics.FromImage(bitmap)) Draw(g, new RectangleF(0, 0, 64, 64), signal);
            IntPtr handle = bitmap.GetHicon();
            try { using var icon = System.Drawing.Icon.FromHandle(handle); return (Icon)icon.Clone(); }
            finally { DestroyIcon(handle); }
        }
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    }
}
