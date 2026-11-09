using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DesktopDock
{
    internal static class Native
    {
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


        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr h);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr h, out RECT r);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);

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
