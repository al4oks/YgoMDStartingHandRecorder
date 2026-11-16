using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace Ygo.Records;

// Unity/DX 窗口不走 GDI 离屏渲染，PrintWindow 全黑;WGC 读 DWM 合成表面，遮挡时也可用
public static class WgcCapture
{
    private static IDirect3DDevice? _device;

    public static async Task<Bitmap?> CaptureAsync(IntPtr hwnd)
    {
        var dbg = Environment.GetEnvironmentVariable("YGO_DEBUGSHOT");
        try
        {
            var item = CreateItem(hwnd);
            if (item == null || item.Size.Width < 8 || item.Size.Height < 8) return null;
            using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                GetDevice(), DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
            using var session = pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;
            session.StartCapture();
            Direct3D11CaptureFrame? frame = null;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 3000)
            {
                frame = pool.TryGetNextFrame();
                if (frame != null) break;
                Thread.Sleep(30);
            }
            if (frame == null)
            {
                if (dbg != null) File.AppendAllText(Path.Combine(dbg, "capture.log"), "wgc: no frame in 3s\n");
                return null;
            }
            using (frame)
            {
                using var sb = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
                var bmp = ToBitmap(sb);
                bmp = CropToClient(hwnd, bmp);
                if (dbg != null) File.AppendAllText(Path.Combine(dbg, "capture.log"), $"wgc: ok {bmp.Width}x{bmp.Height}\n");
                return bmp;
            }
        }
        catch (Exception ex)
        {
            if (dbg != null) File.AppendAllText(Path.Combine(dbg, "capture.log"), $"wgc: {ex.GetType().Name} {ex.Message}\n");
            return null;
        }
    }

    // WGC 表面含边框/标题栏，按窗口 rect 与客户区 rect 之差裁掉非客户区
    private static Bitmap CropToClient(IntPtr hwnd, Bitmap bmp)
    {
        var client = NativeMethods.ClientRectOnScreen(hwnd);
        if (client.Width <= 0 || bmp.Width == client.Width && bmp.Height == client.Height) return bmp;
        // GetWindowRect 含 Win11 不可见 resize 边框，DWM 可见边框才与 WGC 表面对齐
        var hr = NativeMethods.DwmGetWindowAttribute(hwnd, 9, out var wr, Marshal.SizeOf<NativeMethods.RECT>());
        if (hr != 0 && !NativeMethods.GetWindowRect(hwnd, out wr)) return bmp;
        var x = client.X - wr.Left;
        var y = client.Y - wr.Top;
        if (x < 0 || y < 0 || x + client.Width > bmp.Width || y + client.Height > bmp.Height) return bmp;
        var cropped = bmp.Clone(new Rectangle(x, y, client.Width, client.Height), bmp.PixelFormat);
        bmp.Dispose();
        return cropped;
    }

    private static Bitmap ToBitmap(SoftwareBitmap sb)
    {
        using var buf = sb.LockBuffer(BitmapBufferAccessMode.Read);
        using var reference = buf.CreateReference();
        var abi = WinRT.MarshalInterface<Windows.Foundation.IMemoryBufferReference>.FromManaged(reference);
        IntPtr ptr;
        uint capacity;
        try
        {
            var access = (IMemoryBufferByteAccess)Marshal.GetObjectForIUnknown(abi);
            access.GetBuffer(out ptr, out capacity);
        }
        finally
        {
            Marshal.Release(abi);
        }
        var plane = buf.GetPlaneDescription(0);
        var w = sb.PixelWidth;
        var h = sb.PixelHeight;
        var src = new byte[Math.Min(capacity, (uint)(plane.StartIndex + plane.Stride * h))];
        Marshal.Copy(ptr, src, 0, src.Length);
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (var y = 0; y < h; y++)
            Marshal.Copy(src, plane.StartIndex + y * plane.Stride, data.Scan0 + y * data.Stride, w * 4);
        bmp.UnlockBits(data);
        return bmp;
    }

    private static IDirect3DDevice GetDevice()
    {
        if (_device != null) return _device;
        var levels = new[] { 0xb000, 0xa000, 0x9100 };
        var hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, levels, (uint)levels.Length, 7,
            out var dev, out _, out var ctx);
        if (hr != 0) throw new COMException("D3D11CreateDevice failed", hr);
        Marshal.Release(ctx);
        var iid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"); // IDXGIDevice
        Marshal.QueryInterface(dev, iid, out var dxgi);
        Marshal.Release(dev);
        hr = CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var insp);
        Marshal.Release(dxgi);
        if (hr != 0) throw new COMException("CreateDirect3D11DeviceFromDXGIDevice failed", hr);
        _device = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(insp);
        Marshal.Release(insp);
        return _device;
    }

    private static GraphicsCaptureItem? CreateItem(IntPtr hwnd)
    {
        const string cls = "Windows.Graphics.Capture.GraphicsCaptureItem";
        if (WindowsCreateString(cls, cls.Length, out var hs) != 0) return null;
        var iid = typeof(IGraphicsCaptureItemInterop).GUID;
        var hr = RoGetActivationFactory(hs, ref iid, out var fac);
        WindowsDeleteString(hs);
        if (hr != 0) return null;
        var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(fac);
        Marshal.Release(fac);
        var itemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760"); // IGraphicsCaptureItem
        var ptr = interop.CreateForWindow(hwnd, ref itemIid);
        if (ptr == IntPtr.Zero) return null;
        var item = GraphicsCaptureItem.FromAbi(ptr);
        Marshal.Release(ptr);
        return item;
    }

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr hwnd, ref Guid iid);
        IntPtr CreateForMonitor(IntPtr hmonitor, ref Guid iid);
    }

    [ComImport, Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        void GetBuffer(out IntPtr buffer, out uint capacity);
    }

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags,
        int[] featureLevels, uint featureLevelsCount, uint sdkVersion, out IntPtr device, out int featureLevel, out IntPtr context);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("combase.dll")]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string source, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);
}