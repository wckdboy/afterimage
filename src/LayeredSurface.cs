using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Afterimage
{
    // A reusable premultiplied-alpha DIB. No color key, window region or per-frame HBITMAP allocation.
    internal sealed class LayeredSurface : IDisposable
    {
        private IntPtr dc, dib, previous;
        private Bitmap pixels, supersampled;
        internal void EnsureCapacity(Size capacity)
        {
            if (pixels != null && pixels.Width == capacity.Width && pixels.Height == capacity.Height) return;
            Dispose();
            dc = CreateCompatibleDC(IntPtr.Zero);
            var info = new BitmapInfo { Size = 40, Width = capacity.Width, Height = -capacity.Height, Planes = 1, BitCount = 32 };
            dib = CreateDIBSection(dc, ref info, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (dc == IntPtr.Zero || dib == IntPtr.Zero) { int error = Marshal.GetLastWin32Error(); Dispose(); throw new Win32Exception(error); }
            previous = SelectObject(dc, dib);
            pixels = new Bitmap(capacity.Width, capacity.Height, capacity.Width * 4, PixelFormat.Format32bppPArgb, bits);
            supersampled = new Bitmap(capacity.Width * 2, capacity.Height * 2, PixelFormat.Format32bppPArgb);
        }
        internal void Draw(Size size, float scale, Action<Graphics> paint)
        {
            using (var g = Graphics.FromImage(supersampled)) {
                g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.ScaleTransform(scale * 2, scale * 2); paint(g);
            }
            using (var g = Graphics.FromImage(pixels)) {
                g.CompositingMode = CompositingMode.SourceCopy; g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(supersampled, new Rectangle(Point.Empty, size), 0, 0, size.Width * 2, size.Height * 2, GraphicsUnit.Pixel);
            }
        }
        internal void Present(IntPtr handle, Point position, Size size)
        {
            var source = Point.Empty; var blend = new Blend { SourceConstantAlpha = 255, AlphaFormat = 1 };
            if (!UpdateLayeredWindow(handle, IntPtr.Zero, ref position, ref size, dc, ref source, 0, ref blend, 2)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        internal Bitmap Snapshot(Size size)
        {
            // Own these pixels independently of the DIB's resize/disposal lifetime.
            var copy = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(copy)) {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(pixels, new Rectangle(Point.Empty, size), 0, 0, size.Width, size.Height, GraphicsUnit.Pixel);
                g.Flush(FlushIntention.Sync);
            }
            return copy;
        }
        public void Dispose()
        {
            pixels?.Dispose(); pixels = null; supersampled?.Dispose(); supersampled = null;
            if (previous != IntPtr.Zero && dc != IntPtr.Zero) SelectObject(dc, previous);
            if (dib != IntPtr.Zero) DeleteObject(dib); if (dc != IntPtr.Zero) DeleteDC(dc);
            previous = dib = dc = IntPtr.Zero;
        }
        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { internal uint Size; internal int Width, Height; internal ushort Planes, BitCount; internal uint Compression, ImageSize; internal int XPels, YPels; internal uint Used, Important, Color; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { internal byte Operation, Flags, SourceConstantAlpha, AlphaFormat; }
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr destinationDc, ref Point destination, ref Size size, IntPtr sourceDc, ref Point source, uint colorKey, ref Blend blend, uint flags);
    }
}
