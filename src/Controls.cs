using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace DangAn
{
    /// <summary>线性小图标（按名称绘制），统一 1.6px 线宽、圆角端点。</summary>
    public static class Glyph
    {
        public static void Draw(Graphics g, string name, RectangleF r, Color c)
        {
            if (string.IsNullOrEmpty(name)) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(r.Width, r.Height);
            float x = r.X + (r.Width - s) / 2, y = r.Y + (r.Height - s) / 2;
            Func<float, float> X = v => x + v * s / 24f;
            Func<float, float> Y = v => y + v * s / 24f;
            using (var p = new Pen(c, Math.Max(1.2f, s / 13f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var b = new SolidBrush(c))
            {
                switch (name)
                {
                    case "grid":
                        foreach (var o in new[] { new PointF(4, 4), new PointF(13, 4), new PointF(4, 13), new PointF(13, 13) })
                            RR(g, p, X(o.X), Y(o.Y), s * 7 / 24f, s * 7 / 24f, s / 12f);
                        break;
                    case "folder":
                        using (var gp = new GraphicsPath())
                        {
                            gp.AddLines(new[] { new PointF(X(3), Y(18.5f)), new PointF(X(3), Y(6)), new PointF(X(9), Y(6)), new PointF(X(11), Y(8.5f)), new PointF(X(21), Y(8.5f)), new PointF(X(21), Y(18.5f)) });
                            gp.CloseFigure(); g.DrawPath(p, gp);
                        }
                        break;
                    case "contract":
                        RR(g, p, X(5), Y(3), s * 14 / 24f, s * 18 / 24f, s / 12f);
                        g.DrawLine(p, X(8.5f), Y(8), X(15.5f), Y(8)); g.DrawLine(p, X(8.5f), Y(12), X(15.5f), Y(12)); g.DrawLine(p, X(8.5f), Y(16), X(12.5f), Y(16));
                        break;
                    case "chat":
                        using (var gp = new GraphicsPath())
                        {
                            float r0 = s * 3 / 24f;
                            gp.AddArc(X(3), Y(4), r0 * 2, r0 * 2, 180, 90); gp.AddArc(X(21) - r0 * 2, Y(4), r0 * 2, r0 * 2, 270, 90);
                            gp.AddArc(X(21) - r0 * 2, Y(16) - r0 * 2, r0 * 2, r0 * 2, 0, 90);
                            gp.AddLine(X(11), Y(16), X(7), Y(20)); gp.AddLine(X(7), Y(20), X(7), Y(16));
                            gp.AddArc(X(3), Y(16) - r0 * 2, r0 * 2, r0 * 2, 90, 90);
                            gp.CloseFigure(); g.DrawPath(p, gp);
                        }
                        foreach (var dx in new[] { 8f, 12f, 16f }) g.FillEllipse(b, X(dx) - s / 22f, Y(10) - s / 22f, s / 11f, s / 11f);
                        break;
                    case "search":
                        g.DrawEllipse(p, X(4), Y(4), s * 12 / 24f, s * 12 / 24f); g.DrawLine(p, X(14.5f), Y(14.5f), X(20), Y(20));
                        break;
                    case "table":
                        RR(g, p, X(3), Y(4), s * 18 / 24f, s * 16 / 24f, s / 12f);
                        g.DrawLine(p, X(3), Y(9.5f), X(21), Y(9.5f)); g.DrawLine(p, X(3), Y(15), X(21), Y(15)); g.DrawLine(p, X(9.5f), Y(9.5f), X(9.5f), Y(20));
                        break;
                    case "gear":
                        g.DrawEllipse(p, X(8.5f), Y(8.5f), s * 7 / 24f, s * 7 / 24f);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            g.DrawLine(p, X(12 + (float)Math.Cos(a) * 6.5f), Y(12 + (float)Math.Sin(a) * 6.5f), X(12 + (float)Math.Cos(a) * 9f), Y(12 + (float)Math.Sin(a) * 9f));
                        }
                        g.DrawEllipse(p, X(5.5f), Y(5.5f), s * 13 / 24f, s * 13 / 24f);
                        break;
                    case "upload":
                        g.DrawLine(p, X(12), Y(16), X(12), Y(4)); g.DrawLines(p, new[] { new PointF(X(7), Y(9)), new PointF(X(12), Y(4)), new PointF(X(17), Y(9)) });
                        g.DrawLines(p, new[] { new PointF(X(4), Y(15)), new PointF(X(4), Y(20)), new PointF(X(20), Y(20)), new PointF(X(20), Y(15)) });
                        break;
                    case "plus":
                        g.DrawLine(p, X(12), Y(5), X(12), Y(19)); g.DrawLine(p, X(5), Y(12), X(19), Y(12));
                        break;
                    case "refresh":
                        g.DrawArc(p, X(4.5f), Y(4.5f), s * 15 / 24f, s * 15 / 24f, -60, 290);
                        g.DrawLines(p, new[] { new PointF(X(15.5f), Y(3.5f)), new PointF(X(16.5f), Y(6.5f)), new PointF(X(13.5f), Y(7.5f)) });
                        break;
                    case "open":
                        g.DrawLines(p, new[] { new PointF(X(13), Y(4)), new PointF(X(20), Y(4)), new PointF(X(20), Y(11)) });
                        g.DrawLine(p, X(20), Y(4), X(11), Y(13));
                        g.DrawLines(p, new[] { new PointF(X(17), Y(14)), new PointF(X(17), Y(20)), new PointF(X(4), Y(20)), new PointF(X(4), Y(7)), new PointF(X(10), Y(7)) });
                        break;
                    case "backup":
                        using (var gp = new GraphicsPath())
                        {
                            gp.AddArc(X(3), Y(9), s * 8 / 24f, s * 8 / 24f, 90, 180);
                            gp.AddArc(X(6.5f), Y(4), s * 11 / 24f, s * 11 / 24f, 200, 140);
                            gp.AddArc(X(13), Y(8), s * 8 / 24f, s * 9 / 24f, 270, 180);
                            gp.CloseFigure(); g.DrawPath(p, gp);
                        }
                        break;
                    case "copy":
                        RR(g, p, X(8), Y(8), s * 12 / 24f, s * 12 / 24f, s / 12f);
                        g.DrawLines(p, new[] { new PointF(X(5), Y(15)), new PointF(X(4), Y(15)), new PointF(X(4), Y(4)), new PointF(X(15), Y(4)), new PointF(X(15), Y(5)) });
                        break;
                    case "excel":
                        RR(g, p, X(4), Y(3), s * 16 / 24f, s * 18 / 24f, s / 12f);
                        g.DrawLine(p, X(8.5f), Y(8.5f), X(15.5f), Y(15.5f)); g.DrawLine(p, X(15.5f), Y(8.5f), X(8.5f), Y(15.5f));
                        break;
                    case "check":
                        g.DrawLines(p, new[] { new PointF(X(5), Y(12.5f)), new PointF(X(10), Y(17.5f)), new PointF(X(19), Y(7)) });
                        break;
                    case "doc":
                        g.DrawLines(p, new[] { new PointF(X(14), Y(3)), new PointF(X(6), Y(3)), new PointF(X(6), Y(21)), new PointF(X(18), Y(21)), new PointF(X(18), Y(7)), new PointF(X(14), Y(3)), new PointF(X(14), Y(7)), new PointF(X(18), Y(7)) });
                        break;
                    case "coin":
                        g.DrawEllipse(p, X(3.5f), Y(3.5f), s * 17 / 24f, s * 17 / 24f);
                        g.DrawLines(p, new[] { new PointF(X(9), Y(8)), new PointF(X(12), Y(11.5f)), new PointF(X(15), Y(8)) });
                        g.DrawLine(p, X(12), Y(11.5f), X(12), Y(17)); g.DrawLine(p, X(9), Y(12.5f), X(15), Y(12.5f));
                        break;
                    case "alert":
                        g.DrawEllipse(p, X(3.5f), Y(3.5f), s * 17 / 24f, s * 17 / 24f);
                        g.DrawLine(p, X(12), Y(7.5f), X(12), Y(13)); g.FillEllipse(b, X(11), Y(15.3f), s * 2 / 24f, s * 2 / 24f);
                        break;
                    case "box":
                        RR(g, p, X(3), Y(5), s * 18 / 24f, s * 5 / 24f, s / 16f);
                        g.DrawLines(p, new[] { new PointF(X(4.5f), Y(10)), new PointF(X(4.5f), Y(19)), new PointF(X(19.5f), Y(19)), new PointF(X(19.5f), Y(10)) });
                        g.DrawLine(p, X(10), Y(13.5f), X(14), Y(13.5f));
                        break;
                    case "pause":
                        g.DrawLine(p, X(9), Y(6), X(9), Y(18)); g.DrawLine(p, X(15), Y(6), X(15), Y(18));
                        break;
                    case "chev":
                        g.DrawLines(p, new[] { new PointF(X(9), Y(6)), new PointF(X(15), Y(12)), new PointF(X(9), Y(18)) });
                        break;
                }
            }
        }

        static void RR(Graphics g, Pen p, float x, float y, float w, float h, float r)
        {
            using (var gp = new GraphicsPath())
            {
                float d = r * 2;
                gp.AddArc(x, y, d, d, 180, 90); gp.AddArc(x + w - d, y, d, d, 270, 90);
                gp.AddArc(x + w - d, y + h - d, d, d, 0, 90); gp.AddArc(x, y + h - d, d, d, 90, 90);
                gp.CloseFigure(); g.DrawPath(p, gp);
            }
        }
    }

    /// <summary>圆角按钮：主按钮（蓝底白字）、普通（白底描边）、浅色（淡蓝底），可带图标。</summary>
    public class RoundButton : Button
    {
        public enum Kind { Primary, Normal, Soft, Ghost }
        public Kind Style = Kind.Normal;
        public string Icon;
        public Color? TextColor;   // 指定文字颜色（如红色的“删除”）
        bool hover, down;

        public RoundButton()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand; Font = Ui.F(9.5f);
            Padding = new Padding(Ui.S(14), Ui.S(7), Ui.S(14), Ui.S(7));
            Margin = new Padding(Ui.S(4));
        }

        int IconSize { get { return string.IsNullOrEmpty(Icon) ? 0 : Ui.S(16); } }

        public override Size GetPreferredSize(Size proposed)
        {
            var t = TextRenderer.MeasureText(Text, Font);
            int w = Padding.Horizontal + t.Width + (IconSize > 0 ? IconSize + (Text.Length > 0 ? Ui.S(6) : 0) : 0);
            int h = Math.Max(Ui.S(36), Padding.Vertical + t.Height);
            return new Size(w, h);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            ButtonRenderer.DrawParentBackground(g, ClientRectangle, this);
            Color fill, border, fg;
            switch (Style)
            {
                case Kind.Primary:
                    fill = down ? Color.FromArgb(18, 86, 200) : hover ? Color.FromArgb(28, 100, 225) : Ui.Blue; border = fill; fg = Color.White; break;
                case Kind.Soft:
                    fill = down ? Color.FromArgb(205, 224, 255) : hover ? Color.FromArgb(220, 233, 255) : Ui.BlueSoft; border = fill; fg = Ui.Blue; break;
                case Kind.Ghost:
                    fill = down ? Color.FromArgb(226, 230, 236) : hover ? Ui.Hover : Color.Transparent; border = fill; fg = Ui.Text; break;
                default:
                    fill = down ? Color.FromArgb(238, 241, 245) : hover ? Color.FromArgb(246, 248, 251) : Color.White; border = hover ? Color.FromArgb(200, 206, 216) : Ui.Line; fg = Ui.Text; break;
            }
            if (TextColor.HasValue) fg = TextColor.Value;
            if (!Enabled) { fg = Color.FromArgb(160, fg); fill = Color.FromArgb(150, fill); }
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (fill.A > 0 || border.A > 0) Ui.Rounded(g, r, Ui.S(8), fill, border, false);
            var ts = TextRenderer.MeasureText(Text, Font);
            int total = ts.Width + (IconSize > 0 ? IconSize + (Text.Length > 0 ? Ui.S(6) : 0) : 0);
            int x = (Width - total) / 2;
            if (IconSize > 0)
            {
                Glyph.Draw(g, Icon, new RectangleF(x, (Height - IconSize) / 2f, IconSize, IconSize), fg);
                x += IconSize + Ui.S(6);
            }
            if (Text.Length > 0)
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, ts.Width + 2, Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Color.FromArgb(120, Ui.Blue)) { DashStyle = DashStyle.Dot }) g.DrawRectangle(pen, Ui.S(3), Ui.S(3), Width - Ui.S(7), Height - Ui.S(7));
        }
    }

    /// <summary>左侧导航项：图标 + 文字，选中时白色胶囊 + 蓝色竖条。</summary>
    public class NavItem : Control
    {
        public string Icon; public bool Selected; bool hover;
        public NavItem(string text, string icon)
        {
            Text = text; Icon = icon;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent; Cursor = Cursors.Hand; Height = Ui.S(44);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = new Rectangle(0, Ui.S(2), Width - 1, Height - Ui.S(5));
            if (Selected)
            {
                using (var sh = new SolidBrush(Color.FromArgb(14, 20, 40, 80))) using (var gp = Ui.RoundPath(new Rectangle(r.X, r.Y + Ui.S(1), r.Width, r.Height), Ui.S(10))) g.FillPath(sh, gp);
                Ui.Rounded(g, r, Ui.S(10), Color.White, Color.FromArgb(232, 235, 240), false);
                using (var b = new SolidBrush(Ui.Blue)) using (var gp = Ui.RoundPath(new Rectangle(r.X + Ui.S(6), r.Y + Ui.S(11), Ui.S(4), r.Height - Ui.S(22)), Ui.S(2))) g.FillPath(b, gp);
            }
            else if (hover) Ui.Rounded(g, r, Ui.S(10), Color.FromArgb(234, 237, 242), Color.FromArgb(234, 237, 242), false);
            var fg = Selected ? Ui.Blue : Color.FromArgb(60, 68, 82);
            Glyph.Draw(g, Icon, new RectangleF(Ui.S(20), r.Y + (r.Height - Ui.S(19)) / 2f, Ui.S(19), Ui.S(19)), fg);
            using (var f = Ui.F(10.5f, Selected ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(g, Text, f, new Rectangle(Ui.S(50), r.Y, Width - Ui.S(54), r.Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>带放大镜图标的圆角搜索框。</summary>
    public class SearchBox : Control
    {
        public readonly TextBox Box;
        bool focused;
        public SearchBox(string cue, float fontSize)
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Box = new TextBox { BorderStyle = BorderStyle.None, Font = Ui.F(fontSize), BackColor = Color.White, ForeColor = Ui.Text };
            OverviewPage.SetCue(Box, cue);
            Controls.Add(Box);
            Height = Box.PreferredHeight + Ui.S(fontSize > 11 ? 22 : 16);
            Box.GotFocus += (s, e) => { focused = true; Invalidate(); };
            Box.LostFocus += (s, e) => { focused = false; Invalidate(); };
            Cursor = Cursors.IBeam;
            Click += (s, e) => Box.Focus();
        }
        public override string Text { get { return Box == null ? "" : Box.Text; } set { if (Box != null) Box.Text = value; } }
        protected override void OnLayout(LayoutEventArgs e)
        {
            int left = Height - Ui.S(4);
            Box.SetBounds(left, (Height - Box.Height) / 2, Width - left - Ui.S(12), Box.Height);
            base.OnLayout(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.Rounded(e.Graphics, r, Ui.S(9), Color.White, focused ? Ui.Blue : Color.FromArgb(214, 219, 227), false);
            if (focused) using (var p = new Pen(Color.FromArgb(50, Ui.Blue), Ui.S(3))) using (var gp = Ui.RoundPath(new Rectangle(1, 1, Width - 3, Height - 3), Ui.S(9))) e.Graphics.DrawPath(p, gp);
            int ic = Math.Min(Ui.S(18), Height - Ui.S(14));
            Glyph.Draw(e.Graphics, "search", new RectangleF((Height - ic) / 2f + Ui.S(2), (Height - ic) / 2f, ic, ic), Ui.Sub);
        }
    }

    /// <summary>扁平标签栏（下划线式），驱动一个隐藏了标签头的 TabControl。</summary>
    public class TabStrip : Control
    {
        readonly TabControl tabs; int hover = -1;
        readonly List<Rectangle> rects = new List<Rectangle>();
        public TabStrip(TabControl tabs)
        {
            this.tabs = tabs;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Ui.S(46); Cursor = Cursors.Hand; BackColor = Color.White;
            tabs.Appearance = TabAppearance.FlatButtons; tabs.ItemSize = new Size(0, 1); tabs.SizeMode = TabSizeMode.Fixed;
            tabs.SelectedIndexChanged += (s, e) => Invalidate();
            tabs.ControlAdded += (s, e) => { e.Control.TextChanged += (a, b) => Invalidate(); Invalidate(); };
            foreach (TabPage tp in tabs.TabPages) tp.TextChanged += (a, b) => Invalidate();
        }
        void Measure(Graphics g)
        {
            rects.Clear();
            int x = Ui.S(8);
            using (var f = Ui.F(10.5f, FontStyle.Bold))
                foreach (TabPage tp in tabs.TabPages)
                {
                    int w = TextRenderer.MeasureText(g, tp.Text, f).Width + Ui.S(28);
                    rects.Add(new Rectangle(x, 0, w, Height));
                    x += w;
                }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = rects.FindIndex(r => r.Contains(e.Location));
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            int h = rects.FindIndex(r => r.Contains(e.Location));
            if (h >= 0) tabs.SelectedIndex = h;
            base.OnMouseClick(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Measure(g);
            using (var line = new Pen(Ui.Line)) g.DrawLine(line, 0, Height - 1, Width, Height - 1);
            for (int i = 0; i < rects.Count; i++)
            {
                bool sel = i == tabs.SelectedIndex;
                var c = sel ? Ui.Blue : i == hover ? Ui.Text : Ui.Sub;
                using (var f = Ui.F(10.5f, sel ? FontStyle.Bold : FontStyle.Regular))
                    TextRenderer.DrawText(g, tabs.TabPages[i].Text, f, rects[i], c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                if (sel)
                    using (var b = new SolidBrush(Ui.Blue)) using (var gp = Ui.RoundPath(new Rectangle(rects[i].X + Ui.S(12), Height - Ui.S(4), rects[i].Width - Ui.S(24), Ui.S(4)), Ui.S(2))) { g.SmoothingMode = SmoothingMode.AntiAlias; g.FillPath(b, gp); }
            }
        }
    }

    /// <summary>彩色圆底图标（统计卡用）。</summary>
    public class IconDot : Control
    {
        public string Icon; public Color Tint;
        public IconDot(string icon, Color tint)
        {
            Icon = icon; Tint = tint;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Transparent; Size = new Size(Ui.S(40), Ui.S(40));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Color.FromArgb(28, Tint))) using (var gp = Ui.RoundPath(new Rectangle(0, 0, Width - 1, Height - 1), Ui.S(11))) g.FillPath(b, gp);
            int s = Ui.S(20);
            Glyph.Draw(g, Icon, new RectangleF((Width - s) / 2f, (Height - s) / 2f, s, s), Tint);
        }
    }

    /// <summary>纵向堆叠容器：子控件撑满宽度、从上往下排，自身高度随内容变化。</summary>
    public class VStack : Panel
    {
        public VStack()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent; Margin = new Padding(0);
        }
        bool busy;
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (busy) return;
            busy = true;
            try
            {
                int y = Padding.Top, w = Math.Max(10, Width - Padding.Horizontal);
                foreach (Control c in Controls)
                {
                    if (!c.Visible) continue;
                    y += c.Margin.Top;
                    if (c.Left != Padding.Left || c.Top != y || c.Width != w) c.SetBounds(Padding.Left, y, w, c.Height);
                    y += c.Height + c.Margin.Bottom;
                }
                int h = y + Padding.Bottom;
                if (Height != h) Height = h;
            }
            finally { busy = false; }
        }
        protected override void OnControlAdded(ControlEventArgs e) { e.Control.SizeChanged += (s, a) => PerformLayout(); base.OnControlAdded(e); }
    }

    /// <summary>白色圆角区块（带投影），内部纵向排列。</summary>
    public class SectionCard : VStack
    {
        public SectionCard()
        {
            SetStyle(ControlStyles.UserPaint, true);
            Padding = new Padding(Ui.S(26), Ui.S(18), Ui.S(26), Ui.S(22));
            Margin = new Padding(0, 0, 0, Ui.S(16));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Ui.CardShape(e.Graphics, ClientRectangle, Ui.S(14), Color.White, Color.FromArgb(232, 236, 241), false);
            base.OnPaint(e);
        }
    }
}