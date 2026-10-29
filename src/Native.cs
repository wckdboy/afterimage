using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace Afterimage
{
    // Windows SDK COM vtable order. All objects are owned and released on the capture thread.
    internal sealed class Com : IDisposable
    {
        internal IntPtr P;
        internal Com(IntPtr p) { P = p; }
        internal T Fn<T>(int slot) where T : class { return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(P), slot * IntPtr.Size), typeof(T)) as T; }
        internal void U32(string key, int value) { Guid g = new Guid(key); MF.Check(Fn<MF.SetU32>(21)(P, ref g, value)); }
        internal void U64(string key, long value) { Guid g = new Guid(key); MF.Check(Fn<MF.SetU64>(22)(P, ref g, value)); }
        internal void GuidValue(string key, string value) { Guid g = new Guid(key), v = new Guid(value); MF.Check(Fn<MF.SetGuid>(24)(P, ref g, ref v)); }
        public void Dispose() { if (P != IntPtr.Zero) { Marshal.Release(P); P = IntPtr.Zero; } }
    }
    internal static class MF
    {
        internal const string Major = "48eba18e-f8c9-4687-bf11-0a74c9f96a8f", Subtype = "f7e34c9a-42e8-4714-b74b-cb29d72c35e5";
        internal const string Video = "73646976-0000-0010-8000-00aa00389b71", H264 = "34363248-0000-0010-8000-00aa00389b71", RGB32 = "00000016-0000-0010-8000-00aa00389b71";
        internal const string FrameSize = "1652c33d-d6b2-4012-b834-72030849a37d", FrameRate = "c459a2e8-3d2c-4e44-b132-fee5156c7bb0";
        internal const string Aspect = "c6376a1e-8d0a-4027-be45-6d9a0ad39bb6", Interlace = "e2724bb8-e676-4806-b4b2-a8d6efb44ccd";
        internal const string Stride = "644b4e48-1e02-4516-b0eb-c01ca9d49ac6", Bitrate = "20332624-fb0d-4d9e-bd0d-cbf6786c102e";
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetU32(IntPtr p, ref Guid key, int value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetU64(IntPtr p, ref Guid key, long value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetGuid(IntPtr p, ref Guid key, ref Guid value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int Simple(IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int AddStream(IntPtr p, IntPtr type, out int stream);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int InputType(IntPtr p, int stream, IntPtr type, IntPtr attributes);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int Sample(IntPtr p, int stream, IntPtr sample);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int LockBuffer(IntPtr p, out IntPtr data, out int max, out int length);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetLength(IntPtr p, int length);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetTime(IntPtr p, long time);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int AddBuffer(IntPtr p, IntPtr buffer);
        [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFShutdown();
        [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFCreateAttributes(out IntPtr p, int count);
        [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFCreateMediaType(out IntPtr p);
        [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFCreateSample(out IntPtr p);
        [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFCreateMemoryBuffer(int length, out IntPtr p);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] internal static extern int MFCreateSinkWriterFromURL(string path, IntPtr stream, IntPtr attributes, out IntPtr writer);
        [DllImport("ole32.dll")] internal static extern int CoInitializeEx(IntPtr p, int flags);
        [DllImport("ole32.dll")] internal static extern void CoUninitialize();
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] internal static extern void Copy(IntPtr dest, IntPtr src, UIntPtr count);
        internal static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
        internal static long Pair(int a, int b) { return ((long)a << 32) | (uint)b; }
        internal static Com MediaType(int width, int height, int fps, bool output, int bitrate)
        {
            IntPtr p; Check(MFCreateMediaType(out p)); Com t = new Com(p);
            try {
                t.GuidValue(Major, Video); t.GuidValue(Subtype, output ? H264 : RGB32);
                t.U32(Interlace, 2); t.U64(FrameSize, Pair(width, height));
                t.U64(FrameRate, Pair(fps, 1)); t.U64(Aspect, Pair(1, 1));
                if (output) t.U32(Bitrate, bitrate); else t.U32(Stride, width * 4);
                return t;
            } catch { t.Dispose(); throw; }
        }
    }
    internal static class Win
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct CursorInfo { internal int Size, Flags; internal IntPtr Cursor; internal Point Position; }
        [StructLayout(LayoutKind.Sequential)] internal struct IconInfo { internal int IsIcon; internal uint X, Y; internal IntPtr Mask, Color; }
        [StructLayout(LayoutKind.Sequential)] internal struct BitmapInfo { internal uint Size; internal int Width, Height; internal ushort Planes, Bits; internal uint Compression, ImageSize; internal int XPels, YPels; internal uint Used, Important; }
        [StructLayout(LayoutKind.Sequential)] internal struct MouseData { internal Point Point; internal uint Data, Flags, Time; internal UIntPtr Extra; }
        internal delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] internal static extern bool StretchBlt(IntPtr dc, int x, int y, int w, int h, IntPtr source, int sx, int sy, int sw, int sh, uint rop);
        [DllImport("gdi32.dll")] internal static extern int StretchDIBits(IntPtr dc, int x, int y, int w, int h, int sx, int sy, int sw, int sh, IntPtr bits, ref BitmapInfo info, uint usage, uint rop);
        [DllImport("gdi32.dll")] internal static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] internal static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr old);
        [DllImport("user32.dll")] internal static extern bool GetCursorInfo(ref CursorInfo info);
        [DllImport("user32.dll")] internal static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
        [DllImport("user32.dll")] internal static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int w, int h, int step, IntPtr brush, int flags);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] internal static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll")] internal static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool GetDiskFreeSpaceEx(string path, out ulong available, out ulong total, out ulong free);
        internal static bool DesktopAvailable() { IntPtr d = OpenInputDesktop(0, false, 0x0100); if (d == IntPtr.Zero) return false; CloseDesktop(d); return true; }
    }
    internal sealed class ScreenCapture : IDisposable
    {
        private DesktopCapture desktop;
        internal string Backend { get; private set; } = "Compatibility";
        internal string FallbackReason { get; private set; }
        private IntPtr screen, dc, bitmap, old;
        internal IntPtr Pixels;
        internal readonly Rectangle Bounds;
        internal readonly int Width, Height;
        internal ScreenCapture(Rectangle bounds, int maxDimension, bool direct3d = false)
        {
            Bounds = bounds;
            double scale = Math.Min(1.0, (double)maxDimension / Math.Max(bounds.Width, bounds.Height));
            Width = Math.Max(2, (int)(bounds.Width * scale) / 2 * 2); Height = Math.Max(2, (int)(bounds.Height * scale) / 2 * 2);
            try {
                screen = Win.GetDC(IntPtr.Zero); dc = Win.CreateCompatibleDC(screen);
                Win.BitmapInfo info = new Win.BitmapInfo { Size = 40, Width = Width, Height = -Height, Planes = 1, Bits = 32 };
                bitmap = Win.CreateDIBSection(screen, ref info, 0, out Pixels, IntPtr.Zero, 0);
                if (screen == IntPtr.Zero || dc == IntPtr.Zero || bitmap == IntPtr.Zero) throw new Win32Exception("Could not allocate the screen capture buffer.");
                old = Win.SelectObject(dc, bitmap); Win.SetStretchBltMode(dc, 4); Win.SetBrushOrgEx(dc, 0, 0, IntPtr.Zero);
                if (direct3d) try { desktop = new DesktopCapture(bounds); } catch (Exception e) { FallbackReason = e.Message; }
            } catch { Dispose(); throw; }
        }
        internal void Capture(bool cursor)
        {
            bool copied = false, drawPointer = cursor;
            if (desktop != null) {
                try { copied = desktop.CopyTo(dc, Width, Height); if (copied) drawPointer = cursor && desktop.PointerSeparate; }
                catch (Exception e) { FallbackReason = e.Message; desktop.Dispose(); desktop = null; }
            }
            Backend = copied ? "Direct3D 11" : "Compatibility";
            if (!copied && !Win.StretchBlt(dc, 0, 0, Width, Height, screen, Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, 0x40CC0020)) throw new Win32Exception("Screen capture failed. The display may have changed or become unavailable.");
            if (!drawPointer) return;
            Win.CursorInfo ci = new Win.CursorInfo { Size = Marshal.SizeOf(typeof(Win.CursorInfo)) };
            if (!Win.GetCursorInfo(ref ci) || ci.Flags != 1 || !Bounds.Contains(ci.Position.X, ci.Position.Y)) return;
            Win.IconInfo ii;
            if (!Win.GetIconInfo(ci.Cursor, out ii)) return;
            try {
                double sx = (double)Width / Bounds.Width, sy = (double)Height / Bounds.Height;
                Win.DrawIconEx(dc, (int)((ci.Position.X - Bounds.X - ii.X) * sx), (int)((ci.Position.Y - Bounds.Y - ii.Y) * sy), ci.Cursor,
                    Math.Max(1, (int)(System.Windows.Forms.SystemInformation.CursorSize.Width * sx)), Math.Max(1, (int)(System.Windows.Forms.SystemInformation.CursorSize.Height * sy)), 0, IntPtr.Zero, 3);
            } finally { if (ii.Mask != IntPtr.Zero) Win.DeleteObject(ii.Mask); if (ii.Color != IntPtr.Zero) Win.DeleteObject(ii.Color); }
        }
        internal void DrawTestPattern(int frame)
        {
            using (Graphics g = Graphics.FromHdc(dc)) {
                g.Clear(Color.White); g.FillRectangle(Brushes.Red, 0, 0, Width / 2, Height / 2); g.FillRectangle(Brushes.Blue, 0, Height / 2, Width / 2, Height / 2);
                g.FillRectangle(Brushes.LimeGreen, Width / 2, 0, Width / 2, Height / 2); g.FillRectangle(Brushes.Gold, Width / 2, Height / 2, Width / 2, Height / 2);
                using (Font f = new Font("Segoe UI", 28)) g.DrawString("TOP · " + frame, f, Brushes.Black, Width / 2 + 30, 30);
            }
        }
        internal void SaveThumbnail(string path)
        {
            using var source = new Bitmap(Width, Height, Width * 4, System.Drawing.Imaging.PixelFormat.Format32bppRgb, Pixels);
            using var thumbnail = new Bitmap(384, Math.Max(1, Height * 384 / Width));
            using (var g = Graphics.FromImage(thumbnail)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(source, 0, 0, thumbnail.Width, thumbnail.Height); }
            thumbnail.Save(path, System.Drawing.Imaging.ImageFormat.Jpeg);
        }
        public void Dispose() { desktop?.Dispose(); desktop = null; if (dc != IntPtr.Zero && old != IntPtr.Zero) Win.SelectObject(dc, old); if (bitmap != IntPtr.Zero) Win.DeleteObject(bitmap); if (dc != IntPtr.Zero) Win.DeleteDC(dc); if (screen != IntPtr.Zero) Win.ReleaseDC(IntPtr.Zero, screen); bitmap = dc = screen = old = IntPtr.Zero; }
    }
}
