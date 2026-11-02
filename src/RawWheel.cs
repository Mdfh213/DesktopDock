using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopDock
{
    /// <summary>
    /// 用 Raw Input（RIDEV_INPUTSINK）接收滚轮。
    /// 这是唯一既不依赖窗口焦点、也不依赖全局钩子的做法：
    /// WM_MOUSEWHEEL 只会发给焦点窗口，而面板永远不是焦点窗口。
    /// </summary>
    internal sealed class RawWheelSource : NativeWindow, IDisposable
    {
        private const int WM_INPUT = 0x00FF;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const ushort RI_MOUSE_WHEEL = 0x0400;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        // RAWMOUSE：usFlags + (union 4 字节) + ulRawButtons + lLastX + lLastY + ulExtraInformation
        [StructLayout(LayoutKind.Sequential)]
        private struct RAWMOUSE
        {
            public ushort usFlags;
            public ushort _pad;
            public ushort usButtonFlags;
            public ushort usButtonData;
            public uint ulRawButtons;
            public int lLastX;
            public int lLastY;
            public uint ulExtraInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data,
                                                   ref uint size, uint headerSize);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT p);

        private readonly Action<Point, int> _onWheel;
        private bool _registered;

        public RawWheelSource(Action<Point, int> onWheel)
        {
            _onWheel = onWheel;

            CreateParams cp = new CreateParams();
            cp.Caption = "DesktopDockRawInput";
            CreateHandle(cp);

            RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
            rid[0].usUsagePage = 0x01;          // generic desktop
            rid[0].usUsage = 0x02;              // mouse
            rid[0].dwFlags = RIDEV_INPUTSINK;   // 后台也能收到
            rid[0].hwndTarget = Handle;
            _registered = RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            Log.Write("RawInput register=" + _registered);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT)
            {
                try { HandleRaw(m.LParam); }
                catch { }
            }
            base.WndProc(ref m);
        }

        private void HandleRaw(IntPtr hRawInput)
        {
            uint size = 0;
            uint headerSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, headerSize);
            if (size == 0 || size > 256) return;

            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(hRawInput, RID_INPUT, buf, ref size, headerSize) != size) return;

                RAWINPUTHEADER hdr = (RAWINPUTHEADER)Marshal.PtrToStructure(buf, typeof(RAWINPUTHEADER));
                if (hdr.dwType != 0) return;   // 0 = RIM_TYPEMOUSE

                IntPtr mousePtr = new IntPtr(buf.ToInt64() + headerSize);
                RAWMOUSE ms = (RAWMOUSE)Marshal.PtrToStructure(mousePtr, typeof(RAWMOUSE));
                if ((ms.usButtonFlags & RI_MOUSE_WHEEL) == 0) return;

                int delta = (short)ms.usButtonData;
                if (delta == 0) return;

                POINT p;
                if (!GetCursorPos(out p)) return;
                if (_onWheel != null) _onWheel(new Point(p.X, p.Y), delta);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        public void Dispose()
        {
            try { DestroyHandle(); } catch { }
        }
    }
}
