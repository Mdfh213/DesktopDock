using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class Tool
{
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string w);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string w);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool EnumWindows(ProcEnum cb, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    delegate bool ProcEnum(IntPtr h, IntPtr l);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    static void Main(string[] args)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        if (args.Length == 0) { Console.WriteLine("no mode"); return; }

        if (args[0] == "move")
        {
            SetCursorPos(int.Parse(args[1]), int.Parse(args[2]));
            Console.WriteLine("moved");
        }
        else if (args[0] == "cursor")
        {
            POINT p; GetCursorPos(out p);
            Console.WriteLine(p.X + "," + p.Y);
        }
        else if (args[0] == "shot")
        {
            int x = int.Parse(args[1]), y = int.Parse(args[2]);
            int w = int.Parse(args[3]), h = int.Parse(args[4]);
            string path = args[5];
            using (Bitmap bmp = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
                bmp.Save(path, ImageFormat.Png);
            }
            Console.WriteLine("saved " + path + " " + w + "x" + h);
        }
        else if (args[0] == "icons")
        {
            IntPtr lv = FindList();
            if (lv == IntPtr.Zero) { Console.WriteLine("listview not found"); return; }
            ShowWindow(lv, args[1] == "show" ? 5 : 0);
            Console.WriteLine("icons " + args[1] + " ok");
        }
        else if (args[0] == "fs")
        {
            int ms = int.Parse(args[1]);
            Form f = new Form();
            f.FormBorderStyle = FormBorderStyle.None;
            f.StartPosition = FormStartPosition.Manual;
            f.Bounds = Screen.PrimaryScreen.Bounds;
            f.BackColor = Color.Black;
            f.TopMost = false;
            f.ShowInTaskbar = false;
            Timer t = new Timer();
            t.Interval = ms;
            t.Tick += delegate { t.Stop(); f.Close(); };
            f.Shown += delegate { f.Activate(); t.Start(); };
            Application.Run(f);
            Console.WriteLine("fs window closed");
        }
    }

    static IntPtr FindList()
    {
        IntPtr progman = FindWindow("Progman", null);
        IntPtr dv = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (dv == IntPtr.Zero)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                IntPtr d = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (d != IntPtr.Zero) { found = d; return false; }
                return true;
            }, IntPtr.Zero);
            dv = found;
        }
        if (dv == IntPtr.Zero) return IntPtr.Zero;
        return FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
    }
}
