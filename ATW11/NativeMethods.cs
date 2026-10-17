using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ATW11;

/// <summary>一次查询得到的窗口几何信息（全部是物理像素 / 屏幕坐标）。</summary>
public readonly struct WindowMetrics
{
    public readonly IntPtr Hwnd;
    public readonly Rect Frame;        // DWM 可见边框（不含阴影）
    public readonly Rect Client;       // 客户区
    public readonly bool IsMaximized;

    public WindowMetrics(IntPtr hwnd, Rect frame, Rect client, bool isMaximized)
    {
        Hwnd = hwnd;
        Frame = frame;
        Client = client;
        IsMaximized = isMaximized;
    }

    public bool SameShape(in WindowMetrics other)
    {
        return Hwnd == other.Hwnd
               && Frame == other.Frame
               && Client == other.Client
               && IsMaximized == other.IsMaximized;
    }
}

/// <summary>
/// 所有 Win32 调用集中在这里。对外只暴露"找资源管理器窗口 / 取几何 / 取标题图标 / 操作窗口"这几个语义化方法，
/// 方便后面继续加功能时不去碰 P/Invoke 细节。
/// </summary>
internal static class NativeMethods
{
    // ── 消息 / 样式常量 ──
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_CLOSE = 0x0010;
    public const int WM_GETICON = 0x007F;

    public const int HTTRANSPARENT = -1;
    public const int HTCLIENT = 1;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_MINIMIZE = 6;
    private const int SW_MAXIMIZE = 3;
    private const int SW_RESTORE = 9;

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int DWMWA_CLOAKED = 14;

    private const uint VK_ESCAPE = 0x1B;
    public const int HOTKEY_ID_ESC = 0xA711;

    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const int GCLP_HICON = -14;

    private const int SRCCOPY = 0x00CC0020;
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    // ── 结构 ──
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // ── user32 ──
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(int uiAction, int uiParam, StringBuilder pvParam, int fWinIni);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    // ── dwmapi ──
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    // ── gdi32 ──
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    // ════════════════════════ 对外语义方法 ════════════════════════

    public static int ScreenWidth => GetSystemMetrics(SM_CXSCREEN);

    public static int ScreenHeight => GetSystemMetrics(SM_CYSCREEN);

    public static double GetDpiScale(IntPtr hwnd)
    {
        uint dpi = GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    /// <summary>全屏铺满主显示器（物理像素），并设置工具窗口样式（不进 Alt+Tab）。</summary>
    public static void SetupOverlayWindow(IntPtr hwnd, double dpiScale)
    {
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        _ = dpiScale;

        int w = ScreenWidth;
        int h = ScreenHeight;

        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, w, h, SWP_NOOWNERZORDER | SWP_FRAMECHANGED);
        _ = dpiScale;
    }

    public static bool RegisterEscapeHotkey(IntPtr hwnd)
    {
        return RegisterHotKey(hwnd, HOTKEY_ID_ESC, 0, VK_ESCAPE);
    }

    public static void UnregisterEscapeHotkey(IntPtr hwnd)
    {
        UnregisterHotKey(hwnd, HOTKEY_ID_ESC);
    }

