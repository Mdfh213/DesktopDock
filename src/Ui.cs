using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace DesktopDock
{
    /// <summary>图标提取与缓存（缓存到进程退出，避免每次重绘都读盘）。</summary>
    internal static class IconCache
    {
        private static readonly Dictionary<string, Image> _cache =
            new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        public static Image Get(string path, int size)
        {
            string key = path + "|" + size;
            Image cached;
            if (_cache.TryGetValue(key, out cached)) return cached;

            Image img = null;
            try
            {
                if (path != null && path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                {
                    img = GetShellIcon(path, size);
                }
                else if (File.Exists(path) || Directory.Exists(path))
                {
                    using (Icon ico = Icon.ExtractAssociatedIcon(path))
                    {
                        if (ico != null)
                        {
                            using (Bitmap src = ico.ToBitmap())
                            {
                                img = Scale(src, size);
                            }
                        }
                    }
                }
            }
            catch { }

            if (img == null) img = Fallback(Path.GetFileNameWithoutExtension(path), size);
            _cache[key] = img;
            return img;
        }

        private static Image Scale(Bitmap src, int size)
        {
            Bitmap dst = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(src, 0, 0, size, size);
            }
            return dst;
        }

        // ---- 取 shell: 命名空间项的图标（此电脑 / 控制面板 / 回收站…）----
        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(IntPtr pidl, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [System.Runtime.InteropServices.DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr p);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private static Image GetShellIcon(string shellPath, int size)
        {
            IntPtr pidl = IntPtr.Zero;
            try
            {
                uint attrs;
                if (SHParseDisplayName(shellPath, IntPtr.Zero, out pidl, 0, out attrs) != 0 || pidl == IntPtr.Zero)
                    return null;

                SHFILEINFO fi = new SHFILEINFO();
                const uint SHGFI_ICON = 0x000000100, SHGFI_LARGEICON = 0x000000000, SHGFI_PIDL = 0x000000008;
                IntPtr ok = SHGetFileInfo(pidl, 0, ref fi, (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(SHFILEINFO)),
                                          SHGFI_ICON | SHGFI_LARGEICON | SHGFI_PIDL);
                if (ok == IntPtr.Zero || fi.hIcon == IntPtr.Zero) return null;

                try
                {
                    using (Icon ico = Icon.FromHandle(fi.hIcon))
                    using (Bitmap src = ico.ToBitmap())
                        return Scale(src, size);
                }
                finally { DestroyIcon(fi.hIcon); }
            }
            catch { return null; }
            finally { if (pidl != IntPtr.Zero) CoTaskMemFree(pidl); }
        }

        private static Image Fallback(string name, int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.Clear(Color.Transparent);
                Rectangle r = new Rectangle(1, 1, size - 3, size - 3);
                using (GraphicsPath p = Ui.Rounded(r, size / 5))
                using (LinearGradientBrush b = new LinearGradientBrush(r,
                    Color.FromArgb(86, 96, 120), Color.FromArgb(52, 60, 78), LinearGradientMode.ForwardDiagonal))
                {
                    g.FillPath(b, p);
                }
                string letter = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
                Font f = Ui.SegoeFont(size * 0.42f, FontStyle.Bold);
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(letter, f, Brushes.White, new RectangleF(0, 0, size, size), sf);
                }
            }
            return bmp;
        }
    }

    /// <summary>缩放、配色、圆角等绘制辅助。</summary>
    internal static class Ui
    {
        public static float Scale = 1f;

        public static readonly Color PanelBg = Color.FromArgb(20, 20, 24);
        public static readonly Color PanelBorder = Color.FromArgb(46, 46, 56);
        public static readonly Color CellHover = Color.FromArgb(38, 38, 46);
        public static readonly Color CellDown = Color.FromArgb(52, 52, 62);
        public static readonly Color TextColor = Color.FromArgb(236, 236, 242);
        public static readonly Color SubText = Color.FromArgb(146, 146, 158);
        public static readonly Color Accent = Color.FromArgb(96, 170, 255);

        public static int Px(double v)
        {
            return (int)Math.Round(v * Scale);
        }

        public static float Pxf(double v)
        {
            return (float)(v * Scale);
        }

        // 字体缓存：绘制每帧都会取字体，重复 new Font 会持续产生 GDI 句柄和 GC 压力。
        private static readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();

        private static Font Cached(string family, float px, FontStyle style)
        {
            string key = family + "|" + px.ToString("0.###") + "|" + (int)style;
            Font f;
            if (_fonts.TryGetValue(key, out f)) return f;
            f = new Font(family, px, style, GraphicsUnit.Pixel);
            _fonts[key] = f;
            return f;
        }

        /// <summary>微软雅黑（界面文字）。返回值是共享实例，调用方不要 Dispose。</summary>
        public static Font Font(double size, FontStyle style)
        {
            return Cached("Microsoft YaHei UI", Pxf(size), style);
        }

        /// <summary>Segoe UI（字母图标）。返回值是共享实例，调用方不要 Dispose。</summary>
        public static Font SegoeFont(float px, FontStyle style)
        {
            return Cached("Segoe UI", px, style);
        }

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
