using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

class DeskProbe
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string w);
    [DllImport("user32.dll")] static extern bool EnumWindows(ProcEnum cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, ProcEnum cb, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr wp, IntPtr lp, int flags, int timeout, out IntPtr res);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    delegate bool ProcEnum(IntPtr h, IntPtr l);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    const int WM_COMMAND = 0x0111;
    const int TOGGLE_ICONS = 0x7402;

    static string Cls(IntPtr h)
    {
        StringBuilder sb = new StringBuilder(256);
        GetClassName(h, sb, 256);
        return sb.ToString();
    }

    static List<IntPtr> TopLevel(string wantClass)
    {
        List<IntPtr> list = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr l)
        {
            if (Cls(h) == wantClass) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    static void Main(string[] args)
    {
        Console.WriteLine("== Progman ==");
        foreach (IntPtr h in TopLevel("Progman"))
        {
            RECT r; GetWindowRect(h, out r);
            uint pid; GetWindowThreadProcessId(h, out pid);
            Console.WriteLine("  hwnd=" + h + " visible=" + IsWindowVisible(h) + " rect=" + r.Left + "," + r.Top + "," + r.Right + "," + r.Bottom + " pid=" + pid);
            IntPtr dv = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
            while (dv != IntPtr.Zero)
            {
                IntPtr lv = FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
                Console.WriteLine("    DefView=" + dv + " visible=" + IsWindowVisible(dv) + "  ListView=" + lv + " visible=" + (lv != IntPtr.Zero && IsWindowVisible(lv)));
                dv = FindWindowEx(h, dv, "SHELLDLL_DefView", null);
            }
        }

        Console.WriteLine("== WorkerW ==");
        foreach (IntPtr h in TopLevel("WorkerW"))
        {
            IntPtr dv = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr lv = dv == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
            if (dv != IntPtr.Zero)
                Console.WriteLine("  WorkerW=" + h + " visible=" + IsWindowVisible(h) + " DefView=" + dv + " visible=" + IsWindowVisible(dv) + " ListView=" + lv + " visible=" + (lv != IntPtr.Zero && IsWindowVisible(lv)));
        }

        if (args.Length > 0 && args[0] == "toggleall")
        {
            Console.WriteLine("== 对所有 DefView 发 0x7402 ==");
            foreach (IntPtr h in TopLevel("Progman"))
            {
                IntPtr dv = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                while (dv != IntPtr.Zero)
                {
                    IntPtr res;
                    SendMessageTimeout(dv, WM_COMMAND, (IntPtr)TOGGLE_ICONS, IntPtr.Zero, 0, 2000, out res);
                    Console.WriteLine("  sent to " + dv);
                    System.Threading.Thread.Sleep(400);
                    IntPtr lv = FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
                    Console.WriteLine("    now ListView visible=" + (lv != IntPtr.Zero && IsWindowVisible(lv)));
                    dv = FindWindowEx(h, dv, "SHELLDLL_DefView", null);
                }
            }
        }

        if (args.Length > 0 && args[0] == "hideall")
        {
            Console.WriteLine("== ShowWindow(SW_HIDE) 所有 ListView / DefView ==");
            foreach (IntPtr h in TopLevel("Progman"))
            {
                IntPtr dv = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                while (dv != IntPtr.Zero)
                {
                    IntPtr lv = FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
                    if (lv != IntPtr.Zero) Console.WriteLine("  hide listview " + lv + " -> " + ShowWindow(lv, 0));
                    Console.WriteLine("  hide defview  " + dv + " -> " + ShowWindow(dv, 0));
                    dv = FindWindowEx(h, dv, "SHELLDLL_DefView", null);
                }
            }
        }
    }
}
