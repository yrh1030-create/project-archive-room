using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DangAn
{
    /// <summary>办理流程：立项 → 决算，每步显示份数和代表性文件。点步骤看该类资料，点文件名直接打开。</summary>
    public class StageFlow : Control
    {
        readonly Store st; readonly Project p; readonly ProjectInfo info;
        readonly FileRec[] keys;
        public event Action<string> CatClicked;
        /// <summary>右键某一步 →“添加文件到这一步”。</summary>
        public event Action<string> AddFilesTo;
        /// <summary>把文件拖到某一步上。</summary>
        public event Action<string, string[]> DropFilesTo;
        readonly ToolTip tip = new ToolTip();
        int hoverCol = -1; bool hoverFile;

        public StageFlow(Store st, Project p, ProjectInfo info)
        {
            this.st = st; this.p = p; this.info = info;
            DoubleBuffered = true; ResizeRedraw = true; BackColor = Color.White;
            using (var ff = Ui.F(8.5f)) lineH = TextRenderer.MeasureText("字", ff).Height;
            Height = Ui.S(88) + lineH * 2 + Ui.S(6);
            keys = Cats.StageCats.Select(c => st.KeyFile(p, c)).ToArray();
            AllowDrop = true;
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragOver += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.Copy; var pt = PointToClient(new Point(e.X, e.Y)); int c = Math.Max(0, Math.Min(Cats.StageCats.Length - 1, pt.X / Math.Max(1, ColW))); if (c != hoverCol) { hoverCol = c; Invalidate(); } } };
            DragDrop += (s, e) =>
            {
                var pt = PointToClient(new Point(e.X, e.Y));
                int c = Math.Max(0, Math.Min(Cats.StageCats.Length - 1, pt.X / Math.Max(1, ColW)));
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (DropFilesTo != null && files != null) DropFilesTo(Cats.StageCats[c], files);
            };
        }

        readonly int lineH;
        int ColW { get { return Width / Cats.StageCats.Length; } }
        Rectangle FileRect(int i) { return new Rectangle(i * ColW + Ui.S(6), Ui.S(84), ColW - Ui.S(12), lineH * 2 + Ui.S(2)); }

        /// <summary>中文没有空格，系统省略号不可靠：手工截到两行内。</summary>
        static string Fit(string s, Font f, Rectangle r)
        {
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            if (TextRenderer.MeasureText(s, f, new Size(r.Width, int.MaxValue), flags).Height <= r.Height) return s;
            int lo = 1, hi = s.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (TextRenderer.MeasureText(s.Substring(0, mid) + "…", f, new Size(r.Width, int.MaxValue), flags).Height <= r.Height) lo = mid; else hi = mid - 1;
            }
            return s.Substring(0, lo) + "…";
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int c = Math.Min(Cats.StageCats.Length - 1, e.X / Math.Max(1, ColW));
            bool f = FileRect(c).Contains(e.Location) && keys[c] != null;
            if (c != hoverCol || f != hoverFile)
            {
                hoverCol = c; hoverFile = f; Invalidate();
                int k; info.CatCount.TryGetValue(Cats.StageCats[c], out k);
                tip.SetToolTip(this, (f ? "打开：" + keys[c].Rel : (k > 0 ? "查看“" + Cats.Title(Cats.StageCats[c]) + "”全部 " + k + " 份" : "这一步还没有资料")) + "\n右键：添加文件到这一步");
            }
            Cursor = Cursors.Hand;
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hoverCol = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int c = Math.Min(Cats.StageCats.Length - 1, e.X / Math.Max(1, ColW));
            if (e.Button == MouseButtons.Right)
            {
                string cat = Cats.StageCats[c];
                var menu = new ContextMenuStrip { Font = Ui.F(9.5f) };
                menu.Items.Add("添加文件到“" + Cats.StageNames[c] + "”…", null, (s, a) => { if (AddFilesTo != null) AddFilesTo(cat); });
                menu.Items.Add("查看这一步的全部资料", null, (s, a) => { if (CatClicked != null) CatClicked(cat); });
                if (keys[c] != null) menu.Items.Add("打开代表文件", null, (s, a) => Ui.Open(st.FullPath(keys[c])));
                menu.Show(this, e.Location);
                return;
            }
            if (FileRect(c).Contains(e.Location) && keys[c] != null) Ui.Open(st.FullPath(keys[c]));
            else if (CatClicked != null) CatClicked(Cats.StageCats[c]);
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int n = Cats.StageCats.Length, cw = ColW, cy = Ui.S(16), rad = Ui.S(11);
            for (int i = 0; i < n; i++)
            {
                int k; info.CatCount.TryGetValue(Cats.StageCats[i], out k);
                int cx = i * cw + cw / 2;
                if (i < n - 1)
                {
                    int k2; info.CatCount.TryGetValue(Cats.StageCats[i + 1], out k2);
                    using (var pen = new Pen(k > 0 && k2 > 0 ? ProjectCard.Done : Color.FromArgb(222, 226, 232), Ui.S(2)))
                        g.DrawLine(pen, cx + rad + Ui.S(4), cy, cx + cw - rad - Ui.S(4), cy);
                }
                if (i == hoverCol) using (var hb = new SolidBrush(Color.FromArgb(246, 249, 255))) g.FillRectangle(hb, i * cw + 1, 0, cw - 2, Height);
                var circle = new Rectangle(cx - rad, cy - rad, rad * 2, rad * 2);
                if (k > 0)
                {
                    using (var b = new SolidBrush(ProjectCard.Done)) g.FillEllipse(b, circle);
                    using (var pen = new Pen(Color.White, Ui.S(2)))
                        g.DrawLines(pen, new[] { new Point(cx - Ui.S(5), cy), new Point(cx - Ui.S(1), cy + Ui.S(4)), new Point(cx + Ui.S(5), cy - Ui.S(4)) });
                }
                else using (var pen = new Pen(Color.FromArgb(200, 206, 214), Ui.S(2))) g.DrawEllipse(pen, circle);
                using (var nf = Ui.F(10f, FontStyle.Bold))
                    TextRenderer.DrawText(g, Cats.StageNames[i], nf, new Rectangle(i * cw, Ui.S(32), cw, Ui.S(24)), k > 0 ? Ui.Text : Ui.Sub, TextFormatFlags.HorizontalCenter);
                using (var cf = Ui.F(8.5f))
                    TextRenderer.DrawText(g, k > 0 ? k + " 份" : (p.Required.Contains(Cats.StageCats[i]) ? "缺" : "无"), cf, new Rectangle(i * cw, Ui.S(56), cw, Ui.S(20)),
                        k > 0 ? Ui.Sub : (p.Required.Contains(Cats.StageCats[i]) ? Ui.Orange : Color.FromArgb(170, 176, 186)), TextFormatFlags.HorizontalCenter);
                if (keys[i] != null)
                {
                    var fr = FileRect(i);
                    using (var ff = Ui.F(8.5f, i == hoverCol && hoverFile ? FontStyle.Underline : FontStyle.Regular))
                        TextRenderer.DrawText(g, Fit(keys[i].Name, ff, fr), ff, fr, Ui.Blue, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                }
            }
        }
    }

    /// <summary>两列/三列的信息清单（名称 | 内容 | 右侧附注或“复制”）。高度随宽度自动计算。</summary>
    public class InfoList : Control
    {
        public class Row { public string Label, Value, Right; public bool Copy; public object Tag; }
        public readonly List<Row> Rows = new List<Row>();
        public int LabelWidth = 180, RightWidth = 60;
        public event Action<Row> RowClicked;
        readonly List<Rectangle> rowRects = new List<Rectangle>();
        readonly List<Rectangle> copyRects = new List<Rectangle>();
        int hover = -1; bool hoverCopy;
        readonly ToolTip tip = new ToolTip();
        static readonly TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;

        public InfoList() { DoubleBuffered = true; ResizeRedraw = true; BackColor = Color.White; }

        public void Measure()
        {
            rowRects.Clear(); copyRects.Clear();
            int y = 0, lw = Ui.S(LabelWidth), rw = Ui.S(RightWidth);
            int vw = Math.Max(Ui.S(100), Width - lw - rw - Ui.S(24));
            using (var vf = Ui.F(10f))
                foreach (var r in Rows)
                {
                    var sz = TextRenderer.MeasureText(string.IsNullOrEmpty(r.Value) ? "—" : r.Value, vf, new Size(vw, int.MaxValue), Wrap);
                    int h = Math.Max(Ui.S(36), sz.Height + Ui.S(16));
                    rowRects.Add(new Rectangle(0, y, Width, h));
                    copyRects.Add(new Rectangle(Width - rw, y, rw, h));
                    y += h;
                }
            int newH = Math.Max(Ui.S(10), y);
            if (Height != newH) Height = newH;
        }

        protected override void OnResize(EventArgs e) { Measure(); base.OnResize(e); }

        int HitRow(Point pt) { for (int i = 0; i < rowRects.Count; i++) if (rowRects[i].Contains(pt)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = HitRow(e.Location);
            bool c = i >= 0 && copyRects[i].Contains(e.Location) && Rows[i].Copy && !string.IsNullOrEmpty(Rows[i].Value);
            bool clickable = i >= 0 && (c || (RowClicked != null && Rows[i].Tag != null));
            Cursor = clickable ? Cursors.Hand : Cursors.Default;
            if (i != hover || c != hoverCopy) { hover = i; hoverCopy = c; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = HitRow(e.Location);
            if (i < 0) return;
            var r = Rows[i];
            if (copyRects[i].Contains(e.Location) && r.Copy && !string.IsNullOrEmpty(r.Value))
            {
                Clipboard.SetText(r.Value);
                tip.Show("已复制", this, e.X - Ui.S(20), e.Y - Ui.S(30), 900);
            }
            else if (RowClicked != null && r.Tag != null) RowClicked(r);
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (rowRects.Count != Rows.Count) Measure();
            int lw = Ui.S(LabelWidth), rw = Ui.S(RightWidth);
            using (var lf = Ui.F(9.5f)) using (var vf = Ui.F(10f)) using (var line = new Pen(Color.FromArgb(238, 240, 243)))
                for (int i = 0; i < Rows.Count; i++)
                {
                    var r = Rows[i]; var rr = rowRects[i];
                    if (i == hover && (RowClicked != null && r.Tag != null)) using (var hb = new SolidBrush(Color.FromArgb(247, 249, 252))) g.FillRectangle(hb, rr);
                    TextRenderer.DrawText(g, r.Label, lf, new Rectangle(Ui.S(4), rr.Y + Ui.S(8), lw - Ui.S(8), rr.Height - Ui.S(8)), Ui.Sub, Wrap);
                    bool empty = string.IsNullOrEmpty(r.Value);
                    TextRenderer.DrawText(g, empty ? "—" : r.Value, vf, new Rectangle(lw, rr.Y + Ui.S(8), Width - lw - rw - Ui.S(24), rr.Height), empty ? Color.FromArgb(175, 180, 190) : Ui.Text, Wrap);
                    string right = r.Copy ? (empty ? "" : "复制") : r.Right;
                    if (!string.IsNullOrEmpty(right))
                        using (var rf = Ui.F(9f, i == hover && hoverCopy ? FontStyle.Underline : FontStyle.Regular))
                            TextRenderer.DrawText(g, right, rf, new Rectangle(copyRects[i].X, rr.Y + Ui.S(9), rw - Ui.S(4), Ui.S(22)), r.Copy ? Ui.Blue : Ui.Text, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
                    g.DrawLine(line, 0, rr.Bottom - 1, Width, rr.Bottom - 1);
                }
        }
    }
}
