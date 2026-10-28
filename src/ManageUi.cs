using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DangAn
{
    /// <summary>删除项目的确认窗口：只删登记（文件夹保留、不再扫描），或连文件夹一起移到回收站。</summary>
    public static class DeleteProjectDialog
    {
        public static bool Show(IWin32Window owner, Store st, List<Project> projects)
        {
            projects = projects.Where(p => !p.IsRoot).ToList();
            if (projects.Count == 0) return false;
            int files = projects.Sum(p => st.FilesOf(p).Count(f => f.Project == p.Id));
            bool looksLikeCategory = projects.All(p => p.Folders.Count > 0 && p.Folders.All(FolderNames.IsCategory));
            using (var f = new Form())
            {
                f.Text = "删除项目"; f.Font = Ui.F(9.5f); f.BackColor = Color.White; f.ShowIcon = false;
                f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MinimizeBox = false; f.MaximizeBox = false; f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(Ui.S(600), Ui.S(looksLikeCategory ? 360 : 320));
                string names = projects.Count <= 4 ? string.Join("、", projects.Select(p => "“" + p.Name + "”")) : projects.Count + " 个项目";
                var t = new Label { Text = "删除 " + names + "（共 " + files + " 份资料）", Font = Ui.F(11f, FontStyle.Bold), Location = new Point(Ui.S(20), Ui.S(18)), Size = new Size(Ui.S(560), Ui.S(50)) };
                var r1 = new RadioButton { Text = "只删除项目登记，文件夹保留在硬盘上（以后扫描也不再把它当成项目）", Checked = true, Location = new Point(Ui.S(22), Ui.S(80)), Size = new Size(Ui.S(560), Ui.S(28)) };
                var r2 = new RadioButton { Text = "删除项目，并把项目文件夹移到回收站（可以从回收站还原）", Location = new Point(Ui.S(22), Ui.S(116)), Size = new Size(Ui.S(560), Ui.S(28)), ForeColor = Ui.Red };
                var note = new Label { Text = "项目里的合同台账、收支记录、备注也会一起删除。\n如果文件夹里有别的项目的资料，文件夹不会被删除。", ForeColor = Ui.Sub, Location = new Point(Ui.S(22), Ui.S(156)), Size = new Size(Ui.S(560), Ui.S(46)) };
                f.Controls.AddRange(new Control[] { t, r1, r2, note });
                if (looksLikeCategory)
                {
                    var hint = new Label { Text = "提示：它们看起来是某个项目里的分类文件夹（如“1项目合同”“2项目人员”），多半是把单个项目文件夹当成了总目录。选第一项即可；以后软件会把这类文件夹自动归到同一个项目里。", ForeColor = Ui.Orange, Location = new Point(Ui.S(22), Ui.S(206)), Size = new Size(Ui.S(560), Ui.S(64)) };
                    f.Controls.Add(hint);
                }
                var ok = Ui.Btn("删除", true); ok.Location = new Point(Ui.S(390), f.ClientSize.Height - Ui.S(56));
                var no = Ui.Btn("取消", false); no.DialogResult = DialogResult.Cancel; no.Location = new Point(Ui.S(490), f.ClientSize.Height - Ui.S(56));
                ok.Click += (s, e) =>
                {
                    if (r2.Checked && !Ui.Ask("确定把项目文件夹移到回收站？")) return;
                    f.DialogResult = DialogResult.OK;
                };
                f.Controls.Add(ok); f.Controls.Add(no); f.CancelButton = no;
                if (f.ShowDialog(owner) != DialogResult.OK) return false;
                var log = ProjectOps.DeleteProjects(st, projects, r2.Checked);
                Ui.Info(string.Join("\n", log));
                return true;
            }
        }
    }

    /// <summary>一键导入：识别文件夹里的各个项目，确认后导入。</summary>
    public class BatchImportForm : Form
    {
        readonly MainForm m; Store St { get { return m.St; } }
        readonly List<BatchImport.Item> items = new List<BatchImport.Item>();
        ListView lv; Label info; TextBox log; Button go, close;

        public BatchImportForm(MainForm m, IEnumerable<string> folders)
        {
            this.m = m;
            Text = "一键导入"; Font = Ui.F(9.5f); BackColor = Color.White; ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent; Size = new Size(Ui.S(1100), Ui.S(700)); MinimumSize = new Size(Ui.S(800), Ui.S(500));
            foreach (var d in folders) items.AddRange(BatchImport.Analyze(St, d));

            var head = new Panel { Dock = DockStyle.Top, Height = Ui.S(88), Padding = new Padding(Ui.S(18), Ui.S(14), Ui.S(18), 0) };
            var t1 = Ui.Lbl("一键导入：自动识别项目文件夹", 13f, true, Ui.Text); t1.Location = new Point(Ui.S(18), Ui.S(12));
            info = Ui.Lbl("", 9.5f, false, Ui.Sub); info.Location = new Point(Ui.S(20), Ui.S(50));
            head.Controls.Add(t1); head.Controls.Add(info);

            lv = Ui.List("识别出的项目文件夹", 300, "导入到", 330, "文件数", 70, "大小", 90, "位置", 400);
            lv.CheckBoxes = true; lv.OwnerDraw = false; lv.ShowItemToolTips = true; lv.Dock = DockStyle.Fill;
            lv.ItemChecked += (s, e) => { var it = e.Item.Tag as BatchImport.Item; if (it != null) it.Include = e.Item.Checked; UpdateInfo(); };
            var menu = new ContextMenuStrip { Font = Ui.F(9.5f) };
            menu.Items.Add("新建为项目（可改名）…", null, (s, e) => Retarget(true));
            menu.Items.Add("并入已有项目…", null, (s, e) => Retarget(false));
            lv.ContextMenuStrip = menu;
            lv.DoubleClick += (s, e) => { if (lv.SelectedItems.Count > 0) menu.Show(lv, lv.PointToClient(Cursor.Position)); };

            log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Bottom, Height = Ui.S(150), Visible = false, Font = Ui.F(9f), BackColor = Color.FromArgb(250, 250, 251) };
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(62), Padding = new Padding(Ui.S(16), Ui.S(12), Ui.S(16), Ui.S(10)) };
            var tip = Ui.Lbl("双击某一行可改成“新建项目”或“并入已有项目”；取消勾选则不导入。资料是复制进档案文件夹，原文件不动；完全相同的文件只录一次。", 8.5f, false, Ui.Sub);
            tip.Location = new Point(Ui.S(16), Ui.S(22));
            var r = Ui.Row(); r.Dock = DockStyle.Right;
            go = Ui.Btn("开始导入", true, "upload"); go.Click += (s, e) => Run();
            close = Ui.Btn("关闭", false); close.Click += (s, e) => Close();
            r.Controls.AddRange(new Control[] { close, go });
            bottom.Controls.Add(tip); bottom.Controls.Add(r);
            Controls.Add(lv); Controls.Add(log); Controls.Add(head); Controls.Add(bottom);
            lv.BringToFront();
            Fill();
        }

        string TargetText(BatchImport.Item it)
        {
            if (it.Target != null) return "并入已有：" + it.Target.Name;
            if (it.Loose) return "（未识别为项目，双击指定）";
            return "新建项目：" + it.NewName;
        }

        void Fill()
        {
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var it in items)
            {
                var row = new ListViewItem((it.Loose ? "零散资料：" : "") + it.Name) { Tag = it, Checked = it.Include };
                row.SubItems.Add(TargetText(it)); row.SubItems.Add(it.FileCount.ToString()); row.SubItems.Add(Ui.Size(it.Size)); row.SubItems.Add(it.Dir);
                row.ToolTipText = it.Dir + "\n包含：" + string.Join("、", it.Paths.Take(8).Select(Path.GetFileName)) + (it.Paths.Count > 8 ? " 等" : "");
                if (it.Loose) row.ForeColor = Ui.Orange;
                lv.Items.Add(row);
            }
            lv.EndUpdate();
            UpdateInfo();
        }

        void UpdateInfo()
        {
            var inc = items.Where(x => x.Include).ToList();
            info.Text = "识别出 " + items.Count(x => !x.Loose) + " 个项目文件夹" + (items.Any(x => x.Loose) ? "，另有零散资料 " + items.Count(x => x.Loose) + " 处" : "")
                + "。将导入 " + inc.Count + " 项，共 " + inc.Sum(x => x.FileCount) + " 份文件（" + Ui.Size(inc.Sum(x => x.Size)) + "）：其中新建项目 " + inc.Count(x => x.Target == null) + " 个，并入已有 " + inc.Count(x => x.Target != null) + " 个。";
        }

        void Retarget(bool asNew)
        {
            if (lv.SelectedItems.Count == 0) return;
            var it = (BatchImport.Item)lv.SelectedItems[0].Tag;
            if (asNew)
            {
                var n = Ui.Prompt("新建项目", "项目名称：", it.NewName);
                if (string.IsNullOrEmpty(n)) return;
                it.Target = null; it.NewName = n; it.Loose = false;
            }
            else
            {
                var p = Ui.PickProject(St, "把“" + it.Name + "”并入哪个项目？", null);
                if (p == null || p.IsRoot) return;
                it.Target = p;
            }
            it.Include = true;
            Fill();
        }

        void Run()
        {
            var todo = items.Where(x => x.Include && (x.Target != null || !x.Loose)).ToList();
            if (todo.Count == 0) { Ui.Info("没有要导入的项。"); return; }
            go.Enabled = false; close.Enabled = false; lv.Enabled = false; log.Visible = true;
            log.Text = "正在导入，请稍候…\r\n";
            Task.Factory.StartNew(() =>
            {
                List<string> res;
                try { res = BatchImport.Run(St, todo, s => BeginInvoke(new Action(() => Text = "一键导入 — " + s))); }
                catch (Exception ex) { res = new List<string> { "× 出错：" + ex.Message }; }
                BeginInvoke(new Action(() =>
                {
                    log.AppendText(string.Join("\r\n", res) + "\r\n\r\n完成。新导入的扫描件会在后台识别文字。\r\n");
                    Text = "一键导入 — 完成"; close.Enabled = true; close.Text = "完成";
                }));
            });
        }
    }

    /// <summary>一键取消关联：处理误识别出来的一批“项目”（如“1项目合同”…“20项目验收”）。合并成一个项目，或删除登记。</summary>
    public static class UnlinkDialog
    {
        public static bool Show(MainForm m)
        {
            var st = m.St;
            var bad = ProjectOps.LooksMisrecognized(st);
            using (var f = new Form())
            {
                f.Text = "一键取消关联"; f.Font = Ui.F(9.5f); f.BackColor = Color.White; f.ShowIcon = false;
                f.StartPosition = FormStartPosition.CenterParent; f.Size = new Size(Ui.S(860), Ui.S(680)); f.MinimizeBox = false;
                var head = new Panel { Dock = DockStyle.Top, Height = Ui.S(96) };
                var t1 = Ui.Lbl(bad.Count > 0 ? "发现 " + bad.Count + " 个疑似误识别的项目" : "没有发现明显误识别的项目", 12.5f, true, bad.Count > 0 ? Ui.Orange : Ui.Text);
                t1.Location = new Point(Ui.S(18), Ui.S(14));
                var t2 = new Label { Text = "常见原因：把某个项目文件夹当成了总目录，里面的“1项目合同”“2项目人员”…“20项目验收”就各自成了一个项目。\n已自动勾选疑似的；也可以手动勾选其他要处理的项目。", ForeColor = Ui.Sub, Location = new Point(Ui.S(20), Ui.S(48)), Size = new Size(Ui.S(800), Ui.S(44)) };
                head.Controls.Add(t1); head.Controls.Add(t2);

                var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, Font = Ui.F(9.5f), BorderStyle = BorderStyle.FixedSingle };
                lv.Columns.Add("项目", Ui.S(300)); lv.Columns.Add("对应文件夹", Ui.S(360)); lv.Columns.Add("资料", Ui.S(80));
                foreach (var p in bad.Concat(st.RealProjects.Where(x => !bad.Contains(x))))
                {
                    var it = new ListViewItem(p.Name) { Tag = p, Checked = bad.Contains(p) };
                    it.SubItems.Add(string.Join("；", p.Folders)); it.SubItems.Add(st.FilesOf(p).Count() + " 份");
                    if (bad.Contains(p)) it.ForeColor = Ui.Orange;
                    lv.Items.Add(it);
                }

                var opts = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(150), Padding = new Padding(Ui.S(18), Ui.S(10), Ui.S(18), 0) };
                var rMerge = new RadioButton { Text = "合并成一个项目（推荐：它们本来是同一个项目的分类文件夹），项目名称：", Checked = true, AutoSize = true, Location = new Point(Ui.S(18), Ui.S(12)) };
                var name = new TextBox { Text = Path.GetFileName(st.Lib.Root.TrimEnd('\\')), Location = new Point(Ui.S(40), Ui.S(42)), Width = Ui.S(420) };
                var rDel = new RadioButton { Text = "取消关联：删除这些项目登记（文件夹保留在硬盘上，以后扫描也不再当成项目）", AutoSize = true, Location = new Point(Ui.S(18), Ui.S(80)) };
                var relink = new LinkLabel { Text = "整个档案库连错了文件夹？重新选择项目总文件夹…", AutoSize = true, Location = new Point(Ui.S(20), Ui.S(116)), LinkBehavior = LinkBehavior.HoverUnderline };
                relink.LinkClicked += (s, e) => { f.DialogResult = DialogResult.Abort; f.Close(); };
                rMerge.CheckedChanged += (s, e) => name.Enabled = rMerge.Checked;
                opts.Controls.AddRange(new Control[] { rMerge, name, rDel, relink });

                var bottom = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(60), Padding = new Padding(Ui.S(16), Ui.S(10), Ui.S(16), Ui.S(10)) };
                var r = Ui.Row(); r.Dock = DockStyle.Right;
                var ok = Ui.Btn("确定", true, "check"); var no = Ui.Btn("取消", false); no.DialogResult = DialogResult.Cancel;
                r.Controls.AddRange(new Control[] { no, ok }); bottom.Controls.Add(r);
                ok.Click += (s, e) =>
                {
                    var sel = lv.CheckedItems.Cast<ListViewItem>().Select(x => (Project)x.Tag).ToList();
                    if (sel.Count == 0) { Ui.Info("先勾选要处理的项目。"); return; }
                    if (rMerge.Checked && name.Text.Trim() == "") { Ui.Info("请填写合并后的项目名称。"); return; }
                    string what = rMerge.Checked ? "合并成一个项目“" + name.Text.Trim() + "”" : "删除这些项目登记（文件夹保留）";
                    if (!Ui.Ask("把勾选的 " + sel.Count + " 个项目" + what + "？")) return;
                    if (rMerge.Checked)
                    {
                        var p = ProjectOps.MergeInto(st, sel, name.Text.Trim());
                        Ui.Info("已合并成项目“" + p.Name + "”，共 " + st.FilesOf(p).Count() + " 份资料。");
                    }
                    else Ui.Info(string.Join("\n", ProjectOps.DeleteProjects(st, sel, false)));
                    f.DialogResult = DialogResult.OK;
                };
                f.Controls.Add(lv); f.Controls.Add(head); f.Controls.Add(opts); f.Controls.Add(bottom);
                lv.BringToFront();
                f.CancelButton = no;
                var res = f.ShowDialog(m);
                if (res == DialogResult.Abort) { Program.NewLibrary(m); return false; }
                return res == DialogResult.OK;
            }
        }
    }
    /// <summary>名字相同（去掉“188号--”这类编号、地区前缀后）的项目，一键合并。</summary>
    public static class DupMergeDialog
    {
        public static bool Show(MainForm m)
        {
            var st = m.St;
            var groups = ProjectOps.DuplicateGroups(st);
            if (groups.Count == 0) { Ui.Info("没有发现重名项目。"); return false; }
            using (var f = new Form { Text = "合并重名项目", ShowIcon = false, ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false, StartPosition = FormStartPosition.CenterParent, Font = Ui.F(9.5f), ClientSize = new Size(Ui.S(820), Ui.S(420)), BackColor = Color.White })
            {
                var tip = new Label { Text = "下面每一行是同一个项目被分成了几个（如“188号--慧谷湖……”和“慧谷湖……”）。勾选的合并成一个：保留编号最小的那个项目的名称、合同和信息，文件夹都归到它下面，文件本身不动。", Dock = DockStyle.Top, Height = Ui.S(56), Padding = new Padding(Ui.S(10), Ui.S(10), Ui.S(10), 0) };
                var cl = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, BorderStyle = BorderStyle.None, HorizontalScrollbar = true };
                foreach (var g in groups)
                {
                    var keep = g.OrderBy(p => p.Id).First();
                    cl.Items.Add(new KeyValuePair<Project, List<Project>>(keep, g), true);
                }
                cl.Format += (s, e) =>
                {
                    var kv = (KeyValuePair<Project, List<Project>>)e.ListItem;
                    e.Value = "保留 " + kv.Key.Id + "「" + kv.Key.Name + "」 ← 并入 " + string.Join("、", kv.Value.Where(p => p != kv.Key).Select(p => p.Id + "「" + p.Name + "」"));
                };
                var row = Ui.Row(); row.Dock = DockStyle.Bottom; row.Height = Ui.S(52); row.Padding = new Padding(Ui.S(10), Ui.S(8), 0, 0);
                var ok = Ui.Btn("合并所选", true, "check"); ok.Click += (s, e) => f.DialogResult = DialogResult.OK;
                var cancel = Ui.Btn("取消", false); cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                row.Controls.AddRange(new Control[] { ok, cancel });
                f.Controls.Add(cl); f.Controls.Add(row); f.Controls.Add(tip);
                if (f.ShowDialog(m) != DialogResult.OK || cl.CheckedItems.Count == 0) return false;
                int n = 0;
                foreach (KeyValuePair<Project, List<Project>> kv in cl.CheckedItems) { ProjectOps.MergeInto(st, kv.Value, kv.Key.Name); n++; }
                m.SetStatus("已合并 " + n + " 组重名项目");
                return true;
            }
        }
    }

    /// <summary>导出项目资料包：选分类、是否打 zip、导出位置。</summary>
    public static class ExportPackDialog
    {
        public static void Show(IWin32Window owner, MainForm m, Project p)
        {
            var st = m.St;
            var info = st.Info(p);
            using (var f = new Form { Text = "导出资料包 — " + p.Name, ShowIcon = false, ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false, StartPosition = FormStartPosition.CenterParent, Font = Ui.F(9.5f), ClientSize = new Size(Ui.S(560), Ui.S(470)), BackColor = Color.White })
            {
                var tip = new Label { Text = "按分类建文件夹放资料（01 立项及计划、04 合同协议……），附《资料目录.xlsx》和《项目基本情况.docx》，方便交给上级查阅。原文件不动。", Dock = DockStyle.Top, Height = Ui.S(52), Padding = new Padding(Ui.S(10), Ui.S(10), Ui.S(10), 0) };
                var cl = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, BorderStyle = BorderStyle.None };
                foreach (var c in Cats.All) { int k; if (info.CatCount.TryGetValue(c, out k) && k > 0) cl.Items.Add(new KeyValuePair<string, int>(c, k), true); }
                cl.Format += (s, e) => { var kv = (KeyValuePair<string, int>)e.ListItem; e.Value = Cats.Title(kv.Key) + "（" + kv.Value + " 份）"; };
                var zip = new CheckBox { Text = "同时打包成 zip（方便发微信、邮件）", Checked = true, Dock = DockStyle.Bottom, Height = Ui.S(32), Padding = new Padding(Ui.S(10), 0, 0, 0) };
                var row = Ui.Row(); row.Dock = DockStyle.Bottom; row.Height = Ui.S(52); row.Padding = new Padding(Ui.S(10), Ui.S(8), 0, 0);
                var ok = Ui.Btn("选择位置并导出", true, "box"); ok.Click += (s, e) => f.DialogResult = DialogResult.OK;
                var cancel = Ui.Btn("取消", false); cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                row.Controls.AddRange(new Control[] { ok, cancel });
                f.Controls.Add(cl); f.Controls.Add(zip); f.Controls.Add(row); f.Controls.Add(tip);
                if (f.ShowDialog(owner) != DialogResult.OK || cl.CheckedItems.Count == 0) return;
                var cats = cl.CheckedItems.Cast<KeyValuePair<string, int>>().Select(kv => kv.Key).ToList();
                if (cats.Count == cl.Items.Count) cats = null;
                bool z = zip.Checked;
                string outParent;
                using (var d = new FolderBrowserDialog { Description = "选择导出位置（会在里面新建“项目名_资料汇编_日期”文件夹）", SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) })
                {
                    if (d.ShowDialog(owner) != DialogResult.OK) return;
                    outParent = d.SelectedPath;
                }
                m.SetStatus("正在导出资料包……");
                var th = new System.Threading.Thread(() =>
                {
                    ProjectExport.Result r = null; string err = null;
                    try { r = ProjectExport.Run(st, p, outParent, cats, z, m.SetStatus); } catch (Exception ex) { err = ex.Message; }
                    m.BeginInvoke(new Action(() =>
                    {
                        if (err != null) { m.SetStatus("导出失败：" + err); Ui.Info("导出失败：" + err); return; }
                        m.SetStatus("资料包已导出：" + r.Files + " 份");
                        if (Ui.Ask("已导出 " + r.Files + " 份资料（" + Ui.Size(r.Bytes) + "）" + (r.Skipped > 0 ? "，重复副本 " + r.Skipped + " 份没有重复放" : "") + "：\n" + r.Folder + (r.Zip != null ? "\n压缩包：" + r.Zip : "") + (r.Log.Count > 0 ? "\n\n" + string.Join("\n", r.Log.Take(5)) : "") + "\n\n现在打开所在文件夹吗？"))
                            Ui.Reveal(r.Zip ?? r.Folder);
                    }));
                });
                th.IsBackground = true; th.Start();
            }
        }
    }

    /// <summary>查重合并：列出重复的收支、合同，勾选的合并（保留信息最全的一条，其余的内容补进来后删除）。</summary>
    public static class DedupDialog
    {
        public static bool Show(IWin32Window owner, Store st, Project p)
        {
            var pays = Dedup.Payments(p); var cons = Dedup.Contracts(p);
            if (pays.Count + cons.Count == 0) { Ui.Info("没有发现重复的收支或合同记录。"); return false; }
            using (var f = new Form { Text = "查重合并 — " + p.Name, ShowIcon = false, ShowInTaskbar = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, Font = Ui.F(9.5f), ClientSize = new Size(Ui.S(980), Ui.S(520)), BackColor = Color.White })
            {
                var tip = new Label { Text = "下面每一组是同一笔收支 / 同一份合同被登记了多次（常见原因：两张明细表都导入了、AI 和手工各登记了一次）。勾选的合并：保留信息最全的那条，其余几条上有它没有的凭证号、凭证/发票文件等补进来，再删掉其余几条。统计金额会随之更正。", Dock = DockStyle.Top, Height = Ui.S(62), Padding = new Padding(Ui.S(12), Ui.S(10), Ui.S(12), 0) };
                var cl = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, BorderStyle = BorderStyle.None, HorizontalScrollbar = true, Font = Ui.F(9.5f) };
                var items = new List<object>();
                foreach (var g in pays) { items.Add(g); cl.Items.Add("【收支】" + g.Why + "　保留：" + Dedup.Desc(g.Keep) + "　删除：" + string.Join("；", g.Drop.Select(Dedup.Desc)), true); }
                foreach (var g in cons) { items.Add(g); cl.Items.Add("【合同】" + g.Why + "　保留：" + Dedup.Desc(g.Keep) + "　删除：" + string.Join("；", g.Drop.Select(Dedup.Desc)), true); }
                double extra = pays.Sum(g => g.Drop.Sum(x => x.Dir == "收入" ? 0 : x.Amount));
                var sum = new Label { Text = "共 " + items.Count + " 组；收支重复多算了 " + extra.ToString("#,##0.00") + " 元支出" + (pays.Any(g => g.Keep.Dir == "收入") ? "（另有收入重复）" : ""), Dock = DockStyle.Bottom, Height = Ui.S(30), ForeColor = Ui.Orange, Padding = new Padding(Ui.S(12), Ui.S(6), 0, 0) };
                var row = Ui.Row(); row.Dock = DockStyle.Bottom; row.Height = Ui.S(52); row.Padding = new Padding(Ui.S(10), Ui.S(8), 0, 0);
                var ok = Ui.Btn("合并所选", true, "check"); ok.Click += (s, e) => f.DialogResult = DialogResult.OK;
                var cancel = Ui.Btn("取消", false); cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                row.Controls.AddRange(new Control[] { ok, cancel });
                f.Controls.Add(cl); f.Controls.Add(sum); f.Controls.Add(row); f.Controls.Add(tip);
                if (f.ShowDialog(owner) != DialogResult.OK || cl.CheckedItems.Count == 0) return false;
                int n = 0;
                for (int i = 0; i < cl.Items.Count; i++)
                {
                    if (!cl.GetItemChecked(i)) continue;
                    var g = items[i];
                    if (g is Dedup.Group<Payment>) Dedup.Merge(p, (Dedup.Group<Payment>)g); else Dedup.Merge(p, (Dedup.Group<Contract>)g);
                    n++;
                }
                st.Save();
                Ui.Info("已合并 " + n + " 组重复记录。");
                return true;
            }
        }
    }
}