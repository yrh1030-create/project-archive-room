using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DangAn
{
    /// <summary>项目页：点开项目后，概况、关键信息、合同台账、全部资料、备注都在一页里从上往下看。</summary>
    public class ProjectView : Panel, IPage
    {
        readonly MainForm m; public readonly Project P; Store St { get { return m.St; } }
        TextBox name, code; ComboBox type, stage;
        Panel scroller; VStack body;
        SectionCard secOverview, secInfo, secFinance, secContracts, secFiles, secNote;
        DataGridView payGrid;
        DataGridView conGrid; Label conTotal; TextBox note;
        FlowLayoutPanel chipRow; SearchBox fileSearch; VStack filesBox; string fileCat = "";
        readonly List<RoundButton> anchors = new List<RoundButton>();
        readonly Timer anchorTimer = new Timer { Interval = 250 };
        bool loading;

        static readonly string[] KeyFields = { "项目代码", "建设地点", "建设内容及规模", "立项／上级建设计划", "投资概算／可研投资", "资金来源及到位情况",
            "中标／施工单位", "施工合同／成交金额", "实际投资额（元）", "决策／采购形式", "工程进度及验收" };

        public string Crumb { get { return "项目档案室  ›  项目总览  ›  " + P.Name; } }

        public ProjectView(MainForm m, Project p)
        {
            this.m = m; P = p; BackColor = Ui.Bg;
            loading = true;
            // ---------- 顶部（固定）
            var head = new Panel { Height = Ui.S(170), BackColor = Ui.Bg };
            var back = (RoundButton)Ui.Btn("‹  返回项目总览", false); back.Style = RoundButton.Kind.Ghost; back.Location = new Point(-Ui.S(6), 0);
            back.Click += (s, e) => { Commit(); m.ShowPage("项目总览"); };
            var badge = new IconDot(p.IsRoot ? "folder" : "box", ProjectCard.TypeColor(p.IsRoot ? "待分配" : p.Type)) { Location = new Point(0, Ui.S(46)), Size = new Size(Ui.S(56), Ui.S(56)) };
            name = new TextBox { Text = p.Name, Font = Ui.F(17f, FontStyle.Bold), BorderStyle = BorderStyle.None, BackColor = Ui.Bg, ForeColor = Ui.Text, Location = new Point(Ui.S(72), Ui.S(46)), ReadOnly = p.IsRoot, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
            name.Leave += (s, e) => Commit();
            var row = Ui.Row(); row.Location = new Point(Ui.S(66), Ui.S(84));
            Func<string, Label> L = t => { var l = Ui.Lbl(t, 9f, false, Ui.Sub); l.Margin = new Padding(Ui.S(8), Ui.S(11), Ui.S(4), 0); return l; };
            code = new TextBox { Text = p.Code, Width = Ui.S(70), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, Ui.S(7), Ui.S(4), 0) };
            type = new ComboBox { Width = Ui.S(110), DropDownStyle = ComboBoxStyle.DropDown, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, Ui.S(7), Ui.S(4), 0) };
            type.Items.AddRange(new object[] { "专项债券", "通组公路", "安全设施", "抢险整治", "财政补助", "日常养护", "道路工程", "其他" });
            type.Text = p.Type;
            stage = new ComboBox { Width = Ui.S(140), DropDownStyle = ComboBoxStyle.DropDown, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, Ui.S(7), Ui.S(10), 0) };
            stage.Items.Add("（自动判断）");
            stage.Items.AddRange(new object[] { "前期准备", "立项审批中", "设计审批中", "招标采购中", "施工中", "已完工待验收", "已验收待结算", "结算审核中", "决算审核中", "决算已批复", "已办结归档" });
            stage.Text = p.Stage == "" ? "（自动判断）" : p.Stage;
            foreach (Control c in new Control[] { code, type, stage }) c.Leave += (s, e) => { Commit(); Reload(); };
            type.SelectedIndexChanged += (s, e) => { if (!loading) { Commit(); } };
            stage.SelectedIndexChanged += (s, e) => { if (!loading) { Commit(); Reload(); } };
            var openDir = Ui.SoftBtn("打开项目文件夹", "open"); openDir.Click += (s, e) => { if (p.Folders.Count > 0) Ui.Reveal(Path.Combine(St.Lib.Root, p.Folders[0])); };
            var addFile = Ui.SoftBtn("添加资料", "plus"); addFile.Click += (s, e) => AddFiles();
            var delProj = (RoundButton)Ui.Btn("删除项目", false); delProj.Style = RoundButton.Kind.Ghost; delProj.TextColor = Ui.Red;
            delProj.Click += (s, e) => { Commit(); if (DeleteProjectDialog.Show(this, St, new List<Project> { P })) m.ShowPage("项目总览"); };
            delProj.Visible = !p.IsRoot;
            row.Controls.AddRange(new Control[] { L("档号"), code, L("类型"), type, L("办理进展"), stage, openDir, addFile, delProj });
            if (p.IsRoot) { code.Enabled = type.Enabled = stage.Enabled = false; }
            // 跳转条
            var anchorRow = Ui.Row(); anchorRow.Location = new Point(-Ui.S(4), Ui.S(130));
            foreach (var a in new[] { "概况", "关键信息", "财务", "合同台账", "全部资料", "备注" })
            {
                var b = (RoundButton)Ui.Btn(a, false);
                b.Style = RoundButton.Kind.Ghost; b.Margin = new Padding(0, 0, Ui.S(4), 0); b.Padding = new Padding(Ui.S(12), Ui.S(4), Ui.S(12), Ui.S(4));
                string aa = a; b.Click += (s, e) => JumpTo(aa);
                anchors.Add(b); anchorRow.Controls.Add(b);
            }
            head.Controls.AddRange(new Control[] { back, badge, name, row, anchorRow });
            head.Resize += (s, e) => name.Width = head.Width - Ui.S(80);

            // ---------- 可滚动内容
            scroller = new Panel { AutoScroll = true, BackColor = Ui.Bg };
            body = new VStack { Location = new Point(0, 0), Padding = new Padding(0, Ui.S(4), Ui.S(6), Ui.S(30)) };
            scroller.Controls.Add(body);
            scroller.Resize += (s, e) => FitBody();
            body.SizeChanged += (s, e) => { if (scroller.AutoScrollMinSize.Height != body.Height) scroller.AutoScrollMinSize = new Size(0, body.Height); };
            Ui.Stack(this, scroller, head);

            BuildSections();
            anchorTimer.Tick += (s, e) => UpdateAnchors();
            VisibleChanged += (s, e) => anchorTimer.Enabled = Visible;
            Disposed += (s, e) => anchorTimer.Dispose();
            loading = false;
        }

        void FitBody()
        {
            int w = scroller.ClientSize.Width - (scroller.VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth);
            if (w > Ui.S(300) && body.Width != w) body.Width = w;
        }

        // ================== 区块
        void BuildSections()
        {
            secOverview = new SectionCard(); secInfo = new SectionCard(); secFinance = new SectionCard(); secContracts = new SectionCard(); secFiles = new SectionCard(); secNote = new SectionCard();
            body.Controls.AddRange(new Control[] { secOverview, secInfo, secFinance, secContracts, secFiles, secNote });
            BuildPayGrid();

            // 合同台账：表格只建一次
            conGrid = Ui.Grid();
            conGrid.AllowUserToAddRows = false; conGrid.ScrollBars = ScrollBars.None;
            conGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            string[] cols = { "合同类型", "合同相对方", "合同金额（元）", "签订日期", "已付（元）", "未付（元）", "审定／决算", "合同内容", "备注", "来源文件" };
            int[] fw = { 120, 170, 95, 80, 95, 80, 90, 130, 170, 120 };
            for (int i = 0; i < cols.Length; i++)
            {
                var col = new DataGridViewTextBoxColumn { HeaderText = cols[i], FillWeight = fw[i], MinimumWidth = Ui.S(60) };
                if (cols[i].Contains("（元）")) { col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; col.DefaultCellStyle.Format = "#,##0.00"; col.ValueType = typeof(double); }
                conGrid.Columns.Add(col);
            }
            conGrid.CellEndEdit += (s, e) => { CommitContracts(); St.Save(); UpdateConTotal(); };
            conGrid.DataError += (s, e) => { e.ThrowException = false; Ui.Info("金额请填数字。"); };
            conGrid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 9) return;
                string src = Convert.ToString(conGrid.Rows[e.RowIndex].Cells[9].Value);
                if (src == "") return;
                string full = Path.IsPathRooted(src) ? src : Path.Combine(St.Lib.Root, src);
                if (File.Exists(full)) Ui.Open(full); else Ui.Info("找不到：" + full);
            };
            conTotal = Ui.Lbl("", 9.5f, false, Ui.Sub);

            note = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Font = Ui.F(10.5f), BorderStyle = BorderStyle.FixedSingle, AcceptsReturn = true, Height = Ui.S(130) };
            note.Leave += (s, e) => Commit();

            fileSearch = new SearchBox("搜本项目的文件名…", 9.5f) { Width = Ui.S(260) };
            fileSearch.Box.TextChanged += (s, e) => FillFiles();
            chipRow = new FlowLayoutPanel { WrapContents = true, BackColor = Color.White, AutoSize = false };
            filesBox = new VStack();
        }

        public void Reload()
        {
            loading = true;
            var info = St.Info(P);
            int keep = -scroller.AutoScrollPosition.Y;
            body.SuspendLayout();
            DetachReusable();
            FillOverview(info);
            FillInfo();
            FillFinance();
            FillContracts();
            FillFilesSection(info);
            FillNote();
            body.ResumeLayout(true);
            FitBody();
            loading = false;
            scroller.AutoScrollPosition = new Point(0, keep);
            m.SetCrumb(Crumb);
        }

        /// <summary>重建区块前，先把需要复用的控件摘下来，避免被一起释放。</summary>
        void DetachReusable()
        {
            foreach (var k in new Control[] { conGrid, payGrid, note, filesBox, chipRow, fileSearch, conTotal })
                if (k != null && k.Parent != null) k.Parent.Controls.Remove(k);
        }

        static void Clear(Control c)
        {
            foreach (Control x in c.Controls.Cast<Control>().ToList()) { c.Controls.Remove(x); x.Dispose(); }
        }

        Control SectionHead(string title, string hint, params Control[] buttons)
        {
            var h = new Panel { Height = Ui.S(50), BackColor = Color.White, Margin = new Padding(0, 0, 0, Ui.S(6)) };
            var t = Ui.Lbl(title, 13.5f, true, Ui.Text); t.Location = new Point(0, Ui.S(6));
            h.Controls.Add(t);
            if (!string.IsNullOrEmpty(hint)) { var s = Ui.Lbl(hint, 9f, false, Ui.Sub); s.Location = new Point(t.PreferredWidth + Ui.S(10), Ui.S(15)); h.Controls.Add(s); }
            if (buttons.Length > 0)
            {
                var r = Ui.Row(); r.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                r.Controls.AddRange(buttons);
                h.Controls.Add(r);
                h.Resize += (s, e) => r.Location = new Point(h.Width - r.PreferredSize.Width, 0);
            }
            return h;
        }

        // ---------- 概况
        void FillOverview(ProjectInfo info)
        {
            Clear(secOverview);
            var amt = St.Amount(P);
            var tiles = new TableLayoutPanel { Height = Ui.S(92), ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 0, 0, Ui.S(16)), BackColor = Color.White };
            string[,] t = {
                { amt.Value == "" ? "金额" : amt.Value, amt.Key > 0 ? (amt.Key / 10000).ToString("#,##0.00") + " 万元" : "—" },
                { "合同", P.Contracts.Count > 0 ? P.Contracts.Count + " 份 · " + (info.ContractTotal / 10000).ToString("#,##0.00") + " 万" : "—" },
                { "资料", info.FileCount + " 份" },
                { "办理进展", info.Stage }
            };
            string[] icons = { "coin", "contract", "doc", "check" };
            Color[] tints = { Ui.Blue, Ui.Violet, Ui.Teal, info.Stage.Contains("批复") ? Color.FromArgb(22, 150, 100) : Color.FromArgb(234, 120, 20) };
            for (int i = 0; i < 4; i++)
            {
                tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
                var c = new Card { Dock = DockStyle.Fill, Flat = true, Margin = new Padding(0, 0, i < 3 ? Ui.S(12) : 0, 0), Fill = Color.FromArgb(248, 250, 252) };
                var dot = new IconDot(icons[i], tints[i]) { Location = new Point(Ui.S(16), Ui.S(24)) };
                var a = Ui.Lbl(t[i, 0], 9f, false, Ui.Sub); a.Location = new Point(Ui.S(66), Ui.S(16));
                var b = Ui.Lbl(t[i, 1], 13.5f, true, i == 3 ? tints[3] : Ui.Text); b.Location = new Point(Ui.S(64), Ui.S(38));
                c.Controls.Add(dot); c.Controls.Add(a); c.Controls.Add(b);
                tiles.Controls.Add(c, i, 0);
            }
            secOverview.Controls.Add(SectionHead("概况", P.IsRoot ? "根目录里还没归到项目的散件" : P.Code + " · " + P.Type));
            secOverview.Controls.Add(tiles);
            if (!P.IsRoot)
            {
                var fh = new Panel { Height = Ui.S(30), BackColor = Color.White };
                var fl = Ui.Lbl("办理流程", 10.5f, true, Ui.Text); fl.Location = new Point(0, Ui.S(4));
                var fs = Ui.Lbl("点步骤看这一类资料，点蓝色文件名直接打开；右键某一步或把文件拖到某一步上，可直接添加到这一步", 8.5f, false, Ui.Sub); fs.Location = new Point(fl.PreferredWidth + Ui.S(8), Ui.S(8));
                fh.Controls.Add(fl); fh.Controls.Add(fs);
                secOverview.Controls.Add(fh);
                var flow = new StageFlow(St, P, info) { Margin = new Padding(0, 0, 0, Ui.S(12)) };
                flow.CatClicked += c => { fileCat = c; fileSearch.Text = ""; LoadChips(); FillFiles(); JumpTo("全部资料"); };
                flow.AddFilesTo += c =>
                {
                    using (var d = new OpenFileDialog { Multiselect = true, Title = "添加到“" + Cats.Title(c) + "”（可多选，压缩包会自动解压）" })
                        if (d.ShowDialog(this) == DialogResult.OK) AddFilesToCat(c, d.FileNames);
                };
                flow.DropFilesTo += (c, files) => AddFilesToCat(c, files);
                secOverview.Controls.Add(flow);
                var miss = new Card { Flat = true, Height = Ui.S(54), Fill = info.Missing.Count == 0 ? Color.FromArgb(238, 250, 243) : Color.FromArgb(255, 248, 234) };
                var ml = Ui.Lbl(info.Missing.Count == 0 ? "✓ 应有资料齐全（按本项目核对 " + P.Required.Count + " 类）"
                    : "⚠ 还缺 " + info.Missing.Count + " 类：" + string.Join("、", info.Missing.Select(Cats.Title)) + "。把文件拖进窗口即可补上。", 10f, true, info.Missing.Count == 0 ? Color.FromArgb(18, 130, 90) : Ui.Orange);
                ml.Location = new Point(Ui.S(16), Ui.S(16));
                var adj = new LinkLabel { Text = "调整应有清单", AutoSize = true, Font = Ui.F(9f), LinkBehavior = LinkBehavior.HoverUnderline, BackColor = Color.Transparent };
                adj.LinkClicked += (s, e) => { if (RequiredEditor.Edit(this, P)) { St.Save(); Reload(); } };
                miss.Controls.Add(ml); miss.Controls.Add(adj);
                miss.Resize += (s, e) => adj.Location = new Point(miss.Width - adj.Width - Ui.S(16), Ui.S(18));
                secOverview.Controls.Add(miss);
            }
        }

        // ---------- 关键信息
        void FillInfo()
        {
            Clear(secInfo);
            var edit = Ui.Btn("编辑全部栏目（" + P.Fields.Count + "）", false, "table");
            edit.Click += (s, e) => { if (FieldEditor.Edit(this, P)) { St.Save(); Reload(); } };
            secInfo.Controls.Add(SectionHead("关键信息", "填报时点右侧“复制”", edit));
            var kl = new InfoList();
            foreach (var f in KeyFields)
            {
                string v = P.Get(f);
                if (f == "中标／施工单位") { var cc = St.ContractorOf(P); if (cc != "") v = cc; }
                kl.Rows.Add(new InfoList.Row { Label = f.Replace("／", " / "), Value = v, Copy = true });
                if (f == "实际投资额（元）") kl.Rows.Add(new InfoList.Row { Label = "实施时间", Value = St.Period(P), Copy = true });
            }
            secInfo.Controls.Add(kl);
        }

        // ---------- 财务
        const int PC_DATE = 0, PC_DIR = 1, PC_SUM = 2, PC_CON = 3, PC_AMT = 4, PC_VNO = 5, PC_VF = 6, PC_IF = 7, PC_NOTE = 8;

        void BuildPayGrid()
        {
            payGrid = Ui.Grid();
            payGrid.AllowUserToAddRows = false; payGrid.ScrollBars = ScrollBars.None;
            payGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            payGrid.ShowCellToolTips = true;
            payGrid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            Action<string, int> col = (h, w) => payGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = h, FillWeight = w, MinimumWidth = Ui.S(44) });
            col("日期", 80); col("收/支", 42); col("摘要", 230); col("关联合同", 120); col("金额（元）", 95); col("凭证号", 62);
            foreach (var h in new[] { "记账凭证", "发票" })
                payGrid.Columns.Add(new DataGridViewLinkColumn { HeaderText = h, FillWeight = h == "发票" ? 110 : 150, TrackVisitedState = false, LinkBehavior = LinkBehavior.HoverUnderline, LinkColor = Ui.Blue, ActiveLinkColor = Ui.Blue });
            col("备注", 130);
            payGrid.Columns[PC_AMT].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            payGrid.Columns[PC_AMT].DefaultCellStyle.Format = "#,##0.00";
            payGrid.Columns[PC_AMT].ValueType = typeof(double);
            payGrid.Columns[PC_DIR].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            // 按比例手工分配列宽（Fill 模式在先建后放大的表格上分配不准）
            int[] weights = { 118, 62, 270, 150, 140, 86, 180, 150, 130 };
            payGrid.DefaultCellStyle.Padding = new Padding(Ui.S(4), 0, Ui.S(4), 0);
            payGrid.SizeChanged += (s, e) =>
            {
                int total = payGrid.ClientSize.Width - 2, sum = weights.Sum();
                if (total < Ui.S(400)) return;
                for (int i = 0; i < weights.Length; i++) payGrid.Columns[i].Width = Math.Max(Ui.S(40), total * weights[i] / sum);
            };
            payGrid.CellEndEdit += (s, e) => { CommitPayRow(payGrid.Rows[e.RowIndex]); St.Save(); FillFinanceStats(); };
            payGrid.DataError += (s, e) => { e.ThrowException = false; Ui.Info("金额请填数字。"); };
            payGrid.CellContentClick += (s, e) =>
            {
                if (e.RowIndex < 0 || (e.ColumnIndex != PC_VF && e.ColumnIndex != PC_IF)) return;
                var pay = payGrid.Rows[e.RowIndex].Tag as Payment; if (pay == null) return;
                string rel = e.ColumnIndex == PC_VF ? pay.VoucherFile : pay.InvoiceFile;
                if (rel != "" && File.Exists(Path.Combine(St.Lib.Root, rel))) Ui.Open(Path.Combine(St.Lib.Root, rel));
                else LinkFile(pay, e.ColumnIndex == PC_VF);
            };
            var menu = new ContextMenuStrip { Font = Ui.F(9.5f) };
            Func<Payment> cur = () => payGrid.CurrentRow == null ? null : payGrid.CurrentRow.Tag as Payment;
            menu.Items.Add("打开记账凭证", null, (s, e) => { var p = cur(); if (p != null && p.VoucherFile != "") Ui.Open(Path.Combine(St.Lib.Root, p.VoucherFile)); });
            menu.Items.Add("打开发票", null, (s, e) => { var p = cur(); if (p != null && p.InvoiceFile != "") Ui.Open(Path.Combine(St.Lib.Root, p.InvoiceFile)); });
            menu.Items.Add("打开凭证所在文件夹", null, (s, e) => { var p = cur(); if (p != null && p.VoucherFile != "") Ui.Reveal(Path.Combine(St.Lib.Root, p.VoucherFile)); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("关联 / 更换记账凭证…", null, (s, e) => { var p = cur(); if (p != null) LinkFile(p, true); });
            menu.Items.Add("关联 / 更换发票…", null, (s, e) => { var p = cur(); if (p != null) LinkFile(p, false); });
            menu.Items.Add("换成重开的新发票（选新发票文件）…", null, (s, e) =>
            {
                var p = cur(); if (p == null) return;
                using (var d = new OpenFileDialog { Title = "选择重开的新发票（PDF 或图片）", Filter = "发票文件|*.pdf;*.ofd;*.jpg;*.jpeg;*.png|所有文件|*.*" })
                    if (d.ShowDialog(this) == DialogResult.OK) AddInvoices(d.FileNames, p);
            });
            menu.Items.Add("清除凭证关联", null, (s, e) => { var p = cur(); if (p != null) { p.VoucherFile = ""; St.Save(); FillFinance(); } });
            menu.Items.Add("清除发票关联", null, (s, e) => { var p = cur(); if (p != null) { p.InvoiceFile = ""; St.Save(); FillFinance(); } });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("删除这一笔", null, (s, e) => { var p = cur(); if (p != null && Ui.Ask("删除这一笔收支记录？（不会删除凭证、发票文件）")) { P.Payments.Remove(p); St.Save(); FillFinance(); } });
            payGrid.ContextMenuStrip = menu;
            payGrid.CellMouseDown += (s, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) payGrid.CurrentCell = payGrid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)]; };
        }

        Label finStats; Panel finBar;

        void FillFinance()
        {
            Clear(secFinance);
            var add = Ui.Btn("记一笔", false);
            add.Click += (s, e) =>
            {
                var pay = new Payment { Date = DateTime.Today.ToString("yyyy-MM-dd"), Dir = "支出" };
                P.Payments.Add(pay); St.Save(); FillFinance();
                var row = payGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag == pay);
                if (row != null) { payGrid.CurrentCell = row.Cells[PC_SUM]; payGrid.BeginEdit(true); }
            };
            var imp = Ui.Btn("导入明细表", false);
            imp.Click += (s, e) =>
            {
                int n = Finance.Import(St, P); St.Save(); FillFinance();
                Ui.Info(n > 0 ? "已从本项目的收入/支出明细表导入 " + n + " 笔，并自动关联了合同、凭证和发票。\n手工记的笔数和手工关联的文件都已保留。" : "本项目里没有找到“支出明细表 / 收入明细表”（Excel）。可以点“记一笔”手工录入。");
            };
            var ex = Ui.Btn("导出", false);
            ex.Click += (s, e) => Exports.Payments(this, St, P);
            var split = Ui.Btn("拆分发票", false);
            split.Click += (s, e) => SplitInvoices((Button)s);
            var addInv = Ui.Btn("添加发票", false, "plus");
            addInv.Click += (s, e) =>
            {
                using (var d = new OpenFileDialog { Multiselect = true, Title = "选择发票（可多选；重开的新票会提示替换旧票）", Filter = "发票文件|*.pdf;*.ofd;*.jpg;*.jpeg;*.png|所有文件|*.*" })
                    if (d.ShowDialog(this) == DialogResult.OK) AddInvoices(d.FileNames, null);
            };
            var aiFin = Ui.Btn("AI 整理合同收支", true, "coin");
            aiFin.Click += (s, e) => { Commit(); if (AiFinanceForm.Run(this, St, P)) Reload(); };
            secFinance.Controls.Add(SectionHead("财务", "", aiFin, add, addInv, imp, split, ex));

            var sm = Finance.Sum(St, P);
            var tiles = new TableLayoutPanel { Height = Ui.S(88), ColumnCount = 7, RowCount = 1, BackColor = Color.White, Margin = new Padding(0, 0, 0, Ui.S(10)) };
            Func<double, string> W = v => v > 0 ? (v / 10000).ToString("#,##0.00") + " 万" : "—";
            var defs = new[] {
                new object[] { "概算 / 可研投资", sm.Budget, Ui.Sub },
                new object[] { "合同金额", sm.ContractTotal, Ui.Violet },
                new object[] { "实际投资（决算）", sm.Actual, Ui.Teal },
                new object[] { "资金到账", sm.Income, Color.FromArgb(22, 150, 100) },
                new object[] { "已支付" + (sm.PaidBasis == "" ? "" : "（" + sm.PaidBasis + "）"), sm.Paid, Ui.Blue },
                new object[] { "已开票（" + sm.InvoiceCount + " 张）", sm.Invoiced, Color.FromArgb(200, 70, 60) },
                new object[] { "合同未付", sm.Unpaid, sm.Unpaid > 0 ? Ui.Orange : Ui.Sub } };
            var tip = new ToolTip();
            for (int i = 0; i < defs.Length; i++)
            {
                tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / defs.Length));
                var c = new Card { Dock = DockStyle.Fill, Flat = true, Margin = new Padding(0, 0, i < defs.Length - 1 ? Ui.S(10) : 0, 0), Fill = Color.FromArgb(248, 250, 252) };
                var a = Ui.Lbl((string)defs[i][0], 8.5f, false, Ui.Sub); a.Location = new Point(Ui.S(14), Ui.S(12));
                double v = (double)defs[i][1];
                var b = Ui.Lbl(defs[i][0].ToString() == "合同未付" && P.Contracts.Count > 0 && v == 0 ? "0" : W(v), 12.5f, true, (Color)defs[i][2]); b.Location = new Point(Ui.S(12), Ui.S(36));
                c.Controls.Add(a); c.Controls.Add(b);
                if (v > 0) { tip.SetToolTip(b, v.ToString("#,##0.00") + " 元"); tip.SetToolTip(c, v.ToString("#,##0.00") + " 元"); }
                tiles.Controls.Add(c, i, 0);
            }
            secFinance.Controls.Add(tiles);

            finBar = new Panel { Height = Ui.S(52), BackColor = Color.White, Margin = new Padding(0, 0, 0, Ui.S(8)) };
            finBar.Paint += (s, e) => PaintFinBar(e.Graphics);
            secFinance.Controls.Add(finBar);

            if (P.Payments.Count == 0)
            {
                var hint = new Card { Dashed = true, Height = Ui.S(56), Fill = Color.FromArgb(250, 251, 253) };
                var hl = Ui.Lbl("还没有收支明细。点“AI 整理合同收支”让 AI 从合同、凭证里整理，或“导入明细表”“记一笔”。", 9.5f, false, Ui.Sub);
                hl.Location = new Point(Ui.S(16), Ui.S(17)); hint.Controls.Add(hl);
                secFinance.Controls.Add(hint);
            }
            else
            {
                var sp = new Panel { Height = Ui.S(30), BackColor = Color.White };
                finStats = Ui.Lbl("", 9.5f, false, Ui.Sub); finStats.Location = new Point(0, Ui.S(4)); sp.Controls.Add(finStats);
                secFinance.Controls.Add(sp);
                payGrid.Rows.Clear();
                foreach (var pay in P.Payments)
                {
                    int r = payGrid.Rows.Add(pay.Date, pay.Dir, Finance.Short(P, pay.Summary), pay.Contract != "" ? pay.Contract : pay.Party, pay.Amount, pay.VoucherNo, "", "", pay.Note);
                    var row = payGrid.Rows[r]; row.Tag = pay;
                    SetLinkCell(row.Cells[PC_VF], pay.VoucherFile);
                    SetLinkCell(row.Cells[PC_IF], pay.InvoiceFile);
                    if (pay.Dir == "收入") row.Cells[PC_DIR].Style.ForeColor = Color.FromArgb(22, 150, 100);
                    row.Cells[PC_SUM].ToolTipText = pay.Summary + (pay.Source != "手工" ? "\n来源：" + pay.Source : "\n手工录入");
                }
                payGrid.Height = payGrid.ColumnHeadersHeight + payGrid.Rows.Count * payGrid.RowTemplate.Height + Ui.S(4);
                secFinance.Controls.Add(payGrid);
                FillFinanceStats();
            }

            var unl = Finance.Unlinked(St, P);
            if (unl.Count > 0)
            {
                var uh = new Panel { Height = Ui.S(42), BackColor = Color.White, Margin = new Padding(0, Ui.S(10), 0, 0) };
                var ut = Ui.Lbl("还没挂到收支记录上的凭证 / 发票文件", 10.5f, true, Ui.Text); ut.Location = new Point(0, Ui.S(10));
                var us = Ui.Lbl(unl.Count + " 份 · 在上表的“记账凭证 / 发票”格里点“＋ 关联”即可挂上", 8.5f, false, Ui.Sub); us.Location = new Point(ut.PreferredWidth + Ui.S(8), Ui.S(14));
                uh.Controls.Add(ut); uh.Controls.Add(us);
                secFinance.Controls.Add(uh);
                var g = new FileGrid(St, () => Reload(), P) { Files = unl, MaxItems = 6 };
                secFinance.Controls.Add(g);
                if (unl.Count > 6)
                {
                    var mp = new Panel { Height = Ui.S(30), BackColor = Color.White };
                    var more = new LinkLabel { Text = "展开全部 " + unl.Count + " 份 ↓", AutoSize = true, Font = Ui.F(9.5f), LinkBehavior = LinkBehavior.HoverUnderline, Location = new Point(0, Ui.S(4)) };
                    more.LinkClicked += (s, e) => { g.MaxItems = int.MaxValue; g.Relayout(); mp.Visible = false; secFinance.PerformLayout(); };
                    mp.Controls.Add(more); secFinance.Controls.Add(mp);
                }
            }
            anchors[2].Text = "财务" + (P.Payments.Count > 0 ? " " + P.Payments.Count : ""); anchors[2].Invalidate();
        }

        void SetLinkCell(DataGridViewCell cell, string rel)
        {
            var lc = (DataGridViewLinkCell)cell;
            var inv = rel == "" ? null : St.Lib.Invoices.FirstOrDefault(x => x.Rel == rel);
            if (rel != "" && File.Exists(Path.Combine(St.Lib.Root, rel)))
            {
                if (inv != null && inv.Members != null && inv.Members.Count > 0)
                {
                    lc.Value = "发票合集（" + inv.Members.Count + " 张）"; lc.LinkColor = Ui.Blue;
                    lc.ToolTipText = "合计 " + Ui.Money(inv.Amount) + " 元（" + inv.Members.Count + " 张发票合成一个 PDF）\n" + string.Join("\n", inv.Members.Select(Path.GetFileName)) + "\n单击打开 · 右键可更换";
                }
                else if (inv != null)
                {
                    lc.Value = "发票 " + Ui.Money(inv.Amount); lc.LinkColor = Ui.Blue;
                    lc.ToolTipText = (inv.Seller != "" ? inv.Seller + "\n" : "") + (inv.Date != "" ? "开票日期 " + inv.Date + "\n" : "") + "价税合计 " + Ui.Money(inv.Amount) + " 元\n拆自：" + inv.Source + " 第" + inv.Page + "页\n单击打开 · 右键可更换";
                }
                else { lc.Value = Path.GetFileName(rel); lc.LinkColor = Ui.Blue; lc.ToolTipText = rel + "\n单击打开 · 右键可更换"; }
            }
            else
            {
                lc.Value = "＋ 关联"; lc.LinkColor = Color.FromArgb(160, 168, 180); lc.ToolTipText = "单击选择对应的文件";
            }
        }

        void PaintFinBar(Graphics g)
        {
            var sm = Finance.Sum(St, P);
            double basis = sm.Actual > 0 ? sm.Actual : sm.ContractTotal > 0 ? sm.ContractTotal : sm.Budget;
            string basisName = sm.Actual > 0 ? "实际投资" : sm.ContractTotal > 0 ? "合同金额" : "概算";
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = finBar.Width, y = Ui.S(8);
            using (var f = Ui.F(9.5f, FontStyle.Bold)) TextRenderer.DrawText(g, "支付进度", f, new Point(0, y), Ui.Text);
            var bar = new Rectangle(Ui.S(80), y + Ui.S(5), w - Ui.S(420), Ui.S(10));
            using (var b = new SolidBrush(Color.FromArgb(232, 236, 241))) using (var gp = Ui.RoundPath(bar, Ui.S(5))) g.FillPath(b, gp);
            double ratio = basis > 0 ? Math.Min(1.0, sm.Paid / basis) : 0;
            if (ratio > 0)
                using (var b = new SolidBrush(ratio >= 0.999 ? Color.FromArgb(22, 163, 120) : Ui.Blue)) using (var gp = Ui.RoundPath(new Rectangle(bar.X, bar.Y, Math.Max(bar.Height, (int)(bar.Width * ratio)), bar.Height), Ui.S(5))) g.FillPath(b, gp);
            string txt = basis > 0 ? (sm.Paid / basis * 100).ToString("0.0") + "%（已付 ÷ " + basisName + "）" : "暂无金额依据";
            using (var f = Ui.F(9f)) TextRenderer.DrawText(g, txt, f, new Point(bar.Right + Ui.S(12), y), Ui.Sub);
            if (sm.PayCount > 0)
                using (var f = Ui.F(8.5f)) TextRenderer.DrawText(g, "记账凭证已关联 " + sm.WithVoucher + "/" + sm.PayCount + " 笔 · 发票已关联 " + sm.WithInvoice + "/" + sm.PayCount + " 笔", f, new Point(Ui.S(80), y + Ui.S(22)), Color.FromArgb(130, 138, 152));
        }

        void FillFinanceStats()
        {
            if (finStats == null || finStats.IsDisposed) return;
            var outs = P.Payments.Where(x => x.Dir != "收入").ToList(); var ins = P.Payments.Where(x => x.Dir == "收入").ToList();
            finStats.Text = "收支明细：支出 " + outs.Count + " 笔，合计 " + Ui.Money(outs.Sum(x => x.Amount)) + " 元" + (ins.Count > 0 ? "；收入（到账）" + ins.Count + " 笔，合计 " + Ui.Money(ins.Sum(x => x.Amount)) + " 元" : "") + "。单元格可直接修改，右键更多。";
            if (finBar != null) finBar.Invalidate();
        }

        void CommitPayRow(DataGridViewRow r)
        {
            var pay = r.Tag as Payment; if (pay == null) return;
            Func<int, string> sv = i => Convert.ToString(r.Cells[i].Value) ?? "";
            pay.Date = sv(PC_DATE).Trim();
            pay.Dir = sv(PC_DIR).Contains("收") ? "收入" : "支出";
            if (sv(PC_SUM) != Finance.Short(P, pay.Summary)) pay.Summary = sv(PC_SUM);   // 只在真正改过时才覆盖原摘要
            var con = sv(PC_CON);
            if (P.Contracts.Any(c => c.Kind == con)) { pay.Contract = con; var cc = P.Contracts.First(c => c.Kind == con); pay.Party = cc.Party; }
            else { pay.Contract = ""; pay.Party = con; }
            pay.Amount = D(r.Cells[PC_AMT].Value);
            pay.VoucherNo = sv(PC_VNO);
            pay.Note = sv(PC_NOTE);
        }

        void SplitInvoices(Button btn)
        {
            if (!Ui.Ask("从本项目的合同、支付凭证等扫描件里找出发票页，单独拆成 PDF（放在项目文件夹的“" + Invoices.OutFolder + "”里，原文件不动），并按凭证文件夹、金额自动关联到收支记录。\n\n需要逐页识别文字，可能要几分钟。现在开始吗？")) return;
            btn.Enabled = false;
            var cts = new System.Threading.CancellationTokenSource();
            System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                Invoices.Result r = null; string err = null;
                try { r = Invoices.Process(St, P, s => m.SetStatus(s), cts.Token); } catch (Exception ex) { err = ex.Message; }
                BeginInvoke(new Action(() =>
                {
                    btn.Enabled = true;
                    Reload();
                    var sm = Finance.Sum(St, P);
                    m.SetStatus(err != null ? "拆分发票出错：" + err : "拆分发票完成：查 " + r.Scanned + " 份文件 / " + r.Pages + " 页，找到发票 " + r.Found + " 张，新拆出 " + r.Written + " 份，新关联 " + r.Linked + " 笔；发票已关联 " + sm.WithInvoice + "/" + sm.PayCount + " 笔");
                }));
            });
        }

        void LinkFile(Payment pay, bool voucher)
        {
            var rel = FilePicker.Pick(this, St, P, voucher ? "选择这笔的记账凭证" : "选择这笔的发票", pay, voucher);
            if (rel == null) return;
            if (voucher) pay.VoucherFile = rel; else pay.InvoiceFile = rel;
            St.Save(); FillFinance();
        }

        // ---------- 合同台账
        void FillContracts()
        {
            Clear(secContracts);
            var add = Ui.Btn("添加合同", false, "plus");
            add.Click += (s, e) => { conGrid.Rows.Add("施工合同", "", 0.0, DateTime.Today.ToString("yyyy-MM-dd"), 0.0, 0.0, "", "", "", ""); CommitContracts(); St.Save(); FitGrid(); UpdateConTotal(); conGrid.CurrentCell = conGrid.Rows[conGrid.Rows.Count - 1].Cells[1]; conGrid.BeginEdit(true); };
            var del = Ui.Btn("删除选中", false);
            del.Click += (s, e) => { if (conGrid.CurrentRow != null && Ui.Ask("删除这一份合同记录？")) { conGrid.Rows.Remove(conGrid.CurrentRow); CommitContracts(); St.Save(); FitGrid(); UpdateConTotal(); } };
            var ex = Ui.Btn("导出 Excel", false, "excel");
            ex.Click += (s, e) => Exports.Contracts(this, St, new[] { P }, "合同台账_" + P.Code);
            var pdfx = Ui.Btn("导出合同PDF", false, "contract");
            pdfx.Click += (s, e) => { Commit(); m.ExportContractPdfs(new List<Project> { P }, P.Name + " 经济合同 PDF"); };
            secContracts.Controls.Add(SectionHead("合同台账", "单元格可直接修改；双击“来源文件”打开原件", add, del, ex, pdfx));
            var tp = new Panel { Height = Ui.S(30), BackColor = Color.White }; conTotal.Location = new Point(0, Ui.S(4)); tp.Controls.Add(conTotal);
            secContracts.Controls.Add(tp);
            conGrid.Rows.Clear();
            foreach (var c in P.Contracts) conGrid.Rows.Add(c.Kind, c.Party, c.Amount, c.Date, c.Paid, c.Unpaid, c.Final, c.Content, c.Note, c.Source);
            secContracts.Controls.Add(conGrid);
            FitGrid(); UpdateConTotal();
        }

        void FitGrid()
        {
            conGrid.Height = conGrid.ColumnHeadersHeight + Math.Max(1, conGrid.Rows.Count) * conGrid.RowTemplate.Height + Ui.S(4);
        }

        static double D(object v)
        {
            if (v == null) return 0;
            if (v is double) return (double)v;
            double d; double.TryParse(Convert.ToString(v).Replace(",", ""), NumberStyles.Any, CultureInfo.CurrentCulture, out d); return d;
        }

        void CommitContracts()
        {
            if (loading) return;
            var list = new List<Contract>();
            foreach (DataGridViewRow r in conGrid.Rows)
            {
                Func<int, string> sv = i => Convert.ToString(r.Cells[i].Value) ?? "";
                list.Add(new Contract { Kind = sv(0), Party = sv(1), Amount = D(r.Cells[2].Value), Date = sv(3), Paid = D(r.Cells[4].Value), Unpaid = D(r.Cells[5].Value), Final = sv(6), Content = sv(7), Note = sv(8), Source = sv(9) });
            }
            P.Contracts = list;
        }

        void UpdateConTotal()
        {
            conTotal.Text = P.Contracts.Count == 0 ? "还没有合同记录，点右上角“添加合同”。"
                : "共 " + P.Contracts.Count + " 份 · 合同金额 " + Ui.Money(P.Contracts.Sum(c => c.Amount)) + " 元 · 已付 " + Ui.Money(P.Contracts.Sum(c => c.Paid)) + " 元 · 未付 " + P.Contracts.Sum(c => c.Unpaid).ToString("#,##0.00") + " 元";
            anchors[3].Text = "合同台账 " + P.Contracts.Count; anchors[3].Invalidate();
        }

        // ---------- 全部资料
        void FillFilesSection(ProjectInfo info)
        {
            Clear(secFiles);
            var add = Ui.Btn("添加资料", false, "plus"); add.Click += (s, e) => AddFiles();
            int hiddenN = P.IsRoot ? 0 : St.HiddenOf(P).Count;
            var restore = Ui.Btn("恢复已移除的（" + hiddenN + "）", false, "refresh"); restore.Visible = hiddenN > 0;
            restore.Click += (s, e) => RestoreHidden();
            secFiles.Controls.Add(SectionHead("全部资料", info.FileCount + " 份 · 单击文件打开，右键可改分类；每一类右边可直接添加", restore, add));
            var filter = new Panel { Height = Ui.S(46), BackColor = Color.White, Margin = new Padding(0, 0, 0, Ui.S(10)) };
            chipRow.Dock = DockStyle.None; fileSearch.Dock = DockStyle.None;
            filter.Controls.Add(chipRow); filter.Controls.Add(fileSearch);
            filter.Resize += (s, e) =>
            {
                fileSearch.Location = new Point(filter.Width - fileSearch.Width, 0);
                int cw = Math.Max(Ui.S(200), filter.Width - fileSearch.Width - Ui.S(12));
                chipRow.SetBounds(0, 0, cw, filter.Height);
                int h = ChipHeight(cw);
                if (filter.Height != h) filter.Height = h;
            };
            secFiles.Controls.Add(filter);
            secFiles.Controls.Add(filesBox);
            LoadChips();
            FillFiles();
            anchors[4].Text = "全部资料 " + info.FileCount; anchors[4].Invalidate();
        }

        void LoadChips()
        {
            var info = St.Info(P);
            chipRow.SuspendLayout();
            foreach (Control x in chipRow.Controls.Cast<Control>().ToList()) { chipRow.Controls.Remove(x); x.Dispose(); }
            Action<string, string> chip = (key, text) =>
            {
                var b = (RoundButton)Ui.Btn(text, false);
                bool on = fileCat == key;
                b.Style = on ? RoundButton.Kind.Soft : RoundButton.Kind.Ghost;
                b.Font = Ui.F(9.5f, on ? FontStyle.Bold : FontStyle.Regular);
                b.Margin = new Padding(0, 0, Ui.S(2), 0); b.Padding = new Padding(Ui.S(10), Ui.S(4), Ui.S(10), Ui.S(4));
                b.Click += (s, e) => { fileCat = key; LoadChips(); FillFiles(); };
                chipRow.Controls.Add(b);
            };
            chip("", "全部");
            foreach (var c in Cats.All)
            {
                int k; info.CatCount.TryGetValue(c, out k);
                if (k == 0 && !P.Required.Contains(c)) continue;
                chip(c, Cats.Title(c).Split(' ')[0] + (k > 0 ? " " + k : " 缺"));
            }
            chipRow.ResumeLayout();
            if (chipRow.Parent != null) chipRow.Parent.Height = ChipHeight(Math.Max(Ui.S(300), chipRow.Parent.Width - fileSearch.Width - Ui.S(12)));
        }

        /// <summary>分类按钮按宽度换行后需要的高度。</summary>
        int ChipHeight(int width)
        {
            int x0 = 0, rows = 1;
            foreach (Control c in chipRow.Controls) { int cw = c.PreferredSize.Width + c.Margin.Horizontal; if (x0 + cw > width && x0 > 0) { rows++; x0 = 0; } x0 += cw; }
            return Math.Max(fileSearch.Height, rows * Ui.S(40) + Ui.S(6));
        }

        void FillFiles()
        {
            string q = fileSearch.Text.Trim();
            filesBox.SuspendLayout();
            foreach (Control x in filesBox.Controls.Cast<Control>().ToList()) { filesBox.Controls.Remove(x); x.Dispose(); }
            bool all = fileCat == "" && q == "";
            var parts = new List<Control>();
            foreach (var c in Cats.All)
            {
                if (fileCat != "" && c != fileCat) continue;
                var raw = Store.SortForDisplay(St.FilesOf(P).Where(f => f.Cat == c && (q == "" || f.Rel.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))).ToList();
                // 同名同大小的副本只显示一张
                var copies = new Dictionary<FileRec, int>();
                var firstOf = new Dictionary<string, FileRec>();
                var list = new List<FileRec>();
                foreach (var f in raw)
                {
                    string key = System.Text.RegularExpressions.Regex.Replace(f.Name, @"\(\d+\)(?=\.[^.]+$)", "") + "|" + f.Size;
                    FileRec first;
                    if (firstOf.TryGetValue(key, out first)) { copies[first] = (copies.ContainsKey(first) ? copies[first] : 0) + 1; continue; }
                    firstOf[key] = f; list.Add(f);
                }
                bool req = P.Required.Contains(c);
                if (list.Count == 0 && !(req && q == "")) continue;
                parts.Add(GroupHead(c, list.Count, req, raw.Count - list.Count));
                if (list.Count == 0)
                {
                    var hint = new Card { Dashed = true, Height = Ui.S(52), Fill = Color.FromArgb(255, 251, 242), Margin = new Padding(0, 0, 0, Ui.S(16)) };
                    var hl = Ui.Lbl("还没有这类资料。点右上“＋ 添加到这一类”，或把文件拖进窗口；如果已有但分错了类，在文件上右键 → 改为分类。", 9.5f, false, Ui.Orange);
                    hl.Location = new Point(Ui.S(16), Ui.S(15)); hint.Controls.Add(hl);
                    parts.Add(hint);
                    continue;
                }
                var grid = new FileGrid(St, () => { Reload(); }, P) { Files = list, Copies = copies, MaxItems = all ? 6 : int.MaxValue };
                parts.Add(grid);
                if (list.Count > grid.MaxItems)
                {
                    var mp = new Panel { Height = Ui.S(34), BackColor = Color.White, Margin = new Padding(0, 0, 0, Ui.S(10)) };
                    var more = new LinkLabel { Text = "展开全部 " + list.Count + " 份 ↓", AutoSize = true, Font = Ui.F(9.5f), LinkBehavior = LinkBehavior.HoverUnderline, Location = new Point(0, Ui.S(6)) };
                    var g = grid; more.LinkClicked += (s, e) => { g.MaxItems = int.MaxValue; g.Relayout(); mp.Visible = false; filesBox.PerformLayout(); };
                    mp.Controls.Add(more);
                    parts.Add(mp);
                }
                else parts.Add(Ui.Gap(14));
            }
            if (parts.Count == 0)
            {
                var np = new Panel { Height = Ui.S(50), BackColor = Color.White };
                var none = Ui.Lbl(q == "" ? "这个项目还没有资料。把文件拖进窗口即可。" : "没有找到包含“" + q + "”的文件。", 10.5f, false, Ui.Sub);
                none.Location = new Point(0, Ui.S(12)); np.Controls.Add(none);
                parts.Add(np);
            }
            filesBox.Controls.AddRange(parts.ToArray());
            filesBox.ResumeLayout(true);
        }

        Control GroupHead(string cat, int count, bool req, int dupCount)
        {
            var h = new Panel { Height = Ui.S(40), BackColor = Color.White };
            var t = Ui.Lbl(Cats.Title(cat), 11f, true, Ui.Text); t.Location = new Point(0, Ui.S(8));
            var n = Ui.Lbl(count > 0 ? count + " 份" + (dupCount > 0 ? "（另有 " + dupCount + " 份重复副本已合并）" : "") : (req ? "缺" : "0 份"), 9f, count == 0, count == 0 ? Ui.Orange : Ui.Sub);
            n.Location = new Point(t.PreferredWidth + Ui.S(8), Ui.S(12));
            h.Controls.Add(t); h.Controls.Add(n);
            var link = new LinkLabel { Text = fileCat == "" ? "只看这一类 ›" : "‹ 返回全部", AutoSize = true, Font = Ui.F(9f), LinkBehavior = LinkBehavior.HoverUnderline };
            link.LinkClicked += (s, e) => { fileCat = fileCat == "" ? cat : ""; LoadChips(); FillFiles(); };
            h.Controls.Add(link);
            var addTo = new LinkLabel { Text = "＋ 添加到这一类", AutoSize = true, Font = Ui.F(9f, FontStyle.Bold), LinkBehavior = LinkBehavior.HoverUnderline };
            addTo.LinkClicked += (s, e) =>
            {
                using (var d = new OpenFileDialog { Multiselect = true, Title = "添加到“" + Cats.Title(cat) + "”（可多选，压缩包会自动解压）" })
                    if (d.ShowDialog(this) == DialogResult.OK)
                    {
                        if (cat == "09 资金支付" && d.FileNames.Any(x => Path.GetFileName(x).Contains("发票") || Path.GetFileName(x).StartsWith("dzfp"))) AddInvoices(d.FileNames, null);
                        else AddFilesToCat(cat, d.FileNames);
                    }
            };
            h.Controls.Add(addTo);
            h.Resize += (s, e) => { link.Location = new Point(h.Width - link.Width, Ui.S(12)); addTo.Location = new Point(link.Left - addTo.Width - Ui.S(16), Ui.S(12)); };
            return h;
        }

        // ---------- 备注
        void FillNote()
        {
            Clear(secNote);
            secNote.Controls.Add(SectionHead("备注", "随手记：待办、联系人、特殊情况…"));
            note.Text = P.Note;
            secNote.Controls.Add(note);
        }

        // ================== 动作
        void AddFiles()
        {
            using (var d = new OpenFileDialog { Multiselect = true, Title = "选择要放进本项目的资料" })
                if (d.ShowDialog(this) == DialogResult.OK) { Commit(); m.Import(d.FileNames, P); }
        }

        /// <summary>把文件加到办理流程的某一步（固定为该分类）。</summary>
        void AddFilesToCat(string cat, string[] files)
        {
            Commit();
            Cursor = Cursors.WaitCursor;
            List<string> log;
            try { log = St.ImportToCategory(files, P, cat, s => m.SetStatus(s)); St.Scan(null); St.Save(); }
            finally { Cursor = Cursors.Default; }
            int ok = log.Count(x => x.StartsWith("✓") && !x.StartsWith("✓ 已解压")), dup = log.Count(x => x.StartsWith("·")), bad = log.Count(x => x.StartsWith("×"));
            Reload();
            m.SetStatus("已添加到“" + Cats.Title(cat) + "”：" + ok + " 份" + (dup > 0 ? "，已有 " + dup + " 份" : "") + (bad > 0 ? "，失败 " + bad + " 份" : ""));
            if (dup > 0 || bad > 0) Ui.Info(string.Join("\n", log.Where(x => !x.StartsWith("✓")).Take(15)));
            if (TextJob.Pending(St).Count > 0) m.StartOcr();
        }

        /// <summary>添加发票：放进“资金支付”，识别金额、日期、销售方并登记；重开的新票提示替换旧票。forPay 不为空时直接换到这一笔上。</summary>
        void AddInvoices(string[] files, Payment forPay)
        {
            Commit();
            Cursor = Cursors.WaitCursor;
            var msgs = new List<string>(); var recs = new List<InvoiceRec>(); List<string> rels;
            try
            {
                var log = St.ImportToCategory(files, P, "09 资金支付", s => m.SetStatus(s));
                rels = St.LastImported.ToList();
                msgs.AddRange(log.Where(x => x.StartsWith("×")));
                St.Scan(null);
                foreach (var rel in rels)
                {
                    m.SetStatus("正在识别发票：" + Path.GetFileName(rel));
                    var r = Invoices.RegisterFile(St, P, rel);
                    if (r != null) recs.Add(r);
                    else if (forPay == null) msgs.Add("· 没认出是发票（已作为资金支付资料保存）：" + Path.GetFileName(rel));
                }
            }
            finally { Cursor = Cursors.Default; }
            Func<InvoiceRec, string> desc = r => (r.Date != "" ? r.Date + " " : "") + (r.Seller != "" ? r.Seller + " " : "") + Ui.Money(r.Amount) + " 元" + (r.Number != "" ? "（号码 " + r.Number + "）" : "");
            if (forPay != null)
            {
                string rel = recs.Count > 0 ? recs[0].Rel : rels.FirstOrDefault();
                if (rel != null) { ProjectOps.SwapInvoice(St, P, forPay, rel); msgs.Insert(0, "✓ 这一笔已换上新发票：" + (recs.Count > 0 ? desc(recs[0]) : Path.GetFileName(rel)) + "\n  旧发票已从项目里移除（文件保留，可在“全部资料 → 恢复已移除的”找回）。"); }
            }
            else
                foreach (var r in recs)
                {
                    var old = ProjectOps.ReissueOf(St, P, r);
                    if (old != null && Ui.Ask("新发票：" + desc(r) + "\n\n看起来是这张旧发票的重开票：\n" + desc(old) + "\n\n用新发票替换旧发票吗？\n（旧发票从项目里移除，文件保留，可随时恢复；挂在收支上的也会换成新票）"))
                    {
                        foreach (var pay in P.Payments.Where(x => x.InvoiceFile == old.Rel)) pay.InvoiceFile = r.Rel;
                        ProjectOps.RetireInvoice(St, P, old.Rel);
                        msgs.Add("✓ 已用新发票替换旧发票：" + desc(r));
                    }
                    else msgs.Add("✓ 已登记发票：" + desc(r));
                }
            Invoices.Link(St, P);
            St.Save();
            Reload();
            m.SetStatus("发票：登记 " + recs.Count + " 张");
            if (msgs.Count > 0) Ui.Info(string.Join("\n", msgs.Take(20)));
        }

        /// <summary>恢复本项目里之前“移除”的资料。</summary>
        void RestoreHidden()
        {
            var list = St.HiddenOf(P);
            if (list.Count == 0) { Ui.Info("这个项目没有被移除的资料。"); return; }
            using (var f = new Form { Text = "恢复已移除的资料", ShowIcon = false, ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false, StartPosition = FormStartPosition.CenterParent, Font = Ui.F(9.5f), ClientSize = new Size(Ui.S(720), Ui.S(440)), BackColor = Color.White })
            {
                var tip = new Label { Text = "勾选要恢复到本项目的资料（这些文件一直都在，只是之前从项目里移除了）：", Dock = DockStyle.Top, Height = Ui.S(34), Padding = new Padding(Ui.S(10), Ui.S(10), 0, 0) };
                var cl = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, BorderStyle = BorderStyle.None };
                foreach (var r in list) cl.Items.Add(r, true);
                var row = Ui.Row(); row.Dock = DockStyle.Bottom; row.Height = Ui.S(52); row.Padding = new Padding(Ui.S(10), Ui.S(8), 0, 0);
                var ok = Ui.Btn("恢复所选", true, "refresh"); ok.Click += (s, e) => f.DialogResult = DialogResult.OK;
                var cancel = Ui.Btn("取消", false); cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                row.Controls.AddRange(new Control[] { ok, cancel });
                f.Controls.Add(cl); f.Controls.Add(row); f.Controls.Add(tip);
                if (f.ShowDialog(this) != DialogResult.OK || cl.CheckedItems.Count == 0) return;
                foreach (string r in cl.CheckedItems) St.Unhide(r, P);
            }
            St.Scan(null); Invoices.Link(St, P); St.Save(); Reload();
            m.SetStatus("已恢复移除的资料");
        }

        public void JumpTo(string anchor)
        {
            Control target = anchor == "概况" ? secOverview : anchor == "关键信息" ? secInfo : anchor.StartsWith("财务") ? secFinance : anchor.StartsWith("合同") ? secContracts : anchor.StartsWith("全部资料") ? secFiles : secNote;
            scroller.AutoScrollPosition = new Point(0, Math.Max(0, target.Top - Ui.S(4)));
            UpdateAnchors();
        }

        void UpdateAnchors()
        {
            if (!Visible) return;
            int y = -scroller.AutoScrollPosition.Y + Ui.S(40);
            var secs = new Control[] { secOverview, secInfo, secFinance, secContracts, secFiles, secNote };
            int cur = 0;
            for (int i = 0; i < secs.Length; i++) if (secs[i].Top <= y) cur = i;
            if (scroller.VerticalScroll.Visible && -scroller.AutoScrollPosition.Y >= scroller.VerticalScroll.Maximum - scroller.VerticalScroll.LargeChange - 2) cur = secs.Length - 1;
            for (int i = 0; i < anchors.Count; i++)
            {
                var st = i == cur ? RoundButton.Kind.Soft : RoundButton.Kind.Ghost;
                if (anchors[i].Style != st) { anchors[i].Style = st; anchors[i].Font = Ui.F(9.5f, i == cur ? FontStyle.Bold : FontStyle.Regular); anchors[i].Invalidate(); }
            }
        }

        public void Commit()
        {
            if (loading) return;
            if (conGrid.IsCurrentCellInEditMode) conGrid.EndEdit();
            CommitContracts();
            if (!P.IsRoot)
            {
                if (name.Text.Trim() != "") P.Name = name.Text.Trim();
                P.Code = code.Text.Trim(); P.Type = type.Text.Trim();
                P.Stage = stage.Text == "（自动判断）" ? "" : stage.Text.Trim();
            }
            P.Note = note.Text;
            St.Save();
        }
    }

    /// <summary>编辑全部栏目（弹窗）。</summary>
    public static class FieldEditor
    {
        public static bool Edit(IWin32Window owner, Project p)
        {
            using (var f = new Form())
            {
                f.Text = "编辑栏目 — " + p.Name; f.Font = Ui.F(9.5f); f.BackColor = Color.White; f.StartPosition = FormStartPosition.CenterParent;
                f.Size = new Size(Ui.S(1100), Ui.S(720)); f.MinimizeBox = false; f.ShowIcon = false;
                var g = Ui.Grid(); g.AllowUserToAddRows = false;
                g.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "栏目", Width = Ui.S(200) });
                var v = new DataGridViewTextBoxColumn { HeaderText = "内容", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 70 };
                v.DefaultCellStyle.WrapMode = DataGridViewTriState.True; g.Columns.Add(v);
                var s2 = new DataGridViewTextBoxColumn { HeaderText = "依据 / 来源文件", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 30 };
                s2.DefaultCellStyle.ForeColor = Ui.Sub; g.Columns.Add(s2);
                g.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
                foreach (var fl in p.Fields) g.Rows.Add(fl.Name, fl.Value, fl.Source);
                var bar = Ui.Row(); bar.Dock = DockStyle.Top; bar.Padding = new Padding(Ui.S(10), Ui.S(8), 0, Ui.S(4));
                var add = Ui.Btn("添加栏目", false, "plus"); add.Click += (a, b) => { g.Rows.Add("新栏目", "", ""); g.CurrentCell = g.Rows[g.Rows.Count - 1].Cells[0]; g.BeginEdit(true); };
                var del = Ui.Btn("删除栏目", false); del.Click += (a, b) => { if (g.CurrentRow != null) g.Rows.Remove(g.CurrentRow); };
                bar.Controls.AddRange(new Control[] { add, del });
                var bottom = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(60), Padding = new Padding(Ui.S(12), Ui.S(10), Ui.S(16), Ui.S(10)) };
                var r = Ui.Row(); r.Dock = DockStyle.Right;
                var ok = Ui.Btn("保存", true, "check"); ok.DialogResult = DialogResult.OK;
                var no = Ui.Btn("取消", false); no.DialogResult = DialogResult.Cancel;
                r.Controls.AddRange(new Control[] { no, ok }); bottom.Controls.Add(r);
                g.Dock = DockStyle.Fill;
                f.Controls.Add(g); f.Controls.Add(bar); f.Controls.Add(bottom);
                g.BringToFront();
                if (f.ShowDialog(owner) != DialogResult.OK) return false;
                if (g.IsCurrentCellInEditMode) g.EndEdit();
                var list = new List<Field>();
                foreach (DataGridViewRow row in g.Rows)
                {
                    string n = Convert.ToString(row.Cells[0].Value).Trim();
                    if (n != "") list.Add(new Field(n, Convert.ToString(row.Cells[1].Value), Convert.ToString(row.Cells[2].Value)));
                }
                p.Fields = list;
                return true;
            }
        }
    }

    /// <summary>调整“应有资料清单”（弹窗）。</summary>
    public static class RequiredEditor
    {
        public static bool Edit(IWin32Window owner, Project p)
        {
            using (var f = new Form())
            {
                f.Text = "本项目应当具备的资料"; f.Font = Ui.F(10f); f.BackColor = Color.White; f.StartPosition = FormStartPosition.CenterParent;
                f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MinimizeBox = false; f.MaximizeBox = false; f.ShowIcon = false;
                f.ClientSize = new Size(Ui.S(420), Ui.S(470));
                var l = Ui.Lbl("勾选的类别如果一份都没有，就会提示“待补”。", 9.5f, false, Ui.Sub); l.Location = new Point(Ui.S(16), Ui.S(14));
                var cl = new CheckedListBox { CheckOnClick = true, BorderStyle = BorderStyle.None, Location = new Point(Ui.S(16), Ui.S(46)), Size = new Size(Ui.S(388), Ui.S(350)), IntegralHeight = false };
                foreach (var c in Cats.All) cl.Items.Add(Cats.Title(c), p.Required.Contains(c));
                var ok = Ui.Btn("确定", true); ok.DialogResult = DialogResult.OK; ok.Location = new Point(Ui.S(220), Ui.S(410));
                var no = Ui.Btn("取消", false); no.DialogResult = DialogResult.Cancel; no.Location = new Point(Ui.S(318), Ui.S(410));
                f.Controls.AddRange(new Control[] { l, cl, ok, no });
                if (f.ShowDialog(owner) != DialogResult.OK) return false;
                p.Required = new List<string>();
                for (int i = 0; i < Cats.All.Length; i++) if (cl.GetItemChecked(i)) p.Required.Add(Cats.All[i]);
                return true;
            }
        }
    }

    public static class Exports
    {
        public static void Payments(IWin32Window owner, Store st, Project p)
        {
            using (var d = new SaveFileDialog { Filter = "Excel 文件|*.xlsx", FileName = "收支明细_" + p.Code + "_" + DateTime.Now.ToString("yyyyMMdd") + ".xlsx" })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return;
                var rows = new List<IList<object>>();
                int i = 0;
                foreach (var x in p.Payments)
                    rows.Add(new List<object> { ++i, x.Date, x.Dir, x.Summary, x.Contract, x.Party, x.Amount, x.VoucherNo, x.VoucherFile, x.InvoiceFile, x.Note, x.Source });
                var sm = Finance.Sum(st, p);
                rows.Add(new List<object> { "", "", "", "支出合计", "", "", p.Payments.Where(x => x.Dir != "收入").Sum(x => x.Amount), "", "", "", "", "" });
                rows.Add(new List<object> { "", "", "", "收入（到账）合计", "", "", sm.Income, "", "", "", "", "" });
                Store.WriteXlsx(d.FileName, "收支明细", new[] { "序号", "日期", "收/支", "摘要", "关联合同", "对方单位", "金额（元）", "记账凭证号", "凭证文件", "发票文件", "备注", "来源" }, rows);
                if (Ui.Ask("已导出 " + p.Payments.Count + " 笔收支。现在打开吗？")) Ui.Open(d.FileName);
            }
        }

        public static void Contracts(IWin32Window owner, Store st, IEnumerable<Project> projects, string fileName)
        {
            using (var d = new SaveFileDialog { Filter = "Excel 文件|*.xlsx", FileName = fileName + "_" + DateTime.Now.ToString("yyyyMMdd") + ".xlsx" })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return;
                var rows = new List<IList<object>>();
                int i = 0;
                foreach (var p in projects)
                    foreach (var c in p.Contracts)
                        rows.Add(new List<object> { ++i, p.Name, c.Kind, c.Party, c.Amount, c.Date, c.Paid, c.Unpaid, c.Final, c.Content, c.Note, c.Source });
                Store.WriteXlsx(d.FileName, "合同台账", new[] { "序号", "项目", "合同类型", "合同相对方", "合同金额（元）", "签订日期", "已付（元）", "未付（元）", "审定/决算", "合同内容", "备注", "来源文件" }, rows);
                if (Ui.Ask("已导出 " + rows.Count + " 份合同。现在打开吗？")) Ui.Open(d.FileName);
            }
        }
    }
}
