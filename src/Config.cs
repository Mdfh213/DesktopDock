using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DesktopDock
{
    public class DockItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
        /// <summary>空字符串表示顶层；否则是所属分组名。</summary>
        public string Group { get; set; }

        public DockItem() { Name = ""; Path = ""; Group = ""; }
    }

    public class DockConfig
    {
        public bool HideDesktopIcons { get; set; }
        public int HotEdgeWidth { get; set; }
        public int PanelWidth { get; set; }
        public int CellSize { get; set; }
        public int Gap { get; set; }
        public double PanelOpacity { get; set; }
        public List<DockItem> Items { get; set; }
        /// <summary>分组格子的显示顺序（可拖动调整）。</summary>
        public List<string> GroupOrder { get; set; }

        public DockConfig()
        {
            HideDesktopIcons = false;   // 桌面图标交给用户自己管理
            HotEdgeWidth = 6;
            PanelWidth = 328;
            CellSize = 84;
            Gap = 12;
            PanelOpacity = 0.97;
            Items = new List<DockItem>();
            GroupOrder = new List<string>();
        }

        public void Normalize()
        {
            if (HotEdgeWidth < 2 || HotEdgeWidth > 24) HotEdgeWidth = 6;
            if (PanelWidth < 220 || PanelWidth > 640) PanelWidth = 328;
            if (CellSize < 56 || CellSize > 160) CellSize = 84;
            if (Gap < 4 || Gap > 40) Gap = 12;
            if (PanelOpacity < 0.5 || PanelOpacity > 1.0) PanelOpacity = 0.97;
            if (Items == null) Items = new List<DockItem>();
            for (int i = 0; i < Items.Count; i++)
            {
                DockItem it = Items[i];
                if (it == null) { Items.RemoveAt(i); i--; continue; }
                if (it.Name == null) it.Name = "";
                if (it.Path == null) it.Path = "";
                if (it.Group == null) it.Group = "";
            }
            if (GroupOrder == null) GroupOrder = new List<string>();
            // 去掉已不存在或重复的分组名
            for (int i = GroupOrder.Count - 1; i >= 0; i--)
            {
                string g = GroupOrder[i];
                if (string.IsNullOrEmpty(g) || GroupOrder.IndexOf(g) != i || !HasGroup(g))
                    GroupOrder.RemoveAt(i);
            }
        }

        public bool HasGroup(string g)
        {
            for (int i = 0; i < Items.Count; i++)
                if (Items[i].Group == g) return true;
            return false;
        }

        /// <summary>分组显示顺序：先按 GroupOrder，再把未登记的追加到末尾。</summary>
        public List<string> Groups()
        {
            List<string> list = new List<string>();
            if (GroupOrder != null)
            {
                for (int i = 0; i < GroupOrder.Count; i++)
                {
                    string g = GroupOrder[i];
                    if (!string.IsNullOrEmpty(g) && HasGroup(g) && !list.Contains(g)) list.Add(g);
                }
            }
            for (int i = 0; i < Items.Count; i++)
            {
                string g = Items[i].Group;
                if (g != null && g.Length > 0 && !list.Contains(g)) list.Add(g);
            }
            return list;
        }
    }

    internal static class ConfigStore
    {
        public static string FilePath
        {
            get { return Path.Combine(Application.StartupPath, "config.json"); }
        }

        public static DockConfig Load()
        {
            DockConfig cfg = null;
            string path = FilePath;
            if (File.Exists(path))
            {
                try
                {
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    cfg = ser.Deserialize<DockConfig>(File.ReadAllText(path));
                }
                catch (Exception ex)
                {
                    Log.Write("配置读取失败: " + ex.Message);
                    try
                    {
                        File.Copy(path, path + ".bad", true);
                    }
                    catch { }
                }
            }
            if (cfg == null)
            {
                cfg = ScanDesktop();
                Save(cfg);
            }
            cfg.Normalize();
            return cfg;
        }

        public static void Save(DockConfig cfg)
        {
            try
            {
                cfg.Normalize();
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = 4 * 1024 * 1024;
                File.WriteAllText(FilePath, ser.Serialize(cfg));
            }
            catch (Exception ex)
            {
                Log.Write("配置保存失败: " + ex.Message);
            }
        }

        /// <summary>从桌面（用户 + 公共）扫描快捷方式，生成初始配置。</summary>
        public static DockConfig ScanDesktop()
        {
            DockConfig cfg = new DockConfig();
            List<string> dirs = new List<string>();
            string user = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (!string.IsNullOrEmpty(user)) dirs.Add(user);
            if (!string.IsNullOrEmpty(common) && common != user) dirs.Add(common);

            for (int d = 0; d < dirs.Count; d++)
            {
                string[] files;
                try { files = Directory.GetFiles(dirs[d]); }
                catch { continue; }
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Length; i++)
                {
                    string ext = Path.GetExtension(files[i]).ToLowerInvariant();
                    if (ext != ".lnk" && ext != ".url" && ext != ".exe") continue;
                    DockItem it = new DockItem();
                    it.Path = files[i];
                    it.Name = Path.GetFileNameWithoutExtension(files[i]);
                    it.Group = "";
                    cfg.Items.Add(it);
                }
            }
            return cfg;
        }
    }

    internal static class Log
    {
        private static readonly object _lock = new object();

        public static void Write(string msg)
        {
            try
            {
                lock (_lock)
                {
                    string p = Path.Combine(Application.StartupPath, "dock.log");
                    // 超过 512KB 就轮转，避免日志无限增长
                    try
                    {
                        FileInfo fi = new FileInfo(p);
                        if (fi.Exists && fi.Length > 512 * 1024)
                        {
                            string old = p + ".1";
                            if (File.Exists(old)) File.Delete(old);
                            File.Move(p, old);
                        }
                    }
                    catch { }
                    File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
