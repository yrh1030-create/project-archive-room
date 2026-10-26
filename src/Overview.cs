using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DangAn
{
    /// <summary>项目卡片：一眼看清一个项目，点开再细查。</summary>
    public class ProjectCard : Control
    {
        public readonly Project P;
        readonly ProjectInfo info;
        readonly string contractor, period, amountText, amountKind;
        bool hover;
        public static readonly Color Done = Color.FromArgb(22, 163, 120);

        public static Color TypeColor(string type)
        {
            switch (type)
            {
                case "专项债券": return Ui.Violet;
                case "通组公路": return Ui.Blue;
                case "安全设施": return Color.FromArgb(234, 120, 20);
                case "抢险整治": return Color.FromArgb(220, 60, 70);
                case "财政补助": return Ui.Teal;
                case "日常养护": return Color.FromArgb(40, 160, 80);
                case "待分配": return Color.FromArgb(140, 148, 160);
                default: return Color.FromArgb(90, 104, 128);
            }
        }

        public ProjectCard(Store st, Project p, ProjectInfo info)
        {
            P = p; this.info = info;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent; Cursor = Cursors.Hand;
            contractor = st.ContractorOf(p);
            period = st.Period(p);
            var a = st.Amount(p);
            amountText = a.Key > 0 ? (a.Key / 10000).ToString("#,##0.00") + " 万元" : "";
            amountKind = a.Value;
            Height = Ui.S(286);
            var tip = new ToolTip();
            tip.SetToolTip(this, p.Name + (p.Folders.Count > 0 ? "\n文件夹：" + string.Join("；", p.Folders) : "") + "\n单击打开项目概览");
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

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
            var body = Ui.CardShape(g, ClientRectangle, Ui.S(14), Color.White, hover ? Color.FromArgb(150, Ui.Blue) : Color.FromArgb(232, 236, 241), hover);
            int pad = Ui.S(20), x = body.X + pad, w = body.Width - 2 * pad, y = body.Y + Ui.S(18);
            var tc = TypeColor(P.IsRoot ? "待分配" : P.Type);

            // 类型色块
            var badge = new Rectangle(x, y, Ui.S(40), Ui.S(40));
            using (var b = new SolidBrush(Color.FromArgb(26, tc))) using (var gp = Ui.RoundPath(badge, Ui.S(11))) g.FillPath(b, gp);
            Glyph.Draw(g, P.IsRoot ? "folder" : "box", new RectangleF(badge.X + Ui.S(10), badge.Y + Ui.S(10), Ui.S(20), Ui.S(20)), tc);

            // 进展标签（右上）
            string stage = P.IsRoot ? "待分配" : info.Stage;
            Color chipFg, chipBg;
            if (P.IsRoot) { chipFg = Ui.Sub; chipBg = Color.FromArgb(240, 242, 245); }
            else if (stage.Contains("批复") || stage.Contains("办结")) { chipFg = Color.FromArgb(18, 130, 90); chipBg = Color.FromArgb(226, 247, 236); }
            else if (info.StageIndex < 0) { chipFg = Ui.Orange; chipBg = Ui.OrangeSoft; }
            else { chipFg = Ui.Blue; chipBg = Ui.BlueSoft; }
            Rectangle chip;
            using (var cf = Ui.F(8.5f, FontStyle.Bold))
            {
                var sz = TextRenderer.MeasureText(stage, cf);
                chip = new Rectangle(body.Right - pad - sz.Width - Ui.S(30), y + Ui.S(1), sz.Width + Ui.S(30), Ui.S(26));
                using (var b = new SolidBrush(chipBg)) using (var gp = Ui.RoundPath(chip, Ui.S(13))) g.FillPath(b, gp);
                using (var b = new SolidBrush(chipFg)) g.FillEllipse(b, chip.X + Ui.S(10), chip.Y + chip.Height / 2 - Ui.S(3), Ui.S(6), Ui.S(6));
                TextRenderer.DrawText(g, stage, cf, new Rectangle(chip.X + Ui.S(20), chip.Y, chip.Width - Ui.S(28), chip.Height), chipFg, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            // 名称 + 副标题
            int nx = badge.Right + Ui.S(12), nw = chip.Left - nx - Ui.S(10);
            using (var nf = Ui.F(11.5f, FontStyle.Bold))
            {
                int lh = TextRenderer.MeasureText("字", nf).Height;
                TextRenderer.DrawText(g, Fit(P.Name, nf, nw, 2), nf, new Rectangle(nx, y - Ui.S(2), nw, lh * 2), P.IsRoot ? Ui.Sub : Ui.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
            y += Ui.S(52);
            using (var sf = Ui.F(8.5f))
            {
                string sub = P.IsRoot ? "根目录里没放进项目文件夹的资料" : P.Code + "   ·   " + P.Type;
                TextRenderer.DrawText(g, sub, sf, new Point(nx - Ui.S(3), y - Ui.S(4)), Ui.Sub, TextFormatFlags.NoPrefix);
            }
            y += Ui.S(26);

            // 办理流程 7 段
            if (!P.IsRoot)
            {
                int n = Cats.StageNames.Length, gap = Ui.S(5);
                int seg = (w - gap * (n - 1)) / n;
                using (var lf = Ui.F(8f))
                    for (int i = 0; i < n; i++)
                    {
                        int k; info.CatCount.TryGetValue(Cats.StageCats[i], out k);
                        var rr = new Rectangle(x + i * (seg + gap), y, seg, Ui.S(6));
                        using (var b = new SolidBrush(k > 0 ? Done : Color.FromArgb(230, 234, 239))) using (var gp = Ui.RoundPath(rr, Ui.S(3))) g.FillPath(b, gp);
                        string nm = Cats.StageNames[i].Replace("设计审批", "设计").Replace("招标采购", "招标").Replace("签订合同", "合同").Replace("检测验收", "验收").Replace("结算决算", "决算");
                        TextRenderer.DrawText(g, nm, lf, new Rectangle(rr.X - Ui.S(4), y + Ui.S(9), seg + Ui.S(8), Ui.S(18)), k > 0 ? Color.FromArgb(70, 80, 96) : Color.FromArgb(176, 182, 192), TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                    }
            }
            y += Ui.S(38);

            // 关键信息 2×2（浅灰底）
            var factBox = new Rectangle(x, y, w, Ui.S(92));
            using (var b = new SolidBrush(Color.FromArgb(248, 250, 252))) using (var gp = Ui.RoundPath(factBox, Ui.S(10))) g.FillPath(b, gp);
            int col = (w - Ui.S(24)) / 2, fx = x + Ui.S(12);
            Fact(g, fx, y + Ui.S(8), col - Ui.S(8), "施工单位", contractor);
            Fact(g, fx + col + Ui.S(8), y + Ui.S(8), col, amountKind == "" ? "金额" : amountKind, amountText);
            Fact(g, fx, y + Ui.S(50), col - Ui.S(8), "实施时间", period);
            Fact(g, fx + col + Ui.S(8), y + Ui.S(50), col, "资料", info.FileCount + " 份" + (P.Contracts.Count > 0 ? " · 合同 " + P.Contracts.Count + " 份" : ""));
            y = factBox.Bottom + Ui.S(12);

            // 底部状态
            string foot; Color fc; string ic;
            if (P.IsRoot) { foot = "点开后右键资料，归到具体项目"; fc = Ui.Sub; ic = "chev"; }
            else if (info.Missing.Count == 0) { foot = "应有资料齐全"; fc = Color.FromArgb(18, 140, 95); ic = "check"; }
            else { foot = "待补：" + string.Join("、", info.Missing.Select(Cats.Short)); fc = Ui.Orange; ic = "alert"; }
            int fh = Ui.S(22);
            Glyph.Draw(g, ic, new RectangleF(x, y + Ui.S(2), Ui.S(17), Ui.S(17)), fc);
            using (var ff = Ui.F(9f))
                TextRenderer.DrawText(g, foot, ff, new Rectangle(x + Ui.S(22), y, w - Ui.S(80), fh), fc, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (hover)
                using (var ff = Ui.F(9f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "查看详情 ›", ff, new Rectangle(x + w - Ui.S(90), y, Ui.S(90), fh), Ui.Blue, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        static void Fact(Graphics g, int x, int y, int w, string label, string value)
        {
            using (var lf = Ui.F(8f)) TextRenderer.DrawText(g, label, lf, new Point(x, y), Color.FromArgb(130, 138, 152), TextFormatFlags.NoPrefix);
            using (var vf = Ui.F(9.5f))
                TextRenderer.DrawText(g, string.IsNullOrEmpty(value) ? "—" : value, vf, new Rectangle(x, y + Ui.S(16), w, Ui.S(22)),
                    string.IsNullOrEmpty(value) ? Color.FromArgb(176, 182, 192) : Ui.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }

    // ===================================================== 项目总览
    public class OverviewPage : Panel, IPage
    {
        readonly MainForm m; Store St { get { return m.St; } }
        Panel cardScroll, cards; SearchBox search; ComboBox sort; Label[] statNum = new Label[4], statSub = new Label[4];
        Label countLbl; string filter = "全部";
        RoundButton unlink;
        readonly List<RoundButton> tabs = new List<RoundButton>();
        public string Crumb { get { return "项目档案室  ›  项目总览"; } }

        public OverviewPage(MainForm m)
        {
            this.m = m; BackColor = Ui.Bg;
            // 标题行
            var head = new Panel { Height = Ui.S(78) };
            var t1 = Ui.Lbl("我的项目", 20f, true, Ui.Text); t1.Location = new Point(0, 0);
            var t2 = Ui.Lbl("先看每个项目的概况，点卡片进入项目再细查。文件、文件夹、压缩包拖进窗口任意位置即可归档。", 9.5f, false, Ui.Sub); t2.Location = new Point(Ui.S(3), Ui.S(46));
            var hb = Ui.Row(); hb.Dock = DockStyle.Right; hb.Padding = new Padding(0, Ui.S(6), 0, 0);
            var bFolder = Ui.Btn("导入项目文件夹", true, "folder");
            bFolder.Click += (s, e) => { using (var d = new FolderBrowserDialog { Description = "选择要导入的文件夹：装着多个项目文件夹的上级文件夹（每个子文件夹是一个项目），或者单个项目文件夹" }) if (d.ShowDialog(this) == DialogResult.OK) m.BatchImportFolders(new[] { d.SelectedPath }); };
            var bUp = Ui.Btn("上传资料", false, "upload");
            bUp.Click += (s, e) => { using (var d = new OpenFileDialog { Multiselect = true, Title = "选择要上传的资料（可多选，压缩包会自动解压）" }) if (d.ShowDialog(this) == DialogResult.OK) m.Import(d.FileNames, null); };
            var bExp = Ui.MenuBtn("导出", false, "excel",
                "全部项目合同台账（Excel）", (Action)(() => Exports.Contracts(this, St, St.RealProjects, "合同台账")),
                "全部项目经济合同 PDF 包", (Action)(() => m.ExportContractPdfs(St.RealProjects.ToList(), "经济合同 PDF")),
                "某个项目的资料包（按分类）…", (Action)(() => { var p = Ui.PickProject(St, "导出哪个项目的资料包？", null); if (p != null && !p.IsRoot) ExportPackDialog.Show(this, m, p); }));
            hb.Controls.AddRange(new Control[] { bExp, bUp, bFolder });
            head.Controls.AddRange(new Control[] { t1, t2, hb });

            // 统计
            var stats = new TableLayoutPanel { Height = Ui.S(112), ColumnCount = 4, RowCount = 1, BackColor = Color.Transparent };
            string[] titles = { "项目", "归档资料", "合同", "资料待补" };
            string[] icons = { "box", "doc", "contract", "alert" };
            Color[] tints = { Ui.Blue, Ui.Teal, Ui.Violet, Color.FromArgb(234, 120, 20) };
            for (int i = 0; i < 4; i++)
            {
                stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
                var c = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, Ui.S(i < 3 ? 14 : 0), 0) };
                var dot = new IconDot(icons[i], tints[i]) { Location = new Point(Ui.S(20), Ui.S(20)) };
                var lt = Ui.Lbl(titles[i], 9f, false, Ui.Sub); lt.Location = new Point(Ui.S(70), Ui.S(18));
                statNum[i] = Ui.Lbl("0", 17f, true, Ui.Text); statNum[i].Location = new Point(Ui.S(67), Ui.S(38));
                statSub[i] = Ui.Lbl("", 8.5f, false, Color.FromArgb(130, 138, 152)); statSub[i].Location = new Point(Ui.S(21), Ui.S(76));
                c.Controls.AddRange(new Control[] { dot, lt, statNum[i], statSub[i] });
                stats.Controls.Add(c, i, 0);
            }

            // 筛选行
            var bar = new Panel { Height = Ui.S(64) };
            var tabRow = Ui.Row(); tabRow.Location = new Point(0, Ui.S(16));
            foreach (var t in new[] { "全部", "办理中", "已决算批复", "资料待补", "资料齐全" })
            {
                var b = (RoundButton)Ui.Btn(t, false);
                b.Style = RoundButton.Kind.Ghost; b.Margin = new Padding(0, 0, Ui.S(4), 0); b.Padding = new Padding(Ui.S(12), Ui.S(5), Ui.S(12), Ui.S(5));
                string tt = t; b.Click += (s, e) => { filter = tt; Reload(); };
                tabs.Add(b); tabRow.Controls.Add(b);
            }
            countLbl = Ui.Lbl("", 9f, false, Ui.Sub); countLbl.Margin = new Padding(Ui.S(10), Ui.S(10), 0, 0);
            tabRow.Controls.Add(countLbl);
            var right = Ui.Row(); right.Dock = DockStyle.Right; right.Padding = new Padding(0, Ui.S(14), 0, 0);
            sort = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.S(130), Font = Ui.F(9.5f), FlatStyle = FlatStyle.Flat, Margin = new Padding(0, Ui.S(5), 0, 0) };
            sort.Items.AddRange(new object[] { "按档号排序", "按年份排序", "按金额排序", "按待补多少" });
            sort.SelectedIndex = 0;
            sort.SelectedIndexChanged += (s, e) => Reload();
            search = new SearchBox("搜索项目、施工单位…", 10f) { Width = Ui.S(280), Margin = new Padding(Ui.S(12), 0, 0, 0) };
            search.Box.TextChanged += (s, e) => Reload();
            unlink = (RoundButton)Ui.Btn("一键取消关联", false); unlink.Style = RoundButton.Kind.Ghost; unlink.Margin = new Padding(0, 0, Ui.S(10), 0);
            unlink.Click += (s, e) =>
            {
                if (ProjectOps.LooksMisrecognized(St).Count >= 2) { if (UnlinkDialog.Show(m)) m.ReloadAll(); }
                else if (DupMergeDialog.Show(m)) m.ReloadAll();
            };
            right.Controls.AddRange(new Control[] { unlink, sort, search });
            bar.Controls.Add(tabRow); bar.Controls.Add(right);

            cardScroll = new Panel { AutoScroll = true, BackColor = Ui.Bg };
            cards = new Panel { Location = new Point(0, 0), BackColor = Ui.Bg };
            cardScroll.Controls.Add(cards);
            cardScroll.Resize += (s, e) => Layout2();
            Ui.Stack(this, cardScroll, head, stats, bar);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        public static void SetCue(TextBox t, string cue) { t.HandleCreated += (s, e) => SendMessage(t.Handle, 0x1501, (IntPtr)1, cue); }

        /// <summary>手工排卡片（几列自适应），并设定内容总高度，外层面板据此出现滚动条。</summary>
        void Layout2()
        {
            int avail = cardScroll.ClientSize.Width - (cardScroll.VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth) - Ui.S(2);
            if (avail < Ui.S(200)) return;
            int gap = Ui.S(10), vgap = Ui.S(6);
            int cols = Math.Max(1, (avail + gap) / Ui.S(400));
            int w = (avail - gap * (cols - 1)) / cols;
            int i = 0, y = 0, rowH = 0;
            cards.SuspendLayout();
            foreach (Control c in cards.Controls)
            {
                int col = i % cols;
                if (col == 0 && i > 0) { y += rowH + vgap; rowH = 0; }
                c.SetBounds(col * (w + gap), y, w, c.Height);
                rowH = Math.Max(rowH, c.Height);
                i++;
            }
            cards.ResumeLayout();
            cards.Size = new Size(avail, y + rowH + Ui.S(10));
            cardScroll.AutoScrollMinSize = new Size(0, cards.Height);
        }

        static int Year(Project p)
        {
            var mt = System.Text.RegularExpressions.Regex.Match(p.Name + " " + string.Join(" ", p.Folders), @"20\d\d");
            return mt.Success ? int.Parse(mt.Value) : 0;
        }

        public void Reload()
        {
            foreach (var b in tabs)
            {
                bool on = b.Text == filter;
                b.Style = on ? RoundButton.Kind.Soft : RoundButton.Kind.Ghost;
                b.Font = Ui.F(9.5f, on ? FontStyle.Bold : FontStyle.Regular);
                b.Invalidate();
            }
            var all = St.Lib.Projects.ToList();
            var infos = all.ToDictionary(p => p.Id, p => St.Info(p));
            var real = all.Where(p => !p.IsRoot).ToList();
            int full = real.Count(p => infos[p.Id].Missing.Count == 0);
            int approved = real.Count(p => infos[p.Id].Stage.Contains("批复"));
            int loose = infos[Store.RootProjectId].FileCount;
            statNum[0].Text = real.Count + " 个"; statSub[0].Text = "已决算批复 " + approved + " 个 · 办理中 " + (real.Count - approved) + " 个";
            statNum[1].Text = St.Lib.Files.Count.ToString("#,##0") + " 份"; statSub[1].Text = "可全文搜索 " + St.Texts.Count + " 份";
            statNum[2].Text = real.Sum(p => p.Contracts.Count) + " 份"; statSub[2].Text = "合同金额合计 " + (real.Sum(p => p.Contracts.Sum(c => c.Amount)) / 10000).ToString("#,##0") + " 万元";
            statNum[3].Text = (real.Count - full) + " 个项目"; statSub[3].Text = loose > 0 ? "另有 " + loose + " 份散件未归属" : "按应有资料清单核对";

            string q = search.Text.Trim();
            IEnumerable<Project> list = all.Where(p =>
            {
                var info = infos[p.Id];
                if (p.IsRoot) return info.FileCount > 0 && filter == "全部" && q == "";
                if (filter == "办理中" && info.Stage.Contains("批复")) return false;
                if (filter == "已决算批复" && !info.Stage.Contains("批复")) return false;
                if (filter == "资料待补" && info.Missing.Count == 0) return false;
                if (filter == "资料齐全" && info.Missing.Count > 0) return false;
                if (q.Length > 0 && !(p.Name.Contains(q) || p.Code.Contains(q) || p.Type.Contains(q) || p.Fields.Any(f => f.Value.Contains(q)) || p.Contracts.Any(c => c.Party.Contains(q)) || p.Folders.Any(f => f.Contains(q)))) return false;
                return true;
            });
            switch (sort.SelectedIndex)
            {
                case 1: list = list.OrderBy(p => p.IsRoot ? 1 : 0).ThenByDescending(Year).ThenBy(p => p.Code); break;
                case 2: list = list.OrderBy(p => p.IsRoot ? 1 : 0).ThenByDescending(p => St.Amount(p).Key); break;
                case 3: list = list.OrderBy(p => p.IsRoot ? 1 : 0).ThenByDescending(p => infos[p.Id].Missing.Count).ThenBy(p => p.Code); break;
                default: list = list.OrderBy(p => p.IsRoot ? 1 : 0).ThenBy(p => p.Code); break;
            }
            var shown = list.ToList();
            countLbl.Text = "共 " + shown.Count(p => !p.IsRoot) + " 个";
            // 只有发现问题时才出现：分类文件夹被当成项目 → 一键取消关联；名字相同（如“188号--慧谷湖…”和“慧谷湖…”）→ 一键合并
            int bad = ProjectOps.LooksMisrecognized(St).Count, dup = ProjectOps.DuplicateGroups(St).Count;
            unlink.Text = bad >= 2 ? "发现 " + bad + " 个疑似误识别项目 · 一键取消关联" : dup > 0 ? "发现 " + dup + " 组重名项目 · 一键合并" : "";
            unlink.TextColor = Ui.Orange;
            unlink.Visible = bad >= 2 || dup > 0;
            unlink.Invalidate();

            cardScroll.AutoScrollPosition = new Point(0, 0);
            cards.SuspendLayout();
            var old = cards.Controls.Cast<Control>().ToList();
            cards.Controls.Clear();
            foreach (var c in old) c.Dispose();
            foreach (var p in shown)
            {
                var card = new ProjectCard(St, p, infos[p.Id]);
                var pp = p;
                card.Click += (s, e) => m.OpenProject(pp, 0);
                if (!pp.IsRoot)
                {
                    var cm = new ContextMenuStrip { Font = Ui.F(9.5f) };
                    cm.Items.Add("打开项目", null, (s, e) => m.OpenProject(pp, 0));
                    cm.Items.Add("打开项目文件夹", null, (s, e) => { if (pp.Folders.Count > 0) Ui.Reveal(System.IO.Path.Combine(St.Lib.Root, pp.Folders[0])); });
                    cm.Items.Add(new ToolStripSeparator());
                    var di = cm.Items.Add("删除项目…", null, (s, e) => { if (DeleteProjectDialog.Show(this, St, new List<Project> { pp })) m.ReloadAll(); });
                    di.ForeColor = Ui.Red;
                    card.ContextMenuStrip = cm;
                }
                cards.Controls.Add(card);
            }
            cards.ResumeLayout();
            Layout2();
        }
    }
}
