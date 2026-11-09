using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DesktopDock
{
    public class DockItem
    {
        /// <summary>稳定标识，用于在统一顺序表里引用该项。</summary>
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        /// <summary>空字符串表示顶层；否则是所属分组名。</summary>
        public string Group { get; set; }

        public DockItem() { Id = ""; Name = ""; Path = ""; Group = ""; }
    }

    public class DockConfig
    {
        public int HotEdgeWidth { get; set; }
        public int PanelWidth { get; set; }
        public int CellSize { get; set; }
        public int Gap { get; set; }
        public List<DockItem> Items { get; set; }

        /// <summary>
        /// 顶层的统一显示顺序：图标与分组合排在一条序列里。
        /// 元素形如 "i:&lt;Id&gt;"（普通图标）或 "g:&lt;分组名&gt;"（分组格子）。
        /// </summary>
        public List<string> Order { get; set; }

        /// <summary>旧版字段，只用于把老配置迁移进 Order。</summary>
        public List<string> GroupOrder { get; set; }

        public DockConfig()
        {
            HotEdgeWidth = 3;
            PanelWidth = 328;
            CellSize = 84;
            Gap = 12;
            Items = new List<DockItem>();
            Order = new List<string>();
            GroupOrder = new List<string>();
        }

        public void Normalize()
        {
            if (HotEdgeWidth < 1 || HotEdgeWidth > 24) HotEdgeWidth = 3;
            if (PanelWidth < 220 || PanelWidth > 640) PanelWidth = 328;
            if (CellSize < 56 || CellSize > 160) CellSize = 84;
            if (Gap < 4 || Gap > 40) Gap = 12;
            if (Items == null) Items = new List<DockItem>();

            Dictionary<string, bool> usedIds = new Dictionary<string, bool>();
            for (int i = 0; i < Items.Count; i++)
            {
                DockItem it = Items[i];
                if (it == null) { Items.RemoveAt(i); i--; continue; }
                if (it.Name == null) it.Name = "";
                if (it.Path == null) it.Path = "";
                if (it.Group == null) it.Group = "";
                if (it.Id == null) it.Id = "";
                // 旧版用 shell:ControlPanelFolder 打开的是「所有控制面板项」，
                // 与桌面快捷方式（控制面板主页）不是同一个文件夹，视图设置不共通。
                if (it.Path == "::{26EE0668-A00A-44D7-9371-BEB064C98683}") it.Path = "shell:ControlPanelFolder";
                if (it.Id.Length == 0 || usedIds.ContainsKey(it.Id))
                {
                    string id;
                    do { id = Guid.NewGuid().ToString("N").Substring(0, 8); } while (usedIds.ContainsKey(id));
                    it.Id = id;
                }
                usedIds[it.Id] = true;
            }

            if (GroupOrder == null) GroupOrder = new List<string>();
            if (Order == null) Order = new List<string>();
            if (Order.Count == 0) Order = BuildDefaultOrder();

            // 清掉失效项（图标不存在 / 图标已进分组 / 分组不存在），并去重
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            for (int i = Order.Count - 1; i >= 0; i--)
            {
                string tok = Order[i];
                if (!IsTokenValid(tok) || seen.ContainsKey(tok)) { Order.RemoveAt(i); continue; }
                seen[tok] = true;
            }
            // 补上还没登记的（新加入的图标/分组追加到末尾）
            List<string> missing = BuildDefaultOrder();
            for (int i = 0; i < missing.Count; i++)
                if (!seen.ContainsKey(missing[i])) Order.Add(missing[i]);
        }

        /// <summary>默认顺序：顶层图标按 Items 顺序在前，分组按旧 GroupOrder 顺序在后。</summary>
        private List<string> BuildDefaultOrder()
        {
            List<string> list = new List<string>();
            for (int i = 0; i < Items.Count; i++)
            {
                DockItem it = Items[i];
                if (it.Group != null && it.Group.Length == 0) list.Add("i:" + it.Id);
            }
            List<string> groups = GroupsByAppearance();
            for (int i = 0; i < groups.Count; i++) list.Add("g:" + groups[i]);
            return list;
        }

        private List<string> GroupsByAppearance()
        {
            List<string> list = new List<string>();
            // 优先沿用旧字段里记录的分组顺序
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

        private bool IsTokenValid(string tok)
        {
            if (string.IsNullOrEmpty(tok) || tok.Length < 3) return false;
            if (tok.StartsWith("g:"))
            {
                string g = tok.Substring(2);
                return g.Length > 0 && HasGroup(g);
            }
            if (tok.StartsWith("i:"))
            {
                DockItem it = ById(tok.Substring(2));
                return it != null && it.Group.Length == 0;
            }
            return false;
        }

        public DockItem ById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Items.Count; i++)
                if (Items[i].Id == id) return Items[i];
            return null;
        }

        public static string ItemToken(DockItem it) { return "i:" + it.Id; }
        public static string GroupToken(string g) { return "g:" + g; }

        public int IndexOfToken(string tok)
        {
            if (Order == null) return -1;
            return Order.IndexOf(tok);
        }

        public void RemoveToken(string tok)
        {
            if (Order == null || tok == null) return;
            int i = Order.IndexOf(tok);
            if (i >= 0) Order.RemoveAt(i);
        }

        public void InsertToken(int index, string tok)
        {
            if (Order == null || tok == null) return;
            if (index < 0 || index > Order.Count) index = Order.Count;
            Order.Insert(index, tok);
        }

        /// <summary>把顶层图标或分组放到指定位置（用于拖拽换位）。</summary>
        public void MoveTokenTo(string tok, int index)
        {
            if (Order == null) return;
            int cur = Order.IndexOf(tok);
            if (cur < 0) return;
            Order.RemoveAt(cur);
            if (index > Order.Count) index = Order.Count;
            if (index < 0) index = 0;
            Order.Insert(index, tok);
        }

        public bool HasGroup(string g)
        {
            if (string.IsNullOrEmpty(g)) return false;
            for (int i = 0; i < Items.Count; i++)
                if (Items[i].Group == g) return true;
            return false;
        }

        /// <summary>所有分组名，按统一顺序表里的排列返回。</summary>
        public List<string> Groups()
        {
            List<string> list = new List<string>();
            if (Order != null)
            {
                for (int i = 0; i < Order.Count; i++)
                {
                    string tok = Order[i];
                    if (tok.Length > 2 && tok.StartsWith("g:"))
                    {
                        string g = tok.Substring(2);
                        if (HasGroup(g) && !list.Contains(g)) list.Add(g);
                    }
                }
            }
            for (int i = 0; i < Items.Count; i++)
            {
                string g = Items[i].Group;
                if (g != null && g.Length > 0 && !list.Contains(g)) list.Add(g);
            }
            return list;
        }
        /// <summary>
        /// 把一次桌面扫描的结果**追加**到现有配置末尾：
        /// 已存在的路径跳过，不改动任何已有条目、分组和排列顺序。
        /// 返回新增数量，skipped 返回因已存在而跳过的数量。
        /// </summary>
        public int AppendScanned(DockConfig fresh, out int skipped)
        {
            int added = 0;
            skipped = 0;
            if (fresh == null || fresh.Items == null) return 0;

            Dictionary<string, bool> known = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Items.Count; i++)
            {
                string p = Items[i] == null ? null : Items[i].Path;
                if (!string.IsNullOrEmpty(p) && !known.ContainsKey(p)) known[p] = true;
            }

            for (int i = 0; i < fresh.Items.Count; i++)
            {
                DockItem src = fresh.Items[i];
                if (src == null || string.IsNullOrEmpty(src.Path)) continue;
                if (known.ContainsKey(src.Path)) { skipped++; continue; }

                DockItem copy = new DockItem();
                copy.Path = src.Path;
                copy.Name = src.Name;
                copy.Group = "";            // 新扫到的统一放顶层
                Items.Add(copy);
                known[src.Path] = true;
                added++;
            }

            Normalize();                    // 生成 Id，并把 token 追加到 Order 末尾
            return added;
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

        /// <summary>是否记录逐次交互（点击/拖拽等）。默认关闭，用 --debug 启动时打开。</summary>
        public static bool Verbose;

        public static void Debug(string msg)
        {
            if (!Verbose) return;
            Write("  [dbg] " + msg);
        }

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
