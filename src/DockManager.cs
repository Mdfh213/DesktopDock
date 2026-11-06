using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopDock
{
    /// <summary>把所有部件串起来：热区、面板、托盘、全屏检测、桌面图标。</summary>
    internal sealed class DockManager : IDisposable
    {
        public DockConfig Cfg;

        private readonly List<EdgeZone> _zones = new List<EdgeZone>();
        private readonly List<DockPanel> _panels = new List<DockPanel>();
        private readonly Timer _fsTimer = new Timer();
        private readonly Timer _iconTimer = new Timer();
        private readonly Timer _hotTimer = new Timer();
        private readonly Timer _dispTimer = new Timer();
        private string _dispSignature = "";
        private NotifyIcon _tray;
        private RawWheelSource _rawWheel;
        private bool _suspended;
        private bool _paused;
        private bool _shotMode;
        private bool _disposed;

        public bool Suspended { get { return _suspended; } }

        public DockManager()
        {
            Cfg = ConfigStore.Load();
        }

        public void Start()
        {
            Log.Write("Start: begin");
            BuildUi();
            Log.Write("Start: ui built, zones=" + _zones.Count + " panels=" + _panels.Count);

            _fsTimer.Interval = 600;
            _fsTimer.Tick += FsTick;
            _fsTimer.Start();

            _hotTimer.Interval = 90;
            _hotTimer.Tick += HotTick;
            _hotTimer.Start();

            // 显示器/分辨率/缩放变化时自动重排
            _dispSignature = ScreenSignature();
            _dispTimer.Interval = 2500;
            _dispTimer.Tick += DispTick;
            _dispTimer.Start();

            CreateTray();
            Log.Write("Start: tray ok");

            // 滚轮：走 Raw Input（不需要焦点，也不依赖钩子）
            _rawWheel = new RawWheelSource(OnWheel);

            if (Program.Demo && _panels.Count > 0)
            {
                Timer demo = new Timer();
                demo.Interval = 1200;
                demo.Tick += delegate
                {
                    demo.Stop();
                    demo.Dispose();
                    _panels[0].KeepOpen = true;
                    _panels[0].ShowPanel();
                    Log.Write("demo: panel forced");
                    Timer probe = new Timer();
                    probe.Interval = 900;
                    probe.Tick += delegate
                    {
                        probe.Stop();
                        probe.Dispose();
                        DockPanel p0 = _panels[0];
                        Log.Write("demo probe: visible=" + p0.Visible + " bounds=" + p0.Bounds +
                                  " opacity=" + p0.Opacity + " topmost=" + p0.TopMost);
                    };
                    probe.Start();
                };
                demo.Start();

                // 自检：1.8 秒后翻一页，便于截图验收滚动
                Timer demo2 = new Timer();
                demo2.Interval = 1800;
                demo2.Tick += delegate
                {
                    demo2.Stop();
                    demo2.Dispose();
                    _panels[0].Canvas.PageBy(1);
                    Log.Write("demo: page down, canScroll=" + _panels[0].Canvas.CanScroll);
                };
                demo2.Start();
            }
        }

        private void BuildUi()
        {
            Screen[] screens = Screen.AllScreens;
            for (int s = 0; s < screens.Length; s++)
            {
                Screen scr = screens[s];
                for (int side = 0; side < 2; side++)
                {
                    bool left = side == 0;
                    EdgeZone zone = new EdgeZone(left, scr, Cfg.HotEdgeWidth);
                    DockPanel panel = new DockPanel(scr, left, this);
                    zone.Hot += MakeHotHandler(panel);
                    zone.Show();
                    // 点击边缘条 = 用户主动激活，此时可以合法抢前台，滚轮随之可用
                    zone.MouseClick += MakeZoneClickHandler(panel);
                    _zones.Add(zone);
                    _panels.Add(panel);
                }
            }
        }

        private MouseEventHandler MakeZoneClickHandler(DockPanel panel)
        {
            return delegate(object s, MouseEventArgs e)
            {
                if (_suspended) return;
                ShowOnly(panel);
                panel.ShowPanel();
                bool ok = Native.ForceForeground(panel.Handle);
                Log.Write("edge click -> foreground=" + ok);
            };
        }

        private EventHandler MakeHotHandler(DockPanel panel)
        {
            return delegate
            {
                if (_suspended) return;
                panel.ShowPanel();
            };
        }

        public void RefreshPanels()
        {
            for (int i = 0; i < _panels.Count; i++) _panels[i].Canvas.RefreshContent();
        }

        private void FsTick(object sender, EventArgs e)
        {
            IntPtr[] ignore = new IntPtr[_zones.Count + _panels.Count];
            int k = 0;
            for (int i = 0; i < _zones.Count; i++) ignore[k++] = _zones[i].Handle;
            for (int i = 0; i < _panels.Count; i++) ignore[k++] = _panels[i].Handle;

            // 截图工具在前台时：不挂起，而是把侧边栏"钉住"，让它可以被截进图里
            bool shot = IsScreenshotToolForeground();
            if (shot != _shotMode)
            {
                _shotMode = shot;
                for (int i = 0; i < _panels.Count; i++)
                    _panels[i].KeepOpen = shot || Program.Demo;
                Log.Write("截图模式 = " + shot);
            }

            // 截图工具的全屏选框层不能被当成"全屏应用"，否则截图时侧边栏会消失
            bool fs = !shot && FullscreenWatch.IsFullscreen(ignore);
            if (fs && !_suspended) Suspend();
            else if (!fs && _suspended) Resume();
        }

        /// <summary>显示器布局签名（设备名+边界+工作区），变化即重排。</summary>
        private static string ScreenSignature()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Screen[] ss = Screen.AllScreens;
            for (int i = 0; i < ss.Length; i++)
            {
                sb.Append(ss[i].DeviceName).Append(ss[i].Bounds).Append(ss[i].WorkingArea).Append('|');
            }
            sb.Append("scale=").Append(Ui.Scale.ToString("0.00"));
            return sb.ToString();
        }

        private void DispTick(object sender, EventArgs e)
        {
            string sig = ScreenSignature();
            if (sig == _dispSignature) return;
            _dispSignature = sig;
            Log.Write("显示配置变化，重建界面: " + sig);
            try { RebuildUi(); }
            catch (Exception ex) { Log.Write("重建失败: " + ex.Message); }
        }

        /// <summary>丢弃并重建所有面板/热区（显示器或 DPI 变化后）。</summary>
        private void RebuildUi()
        {
            for (int i = 0; i < _panels.Count; i++) { try { _panels[i].Dispose(); } catch { } }
            for (int i = 0; i < _zones.Count; i++) { try { _zones[i].Dispose(); } catch { } }
            _panels.Clear();
            _zones.Clear();
            BuildUi();
            Log.Write("RebuildUi 完成: zones=" + _zones.Count + " panels=" + _panels.Count);
        }

        /// <summary>轮询鼠标位置：进入边缘热区就唤出对应面板（不依赖分层窗口的命中测试）。</summary>
        private static readonly string[] ShotTools = new string[]
        {
            "SnippingTool", "ScreenSketch", "SnipAndSketch", "ScreenClipper",
            "Snipaste", "PixPin", "ShareX", "Screenpresso", "WindowsCamera"
        };

        /// <summary>前台是不是截图工具（截图时自动让路，避免挡住选框）。</summary>
        private static bool IsScreenshotToolForeground()
        {
            try
            {
                IntPtr h = Native.GetForegroundWindow();
                if (h == IntPtr.Zero) return false;
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pid == 0) return false;
                using (Process p = Process.GetProcessById((int)pid))
                {
                    string name = p.ProcessName;
                    for (int i = 0; i < ShotTools.Length; i++)
                        if (string.Equals(name, ShotTools[i], StringComparison.OrdinalIgnoreCase))
                            return true;
                }
            }
            catch { }
            return false;
        }
        /// <summary>同一时刻只允许一个面板处于展开状态（否则左右两侧会同时出现两个）。</summary>
        private void ShowOnly(DockPanel target)
        {
            for (int i = 0; i < _panels.Count; i++)
            {
                if (_panels[i] != target && _panels[i].IsShowingPanel) _panels[i].HidePanel();
            }
        }

        private void HotTick(object sender, EventArgs e)
        {
            if (_suspended) return;
            Point p = Cursor.Position;
            for (int i = 0; i < _panels.Count; i++)
            {
                if (_panels[i].CursorInHotBand(p, Cfg.HotEdgeWidth))
                {
                    if (!_panels[i].IsShowingPanel)
                    {
                        ShowOnly(_panels[i]);
                        _panels[i].ShowPanel();
                    }
                    return;
                }
            }
        }

        /// <summary>滚轮：只作用于鼠标所在、且正在显示的面板。</summary>
        private void AddEdgeItem(ToolStripMenuItem parent, string label, int px)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(label);
            mi.Tag = px;
            mi.Click += delegate { SetHotEdgeWidth(px); };
            parent.DropDownItems.Add(mi);
        }
        private void AddSizeItem(ToolStripMenuItem parent, string label, int size)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(label);
            mi.Tag = size;
            mi.Click += delegate { SetCellSize(size); };
            parent.DropDownItems.Add(mi);
        }

        /// <summary>调整触发热区宽度（需要重建边缘条窗口，宽度在建时固定）。</summary>
        public void SetHotEdgeWidth(int px)
        {
            if (px < 1) px = 1;
            if (px > 24) px = 24;
            Cfg.HotEdgeWidth = px;
            ConfigStore.Save(Cfg);
            RebuildUi();
            Log.Write("触发热区宽度=" + px + " px");
        }

        /// <summary>调整图标大小（同时按比例调整面板宽度）。</summary>
        public void SetCellSize(int size)
        {
            if (size < 56) size = 56;
            if (size > 140) size = 140;
            Cfg.CellSize = size;
            Cfg.PanelWidth = Math.Max(240, Math.Min(560, (int)Math.Round(size * 3.9)));
            ConfigStore.Save(Cfg);
            for (int i = 0; i < _panels.Count; i++) _panels[i].ApplyLayout();
            Log.Write("图标大小=" + size + " 面板宽=" + Cfg.PanelWidth);
        }

        private void OnWheel(Point p, int delta)
        {
            if (_suspended) return;
            bool ctrl = (Native.GetKeyState(0x11) & 0x8000) != 0;
            for (int i = 0; i < _panels.Count; i++)
            {
                DockPanel pn = _panels[i];
                if (!pn.Visible || !pn.IsShowingPanel) continue;
                if (!pn.Bounds.Contains(p)) continue;
                if (ctrl) { SetCellSize(Cfg.CellSize + (delta > 0 ? 4 : -4)); return; }
                if (!pn.Canvas.CanScroll) return;
                int step = Ui.Px(Math.Max(40, Cfg.CellSize));
                pn.Canvas.ScrollBy(-delta / 120 * step);
                return;
            }
        }

        public void Suspend()
        {
            _suspended = true;
            for (int i = 0; i < _panels.Count; i++) _panels[i].HidePanel();
            for (int i = 0; i < _zones.Count; i++) _zones[i].Hide();
            Log.Write("检测到全屏应用，已挂起。");
        }

        public void Resume()
        {
            _suspended = false;
            for (int i = 0; i < _zones.Count; i++) _zones[i].Show();
            Log.Write("全屏应用结束，已恢复。");
        }

        private void IconTick(object sender, EventArgs e)
        {
            if (!Cfg.HideDesktopIcons) return;
            if (DeskIcons.IsCurrentlyVisible()) DeskIcons.SetVisible(false);
        }

        private void ApplyDesktopIcons()
        {
            if (Cfg.HideDesktopIcons) DeskIcons.SetVisible(false);
            else DeskIcons.SetVisible(true);
        }

        private void CreateTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = SystemIcons.Application;
            _tray.Text = "DesktopDock · 桌面侧边栏";
            _tray.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;

            ToolStripMenuItem header = new ToolStripMenuItem("DesktopDock");
            header.Enabled = false;
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem open = new ToolStripMenuItem("打开面板（左侧）");
            open.Click += delegate
            {
                if (_panels.Count > 0) _panels[0].ShowPanel();
            };
            menu.Items.Add(open);

            ToolStripMenuItem testLaunch = new ToolStripMenuItem("测试启动第一个图标");
            testLaunch.Click += delegate
            {
                if (Cfg.Items.Count == 0) return;
                bool ok = DockCanvas.Launch(Cfg.Items[0].Path);
                Log.Write("菜单测试启动 " + Cfg.Items[0].Name + " -> " + (ok ? "OK" : "FAIL"));
            };
            menu.Items.Add(testLaunch);

            ToolStripMenuItem viewLog = new ToolStripMenuItem("查看运行日志");
            viewLog.Click += delegate
            {
                try
                {
                    if (!File.Exists(Path.Combine(Application.StartupPath, "dock.log")))
                        Log.Write("日志已创建");
                    Process.Start("notepad.exe", "\"" + Path.Combine(Application.StartupPath, "dock.log") + "\"");
                }
                catch (Exception ex) { Log.Write("打开日志失败: " + ex.Message); }
            };
            menu.Items.Add(viewLog);

            // 桌面图标由用户自己管理，本工具不再改动

            ToolStripMenuItem pauseItem = new ToolStripMenuItem("暂停边缘响应（截图/录屏时）");
            pauseItem.CheckOnClick = true;
            pauseItem.CheckedChanged += delegate
            {
                _paused = pauseItem.Checked;
                if (_paused)
                {
                    for (int i = 0; i < _panels.Count; i++) if (_panels[i].IsShowingPanel) _panels[i].HidePanel();
                }
                Log.Write("暂停边缘响应 = " + _paused);
            };
            menu.Items.Add(pauseItem);
            ToolStripMenuItem autostart = new ToolStripMenuItem("开机自启");
            autostart.CheckOnClick = true;
            autostart.Checked = RegistryAutostart.IsEnabled();
            autostart.CheckedChanged += delegate
            {
                try
                {
                    if (autostart.Checked) RegistryAutostart.Enable();
                    else RegistryAutostart.Disable();
                }
                catch (Exception ex) { Log.Write("开机自启设置失败: " + ex.Message); }
            };
            menu.Items.Add(autostart);

            ToolStripMenuItem sizeMenu = new ToolStripMenuItem("图标大小");
            AddSizeItem(sizeMenu, "小", 66);
            AddSizeItem(sizeMenu, "中", 76);
            AddSizeItem(sizeMenu, "标准", 84);
            AddSizeItem(sizeMenu, "大", 96);
            AddSizeItem(sizeMenu, "特大", 110);
            menu.Opening += delegate
            {
                for (int i = 0; i < sizeMenu.DropDownItems.Count; i++)
                {
                    ToolStripMenuItem mi = sizeMenu.DropDownItems[i] as ToolStripMenuItem;
                    if (mi != null && mi.Tag != null) mi.Checked = ((int)mi.Tag) == Cfg.CellSize;
                }
            };
            menu.Items.Add(sizeMenu);

            ToolStripMenuItem edgeMenu = new ToolStripMenuItem("触发热区宽度");
            AddEdgeItem(edgeMenu, "1 px（极窄）", 1);
            AddEdgeItem(edgeMenu, "2 px", 2);
            AddEdgeItem(edgeMenu, "3 px（默认）", 3);
            AddEdgeItem(edgeMenu, "4 px", 4);
            AddEdgeItem(edgeMenu, "6 px", 6);
            AddEdgeItem(edgeMenu, "8 px", 8);
            menu.Opening += delegate
            {
                for (int i = 0; i < edgeMenu.DropDownItems.Count; i++)
                {
                    ToolStripMenuItem mi = edgeMenu.DropDownItems[i] as ToolStripMenuItem;
                    if (mi != null && mi.Tag != null) mi.Checked = ((int)mi.Tag) == Cfg.HotEdgeWidth;
                }
            };
            menu.Items.Add(edgeMenu);
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem rescan = new ToolStripMenuItem("重新扫描桌面（追加新图标）");
            rescan.Click += delegate
            {
                try
                {
                    DockConfig fresh = ConfigStore.ScanDesktop();
                    int skipped;
                    int added = Cfg.AppendScanned(fresh, out skipped);
                    ConfigStore.Save(Cfg);
                    RefreshPanels();
                    Log.Write("重新扫描桌面: 新增 " + added + " 项，已存在 " + skipped + " 项（原有布局保持不变）");
                    if (_tray != null)
                    {
                        string tip = added > 0
                            ? "新增 " + added + " 个图标，已追加到末尾（原有布局不变）"
                            : "没有发现新图标，布局保持不变";
                        _tray.ShowBalloonTip(2500, "重新扫描桌面", tip, ToolTipIcon.Info);
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("重新扫描桌面失败: " + ex.Message);
                }
            };
            menu.Items.Add(rescan);

            ToolStripMenuItem edit = new ToolStripMenuItem("编辑配置文件");
            edit.Click += delegate
            {
                try { Process.Start("notepad.exe", "\"" + ConfigStore.FilePath + "\""); }
                catch (Exception ex) { Log.Write("打开配置失败: " + ex.Message); }
            };
            menu.Items.Add(edit);

            ToolStripMenuItem reload = new ToolStripMenuItem("重新加载配置");
            reload.Click += delegate
            {
                Cfg = ConfigStore.Load();
                RefreshPanels();
            };
            menu.Items.Add(reload);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem quit = new ToolStripMenuItem("退出");
            quit.Click += delegate { Application.Exit(); };
            menu.Items.Add(quit);

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate
            {
                if (_panels.Count > 0) _panels[0].ShowPanel();
            };
            _tray.BalloonTipTitle = "DesktopDock 已启动";
            _tray.BalloonTipText = "把鼠标移到屏幕左/右边缘即可唤出侧边栏。";
            _tray.ShowBalloonTip(2500);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _fsTimer.Stop(); _dispTimer.Stop(); } catch { }
            for (int i = 0; i < _panels.Count; i++) { try { _panels[i].Dispose(); } catch { } }
            for (int i = 0; i < _zones.Count; i++) { try { _zones[i].Dispose(); } catch { } }
            if (_tray != null) { try { _tray.Visible = false; _tray.Dispose(); } catch { } }
            if (_rawWheel != null) { try { _rawWheel.Dispose(); } catch { } }
        }
    }

    internal static class RegistryAutostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "DesktopDock";

        public static bool CanRegister()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                    return k != null;
            }
            catch { return false; }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    string v = k.GetValue(Name) as string;
                    return !string.IsNullOrEmpty(v);
                }
            }
            catch { return false; }
        }

        public static void Enable()
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                k.SetValue(Name, "\"" + Application.ExecutablePath + "\"");
        }

        public static void Disable()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                if (k != null) k.DeleteValue(Name, false);
        }
    }
}
