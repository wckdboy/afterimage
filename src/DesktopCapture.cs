using System;
using System.Drawing;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace Afterimage
{
    // One reusable staging texture. No frame queue and no allocation per captured image.
    // The encoder still accepts CPU BGRA: this is not an end-to-end zero-copy pipeline.
    internal sealed class DesktopCapture : IDisposable
    {
        private ID3D11Device device;
        private ID3D11DeviceContext context;
        private IDXGIOutputDuplication duplication;
        private ID3D11Texture2D staging;
        private bool pointerSeparate;
        private readonly Rectangle bounds;
        internal bool PointerSeparate { get { return pointerSeparate; } }

        internal DesktopCapture(Rectangle bounds)
        {
            this.bounds = bounds;
            try {
                using var factory = CreateDXGIFactory1<IDXGIFactory1>();
                for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++) using (adapter) {
                    for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++) using (output) {
                        var d = output.Description;
                        if (d.DesktopCoordinates.Left != bounds.Left || d.DesktopCoordinates.Top != bounds.Top ||
                            d.DesktopCoordinates.Right != bounds.Right || d.DesktopCoordinates.Bottom != bounds.Bottom) continue;
                        if (d.Rotation != ModeRotation.Identity) throw new NotSupportedException("Rotated display uses compatibility capture.");
                        D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                            new[] { FeatureLevel.Level_11_0 }, out device, out context).CheckError();
                        using var output1 = output.QueryInterface<IDXGIOutput1>();
                        duplication = output1.DuplicateOutput(device);
                        return;
                    }
                }
                throw new NotSupportedException("Display is not available through DXGI.");
            } catch { Dispose(); throw; }
        }

        internal bool CopyTo(IntPtr dc, int width, int height)
        {
            var result = duplication.AcquireNextFrame(staging == null ? 100u : 0u, out var info, out var resource);
            if (result.Code == unchecked((int)0x887A0027)) { // DXGI_ERROR_WAIT_TIMEOUT: unchanged desktop
                if (staging == null) return false;
            } else {
                result.CheckError();
                try {
                    using (resource) using (var texture = resource.QueryInterface<ID3D11Texture2D>()) {
                        if (staging == null) {
                            var d = texture.Description;
                            if (d.Width != bounds.Width || d.Height != bounds.Height || d.Format != Format.B8G8R8A8_UNorm)
                                throw new NotSupportedException("Display surface requires compatibility capture.");
                            d.Usage = ResourceUsage.Staging; d.BindFlags = BindFlags.None;
                            d.CPUAccessFlags = CpuAccessFlags.Read; d.MiscFlags = ResourceOptionFlags.None;
                            staging = device.CreateTexture2D(d);
                        }
                        context.CopyResource(staging, texture);
                    }
                    if (info.LastMouseUpdateTime != 0) pointerSeparate = info.PointerPosition.Visible;
                } finally { duplication.ReleaseFrame().CheckError(); }
            }
            context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped).CheckError();
            try {
                // RowPitch can be wider than the visible desktop. Pass that stride to GDI.
                var bitmap = new Win.BitmapInfo { Size = 40, Width = checked((int)mapped.RowPitch / 4), Height = -bounds.Height, Planes = 1, Bits = 32 };
                int copied = Win.StretchDIBits(dc, 0, 0, width, height, 0, 0, bounds.Width, bounds.Height,
                    mapped.DataPointer, ref bitmap, 0, 0x00CC0020);
                if (copied == 0 || copied == -1) throw new InvalidOperationException("Could not copy the desktop surface.");
                return true;
            } finally { context.Unmap(staging, 0); }
        }

        public void Dispose() { staging?.Dispose(); duplication?.Dispose(); context?.Dispose(); device?.Dispose(); staging = null; duplication = null; context = null; device = null; }
    }
}
