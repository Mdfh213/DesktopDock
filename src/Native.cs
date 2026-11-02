using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DesktopDock
{
    internal static class Native
    {
        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string cls, string win);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string win);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr h, int cmd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr h);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr h, out RECT r);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(ProcEnum cb, IntPtr l);

        public delegate bool ProcEnum(IntPtr h, IntPtr l);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr h, int flags);

        [DllImport("user32.dll")]
        public static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr h, int idx);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr h, int idx, int val);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForSystem();

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int vk);
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vk);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                               int x, int y, int cx, int cy, uint flags);

        /// <summary>只移动顶层窗口，不触发 WinForms 布局重算（动画专用，避免崩溃）。</summary>
        public static void MoveTo(IntPtr h, int x, int y)
        {
            try
            {
                const uint NOSIZE = 0x0001, NOZORDER = 0x0004, NOACTIVATE = 0x0010, NOCOPYBITS = 0x0100;
                SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, NOSIZE | NOZORDER | NOACTIVATE | NOCOPYBITS);
            }
            catch { }
        }

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint attach, uint attachTo, bool fAttach);
        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr h);
        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr h);

        /// <summary>强制把窗口提到前台（绕过前台锁定），滚轮才会发给它。</summary>
        public static bool ForceForeground(IntPtr h)
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == h) return true;
                uint dummy;
                uint fgThread = GetWindowThreadProcessId(fg, out dummy);
                uint myThread = GetCurrentThreadId();
                bool attached = false;
                if (fgThread != 0 && fgThread != myThread)
                    attached = AttachThreadInput(fgThread, myThread, true);
                SetForegroundWindow(h);
                BringWindowToTop(h);
                SetFocus(h);
                if (attached) AttachThreadInput(fgThread, myThread, false);
                return GetForegroundWindow() == h;
            }
            catch { return false; }
        }

        /// <summary>让窗口不出现在 Alt-Tab，也不抢焦点。</summary>
        public static void MakeNoActivate(Form f)
        {
            try
            {
                int ex = GetWindowLong(f.Handle, GWL_EXSTYLE);
                ex |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                SetWindowLong(f.Handle, GWL_EXSTYLE, ex);
            }
            catch { }
        }

        /// <summary>Win11 圆角；不支持的系统静默失败。</summary>
        public static void RoundCorners(Form f, int preference)
        {
            try
            {
                int v = preference;
                DwmSetWindowAttribute(f.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref v, sizeof(int));
            }
            catch { }
        }

        public static string ClassOf(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassName(h, sb, 256);
            return sb.ToString();
        }
    }

    /// <summary>全局低级鼠标钩子：面板不抢焦点，也能用滚轮滚动。</summary>
    internal sealed class WheelHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEWHEEL = 0x020A;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public int mouseData;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        private readonly HookProc _proc;      // 必须保活，否则被 GC 回收后回调崩溃
        private IntPtr _hook = IntPtr.Zero;
        private readonly Func<Point, int, bool> _handler;

        public WheelHook(Func<Point, int, bool> handler)
        {
            _handler = handler;
            _proc = Callback;
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
        }

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEWHEEL && _hook != IntPtr.Zero)
                {
                    MSLLHOOKSTRUCT d = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    int delta = (short)((d.mouseData >> 16) & 0xFFFF);
                    if (_handler(new Point(d.pt.X, d.pt.Y), delta)) return (IntPtr)1;
                }
            }
            catch { }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                try { UnhookWindowsHookEx(_hook); } catch { }
                _hook = IntPtr.Zero;
            }
        }
    }

    /// <summary>隐藏 / 显示桌面图标。</summary>
    internal static class DeskIcons
    {
        private static IntPtr _listView = IntPtr.Zero;

        public static IntPtr GetListView()
        {
            IntPtr progman = Native.FindWindow("Progman", null);
            IntPtr defView = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero)
            {
                IntPtr found = IntPtr.Zero;
                Native.EnumWindows(delegate(IntPtr h, IntPtr l)
                {
                    IntPtr dv = Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (dv != IntPtr.Zero) { found = dv; return false; }
                    return true;
                }, IntPtr.Zero);
                defView = found;
            }
            if (defView == IntPtr.Zero) return IntPtr.Zero;
            return Native.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        }

        public static void SetVisible(bool visible)
        {
            IntPtr lv = GetListView();
            if (lv == IntPtr.Zero) return;
            _listView = lv;
            Native.ShowWindow(lv, visible ? Native.SW_SHOW : Native.SW_HIDE);
        }

        public static bool IsCurrentlyVisible()
        {
            IntPtr lv = GetListView();
            if (lv == IntPtr.Zero) return true;
            return Native.IsWindowVisible(lv);
        }
    }

    /// <summary>判断前台窗口是否全屏（用于全屏时自动挂起）。</summary>
    internal static class FullscreenWatch
    {
        public static bool IsFullscreen(IntPtr[] ignore)
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            if (ignore != null)
            {
                for (int i = 0; i < ignore.Length; i++)
                    if (ignore[i] == fg) return false;
            }
            if (!Native.IsWindowVisible(fg)) return false;

            string cls = Native.ClassOf(fg);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" ||
                cls == "Shell_SecondaryTrayWnd" || cls == "Windows.UI.Core.CoreWindow" ||
                cls == "ApplicationFrameWindow") return false;

            Native.RECT r;
            if (!Native.GetWindowRect(fg, out r)) return false;
            if (r.Right - r.Left <= 0 || r.Bottom - r.Top <= 0) return false;

            IntPtr mon = Native.MonitorFromWindow(fg, 2 /* MONITOR_DEFAULTTONEAREST */);
            Native.MONITORINFO mi = new Native.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
            if (!Native.GetMonitorInfo(mon, ref mi)) return false;

            const int tol = 2;
            bool coverL = r.Left <= mi.rcMonitor.Left + tol;
            bool coverT = r.Top <= mi.rcMonitor.Top + tol;
            bool coverR = r.Right >= mi.rcMonitor.Right - tol;
            bool coverB = r.Bottom >= mi.rcMonitor.Bottom - tol;
            return coverL && coverT && coverR && coverB;
        }
    }
}
