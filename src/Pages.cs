using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DangAn
{
    // ===================================================== 全文查资料
    public class SearchPage : Panel, IPage
    {
        readonly MainForm m; Store St { get { return m.St; } }
        TextBox box; ComboBox projBox, catBox; CheckBox nameOnly; ListView lv; RichTextBox preview; Label info;
        public string Crumb { get { return "项目档案室  ›  全文查资料"; } }

        public SearchPage(MainForm m)
        {
            this.m = m; BackColor = Ui.Bg;
            var head = new Panel { Height = Ui.S(64) };
            var t1 = Ui.Lbl("全文查资料", 19f, true, Ui.Text); t1.Location = new Point(0, 0);
            info = Ui.Lbl("", 9.5f, false, Ui.Sub); info.Location = new Point(Ui.S(2), Ui.S(38));
            head.Controls.AddRange(new Control[] { t1, info });

            var bar = new Card { Height = Ui.S(128), Padding = new Padding(Ui.S(14), Ui.S(12), Ui.S(14), Ui.S(8)) };
            var sb = new SearchBox("输入关键词，多个词用空格隔开，例如：石致路 中标通知书   或   涪交函 决算批复", 12.5f) { Location = new Point(Ui.S(20), Ui.S(18)) };
            box = sb.Box;
            box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Run(null); } };
            var go = Ui.Btn("搜索", true, "search"); go.AutoSize = false; go.Size = new Size(Ui.S(110), sb.Height); go.Click += (s, e) => Run(null);
            var row = Ui.Row(); row.Location = new Point(Ui.S(18), Ui.S(18) + sb.Height + Ui.S(12));
            projBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.S(360), Font = Ui.F(9.5f) };
            catBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.S(180), Font = Ui.F(9.5f) };
            nameOnly = new CheckBox { Text = "只搜文件名", AutoSize = true, Margin = new Padding(Ui.S(12), Ui.S(6), 0, 0) };
            var lp = Ui.Lbl("项目", 9.5f, false, Ui.Sub); lp.Margin = new Padding(0, Ui.S(6), Ui.S(4), 0);
            var lc = Ui.Lbl("分类", 9.5f, false, Ui.Sub); lc.Margin = new Padding(Ui.S(12), Ui.S(6), Ui.S(4), 0);
            row.Controls.AddRange(new Control[] { lp, projBox, lc, catBox, nameOnly });
            projBox.SelectedIndexChanged += (s, e) => { if (box.Text.Trim().Length > 0) Run(null); };
            catBox.SelectedIndexChanged += (s, e) => { if (box.Text.Trim().Length > 0) Run(null); };
            nameOnly.CheckedChanged += (s, e) => { if (box.Text.Trim().Length > 0) Run(null); };
            bar.Controls.AddRange(new Control[] { sb, go, row });
            bar.Resize += (s, e) => { go.Location = new Point(bar.Width - go.Width - Ui.S(22), Ui.S(18)); sb.Width = go.Left - Ui.S(30); };

            lv = Ui.List("文件名", 320, "项目", 260, "分类", 130, "命中内容", 700);
            lv.ShowItemToolTips = true;
            FileMenu.Attach(lv, St, () => Run(null));
            lv.SelectedIndexChanged += (s, e) => ShowPreview();
            preview = new RichTextBox { ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = Ui.F(10.5f), DetectUrls = false };
            var pbar = Ui.Row(); pbar.Dock = DockStyle.Top;
            var bo = Ui.Btn("打开文件", true, "open"); bo.Click += (s, e) => { var f = FileMenu.Selected(lv).FirstOrDefault(); if (f != null) Ui.Open(St.FullPath(f)); };
            var br = Ui.Btn("打开所在文件夹", false, "folder"); br.Click += (s, e) => { var f = FileMenu.Selected(lv).FirstOrDefault(); if (f != null) Ui.Reveal(St.FullPath(f)); };
            var bc = Ui.Btn("复制选中文字", false, "copy"); bc.Click += (s, e) => { if (preview.SelectedText.Length > 0) Clipboard.SetText(preview.SelectedText); };
            pbar.Controls.AddRange(new Control[] { bo, br, bc });
            var split = new SplitContainer { Orientation = Orientation.Horizontal, BackColor = Ui.Bg, SplitterWidth = Ui.S(12) };
            var c1 = new Panel { Dock = DockStyle.Fill, BackColor = Color.White }; c1.Controls.Add(lv); lv.Dock = DockStyle.Fill;
            var c2 = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(Ui.S(12), Ui.S(8), Ui.S(12), Ui.S(8)) };
            Ui.Stack(c2, preview, pbar);
            split.Panel1.Controls.Add(c1); split.Panel2.Controls.Add(c2);
            Ui.Stack(this, split, head, bar, Ui.Gap(10));
            Ui.SplitAt(split, 300);
        }

        public void Reload()
        {
            Tag = null;
            info.Text = "在 " + St.Lib.Files.Count + " 份资料的文件名，以及其中 " + St.Texts.Count + " 份已识别出的文字里查找。扫描件需先识别文字（后台自动进行）。";
            var keepP = projBox.SelectedItem; var keepC = catBox.SelectedItem;
            projBox.Items.Clear(); projBox.Items.Add("全部项目");
            foreach (var p in St.Lib.Projects) projBox.Items.Add(p);
            projBox.DisplayMember = "Name";
            projBox.SelectedItem = keepP != null && projBox.Items.Contains(keepP) ? keepP : projBox.Items[0];
            catBox.Items.Clear(); catBox.Items.Add("全部分类"); foreach (var c in Cats.All) catBox.Items.Add(c);
            catBox.SelectedItem = keepC ?? "全部分类";
            BeginInvoke(new Action(() => { box.Focus(); box.SelectAll(); }));
        }

        public void Run(string q)
        {
            if (q != null) box.Text = q;
            q = box.Text.Trim();
            if (q.Length == 0) return;
            var p = projBox.SelectedItem as Project;
            string c = catBox.SelectedIndex > 0 ? (string)catBox.SelectedItem : null;
            var hits = St.Search(q, p == null ? null : p.Id, c, nameOnly.Checked, 1000);
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var h in hits)
            {
                var pr = St.Proj(h.File.Project);
                var it = new ListViewItem(h.File.Name) { Tag = h };
                it.SubItems.Add(pr == null ? "" : pr.Name); it.SubItems.Add(Cats.Short(h.File.Cat)); it.SubItems.Add(h.Snippet);
                it.ToolTipText = h.File.Rel;
                lv.Items.Add(it);
            }
            lv.EndUpdate();
            info.Text = "“" + q + "”找到 " + hits.Count + " 份" + (hits.Count >= 1000 ? "（只显示前 1000 份）" : "") + "。单击看内容，双击打开原文件。";
            preview.Clear();
            if (lv.Items.Count > 0) lv.Items[0].Selected = true;
        }

        void ShowPreview()
        {
            var f = FileMenu.Selected(lv).FirstOrDefault();
            if (f == null) return;
            string text;
            lock (St.Sync) St.Texts.TryGetValue(f.Rel, out text);
            preview.SuspendLayout();
            preview.Clear();
            preview.SelectionFont = Ui.F(10.5f, FontStyle.Bold);
            preview.AppendText(f.Rel + "\n");
            preview.SelectionFont = Ui.F(10.5f);
            if (string.IsNullOrEmpty(text)) { preview.AppendText("\n（这份资料还没有可搜的文字。双击可直接打开查看。）"); preview.ResumeLayout(); return; }
            if (text.Length > 20000) text = text.Substring(0, 20000) + "\n……";
            int start = preview.TextLength;
            preview.AppendText("\n" + text);
            var toks = box.Text.Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries);
            int first = -1;
            foreach (var t in toks)
            {
                int i = start;
                while ((i = preview.Find(t, i, RichTextBoxFinds.None)) >= 0)
                {
                    preview.SelectionBackColor = Color.FromArgb(255, 230, 120);
                    if (first < 0 || i < first) first = i;
                    i += t.Length;
                    if (i >= preview.TextLength) break;
                }
            }
            preview.SelectionStart = first >= 0 ? first : 0; preview.SelectionLength = 0;
            preview.ScrollToCaret();
            preview.ResumeLayout();
        }
    }

    // ===================================================== 智能填表
    public class FillPage : Panel, IPage
    {
        readonly MainForm m; Store St { get { return m.St; } }
        CheckedListBox projList, fieldList; DataGridView grid;
        static readonly string[] Computed = { "序号", "项目名称", "档号", "项目类型", "办理进展", "资料份数", "缺少的资料", "合同份数", "合同金额合计（元）", "已付金额（元）", "未付金额（元）", "施工合同金额（元）", "施工单位", "监理单位", "设计单位", "资金到账（元）", "已支付（元）", "支付进度", "资料所在文件夹" };
        public string Crumb { get { return "项目档案室  ›  智能填表"; } }

        public FillPage(MainForm m)
        {
            this.m = m; BackColor = Ui.Bg;
            var head = new Panel { Height = Ui.S(64) };
            var t1 = Ui.Lbl("智能填表", 19f, true, Ui.Text); t1.Location = new Point(0, 0);
            var t2 = Ui.Lbl("上级发来的表格：点右边“AI 填上级报表”，自动对照档案填写空白格。自己汇总：勾选项目和栏目 → 生成表格 → 复制或导出 Excel。", 9.5f, false, Ui.Sub); t2.Location = new Point(Ui.S(2), Ui.S(38));
            head.Controls.AddRange(new Control[] { t1, t2 });

            var pick = new TableLayoutPanel { Height = Ui.S(300), ColumnCount = 3, RowCount = 1 };
            pick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); pick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); pick.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(150)));
            projList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false, Font = Ui.F(9.5f) };
            fieldList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false, Font = Ui.F(9.5f) };
            var pp = Box("① 选项目", projList, new[] { "全选", "全不选", "只选有合同的" });
            var fp = Box("② 选栏目（按报表列顺序）", fieldList, new[] { "上移", "下移", "清空" });
            var ops = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(Ui.S(8), Ui.S(30), 0, 0) };
            var bGen = Ui.Btn("③ 生成表格", true, "table"); bGen.Width = Ui.S(130); bGen.AutoSize = false; bGen.Height = Ui.S(38);
            var bCopy = Ui.Btn("复制到剪贴板", false, "copy"); bCopy.AutoSize = false; bCopy.Width = Ui.S(130); bCopy.Height = Ui.S(36);
            var bXls = Ui.Btn("导出 Excel", false, "excel"); bXls.AutoSize = false; bXls.Width = Ui.S(130); bXls.Height = Ui.S(36);
            var bAi = Ui.Btn("AI 填上级报表…", true, "excel"); bAi.AutoSize = false; bAi.Width = Ui.S(130); bAi.Height = Ui.S(38);
            bAi.Click += (s, e) => AiFillForm.Start(this, St);
            bGen.Click += (s, e) => Generate(); bCopy.Click += (s, e) => CopyGrid(); bXls.Click += (s, e) => ExportGrid();
            ops.Padding = new Padding(Ui.S(8), Ui.S(4), 0, 0);
            ops.Controls.AddRange(new Control[] { bAi, Ui.Gap(14), bGen, bCopy, bXls, Ui.Lbl("表格里也可直接改，\n改动只影响本次填表。", 8.5f, false, Ui.Sub) });
            pick.Controls.Add(pp, 0, 0); pick.Controls.Add(fp, 1, 0); pick.Controls.Add(ops, 2, 0);

            grid = Ui.Grid();
            grid.AllowUserToAddRows = false;
            grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
            var gc = new Card { Padding = new Padding(Ui.S(2)) }; grid.Dock = DockStyle.Fill; gc.Controls.Add(grid);
            Ui.Stack(this, gc, head, pick, Ui.Gap(10));
        }

        Control Box(string title, Control list, string[] buttons)
        {
            var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, Ui.S(12), 0) };
            var l = new Label { Text = title, Height = Ui.S(28), Font = Ui.F(10f, FontStyle.Bold), ForeColor = Ui.Text, TextAlign = ContentAlignment.MiddleLeft };
            var r = Ui.Row(); r.Dock = DockStyle.Bottom;
            foreach (var b in buttons)
            {
                var btn = Ui.Btn(b, false); string bb = b;
                btn.Click += (s, e) => Action(bb);
                r.Controls.Add(btn);
            }
            p.Controls.Add(r);
            Ui.Stack(p, list, l);
            return p;
        }

        void Action(string a)
        {
            if (a == "全选") for (int i = 0; i < projList.Items.Count; i++) projList.SetItemChecked(i, true);
            if (a == "全不选") for (int i = 0; i < projList.Items.Count; i++) projList.SetItemChecked(i, false);
            if (a == "只选有合同的") for (int i = 0; i < projList.Items.Count; i++) projList.SetItemChecked(i, ((Project)projList.Items[i]).Contracts.Count > 0);
            if (a == "清空") for (int i = 0; i < fieldList.Items.Count; i++) fieldList.SetItemChecked(i, false);
            if (a == "上移" || a == "下移")
            {
                int i = fieldList.SelectedIndex, j = a == "上移" ? i - 1 : i + 1;
                if (i < 0 || j < 0 || j >= fieldList.Items.Count) return;
                var item = fieldList.Items[i]; bool chk = fieldList.GetItemChecked(i);
                fieldList.Items.RemoveAt(i); fieldList.Items.Insert(j, item); fieldList.SetItemChecked(j, chk); fieldList.SelectedIndex = j;
            }
        }

        public void Reload()
        {
            Tag = null;
            var checkedP = new HashSet<string>(projList.CheckedItems.Cast<Project>().Select(p => p.Id));
            projList.Items.Clear();
            foreach (var p in St.RealProjects) projList.Items.Add(p, checkedP.Count == 0 || checkedP.Contains(p.Id));
            projList.DisplayMember = "Name";
            var names = new List<string>();
            foreach (var f in St.Lib.FillFields) if (!names.Contains(f)) names.Add(f);
            foreach (var f in Computed) if (!names.Contains(f)) names.Add(f);
            foreach (var p in St.RealProjects) foreach (var f in p.Fields) if (!names.Contains(f.Name)) names.Add(f.Name);
            fieldList.Items.Clear();
            var sel = St.Lib.FillFields.Count > 0 ? St.Lib.FillFields : new List<string> { "序号", "项目名称", "办理进展", "施工单位", "合同金额合计（元）", "实际开工时间", "实际竣工／完工时间" };
            foreach (var n in names) fieldList.Items.Add(n, sel.Contains(n));
        }

        string Value(Project p, string field, int idx)
        {
            var info = St.Info(p);
            Func<string, Contract> con = k => p.Contracts.FirstOrDefault(c => c.Kind.Contains(k));
            switch (field)
            {
                case "序号": return idx.ToString();
                case "项目名称": return p.Name;
                case "档号": return p.Code;
                case "项目类型": return p.Type;
                case "办理进展": return info.Stage;
                case "资料份数": return info.FileCount.ToString();
                case "缺少的资料": return info.Missing.Count == 0 ? "无" : string.Join("、", info.Missing.Select(Cats.Short));
                case "合同份数": return p.Contracts.Count.ToString();
                case "合同金额合计（元）": return info.ContractTotal == 0 ? "" : info.ContractTotal.ToString("0.00");
                case "已付金额（元）": return info.PaidTotal == 0 ? "" : info.PaidTotal.ToString("0.00");
                case "未付金额（元）": return info.UnpaidTotal.ToString("0.00");
                case "施工合同金额（元）": { var c = con("施工"); return c == null ? "" : c.Amount.ToString("0.00"); }
                case "施工单位": { var c = con("施工"); var v = c == null ? "" : c.Party; return v != "" ? v : p.Get("中标／施工单位"); }
                case "监理单位": { var c = con("监理"); return c == null ? "" : c.Party; }
                case "设计单位": { var c = con("设计"); return c == null ? "" : c.Party; }
                case "资金到账（元）": { var s = Finance.Sum(St, p); return s.Income == 0 ? "" : s.Income.ToString("0.00"); }
                case "已支付（元）": { var s = Finance.Sum(St, p); return s.Paid == 0 ? "" : s.Paid.ToString("0.00"); }
                case "支付进度":
                    {
                        var s = Finance.Sum(St, p);
                        double b = s.Actual > 0 ? s.Actual : s.ContractTotal > 0 ? s.ContractTotal : s.Budget;
                        return b > 0 ? (s.Paid / b * 100).ToString("0.0") + "%" : "";
                    }
                case "资料所在文件夹": return string.Join("；", p.Folders.Select(f => Path.Combine(St.Lib.Root, f)));
            }
            return p.Get(field);
        }

        void Generate()
        {
            var fields = new List<string>();
            for (int i = 0; i < fieldList.Items.Count; i++) if (fieldList.GetItemChecked(i)) fields.Add((string)fieldList.Items[i]);
            if (fields.Count == 0) { Ui.Info("请先勾选要填的栏目。"); return; }
            St.Lib.FillFields = fields; St.Save();
            grid.Columns.Clear(); grid.Rows.Clear();
            foreach (var f in fields)
            {
                int w = f == "序号" ? 50 : f.Contains("名称") ? 300 : f.Contains("说明") || f.Contains("内容") || f.Contains("情况") ? 360 : 160;
                var col = new DataGridViewTextBoxColumn { HeaderText = f, Width = Ui.S(w), SortMode = DataGridViewColumnSortMode.NotSortable };
                col.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
                grid.Columns.Add(col);
            }
            int idx = 0;
            foreach (Project p in projList.CheckedItems)
            {
                idx++;
                grid.Rows.Add(fields.Select(f => (object)Value(p, f, idx)).ToArray());
            }
        }

        void CopyGrid()
        {
            if (grid.Columns.Count == 0) Generate();
            if (grid.Columns.Count == 0) return;
            var sb = new StringBuilder();
            sb.AppendLine(string.Join("\t", grid.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText)));
            foreach (DataGridViewRow r in grid.Rows)
                sb.AppendLine(string.Join("\t", r.Cells.Cast<DataGridViewCell>().Select(c => Clean(Convert.ToString(c.Value)))));
            Clipboard.SetText(sb.ToString());
            Ui.Info("已复制 " + grid.Rows.Count + " 行（含标题行）。到 Excel / WPS 报表里点中起始单元格，按 Ctrl+V 粘贴即可。\n如果报表已有标题，粘贴后删掉第一行。");
        }

        static string Clean(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\t", " "); }

        void ExportGrid()
        {
            if (grid.Columns.Count == 0) Generate();
            if (grid.Columns.Count == 0) return;
            using (var d = new SaveFileDialog { Filter = "Excel 文件|*.xlsx", FileName = "填报表_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".xlsx" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var headers = grid.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText).ToList();
                var rows = new List<IList<object>>();
                foreach (DataGridViewRow r in grid.Rows)
                {
                    var row = new List<object>();
                    for (int i = 0; i < headers.Count; i++)
                    {
                        string v = Convert.ToString(r.Cells[i].Value); double dv;
                        if ((headers[i].Contains("（元）") || headers[i] == "序号" || headers[i].Contains("份数")) && double.TryParse(v, out dv)) row.Add(dv); else row.Add(v);
                    }
                    rows.Add(row);
                }
                Store.WriteXlsx(d.FileName, "填报表", headers, rows);
                if (Ui.Ask("已导出。现在打开吗？")) Ui.Open(d.FileName);
            }
        }
    }

    // ===================================================== 设置与备份
    public class SettingsPage : Panel, IPage
    {
        readonly MainForm m; Store St { get { return m.St; } }
        Label rootLbl, dbLbl, backupLbl, aiLbl; TextBox excl; CheckBox autoBk; ListView plv;
        public string Crumb { get { return "项目档案室  ›  设置与备份"; } }

        public SettingsPage(MainForm m)
        {
            this.m = m; BackColor = Ui.Bg; AutoScroll = true;
            var head = new Panel { Height = Ui.S(56) };
            var t1 = Ui.Lbl("设置与备份", 19f, true, Ui.Text); t1.Location = new Point(0, 0);
            head.Controls.Add(t1);

            // 档案位置
            var loc = new Card { Height = Ui.S(168) };
            var lt = Ui.Lbl("档案位置", 11f, true, Ui.Text); lt.Location = new Point(Ui.S(16), Ui.S(12));
            rootLbl = Ui.Lbl("", 9.5f, false, Ui.Text); rootLbl.Location = new Point(Ui.S(16), Ui.S(44));
            dbLbl = Ui.Lbl("", 9.5f, false, Ui.Sub); dbLbl.Location = new Point(Ui.S(16), Ui.S(70));
            var r1 = Ui.Row(); r1.Location = new Point(Ui.S(12), Ui.S(100));
            var bOpenRoot = Ui.SoftBtn("打开档案文件夹"); bOpenRoot.Click += (s, e) => Ui.Reveal(St.Lib.Root);
            var bOpenDb = Ui.SoftBtn("打开档案库所在位置"); bOpenDb.Click += (s, e) => Ui.Reveal(St.LibPath);
            var bOther = Ui.Btn("连接其他档案库…", false); bOther.Click += (s, e) => Program.SwitchLibrary(m);
            var bNew = Ui.Btn("新建档案库（选另一个项目文件夹）…", false); bNew.Click += (s, e) => Program.NewLibrary(m);
            r1.Controls.AddRange(new Control[] { bOpenRoot, bOpenDb, bOther, bNew });
            loc.Controls.AddRange(new Control[] { lt, rootLbl, dbLbl, r1 });

            // 扫描
            var scan = new Card { Height = Ui.S(170) };
            var st = Ui.Lbl("扫描与文字识别", 11f, true, Ui.Text); st.Location = new Point(Ui.S(16), Ui.S(12));
            var sl = Ui.Lbl("不参与扫描的文件夹（逗号分隔，填根目录下的文件夹名）：", 9.5f, false, Ui.Sub); sl.Location = new Point(Ui.S(16), Ui.S(44));
            excl = new TextBox { Location = new Point(Ui.S(16), Ui.S(70)), Width = Ui.S(760), Font = Ui.F(9.5f) };
            var r2 = Ui.Row(); r2.Location = new Point(Ui.S(12), Ui.S(106));
            var bSave = Ui.Btn("保存并重新扫描", true, "refresh");
            bSave.Click += (s, e) =>
            {
                St.Lib.Excludes = excl.Text.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                m.Rescan();
            };
            var bRetry = Ui.Btn("重新识别以前失败的文件", false);
            bRetry.Click += (s, e) => { lock (St.Sync) St.Lib.TextTried.Clear(); m.StartOcr(); };
            r2.Controls.AddRange(new Control[] { bSave, bRetry, Ui.Lbl("识别用 Windows 自带的中文 OCR；老版 .doc/.xls 需本机装有 Office 或 WPS。", 8.5f, false, Ui.Sub) });
            scan.Controls.AddRange(new Control[] { st, sl, excl, r2 });

            // 项目管理
            var proj = new Card { Height = Ui.S(420) };
            var pt = Ui.Lbl("项目管理", 11f, true, Ui.Text); pt.Location = new Point(Ui.S(16), Ui.S(12));
            var pl = Ui.Lbl("同一个项目散在几个文件夹里时，用“合并到…”并成一个；项目只是登记，删除登记不会删除任何文件。", 9f, false, Ui.Sub); pl.Location = new Point(Ui.S(16), Ui.S(42));
            plv = Ui.List("档号", 70, "项目名称", 380, "类型", 90, "对应文件夹", 600);
            plv.MultiSelect = true;
            plv.Location = new Point(Ui.S(16), Ui.S(70)); plv.Size = new Size(Ui.S(900), Ui.S(280));
            plv.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            plv.BorderStyle = BorderStyle.FixedSingle;
            plv.DoubleClick += (s, e) => { var p = Sel(); if (p != null) m.OpenProject(p, 0); };
            var r3 = Ui.Row(); r3.Location = new Point(Ui.S(12), Ui.S(360));
            var bAdd = Ui.Btn("新建项目", true, "plus"); bAdd.Click += (s, e) => AddProject();
            var bRen = Ui.Btn("改名", false); bRen.Click += (s, e) => { var p = Sel(); if (p == null) return; var n = Ui.Prompt("改名", "项目名称：", p.Name); if (!string.IsNullOrEmpty(n)) { p.Name = n; St.Save(); m.ReloadAll(); } };
            var bMerge = Ui.Btn("合并到…", false); bMerge.Click += (s, e) => Merge();
            var bFolder = Ui.Btn("关联文件夹…", false); bFolder.Click += (s, e) => LinkFolder();
            var bDel = (RoundButton)Ui.Btn("删除所选项目…", false); bDel.TextColor = Ui.Red; bDel.Click += (s, e) => DeleteProject();
            r3.Controls.AddRange(new Control[] { bAdd, bRen, bMerge, bFolder, bDel });
            proj.Controls.AddRange(new Control[] { pt, pl, plv, r3 });
            proj.Resize += (s, e) => plv.Width = proj.Width - Ui.S(32);

            // AI 接口
            var ai = new Card { Height = Ui.S(120) };
            var at = Ui.Lbl("AI 智能填表", 11f, true, Ui.Text); at.Location = new Point(Ui.S(16), Ui.S(12));
            aiLbl = Ui.Lbl("", 9.5f, false, Ui.Text); aiLbl.Location = new Point(Ui.S(16), Ui.S(44));
            var r5 = Ui.Row(); r5.Location = new Point(Ui.S(12), Ui.S(70));
            var bAiSet = Ui.Btn("AI 接口设置（DeepSeek 等）…", true, "gear"); bAiSet.Click += (s, e) => { AiSettingsDialog.Show(this); Reload(); };
            r5.Controls.AddRange(new Control[] { bAiSet, Ui.Lbl("用于“智能填表 → AI 填上级报表”。密钥只保存在本机，不随档案库分享。", 8.5f, false, Ui.Sub) });
            ai.Controls.AddRange(new Control[] { at, aiLbl, r5 });

            // 备份
            var bk = new Card { Height = Ui.S(150) };
            var bt = Ui.Lbl("网盘备份", 11f, true, Ui.Text); bt.Location = new Point(Ui.S(16), Ui.S(12));
            backupLbl = Ui.Lbl("", 9.5f, false, Ui.Text); backupLbl.Location = new Point(Ui.S(16), Ui.S(44));
            autoBk = new CheckBox { Text = "每次关闭软件时自动备份档案库", AutoSize = true, Location = new Point(Ui.S(16), Ui.S(72)) };
            autoBk.CheckedChanged += (s, e) => { St.Lib.AutoBackup = autoBk.Checked; St.Save(); };
            var r4 = Ui.Row(); r4.Location = new Point(Ui.S(12), Ui.S(100));
            var bPick = Ui.Btn("选择备份位置…", false);
            bPick.Click += (s, e) => { using (var d = new FolderBrowserDialog { Description = "选择备份位置（百度网盘/坚果云同步文件夹、U盘等）" }) if (d.ShowDialog(this) == DialogResult.OK) { St.Lib.BackupDir = d.SelectedPath; St.Save(); Reload(); } };
            var bNow = Ui.Btn("立即备份", true, "backup"); bNow.Click += (s, e) => m.DoBackup();
            r4.Controls.AddRange(new Control[] { bPick, bNow, Ui.Lbl("备份内容：项目登记、分类、合同台账、全文索引（原始资料请自行用网盘同步整个项目文件夹）。", 8.5f, false, Ui.Sub) });
            bk.Controls.AddRange(new Control[] { bt, backupLbl, autoBk, r4 });

            var about = Ui.Lbl("项目档案室 " + AppInfo.Version + " · 开发者 " + AppInfo.Developer + " · 资料只保存在本机；只有使用 AI 填表时，才把相关项目的资料发给你设置的 AI 接口。", 8.5f, false, Ui.Sub);
            var aboutP = new Panel { Height = Ui.S(40) }; about.Location = new Point(Ui.S(4), Ui.S(12)); aboutP.Controls.Add(about);
            Ui.Stack(this, null, head, loc, Ui.Gap(12), scan, Ui.Gap(12), proj, Ui.Gap(12), ai, Ui.Gap(12), bk, aboutP);
        }

        Project Sel() { return plv.SelectedItems.Count == 0 ? null : (Project)plv.SelectedItems[0].Tag; }

        public void Reload()
        {
            Tag = null;
            rootLbl.Text = "档案文件夹（资料原件所在）：" + St.Lib.Root;
            dbLbl.Text = "档案库文件：" + St.LibPath + "    上次保存：" + St.Lib.Updated;
            excl.Text = string.Join("，", St.Lib.Excludes);
            backupLbl.Text = "备份位置：" + (string.IsNullOrEmpty(St.Lib.BackupDir) ? "（未设置）" : St.Lib.BackupDir);
            autoBk.Checked = St.Lib.AutoBackup;
            var aic = AiConfig.Load();
            aiLbl.Text = aic.Ready ? "已设置：" + aic.Provider + " · " + aic.Model + " · " + aic.BaseUrl : "未设置 AI 接口（填入 DeepSeek 等的 API 密钥后才能用 AI 填表）";
            plv.BeginUpdate(); plv.Items.Clear();
            foreach (var p in St.Lib.Projects)
            {
                var it = new ListViewItem(p.Code) { Tag = p };
                it.SubItems.Add(p.Name); it.SubItems.Add(p.Type); it.SubItems.Add(string.Join("；", p.Folders));
                plv.Items.Add(it);
            }
            plv.EndUpdate();
        }

        void AddProject()
        {
            var n = Ui.Prompt("新建项目", "项目名称（会在档案文件夹里建同名文件夹）：", "");
            if (string.IsNullOrEmpty(n)) return;
            var p = new Project { Id = St.NewProjectId(), Name = n, Type = Store.GuessType(n) };
            p.Code = p.Id; p.Required = Store.DefaultRequired(p.Type);
            Builder.Complete(St, p);
            St.Lib.Projects.Add(p);
            try { Directory.CreateDirectory(St.ProjectFolder(p)); } catch { }
            St.Save(); m.ReloadAll();
        }

        void Merge()
        {
            var p = Sel(); if (p == null || p.IsRoot) return;
            var to = Ui.PickProject(St, "把“" + p.Name + "”合并到哪个项目？", p);
            if (to == null || to.IsRoot) return;
            if (!Ui.Ask("确定把“" + p.Name + "”合并到“" + to.Name + "”？\n文件夹、合同、字段会并过去（同名字段以目标项目为准），文件本身不动。")) return;
            foreach (var f in p.Folders) if (!to.Folders.Contains(f)) to.Folders.Add(f);
            to.Contracts.AddRange(p.Contracts);
            foreach (var f in p.Fields) if (to.Get(f.Name) == "" && f.Value != "") to.Set(f.Name, f.Value, f.Source);
            if (p.Note != "") to.Note = (to.Note + "\n" + p.Note).Trim();
            foreach (var k in St.Lib.FileProject.Where(kv => kv.Value == p.Id).Select(kv => kv.Key).ToList()) St.Lib.FileProject[k] = to.Id;
            St.Lib.Projects.Remove(p);
            foreach (var f in St.Lib.Files) if (f.Project == p.Id) f.Project = to.Id;
            foreach (var l in St.Lib.SharedWith.Values) { if (l.Remove(p.Id) && !l.Contains(to.Id)) l.Add(to.Id); }
            St.Save(); m.ReloadAll();
        }

        void LinkFolder()
        {
            var p = Sel(); if (p == null || p.IsRoot) return;
            using (var d = new FolderBrowserDialog { Description = "选择属于“" + p.Name + "”的文件夹（须是档案文件夹下的第一级文件夹）", SelectedPath = St.Lib.Root })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string root = St.Lib.Root.TrimEnd('\\') + "\\";
                if (!d.SelectedPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { Ui.Info("请选择档案文件夹（" + St.Lib.Root + "）里面的文件夹。"); return; }
                string top = d.SelectedPath.Substring(root.Length).Split('\\')[0];
                foreach (var o in St.Lib.Projects) if (o != p) o.Folders.RemoveAll(f => string.Equals(f, top, StringComparison.OrdinalIgnoreCase));
                if (!p.Folders.Contains(top)) p.Folders.Add(top);
                St.Lib.Projects.RemoveAll(o => !o.IsRoot && o != p && o.Folders.Count == 0 && o.Contracts.Count == 0 && o.Fields.All(f => f.Value == "" || f.Name == "项目名称"));
                m.Rescan();
            }
        }

        void DeleteProject()
        {
            var sel = plv.SelectedItems.Cast<ListViewItem>().Select(x => (Project)x.Tag).Where(x => !x.IsRoot).ToList();
            if (sel.Count == 0) { Ui.Info("先在列表里选中要删除的项目（按住 Ctrl 或 Shift 可多选）。"); return; }
            if (DeleteProjectDialog.Show(this, St, sel)) m.ReloadAll();
        }
    }
}
