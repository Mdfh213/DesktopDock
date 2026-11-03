using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace DesktopDock
{
    /// <summary>从屏幕边缘滑出的侧边面板。</summary>
    internal sealed class DockPanel : Form
    {
        private readonly Screen _screen;
        private readonly bool _left;
        private readonly DockManager _mgr;
        private readonly DockCanvas _canvas;

        private readonly Timer _anim = new Timer();
        private readonly Timer _hide = new Timer();

        private bool _showing;
        private int _targetX;
        private int _shownX;
        private int _hiddenX;
        private int _outsideMs;

        // 动画：按时间里程计算缓动，避免 delta/3 那种“前快后慢”的拖沓感
        private const int AnimMs = 190;
        private readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();
        private int _animFromX;
        private int _curX;
        private int _animTicks;

        public bool IsLeft { get { return _left; } }
        public bool IsShowingPanel { get { return _showing; } }
        public DockCanvas Canvas { get { return _canvas; } }

        public DockPanel(Screen screen, bool left, DockManager mgr)
        {
            _screen = screen;
            _left = left;
            _mgr = mgr;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Ui.PanelBg;
            DoubleBuffered = true;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;

            // 注意：整块面板通屏高度（上下不留缝），并且不使用 Form.Opacity。
            // Opacity<1 会让窗口变成分层窗口(WS_EX_LAYERED)，子控件的鼠标命中会出问题。
            int margin = Ui.Px(10);
            int w = Ui.Px(mgr.Cfg.PanelWidth);
            Rectangle sb = screen.Bounds;
            int h = sb.Height;

            if (left) { _shownX = sb.Left + margin; _hiddenX = sb.Left - w - 4; }
            else { _shownX = sb.Right - w - margin; _hiddenX = sb.Right + 4; }

            Bounds = new Rectangle(_hiddenX, sb.Top, w, h);

            _canvas = new DockCanvas(this, mgr);
            _canvas.Dock = DockStyle.Fill;
            _canvas.Margin = new Padding(0);
            Controls.Add(_canvas);

            _anim.Interval = 10;
            _anim.Tick += AnimTick;

            _hide.Interval = 120;
            _hide.Tick += HideTick;

            _targetX = _hiddenX;
        }

        // 注意：这里不能用 WS_EX_NOACTIVATE —— 那样窗口拿不到焦点，
        // 而 WM_MOUSEWHEEL 只会发给焦点窗口，滚轮就彻底失效。
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(this, 2);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _canvas.HandleWheel(e.Delta);
        }

        public void ShowPanel()
        {
            if (_mgr.Suspended) { return; }
            _showing = true;
            _outsideMs = 0;
            _targetX = _shownX;
            _canvas.ResetSlide();               // 内容不做位移，整块面板移动
            if (!Visible) { _curX = _hiddenX; Native.MoveTo(Handle, _curX, Bounds.Y); Visible = true; }
            _animFromX = _curX;
            _clock.Restart();
            _hide.Start();
            _anim.Start();

            // 激活面板：保证滚轮（发给焦点窗口）和键盘操作可用
            try
            {
                _canvas.Focus();
                bool ok = Native.ForceForeground(Handle);
                Log.Write("activate me=" + Handle + " fg=" + Native.GetForegroundWindow() + " ok=" + ok);
            }
            catch { }
        }

        public void HidePanel()
        {
            if (!_showing && !Visible) return;
            _showing = false;
            _targetX = _hiddenX;
            _animFromX = _curX;
            _clock.Restart();
            _hide.Stop();
            _anim.Start();
        }

        public Screen ScreenRef { get { return _screen; } }
        public bool IsLeftSide { get { return _left; } }

        /// <summary>鼠标是否停在本侧屏幕边缘的热区里。</summary>
        public bool CursorInHotBand(Point p, int width)
        {
            Rectangle sb = _screen.Bounds;
            int band = Ui.Px(width);
            if (p.Y < sb.Top || p.Y >= sb.Bottom) return false;
            if (_left) return p.X >= sb.Left && p.X < sb.Left + band;
            return p.X <= sb.Right && p.X > sb.Right - band;
        }

        /// <summary>配置改变后（例如图标大小）重新布局面板。</summary>
        public void ApplyLayout()
        {
            int margin = Ui.Px(10);
            int w = Ui.Px(_mgr.Cfg.PanelWidth);
            Rectangle sb = _screen.Bounds;
            int h = sb.Height;

            if (_left) { _shownX = sb.Left + margin; _hiddenX = sb.Left - w - 4; }
            else { _shownX = sb.Right - w - margin; _hiddenX = sb.Right + 4; }

            Bounds = new Rectangle(_showing ? _shownX : _hiddenX, sb.Top, w, h);
            _canvas.RefreshContent();
        }

        private void AnimTick(object sender, EventArgs e)
        {
            _animTicks++;
            if (_animTicks <= 2) Log.Write("AnimTick #" + _animTicks + " enabled=" + _anim.Enabled);
            double p = _clock.ElapsedMilliseconds / (double)(_showing ? AnimMs : 165);
            if (p >= 1.0) p = 1.0;

            // 展开：ease-out（快进慢停）；收起：ease-in（缓起加速离开）
            double eased = _showing ? 1.0 - Math.Pow(1.0 - p, 3.0) : p * p * p;
            int newLeft = (int)Math.Round(_animFromX + (_targetX - _animFromX) * eased);
            _curX = newLeft;
            Native.MoveTo(Handle, newLeft, Bounds.Y);

            // 收起时不做透明度渐变：整块滑出屏幕比“边滑边淡”自然得多

            if (p >= 1.0)
            {
                _curX = _targetX;
                Native.MoveTo(Handle, _targetX, Bounds.Y);
                _anim.Stop();
                if (!_showing)
                {
                    Visible = false;
                    _canvas.ResetToRoot();
                }
            }
        }

        public bool KeepOpen;

        private void HideTick(object sender, EventArgs e)
        {
            if (!_showing) return;
            if (KeepOpen) { _outsideMs = 0; return; }
            if (_canvas.Dragging) { _outsideMs = 0; return; }

            // 面板有外边距，边缘热区与面板之间的缝隙也算“在面板上”
            Rectangle keep = Bounds;
            Rectangle sb = _screen.Bounds;
            if (_left) keep = Rectangle.FromLTRB(sb.Left, Bounds.Top, Bounds.Right, Bounds.Bottom);
            else keep = Rectangle.FromLTRB(Bounds.Left, Bounds.Top, sb.Right, Bounds.Bottom);

            Point p = Cursor.Position;
            if (keep.Contains(p)) { _outsideMs = 0; return; }
            _outsideMs += _hide.Interval;
            if (_outsideMs >= 280) HidePanel();
        }

        public void RefreshConfig() { _canvas.RefreshContent(); }

        public void NotifyMouseInside()
        {
            _outsideMs = 0;
        }
    }

    /// <summary>格子画布：绘制、命中测试、拖拽合并。</summary>
    internal sealed class DockCanvas : Control
    {
        private struct Cell
        {
            public Rectangle Bounds;
            public bool IsGroup;
            public bool IsBack;
            public bool IsAdd;
            public bool Missing;
            public string Group;
            public DockItem Item;
        }

        private readonly DockPanel _panel;
        private readonly DockManager _mgr;
        private readonly System.Collections.Generic.List<Cell> _cells =
            new System.Collections.Generic.List<Cell>();

        private string _group = "";
        private int _hover = -1;
        private int _press = -1;
        private Point _pressPt;
        private bool _dragging;
        private int _dragIndex = -1;
        private Point _cursor;
        private int _cellSize;

        // 滚动
        private int _scrollY;
        private int _maxScroll;
        private int _contentH;
        private int _cols = 1;
        private readonly Timer _snapTimer = new Timer();
        private Rectangle _pageUpRect;
        private Rectangle _pageDownRect;

        // 长按合并
        private readonly Timer _mergeTimer = new Timer();
        private readonly Timer _autoScroll = new Timer();
        private int _hoverSince;
        private bool _mergeMode;

        // 点击兜底（不依赖窗口消息）：轮询物理按键状态
        private bool _pollDown;
        private int _pollDownAt;
        private Point _pollDownPt;
        private int _lastClickIndex = -1;
        private int _lastClickTick;
        private int _lastMsgTick;

        // 操作反馈
        private string _toast = "";
        private int _toastUntil;

        // 内容滑动（窗口不动，只平移绘制内容）
        private float _slideX;
        private float _slideFrom;
        private float _slideTo;
        private readonly System.Diagnostics.Stopwatch _slideClock = new System.Diagnostics.Stopwatch();
        private readonly Timer _slideTimer = new Timer();
        private Action _slideDone;
        private bool _pollLogged;

        /// <summary>清零内容位移（改用整块面板移动时调用）。</summary>
        public void ResetSlide()
        {
            _slideTimer.Stop();
            _slideDone = null;
            _slideX = 0f;
            Invalidate();
        }

        public void SlideIn(bool fromLeft)
        {
            _slideDone = null;
            _pollLogged = false;
            // 左侧面板从屏幕左边缘滑入，右侧面板从右边缘滑入
            _slideFrom = fromLeft ? -Width : Width;
            _slideTo = 0f;
            _slideClock.Restart();
            _slideTimer.Start();
        }

        public void SlideOut(bool toLeft, Action onDone)
        {
            _slideDone = onDone;
            _slideFrom = _slideX;
            // 回退时向各自那一侧滑出：左面板向左、右面板向右
            _slideTo = toLeft ? -Width : Width;
            _slideClock.Restart();
            _slideTimer.Start();
        }

        private void SlideTick(object sender, EventArgs e)
        {
            double p = _slideClock.ElapsedMilliseconds / 190.0;
            if (p >= 1.0) p = 1.0;
            double eased = 1.0 - Math.Pow(1.0 - p, 3.0);
            _slideX = (float)(_slideFrom + (_slideTo - _slideFrom) * eased);
            Invalidate();
            if (p >= 1.0)
            {
                _slideTimer.Stop();
                _slideX = _slideTo;
                Action done = _slideDone;
                _slideDone = null;
                if (done != null) done();
            }
        }

        public bool Dragging { get { return _dragging; } }
        public bool CanScroll { get { return _maxScroll > 0; } }

        /// <summary>面板底部的短提示（启动成功/失败）。</summary>
        public void ShowToast(string text, int ms)
        {
            _toast = text == null ? "" : text;
            _toastUntil = Environment.TickCount + ms;
            Invalidate();
        }

        /// <summary>
        /// 点击兜底：直接轮询物理左键状态，不依赖 WM_LBUTTONDOWN/UP 是否送达。
        /// 与消息路径用时间戳去重，避免重复启动。
        /// </summary>
        private void PollClick()
        {
            if (!_pollLogged) { _pollLogged = true; Log.Write("poll click active: timer running"); }
            bool down = (Native.GetAsyncKeyState(0x01) & 0x8000) != 0;
            if (down && !_pollDown)
            {
                _pollDown = true;
                _pollDownAt = Environment.TickCount;
                _pollDownPt = Cursor.Position;
                return;
            }
            if (down || !_pollDown) return;

            _pollDown = false;
            int held = Environment.TickCount - _pollDownAt;
            if (held > 700) return;                       // 长按交给拖拽逻辑
            if (!_panel.IsShowingPanel || _dragging) return;
            // 窗口消息正常时不要重复处理（否则一次物理点击会被算成两下）
            if (_lastMsgTick != 0 && Environment.TickCount - _lastMsgTick < 900) return;

            Point local = PointToClient(_pollDownPt);
            if (local.X < 0 || local.X > Width || local.Y < 0 || local.Y > Height) return;

            int hit = HitTest(local);
            if (hit < 0) return;

            Log.Write("poll click hit=" + hit + " local=" + local);
            HandleClick(hit);
        }

        /// <summary>滚动：连续位移（不做即时吸附，否则小步滚动会被抹成 0）。</summary>
        public void ScrollBy(int dy)
        {
            if (_maxScroll <= 0) return;
            int v = _scrollY + dy;
            if (v < 0) v = 0;
            if (v > _maxScroll) v = _maxScroll;
            if (v == _scrollY) return;
            _scrollY = v;
            _snapTimer.Stop();
            _snapTimer.Start();          // 停手后再吸附到整行
            Rebuild();
            Invalidate();
        }

        /// <summary>滚轮入口：Ctrl+滚轮调图标大小，否则滚动。</summary>
        public void HandleWheel(int delta)
        {
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                _mgr.SetCellSize(_mgr.Cfg.CellSize + (delta > 0 ? 4 : -4));
                return;
            }
            int step = Ui.Px(Math.Max(40, Cfg.CellSize));
            ScrollBy(-delta / 120 * step);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            HandleWheel(e.Delta);
        }

        private void SnapTick(object sender, EventArgs e)
        {
            _snapTimer.Stop();
            if (_maxScroll <= 0) return;

            // 鼠标还停在上下滚动热区里就跳过吸附，否则会和自动滚动互相拉扯（底部抽搐）
            Point local = PointToClient(Cursor.Position);
            int zone = Ui.Px(64);
            bool inZone = local.X >= -Ui.Px(6) && local.X <= Width + Ui.Px(6) &&
                          ((local.Y >= -Ui.Px(6) && local.Y < zone) ||
                           (local.Y > Height - zone && local.Y <= Height + Ui.Px(6)));
            if (inZone) return;

            int pitch = Ui.Px(Cfg.CellSize) + Ui.Px(Cfg.Gap);
            if (pitch <= 0) return;
            int snapped = (int)Math.Floor(_scrollY / (double)pitch) * pitch;
            if (snapped < 0) snapped = 0;
            if (snapped > _maxScroll) snapped = _maxScroll;
            if (snapped == _scrollY) return;
            _scrollY = snapped;
            Rebuild();
            Invalidate();
        }

        public void PageBy(int dir)
        {
            int page = Math.Max(Ui.Px(80), Height - Ui.Px(60));
            ScrollBy(dir * page);
        }

        public DockCanvas(DockPanel panel, DockManager mgr)
        {
            _panel = panel;
            _mgr = mgr;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Ui.PanelBg;

            _mergeTimer.Interval = 120;
            _mergeTimer.Tick += MergeTick;

            // 鼠标贴住面板上下边缘时自动滚动（不依赖滚轮/焦点）
            _autoScroll.Interval = 55;
            _autoScroll.Tick += AutoScrollTick;
            _autoScroll.Start();

            _snapTimer.Interval = 180;
            _snapTimer.Tick += SnapTick;

            _slideTimer.Interval = 12;
            _slideTimer.Tick += SlideTick;
        }

        private void AutoScrollTick(object sender, EventArgs e)
        {
            if (_toastUntil != 0 && Environment.TickCount > _toastUntil)
            {
                _toastUntil = 0;
                _toast = "";
                Invalidate();
            }

            if (_panel.IsShowingPanel && !_dragging) PollClick();
            if (!_panel.IsShowingPanel) return;

            Point local = PointToClient(Cursor.Position);
            if (local.X < -Ui.Px(6) || local.X > Width + Ui.Px(6)) return;

            int zone = Ui.Px(70);
            bool nearTop = local.Y < zone;
            bool nearBottom = local.Y > Height - zone;

            // 拖动图标时贴住上下边界也会滚动，方便把图标拖到看不见的位置
            if (_dragging)
            {
                if (_maxScroll <= 0) return;
                if (nearTop) ScrollBy(-Ui.Px(26));
                else if (nearBottom) ScrollBy(Ui.Px(26));
                return;
            }

            if (_maxScroll <= 0) return;
            if (local.Y < zone) ScrollBy(-Ui.Px(30));
            else if (local.Y > Height - zone) ScrollBy(Ui.Px(30));
        }

        public void ResetToRoot()
        {
            if (_group.Length == 0 && _scrollY == 0) return;
            _group = "";
            _scrollY = 0;
            Rebuild();
        }

        public void RefreshContent()
        {
            Rebuild();
            Invalidate();
        }

        private DockConfig Cfg { get { return _mgr.Cfg; } }

        private void Rebuild()
        {
            _cells.Clear();
            _hover = -1;

            int pad = Ui.Px(14);
            int gap = Ui.Px(Cfg.Gap);
            int cell = Ui.Px(Cfg.CellSize);
            _cellSize = cell;

            int count;
            if (_group.Length == 0) count = CountRootCells();
            else count = CountGroupCells() + 2; // +1 返回 +1 添加

            _cols = Math.Max(1, (Width - pad * 2 + gap) / (cell + gap));
            if (_cols < 1) _cols = 1;

            int rows = (count + _cols - 1) / _cols;
            if (rows < 1) rows = 1;
            _contentH = pad * 2 + rows * (cell + gap) - gap;
            _maxScroll = Math.Max(0, _contentH - Height);

            if (_scrollY > _maxScroll) _scrollY = _maxScroll;
            if (_scrollY < 0) _scrollY = 0;

            // 翻页按钮（仅内容超出时出现）
            int btn = Ui.Px(24);
            _pageUpRect = new Rectangle(Width - btn - Ui.Px(6), Ui.Px(6), btn, btn);
            _pageDownRect = new Rectangle(Width - btn - Ui.Px(6), Height - btn - Ui.Px(6), btn, btn);

            int usedCols = Math.Min(_cols, Math.Max(1, count));
            int gridW = usedCols * cell + (usedCols - 1) * gap;
            int startX = (Width - gridW) / 2;
            int startY = pad - _scrollY;

            int idx = 0;
            if (_group.Length == 0)
            {
                // 按统一顺序表排布：图标与分组混排，可互相换位
                System.Collections.Generic.Dictionary<string, bool> doneItem = new System.Collections.Generic.Dictionary<string, bool>();
                System.Collections.Generic.Dictionary<string, bool> doneGroup = new System.Collections.Generic.Dictionary<string, bool>();

                for (int k = 0; k < Cfg.Order.Count; k++)
                {
                    string tok = Cfg.Order[k];
                    if (tok.Length < 3) continue;
                    if (tok.StartsWith("g:"))
                    {
                        string gname = tok.Substring(2);
                        if (!Cfg.HasGroup(gname) || doneGroup.ContainsKey(gname)) continue;
                        Cell c = new Cell();
                        c.IsGroup = true;
                        c.Group = gname;
                        c.Bounds = CellRect(idx, _cols, startX, startY, cell, gap);
                        _cells.Add(c);
                        doneGroup[gname] = true;
                        idx++;
                    }
                    else if (tok.StartsWith("i:"))
                    {
                        DockItem it = Cfg.ById(tok.Substring(2));
                        if (it == null || it.Group.Length > 0 || doneItem.ContainsKey(it.Id)) continue;
                        _cells.Add(MakeAppCell(it, idx, _cols, startX, startY, cell, gap));
                        doneItem[it.Id] = true;
                        idx++;
                    }
                }

                // 兜底：顺序表里漏掉的图标/分组
                for (int i = 0; i < Cfg.Items.Count; i++)
                {
                    DockItem it = Cfg.Items[i];
                    if (it.Group != null && it.Group.Length > 0) continue;
                    if (doneItem.ContainsKey(it.Id)) continue;
                    _cells.Add(MakeAppCell(it, idx, _cols, startX, startY, cell, gap));
                    idx++;
                }
                System.Collections.Generic.List<string> groups = Cfg.Groups();
                for (int g = 0; g < groups.Count; g++)
                {
                    if (doneGroup.ContainsKey(groups[g])) continue;
                    Cell c = new Cell();
                    c.IsGroup = true;
                    c.Group = groups[g];
                    c.Bounds = CellRect(idx, _cols, startX, startY, cell, gap);
                    _cells.Add(c);
                    idx++;
                }

                // “添加图标”格子（两种视图都有）
                Cell add = new Cell();
                add.IsAdd = true;
                add.Bounds = CellRect(idx, _cols, startX, startY, cell, gap);
                _cells.Add(add);
                idx++;
            }
            else
            {
                Cell back = new Cell();
                back.IsBack = true;
                back.Bounds = CellRect(idx, _cols, startX, startY, cell, gap);
                _cells.Add(back);
                idx++;
                for (int i = 0; i < Cfg.Items.Count; i++)
                {
                    DockItem it = Cfg.Items[i];
                    if (it.Group != _group) continue;
                    _cells.Add(MakeAppCell(it, idx, _cols, startX, startY, cell, gap));
                    idx++;
                }
                Cell addInGroup = new Cell();
                addInGroup.IsAdd = true;
                addInGroup.Bounds = CellRect(idx, _cols, startX, startY, cell, gap);
                _cells.Add(addInGroup);
            }
        }

        private int CountRootCells()
        {
            int n = 0;
            for (int i = 0; i < Cfg.Items.Count; i++)
            {
                DockItem it = Cfg.Items[i];
                if (it.Group == null || it.Group.Length == 0) n++;
            }
            return n + Cfg.Groups().Count + 1;   // +1 添加格子
        }

        private int CountGroupCells()
        {
            int n = 0;
            for (int i = 0; i < Cfg.Items.Count; i++)
                if (Cfg.Items[i].Group == _group) n++;
            return n;
        }

        private Cell MakeAppCell(DockItem it, int index, int cols, int startX, int startY, int cell, int gap)
        {
            Cell c = new Cell();
            c.Item = it;
            c.Group = it.Group;
            c.Missing = !File.Exists(it.Path) && !Directory.Exists(it.Path);
            c.Bounds = CellRect(index, cols, startX, startY, cell, gap);
            return c;
        }

        private Rectangle CellRect(int index, int cols, int startX, int startY, int cell, int gap)
        {
            int col = index % cols;
            int row = index / cols;
            return new Rectangle(startX + col * (cell + gap), startY + row * (cell + gap), cell, cell);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Rebuild();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush bg = new SolidBrush(Ui.PanelBg))
                g.FillRectangle(bg, ClientRectangle);

            // 内容整体做水平位移（滑入/滑出），窗口本身不动
            GraphicsState state = g.Save();
            int slide = (int)Math.Round(_slideX);
            g.TranslateTransform(slide, 0);

            using (Pen p = new Pen(Ui.PanelBorder, Math.Max(1f, Ui.Pxf(1))))
            using (GraphicsPath path = Ui.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Ui.Px(12)))
                g.DrawPath(p, path);

            if (_cells.Count == 0)
            {
                using (Font f = Ui.Font(12, FontStyle.Regular))
                using (SolidBrush sb = new SolidBrush(Ui.SubText))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("桌面是空的\n右键菜单 → 重新扫描桌面", f, sb, ClientRectangle, sf);
                }
                g.Restore(state);
                return;
            }

            string dragGroup = null;
            DockItem dragItem = null;
            if (_dragging && _dragIndex >= 0 && _dragIndex < _cells.Count)
            {
                dragGroup = _cells[_dragIndex].Group;
                dragItem = _cells[_dragIndex].Item;
            }

            for (int i = 0; i < _cells.Count; i++)
            {
                bool isDragSource = _dragging && i == _dragIndex;
                int alpha = isDragSource ? 90 : 255;
                if (_cells[i].Missing && alpha > 120) alpha = 120;   // 失效的快捷方式变暗
                DrawCell(g, i, alpha);
            }

            if (_dragging && dragItem != null)
            {
                int size = Ui.Px(48);
                Image icon = IconCache.Get(dragItem.Path, size);
                g.DrawImage(icon, _cursor.X - size / 2, _cursor.Y - size / 2, size, size);
            }

            // 翻页按钮
            if (_maxScroll > 0)
            {
                DrawPageButton(g, _pageUpRect, true, _scrollY > 0);
                DrawPageButton(g, _pageDownRect, false, _scrollY < _maxScroll);
            }

            // 滚动条
            if (_maxScroll > 0)
            {
                int trackX = Width - Ui.Px(5);
                int trackW = Ui.Px(3);
                double ratio = (double)Height / _contentH;
                int thumbH = Math.Max(Ui.Px(36), (int)(Height * ratio));
                int travel = Height - thumbH - Ui.Px(8);
                int thumbY = Ui.Px(4) + (int)(travel * ((double)_scrollY / _maxScroll));
                using (GraphicsPath tp = Ui.Rounded(new Rectangle(trackX, Ui.Px(4), trackW, Height - Ui.Px(8)), trackW / 2))
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(28, 28, 34)))
                    g.FillPath(tb, tp);
                using (GraphicsPath tp = Ui.Rounded(new Rectangle(trackX, thumbY, trackW, thumbH), trackW / 2))
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(96, 96, 112)))
                    g.FillPath(tb, tp);
            }

            // 底部提示（启动成功 / 失败）
            if (_toast.Length > 0)
            {
                using (Font f = Ui.Font(11, FontStyle.Regular))
                {
                    Size sz = TextRenderer.MeasureText(_toast, f);
                    int bw = Math.Min(Width - Ui.Px(24), sz.Width + Ui.Px(24));
                    int bh = sz.Height + Ui.Px(12);
                    Rectangle box = new Rectangle((Width - bw) / 2, Height - bh - Ui.Px(18), bw, bh);
                    using (GraphicsPath p = Ui.Rounded(box, Ui.Px(8)))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(235, 32, 34, 42)))
                        g.FillPath(b, p);
                    TextRenderer.DrawText(g, _toast, f, box, Ui.TextColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }

            g.Restore(state);
        }

        private void DrawCell(Graphics g, int index, int alpha)
        {
            Cell c = _cells[index];
            Rectangle r = c.Bounds;

            bool hover = index == _hover;
            bool dropTarget = _dragging && index == _hover && index != _dragIndex;
            bool merging = _mergeMode && dropTarget;
            Color fill = dropTarget
                ? (merging ? Color.FromArgb(64, 96, 148) : Color.FromArgb(50, 50, 62))
                : (hover ? Ui.CellHover : Color.Empty);

            if (fill != Color.Empty)
            {
                using (GraphicsPath path = Ui.Rounded(r, Ui.Px(10)))
                using (SolidBrush b = new SolidBrush(fill))
                    g.FillPath(b, path);
                if (dropTarget)
                {
                    float w = merging ? Ui.Pxf(2.5f) : Ui.Pxf(1.5f);
                    using (Pen pen = new Pen(Ui.Accent, Math.Max(1f, w)))
                    using (GraphicsPath path2 = Ui.Rounded(r, Ui.Px(10)))
                        g.DrawPath(pen, path2);
                }
            }

            if (c.IsBack)
            {
                using (Font f = Ui.Font(13, FontStyle.Regular))
                using (SolidBrush b = new SolidBrush(Ui.TextColor))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("‹  返回", f, b, r, sf);
                }
                return;
            }

            if (c.IsAdd)
            {
                Rectangle inner = new Rectangle(r.X + Ui.Px(6), r.Y + Ui.Px(6),
                                                r.Width - Ui.Px(12), r.Height - Ui.Px(12));
                using (GraphicsPath gp = Ui.Rounded(inner, Ui.Px(10)))
                using (SolidBrush addFill = new SolidBrush(Color.FromArgb(34, 255, 255, 255)))
                    g.FillPath(addFill, gp);

                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;

                    using (Font pf = new Font("Segoe UI", _cellSize * 0.40f, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (SolidBrush pb = new SolidBrush(Color.FromArgb(210, 220, 235)))
                    {
                        RectangleF plusRect = new RectangleF(r.X, r.Y, r.Width, r.Height * 0.66f);
                        g.DrawString("+", pf, pb, plusRect, sf);
                    }
                    using (Font lf = new Font("Microsoft YaHei UI", _cellSize * 0.15f, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (SolidBrush lb = new SolidBrush(Color.FromArgb(160, 170, 185)))
                    {
                        RectangleF labelR = new RectangleF(r.X, r.Y + r.Height * 0.62f, r.Width, r.Height * 0.30f);
                        g.DrawString("添加", lf, lb, labelR, sf);
                    }
                }
                return;
            }
            int iconSize = (int)(_cellSize * 0.56f);
            int labelH = (int)(_cellSize * 0.36f);
            int iconX = r.X + (r.Width - iconSize) / 2;
            int iconY = r.Y + (int)(_cellSize * 0.05f);
            Rectangle labelRect = new Rectangle(r.X + Ui.Px(2), iconY + iconSize + Ui.Px(2),
                                                r.Width - Ui.Px(4), labelH);

            if (c.IsGroup)
            {
                DrawGroupIcon(g, c.Group, iconX, iconY, iconSize);
                DrawLabel(g, c.Group, labelRect, alpha);
                return;
            }

            if (c.Item != null)
            {
                Image img = IconCache.Get(c.Item.Path, iconSize);
                g.DrawImage(img, iconX, iconY, iconSize, iconSize);
                if (c.Missing)
                {
                    int dot = Ui.Px(9);
                    using (SolidBrush db = new SolidBrush(Color.FromArgb(235, 232, 64, 64)))
                        g.FillEllipse(db, iconX + iconSize - dot, iconY, dot, dot);
                }
                string name = string.IsNullOrEmpty(c.Item.Name)
                    ? Path.GetFileNameWithoutExtension(c.Item.Path) : c.Item.Name;
                DrawLabel(g, name, labelRect, alpha);
            }
        }

        private void DrawGroupIcon(Graphics g, string group, int x, int y, int size)
        {
            System.Collections.Generic.List<DockItem> members = new System.Collections.Generic.List<DockItem>();
            for (int i = 0; i < Cfg.Items.Count; i++)
                if (Cfg.Items[i].Group == group) members.Add(Cfg.Items[i]);

            Rectangle tile = new Rectangle(x, y, size, size);
            using (GraphicsPath path = Ui.Rounded(tile, Ui.Px(9)))
            using (LinearGradientBrush b = new LinearGradientBrush(tile,
                Color.FromArgb(58, 62, 76), Color.FromArgb(38, 41, 52), LinearGradientMode.ForwardDiagonal))
                g.FillPath(b, path);

            if (members.Count == 0)
            {
                using (Font f = Ui.Font(16, FontStyle.Regular))
                using (SolidBrush b = new SolidBrush(Ui.SubText))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("▣", f, b, tile, sf);
                }
                return;
            }

            int mini = (size - Ui.Px(12)) / 2;
            for (int i = 0; i < 4 && i < members.Count; i++)
            {
                int cx = x + Ui.Px(4) + (i % 2) * (mini + Ui.Px(4));
                int cy = y + Ui.Px(4) + (i / 2) * (mini + Ui.Px(4));
                Image icon = IconCache.Get(members[i].Path, mini);
                g.DrawImage(icon, cx, cy, mini, mini);
            }
        }

        private void DrawLabel(Graphics g, string text, Rectangle rect, int alpha)
        {
            float px = Math.Max(9f, _cellSize * 0.135f);
            // 必须用 GDI+ 的 DrawString：TextRenderer 走 GDI，会忽略 Graphics 的
            // TranslateTransform，导致滑动时文字不跟着图标一起位移。
            using (Font f = new Font("Microsoft YaHei UI", px, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, Ui.TextColor)))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Near;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                g.DrawString(text, f, b,
                    new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), sf);
            }
        }

        private void DrawPageButton(Graphics g, Rectangle r, bool up, bool enabled)
        {
            int alpha = enabled ? 46 : 16;
            using (GraphicsPath p = Ui.Rounded(r, r.Width / 2))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                g.FillPath(b, p);

            Point[] tri;
            int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            int s = r.Width / 5;
            if (up) tri = new Point[] { new Point(cx, cy - s), new Point(cx - s, cy + s / 2), new Point(cx + s, cy + s / 2) };
            else tri = new Point[] { new Point(cx, cy + s), new Point(cx - s, cy - s / 2), new Point(cx + s, cy - s / 2) };
            using (SolidBrush b = new SolidBrush(enabled ? Color.FromArgb(220, 228, 240) : Color.FromArgb(70, 76, 88)))
                g.FillPolygon(b, tri);
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < _cells.Count; i++)
                if (_cells[i].Bounds.Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _cursor = e.Location;
            _panel.NotifyMouseInside();

            int hit = HitTest(e.Location);
            if (hit != _hover)
            {
                _hover = hit;
                _hoverSince = Environment.TickCount;
                if (_mergeMode) { _mergeMode = false; }
                Invalidate();
            }

            if (_press >= 0 && !_dragging)
            {
                int dx = Math.Abs(e.X - _pressPt.X);
                int dy = Math.Abs(e.Y - _pressPt.Y);
                if (dx > Ui.Px(5) || dy > Ui.Px(5))
                {
                    _dragging = true;
                    _dragIndex = _press;
                    _hoverSince = Environment.TickCount;
                    _mergeTimer.Start();
                    Invalidate();
                }
            }
            else if (_dragging)
            {
                Invalidate();
            }
        }

        private void MergeTick(object sender, EventArgs e)
        {
            if (!_dragging || _hover < 0 || _hover == _dragIndex) { _mergeMode = false; return; }
            Cell c = _cells[_hover];
            bool valid = c.IsGroup || (c.Item != null && !c.IsBack);
            if (!valid) { _mergeMode = false; return; }
            if (!_mergeMode && Environment.TickCount - _hoverSince > 620)
            {
                _mergeMode = true;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_dragging && _hover != -1) { _hover = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _panel.NotifyMouseInside();
            if (e.Button == MouseButtons.Left)
            {
                if (_maxScroll > 0)
                {
                    if (_pageUpRect.Contains(e.Location)) { PageBy(-1); return; }
                    if (_pageDownRect.Contains(e.Location)) { PageBy(1); return; }
                }
                _press = HitTest(e.Location);
                _pressPt = e.Location;
                Log.Write("MouseDown at=" + e.Location + " hit=" + _press + " cells=" + _cells.Count);
                if (_press >= 0) Invalidate();
            }
            else if (e.Button == MouseButtons.Right)
            {
                int hit = HitTest(e.Location);
                if (hit >= 0) ShowMenu(hit, e.Location);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            int release = HitTest(e.Location);
            Log.Write("MouseUp at=" + e.Location + " hit=" + release + " press=" + _press + " dragging=" + _dragging);
            if (_dragging)
            {
                _dragging = false;
                _mergeTimer.Stop();
                int src = _dragIndex;
                _dragIndex = -1;
                bool merge = _mergeMode;
                _mergeMode = false;

                if (src >= 0 && src < _cells.Count && release >= 0 && release != src)
                {
                    Cell s = _cells[src];
                    Cell d = _cells[release];
                    if (s.IsGroup && d.IsGroup) PerformSwap(src, release);   // 分组之间换位置
                    else if (s.IsGroup) PerformSwap(src, release);           // 分组拖到任何格子上＝换位
                    else if (merge || d.IsGroup || d.IsBack) PerformMerge(src, release);
                    else PerformSwap(src, release);
                }
                Invalidate();
            }
            else if (_press >= 0 && _press == release)
            {
                _lastMsgTick = Environment.TickCount;
                HandleClick(release);
            }
            _press = -1;
            Invalidate();
        }

        /// <summary>双击才执行打开（单击只记录，避免误触）。</summary>
        private void HandleClick(int index)
        {
            if (index < 0 || index >= _cells.Count) return;

            if (index == _lastClickIndex && Environment.TickCount - _lastClickTick <= 450)
            {
                _lastClickIndex = -1;
                _lastClickTick = 0;
                ActivateCell(index);
            }
            else
            {
                _lastClickIndex = index;
                _lastClickTick = Environment.TickCount;
            }
        }

        /// <summary>拖到另一个格子上（未长按）＝交换位置。图标与分组可以互相换位。</summary>
        private void PerformSwap(int src, int dst)
        {
            Cell s = _cells[src];
            Cell d = _cells[dst];
            if (s.IsAdd || d.IsAdd || s.IsBack || d.IsBack) return;

            int ia = _group.Length == 0 ? Cfg.IndexOfToken(TokenOf(s)) : -1;
            int ib = _group.Length == 0 ? Cfg.IndexOfToken(TokenOf(d)) : -1;

            if (ia >= 0 && ib >= 0 && ia != ib)
            {
                // 顶层：直接在统一顺序表里对调，支持 图标↔图标 / 图标↔分组 / 分组↔分组
                string tmp = Cfg.Order[ia];
                Cfg.Order[ia] = Cfg.Order[ib];
                Cfg.Order[ib] = tmp;
                ConfigStore.Save(Cfg);
                _mgr.RefreshPanels();
                Log.Write("换位: " + tmp + " <-> " + Cfg.Order[ia]);
                return;
            }

            // 分组内部：交换 Items 里的先后
            if (s.Item == null || d.Item == null) return;
            if (s.Item.Group != d.Item.Group) return;

            int ix = Cfg.Items.IndexOf(s.Item);
            int iy = Cfg.Items.IndexOf(d.Item);
            if (ix < 0 || iy < 0 || ix == iy) return;

            Cfg.Items[ix] = d.Item;
            Cfg.Items[iy] = s.Item;
            ConfigStore.Save(Cfg);
            _mgr.RefreshPanels();
        }

        /// <summary>格子对应的顺序表 token（图标 "i:Id" / 分组 "g:名字"）。</summary>
        private static string TokenOf(Cell c)
        {
            if (c.IsGroup) return DockConfig.GroupToken(c.Group);
            if (c.Item != null) return DockConfig.ItemToken(c.Item);
            return null;
        }

        private void ActivateCell(int index)
        {
            if (index < 0 || index >= _cells.Count) return;
            Cell c = _cells[index];

            if (c.IsBack) { _group = ""; _scrollY = 0; Rebuild(); Invalidate(); return; }
            if (c.IsAdd) { AddIcons(); return; }
            if (c.IsGroup)
            {
                _group = c.Group;
                _scrollY = 0;
                Rebuild();
                Invalidate();
                return;
            }
            if (c.Item != null)
            {
                string nm = string.IsNullOrEmpty(c.Item.Name)
                    ? System.IO.Path.GetFileNameWithoutExtension(c.Item.Path) : c.Item.Name;
                ShowToast("正在启动：" + nm, 1500);
                bool ok = Launch(c.Item.Path);
                if (ok) ShowToast("已启动：" + nm, 1800);
                else ShowToast("启动失败：" + nm, 4500);   // 面板不再自动关闭，方便看结果
            }
        }

        /// <summary>添加图标：多选快捷方式/程序，加入当前视图（根或所在分组）。</summary>
        private void AddIcons()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;
            menu.Items.Add("添加文件…", null, delegate { AddFiles(); });
            menu.Items.Add("添加文件夹…", null, delegate { AddFolder(); });
            menu.Show(Cursor.Position);
        }

        private void AddFiles()
        {
            try
            {
                using (OpenFileDialog dlg = new OpenFileDialog())
                {
                    dlg.Title = "添加图标（可多选）";
                    dlg.Filter = "快捷方式或程序 (*.lnk;*.url;*.exe)|*.lnk;*.url;*.exe|所有文件 (*.*)|*.*";
                    dlg.Multiselect = true;
                    dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                    if (dlg.ShowDialog() != DialogResult.OK) return;

                    string[] files = dlg.FileNames;
                    for (int i = 0; i < files.Length; i++)
                    {
                        DockItem it = new DockItem();
                        it.Path = files[i];
                        it.Name = Path.GetFileNameWithoutExtension(files[i]);
                        it.Group = _group;
                        Cfg.Items.Add(it);
                    }
                    Cfg.Normalize();
                    ConfigStore.Save(Cfg);
                    _panel.RefreshConfig();
                    _mgr.RefreshPanels();
                    ShowToast("已添加 " + files.Length + " 个图标", 1800);
                    Log.Write("添加图标 " + files.Length + " 个到分组[" + _group + "]");
                }
            }
            catch (Exception ex)
            {
                Log.Write("添加图标失败: " + ex.Message);
                ShowToast("添加失败：" + ex.Message, 4000);
            }
        }

        private void AddFolder()
        {
            try
            {
                using (FolderBrowserDialog dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "选择一个文件夹，点击图标即可打开它";
                    dlg.ShowNewFolderButton = false;

                    if (dlg.ShowDialog() != DialogResult.OK) return;

                    string folder = dlg.SelectedPath;
                    if (string.IsNullOrEmpty(folder)) return;

                    DockItem it = new DockItem();
                    it.Path = folder;
                    it.Name = new DirectoryInfo(folder).Name;
                    if (string.IsNullOrEmpty(it.Name)) it.Name = folder;
                    it.Group = _group;
                    Cfg.Items.Add(it);

                    Cfg.Normalize();
                    ConfigStore.Save(Cfg);
                    _panel.RefreshConfig();
                    _mgr.RefreshPanels();
                    ShowToast("已添加文件夹：" + it.Name, 1800);
                    Log.Write("添加文件夹 " + folder + " 到分组[" + _group + "]");
                }
            }
            catch (Exception ex)
            {
                Log.Write("添加文件夹失败: " + ex.Message);
                ShowToast("添加失败：" + ex.Message, 4000);
            }
        }

        private void PerformMerge(int src, int dst)
        {
            Cell s = _cells[src];
            Cell d = _cells[dst];
            if (s.Item == null) return;

            string sTok = DockConfig.ItemToken(s.Item);
            int sPos = Cfg.IndexOfToken(sTok);

            if (d.IsBack)
            {
                // 从分组里拖到“返回” => 移出分组，回到顶层末尾
                s.Item.Group = "";
                if (Cfg.IndexOfToken(sTok) < 0) Cfg.InsertToken(Cfg.Order.Count, sTok);
            }
            else if (d.IsGroup)
            {
                // 拖到分组格子上 => 加入该分组，从顶层顺序里移除
                s.Item.Group = d.Group;
                Cfg.RemoveToken(sTok);
            }
            else if (d.Item != null)
            {
                string name = d.Item.Group;
                string dTok = DockConfig.ItemToken(d.Item);
                int dPos = Cfg.IndexOfToken(dTok);

                if (string.IsNullOrEmpty(name))
                {
                    // 两个图标合并成新分组：分组格子出现在两者原来的位置
                    name = NextGroupName();
                    d.Item.Group = name;
                    s.Item.Group = name;

                    int insertAt;
                    if (sPos >= 0 && dPos >= 0) insertAt = Math.Min(sPos, dPos);
                    else insertAt = Math.Max(sPos, dPos);

                    Cfg.RemoveToken(sTok);
                    Cfg.RemoveToken(dTok);
                    Cfg.InsertToken(insertAt < 0 ? Cfg.Order.Count : insertAt, DockConfig.GroupToken(name));
                }
                else
                {
                    s.Item.Group = name;
                    Cfg.RemoveToken(sTok);
                }
            }
            ConfigStore.Save(Cfg);
            _mgr.RefreshPanels();
        }

        private string NextGroupName()
        {
            for (int n = 1; n < 999; n++)
            {
                string candidate = "分组 " + n;
                bool used = false;
                System.Collections.Generic.List<string> groups = Cfg.Groups();
                for (int i = 0; i < groups.Count; i++) if (groups[i] == candidate) { used = true; break; }
                if (!used) return candidate;
            }
            return "分组";
        }

        /// <summary>解析 .lnk 的真实目标（WScript.Shell 后期绑定，无需引用 COM 组件）。</summary>
        private static bool ResolveShortcut(string lnkPath, out string target, out string workDir)
        {
            target = null; workDir = null;
            object shell = null, sc = null;
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return false;
                shell = Activator.CreateInstance(t);
                sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
                                    null, shell, new object[] { lnkPath });
                Type sct = sc.GetType();
                target = (string)sct.InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null);
                workDir = (string)sct.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.GetProperty, null, sc, null);
                return !string.IsNullOrEmpty(target);
            }
            catch { return false; }
            finally
            {
                try { if (sc != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(sc); } catch { }
                try { if (shell != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shell); } catch { }
            }
        }

        public static bool Launch(string path)
        {
            try
            {
                Log.Write("Launch: " + path);
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    Log.Write("  目标文件不存在");
                    return false;
                }

                string ext = Path.GetExtension(path).ToLowerInvariant();

                if (Directory.Exists(path))
                {
                    Process.Start("explorer.exe", "\"" + path + "\"");
                    Log.Write("Launch OK (文件夹)");
                    return true;
                }

                // 1) 和资源管理器双击完全一致：ShellExecute 打开原始文件
                //    （会遵循快捷方式的全部设置：管理员身份、工作目录、兼容性标记）
                if (ext == ".url")
                {
                    string url = ReadUrlFile(path);
                    Log.Write("  .url -> " + url);
                    if (!string.IsNullOrEmpty(url))
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                            Log.Write("Launch OK (协议) " + url);
                            return true;
                        }
                        catch (Exception ex) { Log.Write("  协议启动失败: " + ex.Message); }
                    }
                }

                try
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    Log.Write("Launch OK (shell，等同双击)");
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Write("  shell 启动失败: " + ex.GetType().Name + ": " + ex.Message);
                }

                // 2) 兜底：解析快捷方式真实目标，直接起进程（带它的工作目录）
                if (ext == ".lnk")
                {
                    string target, workDir;
                    if (ResolveShortcut(path, out target, out workDir) && File.Exists(target))
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(target);
                        psi.UseShellExecute = true;
                        if (!string.IsNullOrEmpty(workDir) && Directory.Exists(workDir))
                            psi.WorkingDirectory = workDir;
                        Process.Start(psi);
                        Log.Write("Launch OK (兜底：直达目标) " + target);
                        return true;
                    }
                }

                Log.Write("Launch FAILED: 所有方式都失败");
                return false;
            }
            catch (Exception ex)
            {
                Log.Write("Launch FAILED: " + path + " -> " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }
        private static string ReadUrlFile(string path)
        {
            try
            {
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string l = lines[i].Trim();
                    if (l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                        return l.Substring(4).Trim();
                }
            }
            catch { }
            return null;
        }

        private void ShowMenu(int index, Point at)
        {
            Cell c = _cells[index];
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;

            if (!c.IsBack && !c.IsGroup && c.Item != null)
            {
                DockItem item = c.Item;
                menu.Items.Add("打开", null, delegate { Launch(item.Path); });
                menu.Items.Add("打开所在位置", null, delegate
                {
                    try { Process.Start("explorer.exe", "/select,\"" + item.Path + "\""); }
                    catch { }
                });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("重命名…", null, delegate
                {
                    string v = InputBox.Ask("重命名", item.Name);
                    if (v != null) { item.Name = v; ConfigStore.Save(Cfg); RefreshContent(); }
                });
                if (!string.IsNullOrEmpty(item.Group))
                {
                    menu.Items.Add("移出分组", null, delegate
                    {
                        item.Group = "";
                        string tk = DockConfig.ItemToken(item);
                        if (Cfg.IndexOfToken(tk) < 0) Cfg.InsertToken(Cfg.Order.Count, tk);
                        ConfigStore.Save(Cfg);
                        _mgr.RefreshPanels();
                    });
                }
                menu.Items.Add("删除", null, delegate
                {
                    Cfg.Items.Remove(item);
                    Cfg.Normalize();          // 顺带清掉顺序表里的失效项
                    ConfigStore.Save(Cfg);
                    _mgr.RefreshPanels();
                });
            }
            else if (c.IsGroup)
            {
                string g = c.Group;
                menu.Items.Add("打开分组", null, delegate { _group = g; Rebuild(); Invalidate(); });
                menu.Items.Add("重命名分组…", null, delegate
                {
                    string v = InputBox.Ask("重命名分组", g);
                    if (v != null && v.Length > 0)
                    {
                        for (int i = 0; i < Cfg.Items.Count; i++)
                            if (Cfg.Items[i].Group == g) Cfg.Items[i].Group = v;
                        int gi = Cfg.IndexOfToken(DockConfig.GroupToken(g));
                        if (gi >= 0) Cfg.Order[gi] = DockConfig.GroupToken(v);
                        ConfigStore.Save(Cfg);
                        _mgr.RefreshPanels();
                    }
                });
                menu.Items.Add("解散分组", null, delegate
                {
                    for (int i = 0; i < Cfg.Items.Count; i++)
                        if (Cfg.Items[i].Group == g) Cfg.Items[i].Group = "";
                    Cfg.Normalize();          // 成员自动补回顶层顺序
                    ConfigStore.Save(Cfg);
                    _mgr.RefreshPanels();
                });
            }

            if (menu.Items.Count > 0) menu.Show(this, at);
            else menu.Dispose();
        }
    }

    /// <summary>极简输入框（重命名用）。</summary>
    internal static class InputBox
    {
        public static string Ask(string title, string initial)
        {
            Form f = new Form();
            f.Text = title;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.MinimizeBox = false;
            f.MaximizeBox = false;
            f.TopMost = true;
            f.ClientSize = new Size(Ui.Px(280), Ui.Px(110));

            TextBox tb = new TextBox();
            tb.Text = initial;
            tb.Font = Ui.Font(12, FontStyle.Regular);
            tb.SetBounds(Ui.Px(14), Ui.Px(16), Ui.Px(252), Ui.Px(26));

            Button ok = new Button();
            ok.Text = "确定";
            ok.DialogResult = DialogResult.OK;
            ok.SetBounds(Ui.Px(150), Ui.Px(58), Ui.Px(52), Ui.Px(28));

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(Ui.Px(210), Ui.Px(58), Ui.Px(52), Ui.Px(28));

            f.Controls.Add(tb);
            f.Controls.Add(ok);
            f.Controls.Add(cancel);
            f.AcceptButton = ok;
            f.CancelButton = cancel;

            string result = null;
            ok.Click += delegate { result = tb.Text.Trim(); };
            if (f.ShowDialog() == DialogResult.OK) return result;
            return null;
        }
    }
}
