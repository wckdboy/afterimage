using System;
using Vortice.MediaFoundation;
using static Vortice.MediaFoundation.MediaFactory;

namespace Afterimage
{
    internal sealed class VideoWriter : IDisposable
    {
        private IMFSinkWriter writer;
        private readonly int stream, bytes;
        internal VideoWriter(string path, int width, int height, int fps, int bitrate, bool hardware)
        {
            bytes = checked(width * height * 4);
            try {
                using var attributes = MFCreateAttributes(3);
                attributes.Set(new Guid("a634a91c-822b-41b9-a494-4de4643612b0"), hardware ? 1u : 0u);
                attributes.Set(new Guid("9c27891a-ed7a-40e1-88e8-b22727a024ee"), 1u);
                // Default sink throttling provides back-pressure and bounds encoder memory.
                writer = MFCreateSinkWriterFromURL(path, null, attributes);
                using var output = Type(width, height, fps, bitrate, true);
                stream = writer.AddStream(output);
                using var input = Type(width, height, fps, bitrate, false);
                writer.SetInputMediaType(stream, input, null);
                writer.BeginWriting();
            } catch { writer?.Dispose(); writer = null; throw; }
        }
        private static IMFMediaType Type(int w, int h, int fps, int bitrate, bool output)
        {
            var t = MFCreateMediaType();
            try {
                t.Set(new Guid(MF.Major), new Guid(MF.Video));
                t.Set(new Guid(MF.Subtype), new Guid(output ? MF.H264 : MF.RGB32));
                t.Set(new Guid(MF.Interlace), 2u);
                t.Set(new Guid(MF.FrameSize), (ulong)MF.Pair(w, h));
                t.Set(new Guid(MF.FrameRate), (ulong)MF.Pair(fps, 1));
                t.Set(new Guid(MF.Aspect), (ulong)MF.Pair(1, 1));
                t.Set(new Guid(output ? MF.Bitrate : MF.Stride), (uint)(output ? bitrate : w * 4));
                return t;
            } catch { t.Dispose(); throw; }
        }
        internal void Write(IntPtr pixels, long time100ns, long duration100ns)
        {
            using var buffer = MFCreateMemoryBuffer(bytes);
            buffer.Lock(out var data, out _, out _);
            try { MF.Copy(data, pixels, (UIntPtr)(uint)bytes); } finally { buffer.Unlock(); }
            buffer.CurrentLength = bytes;
            using var sample = MFCreateSample();
            sample.AddBuffer(buffer);
            sample.SampleTime = time100ns; sample.SampleDuration = duration100ns;
            writer.WriteSample(stream, sample);
        }
        public void Dispose()
        {
            var w = writer; writer = null;
            if (w == null) return;
            try { w.Finalize(); } finally { w.Dispose(); }
        }
    }
}
