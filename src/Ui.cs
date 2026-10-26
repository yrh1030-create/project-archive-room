using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DangAn
{
    public static class Ui
    {
        public static float Scale = 1f;
        public static int S(int v) { return (int)Math.Round(v * Scale); }

        public static readonly Color Bg = Color.FromArgb(244, 246, 249);      // 内容区画布
        public static readonly Color Side = Color.FromArgb(238, 241, 245);    // 侧栏
        public static readonly Color Line = Color.FromArgb(226, 230, 236);
        public static readonly Color Text = Color.FromArgb(24, 31, 44);
        public static readonly Color Sub = Color.FromArgb(105, 114, 130);
        public static readonly Color Blue = Color.FromArgb(37, 99, 235);
        public static readonly Color BlueSoft = Color.FromArgb(234, 241, 255);
        public static readonly Color Teal = Color.FromArgb(14, 150, 140);
        public static readonly Color Violet = Color.FromArgb(124, 77, 222);
        public static readonly Color Green = Color.FromArgb(21, 128, 61);
        public static readonly Color GreenSoft = Color.FromArgb(225, 245, 232);
        public static readonly Color Orange = Color.FromArgb(194, 100, 8);
        public static readonly Color OrangeSoft = Color.FromArgb(254, 243, 214);
        public static readonly Color Red = Color.FromArgb(200, 40, 40);
        public static readonly Color Hover = Color.FromArgb(236, 238, 242);

        public const string FontName = "Microsoft YaHei UI";
        public static Font F(float size) { return new Font(FontName, size); }
        public static Font F(float size, FontStyle st) { return new Font(FontName, size, st); }

        public static Button Btn(string text, bool primary) { return Btn(text, primary, null); }

        public static Button Btn(string text, bool primary, string icon)
        {
            var b = new RoundButton { Text = text, Icon = icon, Style = primary ? RoundButton.Kind.Primary : RoundButton.Kind.Normal };
            b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            if (primary) b.Font = F(9.5f, FontStyle.Bold);
            return b;
        }

        /// <summary>带下拉菜单的按钮：点一下在按钮下方弹出菜单。items 依次为“菜单文字, 动作”。</summary>
        public static Button MenuBtn(string text, bool primary, string icon, params object[] items)
        {
            var b = Btn(text + " ▾", primary, icon);
            var menu = new ContextMenuStrip { Font = F(9.5f) };
            for (int i = 0; i + 1 < items.Length; i += 2) { var act = (Action)items[i + 1]; menu.Items.Add((string)items[i], null, (s, e) => act()); }
            b.Click += (s, e) => menu.Show(b, new Point(0, b.Height));
            return b;
        }

        public static Button SoftBtn(string text) { return SoftBtn(text, null); }

        public static Button SoftBtn(string text, string icon)
        {
            var b = (RoundButton)Btn(text, false, icon);
            b.Style = RoundButton.Kind.Soft;
            return b;
        }

        public static GraphicsPath RoundPath(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>柔和投影 + 圆角白卡。返回卡片本体区域。</summary>
        public static Rectangle CardShape(Graphics g, Rectangle bounds, int radius, Color fill, Color border, bool lifted)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int sh = S(lifted ? 6 : 3);
            var body = new Rectangle(bounds.X + sh / 2, bounds.Y + 1, bounds.Width - sh - 1, bounds.Height - sh - 2);
            for (int i = sh; i >= 1; i--)
            {
                int a = (lifted ? 7 : 5) * (sh - i + 1) / sh + 1;
                using (var b = new SolidBrush(Color.FromArgb(a, 16, 30, 60)))
                using (var p = RoundPath(new Rectangle(body.X - i / 2, body.Y + i / 2 + (lifted ? i / 2 : 0), body.Width + i, body.Height + i / 2), radius + i / 2)) g.FillPath(b, p);
            }
            Rounded(g, body, radius, fill, border, false);
            return body;
        }

        public static Label Lbl(string text, float size, bool bold, Color color)
        {
            var l = new Label();
            l.Text = text; l.AutoSize = true; l.Font = F(size, bold ? FontStyle.Bold : FontStyle.Regular);
            l.ForeColor = color; l.BackColor = Color.Transparent; l.Margin = new Padding(S(2));
            return l;
        }

        public static ListView List(params object[] cols)
        {
            var lv = new DoubleBufferedListView();
            lv.View = View.Details; lv.FullRowSelect = true; lv.HideSelection = false;
            lv.BorderStyle = BorderStyle.None; lv.Font = F(9.5f); lv.ForeColor = Text;
            lv.HeaderStyle = ColumnHeaderStyle.Clickable;
            var il = new ImageList(); il.ImageSize = new Size(1, S(34)); lv.SmallImageList = il;
            for (int i = 0; i + 1 < cols.Length; i += 2)
            {
                var ch = lv.Columns.Add((string)cols[i], S((int)cols[i + 1]));
                ch.Tag = (int)cols[i + 1];
                if (((string)cols[i]).Contains("金额") || ((string)cols[i]).Contains("（元）") || (string)cols[i] == "大小" || (string)cols[i] == "资料") ch.TextAlign = HorizontalAlignment.Right;
            }
            OwnerDrawList(lv);
            EnableSort(lv);
            lv.ClientSizeChanged += (s, e) => FillColumn(lv);
            lv.Resize += (s, e) => FillColumn(lv);
            lv.VisibleChanged += (s, e) => { if (lv.Visible) lv.BeginInvoke(new Action(() => FillColumn(lv))); };
            return lv;
        }

        /// <summary>自绘表格：浅灰表头、淡蓝选中行、细分隔线。</summary>
        public static void OwnerDrawList(ListView lv)
        {
            lv.OwnerDraw = true;
            lv.BackColor = Color.White;
            var line = Color.FromArgb(240, 242, 246);
            lv.DrawColumnHeader += (s, e) =>
            {
                using (var b = new SolidBrush(Color.FromArgb(248, 250, 252))) e.Graphics.FillRectangle(b, e.Bounds);
                using (var p = new Pen(Line)) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
                if (e.Header.TextAlign == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;
                var r = new Rectangle(e.Bounds.X + S(10), e.Bounds.Y, e.Bounds.Width - S(20), e.Bounds.Height);
                using (var f = F(9f, FontStyle.Bold)) TextRenderer.DrawText(e.Graphics, e.Header.Text, f, r, Sub, flags);
            };
            lv.DrawItem += (s, e) => { };
            lv.DrawSubItem += (s, e) =>
            {
                var bounds = e.Bounds;
                if (e.ColumnIndex == 0) bounds.Width = lv.Columns[0].Width;
                bool sel = e.Item.Selected;
                using (var b = new SolidBrush(sel ? Color.FromArgb(232, 240, 255) : Color.White)) e.Graphics.FillRectangle(b, bounds);
                if (sel && e.ColumnIndex == 0) using (var b = new SolidBrush(Blue)) e.Graphics.FillRectangle(b, bounds.X, bounds.Y + S(6), S(3), bounds.Height - S(12));
                using (var p = new Pen(line)) e.Graphics.DrawLine(p, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
                var col = e.Item.UseItemStyleForSubItems ? e.Item.ForeColor : e.SubItem.ForeColor;
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
                if (lv.Columns[e.ColumnIndex].TextAlign == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;
                var r = new Rectangle(bounds.X + S(10), bounds.Y, bounds.Width - S(20), bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, lv.Font, r, col, flags);
            };
            lv.MouseUp += (s, e) => lv.Invalidate();
        }

        public static void EnableSort(ListView lv)
        {
            lv.ColumnClick += (s, e) =>
            {
                var cur = lv.ListViewItemSorter as ColSorter;
                bool asc = cur == null || cur.Col != e.Column || !cur.Asc;
                lv.ListViewItemSorter = new ColSorter { Col = e.Column, Asc = asc };
            };
        }

        class ColSorter : System.Collections.IComparer
        {
            public int Col; public bool Asc;
            public int Compare(object x, object y)
            {
                var a = (ListViewItem)x; var b = (ListViewItem)y;
                string sa = Col < a.SubItems.Count ? a.SubItems[Col].Text : "", sb = Col < b.SubItems.Count ? b.SubItems[Col].Text : "";
                double da, db; int r;
                if (double.TryParse(sa.Replace(",", "").Replace(" 份", "").Replace("份", ""), out da) && double.TryParse(sb.Replace(",", "").Replace(" 份", "").Replace("份", ""), out db)) r = da.CompareTo(db);
                else r = string.Compare(sa, sb, StringComparison.CurrentCulture);
                return Asc ? r : -r;
            }
        }

        public static DataGridView Grid()
        {
            var g = new DataGridView();
            g.BackgroundColor = Color.White; g.BorderStyle = BorderStyle.None; g.Font = F(9.5f);
            g.GridColor = Line; g.RowHeadersVisible = false; g.AllowUserToResizeRows = false;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252); g.ColumnHeadersDefaultCellStyle.ForeColor = Sub;
            g.ColumnHeadersDefaultCellStyle.Font = F(9f, FontStyle.Bold);
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(248, 250, 252);
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(S(6), 0, S(6), 0);
            g.GridColor = Color.FromArgb(238, 241, 245);
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersHeight = S(34);
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(232, 240, 255); g.DefaultCellStyle.SelectionForeColor = Text;
            g.DefaultCellStyle.Padding = new Padding(S(6), S(3), S(6), S(3));
            g.RowTemplate.Height = S(34);
            g.ColumnHeadersHeight = S(38);
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            return g;
        }

        public static string Money(double v) { return v == 0 ? "" : v.ToString("#,##0.00"); }
        public static string Size(long b)
        {
            if (b < 1024) return b + " B";
            if (b < 1024 * 1024) return (b / 1024.0).ToString("0") + " KB";
            if (b < 1024L * 1024 * 1024) return (b / 1048576.0).ToString("0.0") + " MB";
            return (b / 1073741824.0).ToString("0.00") + " GB";
        }

        public static void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("打不开：" + path + "\n" + ex.Message, "项目档案室", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        public static void Reveal(string path)
        {
            try
            {
                if (File.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
                else MessageBox.Show("找不到：" + path);
            }
            catch { }
        }

        public static void Info(string msg) { MessageBox.Show(msg, "项目档案室", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        public static bool Ask(string msg) { return MessageBox.Show(msg, "项目档案室", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes; }

        public static string Prompt(string title, string label, string value)
        {
            using (var f = new Form())
            {
                f.Text = title; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false; f.MaximizeBox = false; f.Font = F(9.5f); f.BackColor = Color.White;
                f.ClientSize = new Size(S(520), S(150));
                var l = Lbl(label, 9.5f, false, Text); l.Location = new Point(S(16), S(16));
                var t = new TextBox(); t.Text = value ?? ""; t.Location = new Point(S(16), S(46)); t.Width = S(488);
                var ok = Btn("确定", true); ok.DialogResult = DialogResult.OK; ok.Location = new Point(S(330), S(92));
                var no = Btn("取消", false); no.DialogResult = DialogResult.Cancel; no.Location = new Point(S(420), S(92));
                f.Controls.AddRange(new Control[] { l, t, ok, no });
                f.AcceptButton = ok; f.CancelButton = no;
                return f.ShowDialog() == DialogResult.OK ? t.Text.Trim() : null;
            }
        }

        public static Project PickProject(Store st, string title, Project exclude)
        {
            using (var f = new Form())
            {
                f.Text = title; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false; f.MaximizeBox = false; f.Font = F(9.5f); f.BackColor = Color.White;
                f.ClientSize = new Size(S(560), S(460));
                var lb = new ListBox(); lb.Location = new Point(S(16), S(16)); lb.Size = new Size(S(528), S(380)); lb.IntegralHeight = false;
                foreach (var p in st.Lib.Projects) if (p != exclude) lb.Items.Add(p);
                lb.DisplayMember = "Name";
                var ok = Btn("确定", true); ok.DialogResult = DialogResult.OK; ok.Location = new Point(S(370), S(410));
                var no = Btn("取消", false); no.DialogResult = DialogResult.Cancel; no.Location = new Point(S(460), S(410));
                lb.DoubleClick += (s, e) => { if (lb.SelectedItem != null) f.DialogResult = DialogResult.OK; };
                f.Controls.AddRange(new Control[] { lb, ok, no });
                return f.ShowDialog() == DialogResult.OK ? lb.SelectedItem as Project : null;
            }
        }

        /// <summary>依次叠放 Dock=Top 的控件（第一个在最上面），最后一个 Dock=Fill。</summary>
        public static void Stack(Control parent, Control fill, params Control[] tops)
        {
            foreach (var c in tops) { c.Dock = DockStyle.Top; parent.Controls.Add(c); c.BringToFront(); }
            if (fill != null) { fill.Dock = DockStyle.Fill; parent.Controls.Add(fill); fill.BringToFront(); }
        }

        /// <summary>容器尺寸确定后再设分隔位置（第一块面板宽/高 px）。</summary>
        public static void SplitAt(SplitContainer sp, int px)
        {
            bool done = false;
            EventHandler h = null;
            h = (s, e) =>
            {
                int total = sp.Orientation == Orientation.Vertical ? sp.Width : sp.Height;
                if (done || total < S(px) + S(120)) return;
                done = true;
                try { sp.SplitterDistance = S(px); } catch { }
                sp.SizeChanged -= h;
            };
            sp.SizeChanged += h;
            sp.FixedPanel = FixedPanel.Panel1;
        }

        /// <summary>让声明宽度最大的一列吃掉剩余宽度，避免出现横向滚动条。</summary>
        public static void FillColumn(ListView lv)
        {
            if (lv.Columns.Count == 0) return;
            int fill = 0;
            for (int i = 1; i < lv.Columns.Count; i++) if ((int)lv.Columns[i].Tag > (int)lv.Columns[fill].Tag) fill = i;
            int other = 0;
            for (int i = 0; i < lv.Columns.Count; i++) if (i != fill) other += lv.Columns[i].Width;
            int w = lv.ClientSize.Width - other - 2;
            lv.Columns[fill].Width = Math.Max(S(120), w);
        }

        public static Panel Gap(int h) { var p = new Panel(); p.Height = S(h); p.BackColor = Color.Transparent; return p; }

        public static FlowLayoutPanel Row()
        {
            var f = new FlowLayoutPanel();
            f.AutoSize = true; f.AutoSizeMode = AutoSizeMode.GrowAndShrink; f.WrapContents = false;
            f.BackColor = Color.Transparent; f.Margin = new Padding(0); f.Padding = new Padding(0);
            return f;
        }

        public static void Rounded(Graphics g, Rectangle r, int radius, Color fill, Color border, bool dashed)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath())
            {
                int d = radius * 2;
                path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border.A > 0) using (var p = new Pen(border, Math.Max(1, S(1)))) { if (dashed) p.DashPattern = new float[] { 4, 3 }; g.DrawPath(p, path); }
            }
        }
    }

    public class DoubleBufferedListView : ListView
    {
        public DoubleBufferedListView() { DoubleBuffered = true; }
    }

    /// <summary>圆角卡片容器。</summary>
    public class Card : Panel
    {
        public bool Dashed;
        public Color Fill = Color.White;
        public bool Flat;
        public Card() { DoubleBuffered = true; BackColor = Color.Transparent; Padding = new Padding(Ui.S(18), Ui.S(14), Ui.S(18), Ui.S(16)); ResizeRedraw = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var r = ClientRectangle; r.Width -= 1; r.Height -= 1;
            if (Dashed || Flat) Ui.Rounded(e.Graphics, r, Ui.S(12), Fill, Dashed ? Color.FromArgb(170, 178, 190) : Color.FromArgb(232, 236, 241), Dashed);
            else Ui.CardShape(e.Graphics, ClientRectangle, Ui.S(12), Fill, Color.FromArgb(232, 236, 241), false);
            base.OnPaint(e);
        }
    }

    /// <summary>文件列表右键菜单。</summary>
    public static class FileMenu
    {
        public static List<FileRec> Selected(ListView lv)
        {
            var r = new List<FileRec>();
            foreach (ListViewItem it in lv.SelectedItems) { var f = it.Tag as FileRec; if (f != null) r.Add(f); var h = it.Tag as Hit; if (h != null) r.Add(h.File); }
            return r;
        }

        /// <summary>资料右键菜单（表格和文件卡片共用）。sel 返回当前选中的资料。</summary>
        public static ContextMenuStrip Build(Store st, Func<List<FileRec>> sel, Action changed) { return Build(st, sel, changed, null); }

        /// <summary>proj 不为空时（在某个项目页里），多出“从本项目移除”。</summary>
        public static ContextMenuStrip Build(Store st, Func<List<FileRec>> sel, Action changed, Func<Project> proj)
        {
            var m = new ContextMenuStrip(); m.Font = Ui.F(9.5f);
            var open = m.Items.Add("打开");
            var reveal = m.Items.Add("打开所在文件夹");
            var copy = m.Items.Add("复制文件路径");
            m.Items.Add(new ToolStripSeparator());
            var cat = new ToolStripMenuItem("改为分类");
            foreach (var c in Cats.All)
            {
                string cc = c;
                cat.DropDownItems.Add(Cats.Title(c), null, (s, e) =>
                {
                    foreach (var f in sel()) { st.Lib.FileCat[f.Rel] = cc; f.Cat = cc; }
                    st.Save(); changed();
                });
            }
            m.Items.Add(cat);
            var move = m.Items.Add("归到其他项目…");
            var share = m.Items.Add("同时显示在其他项目…（共用，不复制文件）");
            var unshare = m.Items.Add("取消共用");
            var auto = m.Items.Add("恢复自动分类和归属");
            share.Click += (s, e) =>
            {
                var l = sel(); if (l.Count == 0) return;
                var p = Ui.PickProject(st, "让这 " + l.Count + " 份资料同时显示在哪个项目？（文件不复制、不移动）", null);
                if (p == null) return;
                foreach (var f in l)
                {
                    if (f.Project == p.Id) continue;
                    List<string> lst;
                    if (!st.Lib.SharedWith.TryGetValue(f.Rel, out lst)) st.Lib.SharedWith[f.Rel] = lst = new List<string>();
                    if (!lst.Contains(p.Id)) lst.Add(p.Id);
                }
                st.Save(); changed();
            };
            unshare.Click += (s, e) => { foreach (var f in sel()) st.Lib.SharedWith.Remove(f.Rel); st.Save(); changed(); };
            m.Opening += (s, e) => { var l = sel(); unshare.Visible = l.Any(f => st.SharedOf(f).Count > 0); };
            open.Click += (s, e) => { foreach (var f in sel().Take(5)) Ui.Open(st.FullPath(f)); };
            reveal.Click += (s, e) => { var f = sel().FirstOrDefault(); if (f != null) Ui.Reveal(st.FullPath(f)); };
            copy.Click += (s, e) => { var l = sel(); if (l.Count > 0) Clipboard.SetText(string.Join("\r\n", l.Select(f => st.FullPath(f)))); };
            move.Click += (s, e) =>
            {
                var l = sel(); if (l.Count == 0) return;
                var p = Ui.PickProject(st, "把 " + l.Count + " 份资料归到哪个项目？（只改登记，不移动文件）", null);
                if (p == null) return;
                foreach (var f in l) { st.Lib.FileProject[f.Rel] = p.Id; f.Project = p.Id; }
                st.Save(); changed();
            };
            auto.Click += (s, e) =>
            {
                foreach (var f in sel()) { st.Lib.FileProject.Remove(f.Rel); st.Lib.FileCat.Remove(f.Rel); f.Project = st.Assign(f.Rel); f.Cat = st.CatOf(f.Rel); }
                st.Save(); changed();
            };

            // ---- 更新、移除、删除
            m.Items.Add(new ToolStripSeparator());
            var replace = m.Items.Add("替换为新版本…（旧文件进回收站）");
            var remove = m.Items.Add("从本项目移除（文件保留）");
            var del = m.Items.Add("删除文件（移到回收站）");
            del.ForeColor = Ui.Red;
            m.Opening += (s, e) =>
            {
                var l = sel();
                replace.Enabled = l.Count == 1;
                remove.Visible = proj != null && proj() != null && !proj().IsRoot;
                remove.Text = l.Count > 1 ? "从本项目移除这 " + l.Count + " 份（文件保留）" : "从本项目移除（文件保留）";
                del.Text = l.Count > 1 ? "删除这 " + l.Count + " 份文件（移到回收站）" : "删除文件（移到回收站）";
            };
            replace.Click += (s, e) =>
            {
                var f = sel().FirstOrDefault(); if (f == null) return;
                using (var d = new OpenFileDialog { Title = "选择新版本，替换“" + f.Name + "”" })
                {
                    if (d.ShowDialog() != DialogResult.OK) return;
                    if (!Ui.Ask("用“" + Path.GetFileName(d.FileName) + "”替换“" + f.Name + "”？\n\n新文件会放到原来的位置，沿用原来的分类、归属和财务关联；旧文件移到回收站（需要时可以从回收站还原）。")) return;
                    try { ProjectOps.ReplaceFile(st, f, d.FileName); changed(); }
                    catch (Exception ex) { Ui.Info("替换失败：" + ex.Message); }
                }
            };
            remove.Click += (s, e) =>
            {
                var l = sel(); var p = proj == null ? null : proj();
                if (l.Count == 0 || p == null) return;
                if (!Ui.Ask("把 " + (l.Count == 1 ? "“" + l[0].Name + "”" : l.Count + " 份资料") + "从“" + p.Name + "”里移除？\n\n文件本身不删除，只是这个项目里不再显示（共用的只取消共用）。\n需要时可在“设置与备份”里恢复。")) return;
                foreach (var f in l) ProjectOps.RemoveFromProject(st, f, p);
                st.Save(); changed();
            };
            del.Click += (s, e) =>
            {
                var l = sel(); if (l.Count == 0) return;
                if (!Ui.Ask("删除 " + (l.Count == 1 ? "“" + l[0].Name + "”" : l.Count + " 份文件") + "？\n\n文件会移到 Windows 回收站（可以从回收站还原），档案库里的登记和财务关联一并清除。")) return;
                int bad = 0;
                foreach (var f in l) { try { ProjectOps.DeleteFile(st, f); } catch { bad++; } }
                st.Save(); changed();
                if (bad > 0) Ui.Info(bad + " 份文件没能删除（可能正被别的程序打开）。");
            };
            return m;
        }

        public static void Attach(ListView lv, Store st, Action changed)
        {
            lv.ContextMenuStrip = Build(st, () => Selected(lv), changed);
            lv.DoubleClick += (s, e) => { var f = Selected(lv).FirstOrDefault(); if (f != null) Ui.Open(st.FullPath(f)); };
            lv.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { var f = Selected(lv).FirstOrDefault(); if (f != null) Ui.Open(st.FullPath(f)); } };
        }

        public static ListViewItem Item(Store st, FileRec f, bool withProject)
        {
            var it = new ListViewItem(f.Name);
            if (withProject) { var p = st.Proj(f.Project); it.SubItems.Add(p == null ? "" : p.Name); }
            it.SubItems.Add(Cats.Short(f.Cat));
            it.SubItems.Add(Ui.Size(f.Size));
            it.SubItems.Add(f.Time);
            it.SubItems.Add(f.Dir);
            it.Tag = f;
            it.ToolTipText = f.Rel;
            return it;
        }
    }
}
