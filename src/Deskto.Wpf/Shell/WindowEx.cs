using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Deskto.Wpf.Shell;

/// <summary>Windows API 辅助：置顶、点击穿透、挂到桌面 WorkerW（置于图标之下，实验性）。</summary>
internal static class WindowEx
{
    private static readonly IntPtr HwndTopMost = new(-1);
    private const uint SwpNomove = 0x0002;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNoactivate = 0x0010;
    private const int GwlExstyle = -20;
    private const int GwlHwndparent = -8;    // 设父窗口但不改变 z-order（安全，不崩）
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoactivate = 0x08000000; // 点击不激活窗口，永不弹到前方
    private const int GwChild = 5;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private static readonly IntPtr HwndNoTopmost = new(-2);

    /// <summary>
    /// 把窗口挂到桌面 Shell 层（SHELLDLL_DefView），Win+D 后自动恢复。
    /// 用 EnsureHandle + Owner（不是 SetParent），WPF 不崩溃。
    /// 必须在窗口 Show() 之前调用。
    /// </summary>
    public static void AttachToDesktop(System.Windows.Window window)
    {
        var helper = new System.Windows.Interop.WindowInteropHelper(window);
        helper.EnsureHandle();
        var defView = FindDesktopDefView();
        if (defView != IntPtr.Zero)
            helper.Owner = defView;
    }

    /// <summary>找到 SHELLDLL_DefView 的 HWND——可能是 Progman 或 WorkerW 的子窗口。</summary>
    private static IntPtr FindDesktopDefView()
    {
        // 先试 Progman 直系
        var progman = FindWindow("ProgMan", null);
        if (progman != IntPtr.Zero)
        {
            var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero) return defView;
        }

        // 再枚举所有顶层窗口，找到含 SHELLDLL_DefView 的那个（WorkerW）
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            var child = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (child != IntPtr.Zero) { found = child; return false; }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>降为非置顶（用于分区窗口）。</summary>
    public static bool ApplyDesktopLayer(IntPtr hWnd)
    {
        return SetWindowPos(hWnd, HwndNoTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);
    }

    /// <summary>压到 Z 序最底层——让普通窗口自然在上方。</summary>
    public static void SendToBottom(IntPtr hWnd)
    {
        SetWindowPos(hWnd, (IntPtr)1, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);
    }

    /// <summary>
    /// Hook：拦截 WM_WINDOWPOSCHANGING，强制窗口永远留在 Z 序最底层。
    /// Rainmeter 等桌面小部件都用这个方案。
    /// </summary>
    public static System.Windows.Interop.HwndSourceHook PinToBottom()
    {
        const int WmWindowposchanging = 0x0046;
        return (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg != WmWindowposchanging) return IntPtr.Zero;
            // WINDOWPOS 结构：hwndInsertAfter 在偏移 4 字节处（32位）或 8 字节处（64位）
            // hwnd, hwndInsertAfter, x, y, cx, cy, flags
            // 我们用 Marshal 读 struct
            var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            wp.hwndInsertAfter = (IntPtr)1; // HWND_BOTTOM
            Marshal.StructureToPtr(wp, lParam, false);
            return IntPtr.Zero;
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    /// <summary>整窗点击穿透</summary>
    public static void SetClickThrough(IntPtr hWnd, bool enable)
    {
        var style = GetWindowLong(hWnd, GwlExstyle);
        style = enable ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLong(hWnd, GwlExstyle, style);
    }

    /// <summary>禁止窗口激活——点击不弹到前方，但不影响鼠标交互。</summary>
    public static void SetNoActivate(IntPtr hWnd, bool enable)
    {
        var style = GetWindowLong(hWnd, GwlExstyle);
        style = enable ? style | WsExNoactivate : style & ~WsExNoactivate;
        SetWindowLong(hWnd, GwlExstyle, style);
    }

    /// <summary>
    /// 找到桌面 WorkerW（图标之下那一层）。不同 Windows 版本结构不同，失败返回 IntPtr.Zero。
    /// 失败时调用方应回退为置顶覆盖模式。
    /// </summary>
    public static IntPtr GetWorkerW()
    {
        var progman = FindWindow("ProgMan", null);
        // 触发 Explorer 创建 WorkerW 层
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            if (sb.ToString() == "WorkerW")
            {
                // 含 SHELLDLL_DefView 子窗口的那个就是图标层之下的 WorkerW
                var child = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (child != IntPtr.Zero)
                    found = hwnd;
            }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>把窗口挂到桌面之下（实验性）。返回是否成功。</summary>
    public static bool ParentToDesktop(IntPtr hWnd)
    {
        var w = GetWorkerW();
        if (w == IntPtr.Zero) return false;
        SetParent(hWnd, w);
        return true;
    }
}
