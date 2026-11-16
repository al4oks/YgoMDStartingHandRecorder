using System.Runtime.InteropServices;
using System.Text;

namespace Ygo.Records;

public static class NativeMethods
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int count);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT rect, int size);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    public const uint PW_CLIENTONLY = 1;
    public const uint PW_RENDERFULLCONTENT = 2;

    public static List<(IntPtr Hwnd, string Title)> VisibleWindows()
    {
        var list = new List<(IntPtr, string)>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var sb = new StringBuilder(256);
            GetWindowText(h, sb, sb.Capacity);
            var title = sb.ToString();
            if (title.Length > 0) list.Add((h, title));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static Rectangle ClientRectOnScreen(IntPtr hwnd)
    {
        if (!GetClientRect(hwnd, out var r)) return Rectangle.Empty;
        var tl = new POINT { X = r.Left, Y = r.Top };
        var br = new POINT { X = r.Right, Y = r.Bottom };
        if (!ClientToScreen(hwnd, ref tl) || !ClientToScreen(hwnd, ref br)) return Rectangle.Empty;
        return Rectangle.FromLTRB(tl.X, tl.Y, br.X, br.Y);
    }

    public static Bitmap? CaptureWindow(IntPtr hwnd, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return null;
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, size.Width, size.Height);
        var old = SelectObject(mem, bmp);
        var ok = PrintWindow(hwnd, mem, PW_CLIENTONLY | PW_RENDERFULLCONTENT);
        var err = Marshal.GetLastWin32Error();
        SelectObject(mem, old);
        DeleteDC(mem);
        ReleaseDC(IntPtr.Zero, screen);
        var dbg = Environment.GetEnvironmentVariable("YGO_DEBUGSHOT");
        if (!ok)
        {
            if (dbg != null) File.AppendAllText(Path.Combine(dbg, "capture.log"), $"printwindow=false err={err} size={size}\n");
            DeleteObject(bmp);
            return null;
        }
        var img = (Bitmap)Image.FromHbitmap(bmp);
        DeleteObject(bmp);
        var blank = IsBlank(img);
        if (dbg != null) File.AppendAllText(Path.Combine(dbg, "capture.log"), $"printwindow=true blank={blank} size={size} px0={img.GetPixel(0, 0)}\n");
        if (blank) { img.Dispose(); return null; }
        return img;
    }

    private static bool IsBlank(Bitmap b)
    {
        for (var y = 0; y < 5; y++)
        for (var x = 0; x < 5; x++)
        {
            var p = b.GetPixel(x * (b.Width - 1) / 4, y * (b.Height - 1) / 4);
            if (p.R != 0 || p.G != 0 || p.B != 0) return false;
        }
        return true;
    }
}