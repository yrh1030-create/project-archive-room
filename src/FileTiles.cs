using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DangAn
{
    /// <summary>文件类型标识（颜色 + 缩写）。</summary>
    public static class FileKind
    {
        public static void Badge(string ext, out string label, out Color color)
        {
            switch (ext)
            {
                case ".pdf": label = "PDF"; color = Color.FromArgb(220, 60, 60); break;
                case ".ofd": label = "OFD"; color = Color.FromArgb(200, 70, 40); break;
                case ".doc": case ".docx": case ".wps": label = "DOC"; color = Color.FromArgb(40, 100, 220); break;
                case ".xls": case ".xlsx": case ".et": case ".csv": label = "XLS"; color = Color.FromArgb(24, 140, 80); break;
                case ".jpg": case ".jpeg": case ".png": case ".bmp": case ".heic": case ".tif": case ".tiff": label = "IMG"; color = Color.FromArgb(140, 80, 210); break;
                case ".dwg": case ".dxf": label = "CAD"; color = Color.FromArgb(210, 130, 20); break;
                case ".zip": case ".rar": case ".7z": label = "ZIP"; color = Color.FromArgb(110, 110, 125); break;
                case ".txt": case ".md": label = "TXT"; color = Color.FromArgb(100, 116, 139); break;
                default: label = ext.Length > 1 ? ext.Substring(1, Math.Min(3, ext.Length - 1)).ToUpperInvariant() : "?"; color = Color.FromArgb(120, 130, 145); break;
            }
        }
    }

    /// <summary>文件卡片网格：每份资料一张小卡（类型标识 + 名称 + 日期大小），单击打开，右键更多。</summary>
    public class FileGrid : Control
    {
        readonly Store st;
        public List<FileRec> Files = new List<FileRec>();
        /// <summary>同名同大小的副本数（不含自身），在卡片上提示。</summary>
        public Dictionary<FileRec, int> Copies = new Dictionary<FileRec, int>();
        public int MaxItems = int.MaxValue;
        readonly List<Rectangle> rects = new List<Rectangle>();
        int hover = -1, menuTarget = -1;
        readonly ToolTip tip = new ToolTip();
        public int TileMin = 290;

        /// <summary>Ctrl+单击选中的卡片（可多选，右键对选中的一起操作）。</summary>
        readonly HashSet<int> picked = new HashSet<int>();

        public FileGrid(Store st, Action changed) : this(st, changed, null) { }

        public FileGrid(Store st, Action changed, Project project)
        {
            this.st = st;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            ContextMenuStrip = FileMenu.Build(st, () =>
            {
                var l = new List<FileRec>();
                if (menuTarget >= 0 && menuTarget < Files.Count)
                {
                    if (picked.Contains(menuTarget)) l.AddRange(picked.Where(i => i < Files.Count).OrderBy(i => i).Select(i => Files[i]));
                    else l.Add(Files[menuTarget]);
                }
                return l;
            }, changed, project == null ? (Func<Project>)null : () => project);
            ContextMenuStrip.Opening += (s, e) => { if (menuTarget < 0) e.Cancel = true; };
            Margin = new Padding(0, 0, 0, Ui.S(6));
        }

        public int Shown { get { return Math.Min(Files.Count, MaxItems); } }

        public void Relayout()
        {
            rects.Clear();
            int gap = Ui.S(10), th = Ui.S(68);
            int cols = Math.Max(1, (Width + gap) / (Ui.S(TileMin) + gap));
            int tw = (Width - gap * (cols - 1)) / cols;
            for (int i = 0; i < Shown; i++)
                rects.Add(new Rectangle((i % cols) * (tw + gap), (i / cols) * (th + gap), tw, th));
            int h = rects.Count == 0 ? 0 : rects[rects.Count - 1].Bottom + Ui.S(2);
            if (Height != h) Height = h;
            Invalidate();
        }

        protected override void OnResize(EventArgs e) { Relayout(); base.OnResize(e); }

        int Hit(Point p) { for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h != hover)
            {
                hover = h; Invalidate();
                Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
                tip.SetToolTip(this, h >= 0 ? Files[h].Rel + "\n单击打开 · Ctrl+单击多选 · 右键更多" : "");
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) menuTarget = Hit(e.Location);
            base.OnMouseDown(e);
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (e.Button == MouseButtons.Left && h >= 0)
            {
                if ((ModifierKeys & Keys.Control) != 0) { if (!picked.Remove(h)) picked.Add(h); Invalidate(); }
                else if (picked.Count > 0) { picked.Clear(); Invalidate(); }
                else Ui.Open(st.FullPath(Files[h]));
            }
            base.OnMouseClick(e);
        }

        static string Fit(string s, Font f, int width, int lines)
        {
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            int lh = TextRenderer.MeasureText("字", f).Height;
            if (TextRenderer.MeasureText(s, f, new Size(width, int.MaxValue), flags).Height <= lh * lines) return s;
            int lo = 1, hi = s.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (TextRenderer.MeasureText(s.Substring(0, mid) + "…", f, new Size(width, int.MaxValue), flags).Height <= lh * lines) lo = mid; else hi = mid - 1;
            }
            return s.Substring(0, lo) + "…";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (rects.Count != Shown) Relayout();
            using (var nf = Ui.F(9.5f)) using (var mf = Ui.F(8f)) using (var bf = Ui.F(7.5f, FontStyle.Bold))
                for (int i = 0; i < rects.Count; i++)
                {
                    if (!e.ClipRectangle.IntersectsWith(rects[i])) continue;
                    var f = Files[i]; var r = rects[i];
                    bool hv = i == hover, pk = picked.Contains(i);
                    Ui.Rounded(g, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), Ui.S(10), pk ? Color.FromArgb(232, 240, 255) : hv ? Color.FromArgb(246, 249, 255) : Color.White, pk ? Ui.Blue : hv ? Color.FromArgb(150, Ui.Blue) : Color.FromArgb(228, 232, 238), false);
                    if (pk)
                    {
                        var ck = new Rectangle(r.Right - Ui.S(24), r.Y + Ui.S(6), Ui.S(18), Ui.S(18));
                        using (var b = new SolidBrush(Ui.Blue)) g.FillEllipse(b, ck);
                        Glyph.Draw(g, "check", new RectangleF(ck.X + Ui.S(3), ck.Y + Ui.S(3), Ui.S(12), Ui.S(12)), Color.White);
                    }
                    string lab; Color col;
                    FileKind.Badge(f.Ext, out lab, out col);
                    var badge = new Rectangle(r.X + Ui.S(12), r.Y + (r.Height - Ui.S(40)) / 2, Ui.S(36), Ui.S(40));
                    using (var b = new SolidBrush(Color.FromArgb(24, col))) using (var gp = Ui.RoundPath(badge, Ui.S(7))) g.FillPath(b, gp);
                    // 折角
                    using (var b = new SolidBrush(Color.FromArgb(70, col)))
                        g.FillPolygon(b, new[] { new Point(badge.Right - Ui.S(10), badge.Y), new Point(badge.Right, badge.Y + Ui.S(10)), new Point(badge.Right - Ui.S(10), badge.Y + Ui.S(10)) });
                    TextRenderer.DrawText(g, lab, bf, new Rectangle(badge.X, badge.Y + Ui.S(14), badge.Width, Ui.S(20)), col, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                    int tx = badge.Right + Ui.S(12), tw = r.Right - tx - Ui.S(10);
                    string name = System.IO.Path.GetFileNameWithoutExtension(f.Name);
                    int lh = TextRenderer.MeasureText("字", nf).Height;
                    TextRenderer.DrawText(g, Fit(name, nf, tw, 2), nf, new Rectangle(tx, r.Y + Ui.S(7), tw, lh * 2), hv ? Ui.Blue : Ui.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                    string meta = f.Time.Length >= 10 ? f.Time.Substring(0, 10) : f.Time;
                    meta += "  ·  " + Ui.Size(f.Size);
                    string dir = f.Dir.Contains("\\") ? f.Dir.Substring(f.Dir.LastIndexOf('\\') + 1) : "";
                    int cp;
                    if (Copies.TryGetValue(f, out cp) && cp > 0) meta = "另有 " + cp + " 份相同副本  ·  " + meta;
                    var shared = st.SharedOf(f);
                    if (shared.Count > 0 || st.Lib.SharedWith.ContainsKey(f.Rel)) meta = "多项目共用  ·  " + meta;
                    else if (dir != "") meta += "  ·  " + dir;
                    TextRenderer.DrawText(g, meta, mf, new Rectangle(tx, r.Bottom - Ui.S(22), tw, Ui.S(18)), Color.FromArgb(140, 148, 160), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                }
        }
    }

    /// <summary>滚轮跟着鼠标走：鼠标在哪个可滚动区域上就滚哪个（不必先点一下）。</summary>
    public class WheelRouter : IMessageFilter
    {
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x020A && m.Msg != 0x020E) return false;
            int lp = unchecked((int)m.LParam.ToInt64());
            var pt = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
            var hwnd = WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) return false;
            var c = Control.FromChildHandle(hwnd);
            Control target = null;
            for (var x = c; x != null; x = x.Parent)
            {
                if (x is ComboBox) return false;
                var dg = x as DataGridView;
                if (dg != null && dg.ScrollBars == ScrollBars.None) continue;
                if (x is ListView || x is TreeView || x is DataGridView || x is ListBox || (x is TextBoxBase && ((TextBoxBase)x).Multiline)) { target = x; break; }
                var sc = x as ScrollableControl;
                if (sc != null && sc.AutoScroll && (sc.VerticalScroll.Visible || sc.HorizontalScroll.Visible)) { target = x; break; }
            }
            if (target == null || target.Handle == m.HWnd) return false;
            SendMessage(target.Handle, m.Msg, m.WParam, m.LParam);
            return true;
        }
    }
}
