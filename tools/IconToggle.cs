using System;
using System.Runtime.InteropServices;
using System.Text;

class IconToggle
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string w);
    [DllImport("user32.dll")] static extern bool EnumWindows(ProcEnum cb, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);

    delegate bool ProcEnum(IntPtr h, IntPtr l);

    const int WM_COMMAND = 0x0111;
    const int TOGGLE_ICONS = 0x7402;

    static void Main(string[] args)
    {
        IntPtr dv = FindDefView();
        IntPtr lv = dv == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
        StringBuilder cls = new StringBuilder(256);
        if (dv != IntPtr.Zero) GetClassName(dv, cls, 256);
        Console.WriteLine("defview=" + dv + " class=" + cls + " listview=" + lv);
        Console.WriteLine("lv visible=" + (lv != IntPtr.Zero && IsWindowVisible(lv)) +
                          "  dv visible=" + (dv != IntPtr.Zero && IsWindowVisible(dv)));

        if (args.Length > 0 && args[0] == "toggle" && dv != IntPtr.Zero)
        {
            SendMessage(dv, WM_COMMAND, (IntPtr)TOGGLE_ICONS, IntPtr.Zero);
            System.Threading.Thread.Sleep(500);
            Console.WriteLine("after toggle: lv visible=" + (lv != IntPtr.Zero && IsWindowVisible(lv)));
        }
    }

    static IntPtr FindDefView()
    {
        IntPtr progman = FindWindow("Progman", null);
        IntPtr dv = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (dv != IntPtr.Zero) return dv;
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr l)
        {
            IntPtr d = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (d != IntPtr.Zero) { found = d; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
