using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Afterimage
{
    // Diagnostic decoder: verifies finalized clips through Windows' own H.264 decoder.
    internal static class Validation
    {
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IntPtr reader);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetType(IntPtr p, int stream, IntPtr reserved, IntPtr type);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetMediaType(IntPtr p, int stream, out IntPtr type);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetU64(IntPtr p, ref Guid key, out long value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetU32(IntPtr p, ref Guid key, out int value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Read(IntPtr p, int stream, int control, out int actualStream, out int flags, out long timestamp, out IntPtr sample);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Buffer(IntPtr p, out IntPtr buffer);
        internal static int Run(string folder)
        {
            List<object> results = new List<object>(); int code = 0;
            try {
                MF.Check(MF.MFStartup(0x00020070, 0));
                foreach (string file in Directory.GetFiles(folder, "clip-*.mp4")) results.Add(Decode(file));
                if (results.Count == 0) throw new Exception("No clips found.");
            } catch (Exception e) { results.Add(new { error = e.ToString() }); code = 1; }
            finally { MF.MFShutdown(); }
            File.WriteAllText(Path.Combine(folder, "validation.json"), Json.Encode(new { passed = code == 0, clips = results })); return code;
        }
        private static object Decode(string file)
        {
            IntPtr p; MF.Check(MF.MFCreateAttributes(out p, 1)); Com reader;
            using (Com a = new Com(p)) { a.U32("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d", 1); MF.Check(MFCreateSourceReaderFromURL(file, a.P, out p)); reader = new Com(p); }
            using (reader) {
                MF.Check(MF.MFCreateMediaType(out p));
                using (Com type = new Com(p)) { type.GuidValue(MF.Major, MF.Video); type.GuidValue(MF.Subtype, MF.RGB32); MF.Check(reader.Fn<SetType>(7)(reader.P, -4, IntPtr.Zero, type.P)); }
                MF.Check(reader.Fn<GetMediaType>(6)(reader.P, -4, out p)); int width, height, stride;
                using (Com current = new Com(p)) { Guid key = new Guid(MF.FrameSize); long dimensions; MF.Check(current.Fn<GetU64>(8)(current.P, ref key, out dimensions)); width = (int)(dimensions >> 32); height = (int)(dimensions & 0xffffffff); key = new Guid(MF.Stride); if (current.Fn<GetU32>(7)(current.P, ref key, out stride) < 0) stride = -width * 4; }
                int frames = 0; long previous = -1, last = 0;
                while (true) {
                    int stream, flags; long timestamp;
                    MF.Check(reader.Fn<Read>(9)(reader.P, -4, 0, out stream, out flags, out timestamp, out p));
                    if (p != IntPtr.Zero) using (Com sample = new Com(p)) {
                        if (timestamp < previous) throw new Exception("Non-monotonic video timestamps."); previous = timestamp; last = timestamp;
                        if (frames == 0) {
                            MF.Check(sample.Fn<Buffer>(41)(sample.P, out p));
                            using (Com buffer = new Com(p)) {
                                IntPtr data; int max, length; MF.Check(buffer.Fn<MF.LockBuffer>(3)(buffer.P, out data, out max, out length));
                                try {
                                    IntPtr top = stride < 0 ? IntPtr.Add(data, (height - 1) * -stride) : data;
                                    using (Bitmap bmp = new Bitmap(width, height, stride, PixelFormat.Format32bppRgb, top)) bmp.Save(Path.ChangeExtension(file, ".first-frame.png"), ImageFormat.Png);
                                } finally { MF.Check(buffer.Fn<MF.Simple>(4)(buffer.P)); }
                            }
                        }
                        frames++;
                    }
                    if ((flags & 2) != 0) break;
                    if ((flags & 1) != 0) throw new Exception("Decoder reported a stream error.");
                }
                if (frames == 0) throw new Exception("Clip contains no decoded frames.");
                return new { file = Path.GetFileName(file), width = width, height = height, decoded_frames = frames, last_timestamp_ms = last / 10000.0 };
            }
        }
    }
}
