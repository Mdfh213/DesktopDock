using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace DesktopDock
{
    internal static class Program
    {
        private static Mutex _mutex;
        public static bool Demo;
        public const string Version = "1.4.2";

        [STAThread]
        private static void Main(string[] args)
        {
            bool selftest = false;
            bool testLaunch = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--selftest") selftest = true;
                if (args[i] == "--demo") Demo = true;
                if (args[i] == "--testlaunch") testLaunch = true;
            }

            EnableDpi();

            if (selftest) { SelfTest.Run(); return; }
            if (testLaunch) { SelfTest.TestLaunch(); return; }

            bool created;
            _mutex = new Mutex(true, "DesktopDock-SingleInstance", out created);
            if (!created) return;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                Log.Write("THREAD_EXCEPTION: " + e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Log.Write("UNHANDLED: " + e.ExceptionObject);
            };

            Log.Write("boot v" + Version + " pid=" + System.Diagnostics.Process.GetCurrentProcess().Id + " scale=" + Ui.Scale);

            DockContext ctx = new DockContext();
            Application.Run(ctx);
            Log.Write("run loop exited");
            GC.KeepAlive(_mutex);
        }

        private static void EnableDpi()
        {
            try
            {
                if (!Native.SetProcessDpiAwarenessContext(new IntPtr(-4)))
                    Native.SetProcessDPIAware();
            }
            catch
            {
                try { Native.SetProcessDPIAware(); } catch { }
            }

            float scale = 1f;
            try
            {
                uint dpi = Native.GetDpiForSystem();
                if (dpi > 0) scale = dpi / 96f;
            }
            catch { }
            if (scale <= 1.001f)
            {
                try
                {
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                        scale = g.DpiX / 96f;
                }
                catch { }
            }
            if (scale < 1f) scale = 1f;
            if (scale > 4f) scale = 4f;
            Ui.Scale = scale;
        }
    }

    internal sealed class DockContext : ApplicationContext
    {
        private readonly DockManager _mgr;

        public DockContext()
        {
            _mgr = new DockManager();
            // 首次载入后立刻回写一次：让新生成的 Id / Order 落盘，
            // 否则每次启动都会重新生成 Id，已保存的顺序会对不上。
            try { ConfigStore.Save(ConfigStore.Load()); } catch { }
            try { _mgr.Start(); }
            catch (Exception ex)
            {
                Log.Write("启动失败: " + ex);
                MessageBox.Show("DesktopDock 启动失败：" + ex.Message, "DesktopDock",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                ExitThread();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _mgr != null) _mgr.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnMainFormClosed(object sender, EventArgs e)
        {
            base.OnMainFormClosed(sender, e);
        }
    }

    /// <summary>无界面自检：不弹窗，只跑关键逻辑并写日志，供命令行验收。</summary>
    internal static class SelfTest
    {
        /// <summary>验证"点击图标 → 启动程序"这条链路本身是否可用。</summary>
        public static void TestLaunch()
        {
            DockConfig cfg = ConfigStore.Load();
            if (cfg.Items.Count == 0) { Log.Write("TESTLAUNCH: 没有条目"); return; }

            int ok = 0, fail = 0;
            for (int i = 0; i < cfg.Items.Count && i < 5; i++)
            {
                DockItem it = cfg.Items[i];
                bool r = DockCanvas.Launch(it.Path);
                Log.Write("TESTLAUNCH " + (r ? "OK  " : "FAIL") + "  " + it.Name + "  <- " + it.Path);
                if (r) ok++; else fail++;
            }
            Log.Write("TESTLAUNCH 汇总: ok=" + ok + " fail=" + fail);
        }

        public static void Run()
        {
            string logPath = Path.Combine(Application.StartupPath, "selftest.txt");
            using (StreamWriter w = new StreamWriter(logPath, false, System.Text.Encoding.UTF8))
            {
                w.AutoFlush = true;
                Action<string> wl = delegate(string s)
                {
                    Console.WriteLine(s);
                    w.WriteLine(s);
                };

                wl("== DesktopDock selftest ==");
                wl("scale = " + Ui.Scale.ToString("0.00"));
                wl("screens = " + Screen.AllScreens.Length);
                for (int i = 0; i < Screen.AllScreens.Length; i++)
                {
                    Screen s = Screen.AllScreens[i];
                    wl("  screen" + i + " " + s.Bounds + " primary=" + s.Primary);
                }

                IntPtr lv = DeskIcons.GetListView();
                wl("desktop listview handle = " + lv);
                wl("desktop icons visible = " + DeskIcons.IsCurrentlyVisible());

                wl("fullscreen now = " + FullscreenWatch.IsFullscreen(null));

                DockConfig cfg = ConfigStore.Load();
                wl("config path = " + ConfigStore.FilePath);
                wl("items = " + cfg.Items.Count + ", groups = " + cfg.Groups().Count);
                wl("order = " + cfg.Order.Count + " 项（顶层图标+分组混排）");
                System.Text.StringBuilder ob = new System.Text.StringBuilder("  ");
                for (int i = 0; i < cfg.Order.Count && i < 12; i++)
                {
                    string tok = cfg.Order[i];
                    if (tok.StartsWith("g:")) ob.Append("[组]" + tok.Substring(2));
                    else
                    {
                        DockItem oi = cfg.ById(tok.Substring(2));
                        ob.Append(oi != null ? oi.Name : "?");
                    }
                    ob.Append("  ");
                }
                wl(ob.ToString());
                for (int i = 0; i < cfg.Items.Count && i < 8; i++)
                    wl("  item: " + cfg.Items[i].Name + " | " + cfg.Items[i].Path);

                int iconOk = 0;
                for (int i = 0; i < cfg.Items.Count; i++)
                    if (IconCache.Get(cfg.Items[i].Path, 32) != null) iconOk++;
                wl("icons extracted = " + iconOk + "/" + cfg.Items.Count);

                wl("autostart enabled = " + RegistryAutostart.IsEnabled());
                wl("== done ==");
            }
        }
    }
}
