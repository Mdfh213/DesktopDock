using System;

namespace DesktopDock
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            if (args.Length > 0 && args[0] == "mergetest") { MergeTest.Run(); return; }
            Console.WriteLine("用法: MergeTest.exe mergetest");
        }
    }
}
