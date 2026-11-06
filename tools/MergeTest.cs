using System;
using System.Collections.Generic;
using System.IO;

namespace DesktopDock
{
    internal static class MergeTest
    {
        public static void Run()
        {
            string path = ConfigStore.FilePath;
            Console.WriteLine("配置: " + path);
            DockConfig cfg = ConfigStore.Load();
            int beforeItems = cfg.Items.Count, beforeOrder = cfg.Order.Count;
            Console.WriteLine("扫描前: items=" + beforeItems + " order=" + beforeOrder);
            Console.Write("  前 3 个顺序: ");
            for (int i = 0; i < 3 && i < cfg.Order.Count; i++) Console.Write(TokenName(cfg, cfg.Order[i]) + "  ");
            Console.WriteLine();

            // 构造一次"扫描结果"：5 个已存在 + 2 个新的
            DockConfig fresh = new DockConfig();
            int taken = 0;
            for (int i = 0; i < cfg.Items.Count && taken < 5; i++)
            {
                if (string.IsNullOrEmpty(cfg.Items[i].Group)) { fresh.Items.Add(Clone(cfg.Items[i])); taken++; }
            }
            fresh.Items.Add(MakeItem("新图标A", @"C:\Test\newA.lnk"));
            fresh.Items.Add(MakeItem("新图标B", @"C:\Test\newB.lnk"));

            int skipped;
            int added = cfg.AppendScanned(fresh, out skipped);

            Console.WriteLine("扫描结果: 新扫描 " + fresh.Items.Count + " 项 -> 新增 " + added + "，跳过(已存在) " + skipped);
            Console.WriteLine("扫描后: items=" + cfg.Items.Count + " order=" + cfg.Order.Count);
            Console.Write("  前 3 个顺序: ");
            for (int i = 0; i < 3 && i < cfg.Order.Count; i++) Console.Write(TokenName(cfg, cfg.Order[i]) + "  ");
            Console.WriteLine();
            Console.Write("  末 3 个顺序: ");
            for (int i = Math.Max(0, cfg.Order.Count - 3); i < cfg.Order.Count; i++) Console.Write(TokenName(cfg, cfg.Order[i]) + "  ");
            Console.WriteLine();

            bool okCount = cfg.Items.Count == beforeItems + 2;
            bool okAdded = added == 2 && skipped == 5;
            Console.WriteLine("检查: 条目 +2 = " + okCount + " / 新增2跳过5 = " + okAdded);
            Console.WriteLine(okCount && okAdded ? "== 通过 ==" : "== 失败 ==");
        }

        private static DockItem Clone(DockItem s)
        {
            DockItem it = new DockItem();
            it.Id = s.Id; it.Name = s.Name; it.Path = s.Path; it.Group = s.Group;
            return it;
        }

        private static DockItem MakeItem(string name, string path)
        {
            DockItem it = new DockItem();
            it.Name = name; it.Path = path; it.Group = "";
            return it;
        }

        private static string TokenName(DockConfig cfg, string tok)
        {
            if (tok.StartsWith("g:")) return "[组]" + tok.Substring(2);
            DockItem it = cfg.ById(tok.Substring(2));
            return it != null ? it.Name : "?";
        }
    }
}
