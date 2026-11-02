using System;
using System.Drawing;
using System.Windows.Forms;

namespace DesktopDock
{
    /// <summary>贴在屏幕左/右边缘的透明触发条，鼠标进入即请求弹出面板。</summary>
    internal sealed class EdgeZone : Form
    {
        public bool IsLeft;
        public Screen ScreenRef;
        public event EventHandler Hot;

        private bool _hot;

        public EdgeZone(bool left, Screen screen, int width)
        {
            IsLeft = left;
            ScreenRef = screen;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(18, 18, 22);
            Opacity = 0.02;          // 几乎不可见，但保证可被鼠标命中
            Cursor = Cursors.Default;
            AutoScaleMode = AutoScaleMode.None;

            int w = Math.Max(2, Ui.Px(width));
            Rectangle b = screen.Bounds;
            if (left) Bounds = new Rectangle(b.Left, b.Top, w, b.Height);
            else Bounds = new Rectangle(b.Right - w, b.Top, w, b.Height);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hot = true;
            if (Hot != null) Hot(this, EventArgs.Empty);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hot = false;
        }

        public bool IsHot { get { return _hot; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(150, 150, 165)))
            {
                int barW = Math.Max(1, Width / 3);
                int x = IsLeft ? Width - barW : 0;
                int inset = Ui.Px(40);
                e.Graphics.FillRectangle(b, new Rectangle(x, inset, barW, Height - inset * 2));
            }
        }
    }
}