    /// <summary>找出所有"真正的文件资源管理器窗口"。</summary>
    public static List<IntPtr> FindExplorerWindows()
    {
        var found = new List<IntPtr>();

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
            {
                return true;
            }

            string cls = GetClassNameText(hWnd);
            if (cls != "CabinetWClass" && cls != "ExploreWClass")
            {
                return true;
            }

            if (!IsExplorerProcess(hWnd))
            {
                return true;
            }

            if (IsCloaked(hWnd))
            {
                return true;
            }

            if (!TryGetMetrics(hWnd, out WindowMetrics m))
            {
                return true;
            }

            // 太小的窗口（比如某些辅助窗体）不要
            if (m.Frame.Width < 240 || m.Frame.Height < 160)
            {
                return true;
            }

            found.Add(hWnd);
            return true;
        }, IntPtr.Zero);

        return found;
    }

    public static bool IsExplorerProcess(IntPtr hWnd)
    {
        GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)pid);
            return string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetMetrics(IntPtr hWnd, out WindowMetrics metrics)
    {
        metrics = default;

        RECT frame;
        int hr = DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out frame, Marshal.SizeOf<RECT>());
        if (hr != 0)
        {
            if (!GetWindowRect(hWnd, out frame))
            {
                return false;
            }
        }

        if (!GetClientRect(hWnd, out RECT client))
        {
            return false;
        }

        POINT origin = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(hWnd, ref origin))
        {
            return false;
        }

        var frameRect = new Rect(frame.Left, frame.Top,
            Math.Max(1, frame.Width), Math.Max(1, frame.Height));
        var clientRect = new Rect(origin.X, origin.Y,
            Math.Max(1, client.Width), Math.Max(1, client.Height));

        metrics = new WindowMetrics(hWnd, frameRect, clientRect, IsZoomed(hWnd));
        return true;
    }

    public static string GetTitle(IntPtr hWnd)
    {
        int length = GetWindowTextLength(hWnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(length + 2);
        GetWindowText(hWnd, sb, sb.Capacity);
        string title = sb.ToString();

        // Win11 的标题形如 "桌面 - 文件资源管理器"，Win7 风格只保留 "桌面"
        string[] suffixes =
        {
            " - 文件资源管理器",
            " - File Explorer",
            " - Windows 资源管理器",
            " - Windows Explorer",
        };

        foreach (string suffix in suffixes)
        {
            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                title = title[..^suffix.Length];
                break;
            }
        }

        return title.Trim();
    }

    public static ImageSource? GetWindowIcon(IntPtr hWnd)
    {
        IntPtr hIcon = SendMessage(hWnd, WM_GETICON, new IntPtr(ICON_BIG), IntPtr.Zero);
        if (hIcon == IntPtr.Zero)
        {
            hIcon = SendMessage(hWnd, WM_GETICON, new IntPtr(ICON_SMALL), IntPtr.Zero);
        }

        if (hIcon == IntPtr.Zero)
        {
            try
            {
                hIcon = GetClassLongPtr(hWnd, GCLP_HICON);
            }
            catch
            {
                hIcon = IntPtr.Zero;
            }
        }

        if (hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            // 图标句柄属于目标窗口，读完即弃，绝不去 Destroy 它
            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    public static void MinimizeWindow(IntPtr hWnd) => ShowWindow(hWnd, SW_MINIMIZE);

    public static void ToggleMaximize(IntPtr hWnd)
    {
        ShowWindow(hWnd, IsZoomed(hWnd) ? SW_RESTORE : SW_MAXIMIZE);
    }

    public static void CloseWindow(IntPtr hWnd) => PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    /// <summary>把窗口移动到指定位置（只动位置，不动大小 / Z 序 / 激活状态）。</summary>
    public static void MoveWindowTo(IntPtr hWnd, int x, int y)
    {
        SetWindowPos(hWnd, IntPtr.Zero, x, y, 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    public static void CaptureMouse(IntPtr hWnd) => SetCapture(hWnd);

    public static void ReleaseMouseCapture() => ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    /// <summary>按 Z 序（最上面优先）列出所有顶层窗口。</summary>
    public static List<IntPtr> TopLevelWindows()
    {
        var list = new List<IntPtr>();

        EnumWindows((hWnd, _) =>
        {
            list.Add(hWnd);
            return true;
        }, IntPtr.Zero);

        return list;
    }

    public static bool IsVisible(IntPtr hWnd) => IsWindowVisible(hWnd);

    /// <summary>这个窗口是不是我们自己进程的（覆盖层 / 控制中心 / 隐藏辅助窗口）。</summary>
    public static bool IsOwnProcessWindow(IntPtr hWnd)
    {
        GetWindowThreadProcessId(hWnd, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    public static bool IsCloakedWindow(IntPtr hWnd) => IsCloaked(hWnd);

    public static bool TryGetWindowBounds(IntPtr hWnd, out System.Windows.Rect bounds)
    {
        bounds = default;
        if (!GetWindowRect(hWnd, out RECT r))
        {
            return false;
        }

        bounds = new System.Windows.Rect(r.Left, r.Top, Math.Max(1, r.Width), Math.Max(1, r.Height));
        return true;
    }

    /// <summary>窗口"看得见"的那部分边框（DWM 扩展边框，不含阴影和不可见缩放边），用于遮挡裁剪。</summary>
    public static bool TryGetVisibleBounds(IntPtr hWnd, out System.Windows.Rect bounds)
    {
        RECT r;
        int hr = DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf<RECT>());
        if (hr != 0)
        {
            if (!GetWindowRect(hWnd, out r))
            {
                bounds = default;
                return false;
            }
        }

        bounds = new System.Windows.Rect(r.Left, r.Top, Math.Max(1, r.Width), Math.Max(1, r.Height));
        return true;
    }

    /// <summary>光标位置（物理像素）。不用消息里的 lParam：跨 DPI 感知时它会被系统改写。</summary>
    public static bool TryGetCursorPosition(out System.Windows.Point screenPoint)
    {
        screenPoint = default;
        if (!GetCursorPos(out POINT p))
        {
            return false;
        }

        screenPoint = new System.Windows.Point(p.X, p.Y);
        return true;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
    public static bool IsMinimized(IntPtr hWnd) => IsIconic(hWnd);

    public static bool IsWindowAlive(IntPtr hWnd) => IsWindow(hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    private static bool IsCloaked(IntPtr hWnd)
    {
        int hr = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int));
        return hr == 0 && cloaked != 0;
    }

    private static string GetClassNameText(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    // ════════════════════════ 截图相关 ════════════════════════

    /// <summary>截取整个主屏（GDI BitBlt，只进内存）。</summary>
    public static BitmapSource? CaptureScreen()
    {
        int w = ScreenWidth;
        int h = ScreenHeight;
        if (w <= 0 || h <= 0)
        {
            return null;
        }

        IntPtr screenDc = IntPtr.Zero;
        IntPtr memDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr old = IntPtr.Zero;

        try
        {
            screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
            {
                return null;
            }

            memDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, w, h);
            if (memDc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                return null;
            }

            old = SelectObject(memDc, bitmap);
            if (!BitBlt(memDc, 0, 0, w, h, screenDc, 0, 0, SRCCOPY))
            {
                return null;
            }

            BitmapSource created = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            created.Freeze();

            int stride = (created.Format.BitsPerPixel * created.PixelWidth + 7) / 8;
            byte[] pixels = new byte[stride * created.PixelHeight];
            created.CopyPixels(pixels, stride, 0);

            BitmapSource detached = BitmapSource.Create(
                created.PixelWidth, created.PixelHeight, 96, 96,
                created.Format, created.Palette, pixels, stride);
            detached.Freeze();
            return detached;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (memDc != IntPtr.Zero && old != IntPtr.Zero)
            {
                SelectObject(memDc, old);
            }

            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            if (memDc != IntPtr.Zero)
            {
                DeleteDC(memDc);
            }

            if (screenDc != IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    /// <summary>
    /// 用 PrintWindow(PW_RENDERFULLCONTENT) 抓某个窗口的完整画面（含标题栏/标签栏）。
    /// 这是"截图重绘"路线的核心 API，即使窗口被我们的覆盖层遮住也能取到 DWM 表面。
    /// </summary>
    public static BitmapSource? CaptureWindow(IntPtr hWnd)
    {
        if (!TryGetMetrics(hWnd, out WindowMetrics m))
        {
            return null;
        }

        int w = (int)Math.Round(m.Frame.Width);
        int h = (int)Math.Round(m.Frame.Height);
        if (w <= 0 || h <= 0)
        {
            return null;
        }

        IntPtr windowDc = IntPtr.Zero;
        IntPtr memDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr old = IntPtr.Zero;

        try
        {
            windowDc = GetWindowDC(hWnd);
            memDc = CreateCompatibleDC(windowDc);
            bitmap = CreateCompatibleBitmap(windowDc, w, h);
            if (windowDc == IntPtr.Zero || memDc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                return null;
            }

            old = SelectObject(memDc, bitmap);
            if (!PrintWindow(hWnd, memDc, PW_RENDERFULLCONTENT))
            {
                return null;
            }

            BitmapSource created = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            created.Freeze();

            int stride = (created.Format.BitsPerPixel * created.PixelWidth + 7) / 8;
            byte[] pixels = new byte[stride * created.PixelHeight];
            created.CopyPixels(pixels, stride, 0);

            BitmapSource detached = BitmapSource.Create(
                created.PixelWidth, created.PixelHeight, 96, 96,
                created.Format, created.Palette, pixels, stride);
            detached.Freeze();
            return detached;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (memDc != IntPtr.Zero && old != IntPtr.Zero)
            {
                SelectObject(memDc, old);
            }

            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            if (memDc != IntPtr.Zero)
            {
                DeleteDC(memDc);
            }

            if (windowDc != IntPtr.Zero)
            {
                ReleaseDC(hWnd, windowDc);
            }
        }
    }

    /// <summary>当前桌面壁纸文件路径（可能是纯色 / 幻灯片，取不到就返回 null）。</summary>
    public static string? GetWallpaperPath()
    {
        var sb = new StringBuilder(1024);
        try
        {
            if (SystemParametersInfo(0x0073, sb.Capacity, sb, 0))
            {
                string path = sb.ToString();
                return string.IsNullOrWhiteSpace(path) ? null : path;
            }
        }
        catch
        {
            // 忽略
        }

        return null;
    }
}
