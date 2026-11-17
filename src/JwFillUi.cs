using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace DangAn
{
    /// <summary>
    /// 交委“以奖代补”填报台：自己勾选要报的项目 → AI 查资料（缺件预检、字段抽取带依据、附件转成 ≤5MB 的 PDF）→ 核对、改 →
    /// 打开交委系统并登录（验证码自己输）→ 在浏览器里打开该项目的页面 → 填入当前页 / 选入附件。软件不点审核、上报、提交。
    /// </summary>
    public class JwCenterForm : Form
    {
        readonly Store st;
        readonly JwBrowser browser = new JwBrowser();
        JwPortal portal;
        ListView projList; TextBox filter; CheckBox supervised, overwrite;
        DataGridView fieldGrid, attGrid, preGrid; Label acctLbl, projTitle; TextBox log, notes;
        readonly Dictionary<string, JwPrecheck.Result> pre = new Dictionary<string, JwPrecheck.Result>();
        Project cur; JwProjectData data; bool busy, loading;
        readonly List<Control> busyButtons = new List<Control>();

        static JwCenterForm open;

        public static void Start(IWin32Window owner, Store st)
        {
            if (open != null && !open.IsDisposed) { open.Activate(); return; }
            open = new JwCenterForm(st);
            open.Show(owner);
        }

        JwCenterForm(Store st)
        {
            this.st = st; portal = JwPortal.Load();
            Text = "交委以奖代补填报（招投标信息、质量及验收）";
            Font = Ui.F(9.5f); BackColor = Ui.Bg;
            Size = new Size(Ui.S(1360), Ui.S(860)); StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(Ui.S(1000), Ui.S(640));

            // ---- 顶栏
            var top = new Panel { Dock = DockStyle.Top, Height = Ui.S(84), Padding = new Padding(Ui.S(14), Ui.S(8), Ui.S(14), 0) };
            var t1 = Ui.Lbl("交委以奖代补填报", 15f, true, Ui.Text); t1.Location = new Point(Ui.S(14), Ui.S(6));
            acctLbl = Ui.Lbl("", 9f, false, Ui.Sub); acctLbl.Location = new Point(Ui.S(16), Ui.S(40));
            var r = Ui.Row(); r.Dock = DockStyle.Right; r.Width = Ui.S(760); r.FlowDirection = FlowDirection.RightToLeft; r.Padding = new Padding(0, Ui.S(4), 0, 0);
            var bSet = Ui.Btn("网址和账号…", false, "gear"); bSet.Click += (s, e) => { JwSettingsDialog.Show(this, st); portal = JwPortal.Load(); ShowAcct(); };
            var bLogin = Ui.Btn("打开交委系统并登录", true); bLogin.Click += (s, e) => Login();
            var bLogs = Ui.SoftBtn("填报记录"); bLogs.Click += (s, e) => Ui.Reveal(JwLog.Dir);
            var bMap = Ui.SoftBtn("字段映射"); bMap.Click += (s, e) => { JwMapping.Load(); Ui.Reveal(JwMapping.Path0); };
            r.Controls.AddRange(new Control[] { bSet, bLogin, bMap, bLogs });
            top.Controls.Add(r); top.Controls.Add(t1); top.Controls.Add(acctLbl);
            var tip = Ui.Lbl("① 左边勾选要报的项目 → ② AI 查资料 → ③ 核对右边的值和依据（可直接改）→ ④ 登录交委系统、打开该项目页面 → ⑤ 填入当前页、选入附件 → ⑥ 自己检查后自己保存 / 上报。", 9f, false, Ui.Sub);
            tip.Location = new Point(Ui.S(16), Ui.S(60)); top.Controls.Add(tip);

            // ---- 左：项目
            var left = new Panel { Dock = DockStyle.Left, Width = Ui.S(330), Padding = new Padding(Ui.S(12), Ui.S(6), Ui.S(6), Ui.S(8)) };
            var lt = new Label { Text = "要报的项目（自己勾选）", Dock = DockStyle.Top, Height = Ui.S(26), Font = Ui.F(10f, FontStyle.Bold), ForeColor = Ui.Text };
            filter = new TextBox { Dock = DockStyle.Top, Font = Ui.F(9.5f) };
            filter.TextChanged += (s, e) => LoadProjects();
            projList = new ListView { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false, MultiSelect = false, HeaderStyle = ColumnHeaderStyle.None, BorderStyle = BorderStyle.FixedSingle, Font = Ui.F(9.5f) };
            projList.Columns.Add("项目", Ui.S(290));
            projList.SelectedIndexChanged += (s, e) => { if (loading || projList.SelectedItems.Count == 0) return; var it = projList.SelectedItems[0].Tag as Item; if (it != null) ShowProject(it.P); };
            projList.ItemChecked += (s, e) => { if (loading) return; var it = e.Item.Tag as Item; if (it == null) return; if (e.Item.Checked) chosen.Add(it.P.Id); else chosen.Remove(it.P.Id); };
            projList.Resize += (s, e) => { if (projList.Columns.Count > 0) projList.Columns[0].Width = projList.ClientSize.Width - 2; };
            var lb = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Ui.S(210), FlowDirection = FlowDirection.TopDown, WrapContents = false };
            supervised = new CheckBox { Text = "本项目走交委监管程序", AutoSize = true, Checked = true };
            supervised.CheckedChanged += (s, e) =>
            {
                if (loading || cur == null) return;
                cur.JwSupervised = supervised.Checked; st.Save();
                pre[cur.Id] = JwPrecheck.Check(st, cur); ShowProject(cur);
            };
            var supTip = Ui.Lbl("不走的：施工许可、质量监督通知书暂免。", 8.5f, false, Ui.Sub);
            var bNone = Ui.Btn("全不勾", false); bNone.Click += (s, e) => { chosen.Clear(); LoadProjects(); };
            var bAi = Ui.Btn("AI 查资料（勾选的项目）", true); bAi.AutoSize = false; bAi.Size = new Size(Ui.S(290), Ui.S(40)); bAi.Click += (s, e) => RunAi();
            var bXls = Ui.Btn("导出缺件清单（勾选的项目）", false, "excel"); bXls.AutoSize = false; bXls.Size = new Size(Ui.S(290), Ui.S(34)); bXls.Click += (s, e) => ExportMissing();
            busyButtons.Add(bAi);
            lb.Controls.AddRange(new Control[] { supervised, supTip, bNone, bAi, bXls });
            left.Controls.Add(projList); left.Controls.Add(filter); left.Controls.Add(lt); left.Controls.Add(lb);

            // ---- 右：当前项目
            var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(6), Ui.S(6), Ui.S(14), Ui.S(8)) };
            projTitle = new Label { Dock = DockStyle.Top, Height = Ui.S(28), Font = Ui.F(11f, FontStyle.Bold), ForeColor = Ui.Text, Text = "（点左边的项目名查看）" };
            var ops = Ui.Row(); ops.Dock = DockStyle.Top; ops.Height = Ui.S(44);
            var bBid = Ui.Btn("填入当前页：招投标信息", true); bBid.Click += (s, e) => FillPage(JwExtract.Bid);
            var bAcc = Ui.Btn("填入当前页：质量及验收", true); bAcc.Click += (s, e) => FillPage(JwExtract.Acc);
            var bAtt = Ui.Btn("选入附件（当前页）", false); bAtt.Click += (s, e) => AttachPage();
            var bOne = Ui.Btn("只重查这个项目", false); bOne.Click += (s, e) => { if (cur != null) RunAi(new List<Project> { cur }); };
            overwrite = new CheckBox { Text = "覆盖页面上已有的值", AutoSize = true, Margin = new Padding(Ui.S(10), Ui.S(10), 0, 0) };
            busyButtons.AddRange(new Control[] { bBid, bAcc, bAtt, bOne });
            ops.Controls.AddRange(new Control[] { bBid, bAcc, bAtt, bOne, overwrite });

            var tabs = new TabControl { Dock = DockStyle.Fill, Font = Ui.F(9.5f) };
            fieldGrid = Ui.Grid(); fieldGrid.Dock = DockStyle.Fill; fieldGrid.AllowUserToAddRows = false; fieldGrid.ReadOnly = false;
            foreach (var c in new[] { "模块", "分组", "字段", "单位", "准备填入", "档案原值", "依据", "可信度" })
                fieldGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = c, ReadOnly = c != "准备填入", SortMode = DataGridViewColumnSortMode.NotSortable });
            SetWidths(fieldGrid, 86, 100, 110, 56, 170, 170, 380, 70);
            fieldGrid.Columns[5].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            fieldGrid.Columns[6].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            fieldGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            fieldGrid.Columns[4].DefaultCellStyle.BackColor = Color.FromArgb(255, 252, 235);
            fieldGrid.CellEndEdit += (s, e) => EditField(e.RowIndex);
            var tf = new TabPage("字段核对"); tf.Controls.Add(fieldGrid);
            notes = new TextBox { Dock = DockStyle.Bottom, Height = Ui.S(96), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, ForeColor = Ui.Orange, BackColor = Color.FromArgb(255, 251, 240), Font = Ui.F(9f), BorderStyle = BorderStyle.None };
            tf.Controls.Add(notes);

            attGrid = Ui.Grid(); attGrid.Dock = DockStyle.Fill; attGrid.AllowUserToAddRows = false; attGrid.ReadOnly = false;
            attGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "上传" });
            foreach (var c in new[] { "模块", "类别", "原件", "原大小", "上传副本", "处理", "依据", "状态" })
                attGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = c, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            SetWidths(attGrid, 46, 90, 160, 330, 70, 80, 200, 260, 160);
            attGrid.CurrentCellDirtyStateChanged += (s, e) => { if (attGrid.IsCurrentCellDirty) attGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            attGrid.CellValueChanged += (s, e) =>
            {
                if (loading || e.ColumnIndex != 0 || e.RowIndex < 0 || data == null) return;
                var a = attGrid.Rows[e.RowIndex].Tag as JwAttach; if (a == null) return;
                a.Use = Convert.ToBoolean(attGrid.Rows[e.RowIndex].Cells[0].Value); data.Save(st);
            };
            attGrid.CellDoubleClick += (s, e) => { if (e.RowIndex < 0) return; var a = attGrid.Rows[e.RowIndex].Tag as JwAttach; if (a != null) Ui.Open(e.ColumnIndex == 5 && a.Pdf != "" ? a.Pdf : Path.Combine(st.Lib.Root, a.Rel)); };
            var ta = new TabPage("附件"); ta.Controls.Add(attGrid);
            var attOps = Ui.Row(); attOps.Dock = DockStyle.Bottom; attOps.Height = Ui.S(42);
            var bPrep = Ui.Btn("重新准备附件 PDF（≤5MB）", false); bPrep.Click += (s, e) => { if (cur != null) RunPrep(cur); };
            var bCache = Ui.SoftBtn("打开上传副本文件夹"); bCache.Click += (s, e) => Ui.Reveal(JwPdf.CacheDir);
            busyButtons.Add(bPrep);
            attOps.Controls.AddRange(new Control[] { bPrep, bCache, Ui.Lbl("双击原件打开原文件，双击“上传副本”打开压缩后的 PDF。原件不改动。", 8.5f, false, Ui.Sub) });
            ta.Controls.Add(attOps);

            preGrid = Ui.Grid(); preGrid.Dock = DockStyle.Fill; preGrid.AllowUserToAddRows = false; preGrid.ReadOnly = true;
            foreach (var c in new[] { "模块", "附件类别", "要求", "状态", "找到的文件", "依据", "可信度" })
                preGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = c, SortMode = DataGridViewColumnSortMode.NotSortable });
            SetWidths(preGrid, 90, 170, 60, 220, 420, 300, 60);
            preGrid.Columns[4].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            preGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            var tp = new TabPage("缺件预检"); tp.Controls.Add(preGrid);
            tabs.TabPages.AddRange(new[] { tf, ta, tp });

            main.Controls.Add(tabs); main.Controls.Add(ops); main.Controls.Add(projTitle);

            // ---- 底：记录
            log = new TextBox { Dock = DockStyle.Bottom, Height = Ui.S(110), Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, Font = Ui.F(9f), BackColor = Color.White };

            Controls.Add(main); Controls.Add(left); Controls.Add(log); Controls.Add(top);
            LoadProjects(); ShowAcct();
            FormClosing += (s, e) => { if (busy && !Ui.Ask("还在处理，确定关闭吗？")) e.Cancel = true; };
        }

        class Item { public Project P; public string Tag; public override string ToString() { return P.Name + (Tag != "" ? "  " + Tag : ""); } }
        readonly HashSet<string> chosen = new HashSet<string>();

        static void SetWidths(DataGridView g, params int[] w) { for (int i = 0; i < w.Length && i < g.Columns.Count; i++) g.Columns[i].Width = Ui.S(w[i]); }

        void ShowAcct()
        {
            var live = portal.Accounts.Where(a => !a.Disabled).ToList();
            acctLbl.Text = portal.Name + " · " + portal.Url + " · " + (live.Count == 0 ? "还没有账号（点“网址和账号…”添加）" : live.Count + " 个账号，默认 " + (portal.AccountFor("") != null ? portal.AccountFor("").ToString() : ""));
        }

        void LoadProjects()
        {
            loading = true;
            var sel = cur;
            projList.BeginUpdate(); projList.Items.Clear();
            string f = filter.Text.Trim();
            foreach (var p in st.RealProjects.Where(p => f == "" || p.Name.Contains(f) || (p.Code ?? "").Contains(f)))
            {
                var d = JwProjectData.Load(st, p.Id);
                string tag = d.Extracted != "" ? "［已查 " + d.Extracted.Substring(5, 5) + "］" : "";
                var it = new Item { P = p, Tag = tag };
                var li = new ListViewItem(it.ToString()) { Tag = it, Checked = chosen.Contains(p.Id) };
                if (p == sel) li.Selected = true;
                projList.Items.Add(li);
            }
            projList.EndUpdate();
            loading = false;
        }

        void Log(string s)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; }
            log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + "\r\n");
        }

        void Busy(bool b) { busy = b; foreach (var c in busyButtons) c.Enabled = !b; Cursor = b ? Cursors.AppStarting : Cursors.Default; }

        // ------------------------------------------------------------ 显示当前项目
        void ShowProject(Project p)
        {
            loading = true;
            cur = p; data = JwProjectData.Load(st, p.Id);
            JwPrecheck.Result r;
            if (!pre.TryGetValue(p.Id, out r)) { r = JwPrecheck.Check(st, p); pre[p.Id] = r; }
            var acc = portal.AccountFor(p.Id);
            projTitle.Text = p.Name + "    " + r.Status + (r.Direct ? " · 直接委托" : "") + (acc != null ? "    账号：" + acc : "") + (data.Extracted != "" ? "    上次查资料：" + data.Extracted : "    还没查资料");
            projTitle.ForeColor = r.Missing == 0 ? Ui.Text : Ui.Orange;
            supervised.Checked = p.Supervised;

            fieldGrid.Rows.Clear();
            var fields = data.Fields.Count > 0 ? data.Fields : JwExtract.Defs.Where(d => d.Kind != "picker").Select(d => new JwField { Module = d.Module, Group = d.Group, Label = d.Label, Unit = d.Unit }).ToList();
            foreach (var f in fields)
            {
                int i = fieldGrid.Rows.Add(f.Module, f.Group, f.Label, f.Unit, f.Value, f.Raw, f.Source, f.Confidence + (f.Edited ? "（人改）" : ""));
                var row = fieldGrid.Rows[i]; row.Tag = f;
                if (f.Value == "") row.Cells[4].Style.BackColor = Color.FromArgb(245, 245, 245);
                if (f.Value == "") row.DefaultCellStyle.ForeColor = Ui.Sub;
                else if (f.Confidence == "低" || f.Source.StartsWith("【")) row.DefaultCellStyle.ForeColor = Ui.Orange;
            }
            notes.Text = data.Notes.Count > 0 ? "AI 提示需要核对：\r\n" + string.Join("\r\n", data.Notes.Select((n, i) => (i + 1) + ". " + n)) : data.Extracted == "" ? "还没查资料：在左边勾选这个项目，点“AI 查资料”。" : "";

            // 还没查资料时，先按缺件预检列出附件，方便先看
            if (data.Attachments.Count == 0 && r.Slots.Any(x => x.Files.Count > 0)) { JwExtract.FillAttachments(st, p, data, r); data.ProjectId = p.Id; data.Save(st); }
            attGrid.Rows.Clear();
            foreach (var a in data.Attachments)
            {
                string state = a.Uploaded != "" ? a.Uploaded : a.Pdf == "" ? "未准备" : File.Exists(a.Pdf) ? "待上传" : "副本丢了，请重新准备";
                int i = attGrid.Rows.Add(a.Use, a.Module, a.Kind, Path.GetFileName(a.Rel), Ui.Size(a.OrigSize), a.PdfSize > 0 ? Ui.Size(a.PdfSize) : "", a.Level, a.Why, state);
                attGrid.Rows[i].Tag = a;
                if (a.PdfSize > JwPdf.Limit || state.StartsWith("副本")) attGrid.Rows[i].DefaultCellStyle.ForeColor = Ui.Red;
            }

            preGrid.Rows.Clear();
            foreach (var s in r.Slots)
            {
                int i = preGrid.Rows.Add(s.Module, s.Kind, s.Required ? "必传" : s.Module == "招投标信息" ? "暂免" : "参考", s.Status, string.Join("\n", s.Files.Select(x => x.Name)), s.Why, s.Confidence);
                if (s.Required && s.Files.Count == 0) preGrid.Rows[i].DefaultCellStyle.ForeColor = Ui.Red;
                else if (s.Status.Contains("会议纪要")) preGrid.Rows[i].DefaultCellStyle.ForeColor = Ui.Orange;
            }
            loading = false;
        }

        void EditField(int row)
        {
            if (loading || row < 0 || data == null) return;
            var f = fieldGrid.Rows[row].Tag as JwField; if (f == null) return;
            string v = Convert.ToString(fieldGrid.Rows[row].Cells[4].Value ?? "").Trim();
            if (v == f.Value) return;
            if (!data.Fields.Contains(f)) data.Fields.Add(f);
            f.Value = v; f.Edited = true; data.Save(st);
            fieldGrid.Rows[row].Cells[7].Value = f.Confidence + "（人改）";
        }

        // ------------------------------------------------------------ AI 查资料
        List<Project> Chosen() { return st.RealProjects.Where(p => chosen.Contains(p.Id)).ToList(); }

        void RunAi() { RunAi(Chosen()); }

        void RunAi(List<Project> ps)
        {
            if (busy) return;
            if (ps.Count == 0) { Ui.Info("请先在左边勾选要报的项目（只查勾选的，不会全部都查）。"); return; }
            var cfg = AiConfig.Load();
            if (!cfg.Ready)
            {
                if (Ui.Ask("还没有设置 AI 接口（DeepSeek 等）的密钥。现在去设置吗？")) AiSettingsDialog.Show(this);
                cfg = AiConfig.Load(); if (!cfg.Ready) return;
            }
            if (ps.Count > 1 && !Ui.Ask("要让 AI 查 " + ps.Count + " 个项目的资料（缺件预检、抽取招投标和验收字段、准备附件 PDF）。\n每个项目大约要 1～3 分钟，确定开始吗？")) return;
            Busy(true);
            var th = new Thread(() =>
            {
                int ok = 0;
                foreach (var p in ps)
                {
                    try
                    {
                        Log("—— " + p.Name);
                        var r = JwPrecheck.Check(st, p);
                        lock (pre) pre[p.Id] = r;
                        Log("缺件预检：" + r.Status + (r.Missing > 0 ? "，缺 " + string.Join("、", r.Slots.Where(s => s.Required && s.Files.Count == 0).Select(s => s.Kind)) : ""));
                        var d = JwExtract.Run(st, cfg, p, Log);
                        JwExtract.FillAttachments(st, p, d, r);
                        d.Save(st);
                        Prep(p, d);
                        ok++;
                    }
                    catch (Exception ex) { Log("✘ " + p.Name + "：" + ex.Message); }
                }
                Log("完成：" + ok + " / " + ps.Count + " 个项目。请逐项核对右边的值和依据，橙色是可信度低的。");
                if (!IsDisposed) BeginInvoke(new Action(() => { Busy(false); LoadProjects(); if (cur != null) ShowProject(cur); else if (ps.Count > 0) ShowProject(ps[0]); }));
            }) { IsBackground = true };
            th.SetApartmentState(ApartmentState.STA);   // Word/Excel 转 PDF 要 STA
            th.Start();
        }

        void RunPrep(Project p)
        {
            if (busy) return;
            Busy(true);
            var th = new Thread(() =>
            {
                try { var d = JwProjectData.Load(st, p.Id); JwPrecheck.Result r; lock (pre) pre.TryGetValue(p.Id, out r); if (r == null) r = JwPrecheck.Check(st, p); JwExtract.FillAttachments(st, p, d, r); Prep(p, d); }
                catch (Exception ex) { Log("✘ " + ex.Message); }
                if (!IsDisposed) BeginInvoke(new Action(() => { Busy(false); if (cur == p) ShowProject(p); }));
            }) { IsBackground = true };
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
        }

        /// <summary>勾了“上传”的附件转成 ≤5MB 的 PDF 副本；拆成几份的，每份单独一行。</summary>
        void Prep(Project p, JwProjectData d)
        {
            foreach (var a in d.Attachments.Where(x => x.Use && x.Uploaded == "" && !x.Level.StartsWith("拆分")).ToList())
            {
                string src = Path.Combine(st.Lib.Root, a.Rel);
                try
                {
                    if (!File.Exists(src)) throw new IOException("原件不在了");
                    a.OrigSize = new FileInfo(src).Length;
                    string h = Store.Sha1(src);
                    if (a.Pdf != "" && File.Exists(a.Pdf) && a.Hash == h) continue;
                    Log("准备附件：" + a.Kind + " · " + Path.GetFileName(a.Rel));
                    var outs = JwPdf.Prepare(src);
                    a.Pdf = outs[0].Path; a.PdfSize = outs[0].Size; a.Level = outs[0].Level; a.Hash = h;
                    d.Attachments.RemoveAll(x => x != a && x.Rel == a.Rel && x.Kind == a.Kind && x.Level.StartsWith("拆分") && x.Uploaded == "");
                    int at = d.Attachments.IndexOf(a);
                    for (int i = 1; i < outs.Count; i++)
                        d.Attachments.Insert(at + i, new JwAttach { Module = a.Module, Kind = a.Kind, Rel = a.Rel, OrigSize = a.OrigSize, Pdf = outs[i].Path, PdfSize = outs[i].Size, Level = outs[i].Level, Why = a.Why, Confidence = a.Confidence, Hash = h });
                    if (outs[0].Level.Contains("较低") || outs.Count > 1) Log("　注意：" + Path.GetFileName(a.Rel) + " " + (outs.Count > 1 ? "拆成了 " + outs.Count + " 份" : outs[0].Level) + "，上传前打开看一眼是否清楚");
                }
                catch (Exception ex) { a.Level = "失败：" + ex.Message; Log("✘ " + a.Kind + "：" + ex.Message); }
            }
            d.Save(st);
        }

        void ExportMissing()
        {
            var ps = Chosen();
            if (ps.Count == 0) { if (!Ui.Ask("没有勾选项目。导出全部项目的缺件清单吗？")) return; ps = st.RealProjects.ToList(); }
            using (var dlg = new SaveFileDialog { Filter = "Excel 文件|*.xlsx", FileName = "交委填报缺件清单_" + DateTime.Now.ToString("yyyyMMdd") + ".xlsx" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var rs = ps.Select(p => { JwPrecheck.Result r; if (!pre.TryGetValue(p.Id, out r)) { r = JwPrecheck.Check(st, p); pre[p.Id] = r; } return r; }).ToList();
                JwPrecheck.Export(dlg.FileName, rs);
                Log("已导出缺件清单：" + dlg.FileName + "（" + rs.Count + " 个项目，缺件 " + rs.Count(x => x.Missing > 0) + " 个）");
                if (Ui.Ask("已导出。现在打开吗？")) Ui.Open(dlg.FileName);
            }
        }

        // ------------------------------------------------------------ 浏览器
        JwAccount AccountOrAsk()
        {
            var acc = portal.AccountFor(cur != null ? cur.Id : "");
            if (acc == null)
            {
                if (Ui.Ask("还没有设置交委系统账号。现在去设置吗？")) { JwSettingsDialog.Show(this, st); portal = JwPortal.Load(); ShowAcct(); }
                acc = portal.AccountFor(cur != null ? cur.Id : "");
            }
            return acc;
        }

        void Login()
        {
            if (busy) return;
            var acc = AccountOrAsk(); if (acc == null) return;
            if (acc.Password == "") { Ui.Info("账号 " + acc.User + " 还没有保存密码（或换了电脑 / Windows 用户，密码解不开了），请在“网址和账号…”里重新输入。"); return; }
            Busy(true);
            Log("打开 " + portal.Name + "：" + portal.Url + "（账号 " + acc.User + "）");
            var th = new Thread(() =>
            {
                string msg;
                try
                {
                    msg = browser.Login(portal, acc, host => (bool)Invoke(new Func<bool>(() =>
                        Ui.Ask("第一次向 " + host + " 发送账号密码。\n请确认这是交委“以奖代补”系统的地址（来自交委通知），再点“是”。\n\n以后换了网址主机，还会再问。"))));
                }
                catch (Exception ex) { msg = "✘ " + ex.Message; }
                Log(msg);
                if (!IsDisposed) BeginInvoke(new Action(() => Busy(false)));
            }) { IsBackground = true };
            th.Start();
        }

        void FillPage(string module)
        {
            if (busy) return;
            if (cur == null || data == null) { Ui.Info("请先在左边点选要填的项目。"); return; }
            if (data.Fields.Count == 0 || data.Fields.All(f => f.Value == "")) { Ui.Info("这个项目还没有可填的值。请先勾选它点“AI 查资料”，或在“字段核对”里手工填。"); return; }
            var acc = portal.AccountFor(cur.Id);
            var p = cur; var d0 = data; var fields = data.Fields.ToList(); bool over = overwrite.Checked;
            Busy(true);
            var th = new Thread(() =>
            {
                try
                {
                    var has = browser.PageHasText(portal, p.Name);
                    bool go = (bool)Invoke(new Func<bool>(() => Ui.Ask("把“" + p.Name + "”的值填进浏览器当前打开的“" + module + "”页面？\n" +
                        (has == false ? "\n⚠ 当前页面上没找到这个项目名，可能打开的是别的项目，请先确认！\n" : "") +
                        "\n只填空栏" + (over ? "，并覆盖已有值" : "，页面已有的值不覆盖") + "；这一页的附件也一起选入（表单要先保存过）。\n不会点保存、审核、上报。")));
                    if (!go) { Log("已取消填入。"); }
                    else
                    {
                        Log("填入“" + module + "”：" + p.Name);
                        var res = browser.Fill(portal, p.Name, acc != null ? acc.User : "", module, fields, JwMapping.Load(), over);
                        foreach (var r in res) Log("　" + r);
                        Log("填好 " + res.Count(r => r.Ok) + " 项，没填 " + res.Count(r => !r.Ok && r.Group != "提示") + " 项（绿框是软件填的）。请在浏览器里核对，补齐需要人选的栏（计划项目名称、项目名称等）。");
                        // 接着选入这一页的附件（系统要求表单先保存过才能传附件）
                        var todo = d0.Attachments.Where(a => a.Module == module && a.Use && a.Uploaded == "" && a.Pdf != "" && File.Exists(a.Pdf) && a.PdfSize <= JwPdf.Limit).ToList();
                        var notReady = d0.Attachments.Count(a => a.Module == module && a.Use && a.Uploaded == "" && (a.Pdf == "" || !File.Exists(a.Pdf)));
                        if (notReady > 0) Log("　有 " + notReady + " 个附件还没准备好 PDF：到“附件”页点“重新准备附件 PDF”。");
                        if (todo.Count == 0) Log("　这一页没有待传的附件（“附件”页里勾选“上传”并准备好 PDF 的才会传）。");
                        else
                        {
                            try
                            {
                                var ar = browser.Attach(portal, p.Name, acc != null ? acc.User : "", todo, JwMapping.Load(), s => Log("　" + s));
                                d0.Save(st);
                                Log("附件：选入 " + ar.Count(x => x.StartsWith("✔")) + " / " + todo.Count + " 个。请点附件区的“保存”上传，再点页面上方“保存”。");
                            }
                            catch (NotSavedException nse) { Log("◆ 附件没选：" + nse.Message); }
                        }
                        if (!IsDisposed) BeginInvoke(new Action(() => { if (cur == p) ShowProject(p); }));
                    }
                }
                catch (Exception ex) { Log("✘ " + ex.Message); }
                if (!IsDisposed) BeginInvoke(new Action(() => Busy(false)));
            }) { IsBackground = true };
            th.Start();
        }

        void AttachPage()
        {
            if (busy) return;
            if (cur == null || data == null) { Ui.Info("请先在左边点选项目。"); return; }
            var p = cur;
            var todo = data.Attachments.Where(a => a.Use && a.Uploaded == "" && a.Pdf != "" && File.Exists(a.Pdf) && a.PdfSize <= JwPdf.Limit).ToList();
            if (todo.Count == 0) { Ui.Info("没有待上传的附件。请在“附件”页勾选要传的，并先准备好 PDF 副本。"); return; }
            var mods = todo.Select(a => a.Module).Distinct().ToList();
            string module = mods.Count == 1 ? mods[0] : null;
            if (module == null)
            {
                module = Ui.Ask("待上传附件里既有招投标的，也有验收的。\n浏览器当前是“招投标信息”页面吗？\n（是：传招投标附件；否：传质量及验收附件）") ? JwExtract.Bid : JwExtract.Acc;
                todo = todo.Where(a => a.Module == module).ToList();
            }
            if (!Ui.Ask("把 " + todo.Count + " 个附件选入浏览器当前的“" + module + "”页面？\n\n" + string.Join("\n", todo.Select(a => "· " + a.Kind + "：" + Path.GetFileName(a.Pdf) + "（" + Ui.Size(a.PdfSize) + "）")) +
                "\n\n软件会在附件区点“新增”加行、选文件、选类型；不会点审核、上报、提交。")) return;
            var acc = portal.AccountFor(p.Id); var d = data;
            Busy(true);
            var th = new Thread(() =>
            {
                try
                {
                    var res = browser.Attach(portal, p.Name, acc != null ? acc.User : "", todo, JwMapping.Load(), s => Log("　" + s));
                    d.Save(st);
                    Log("附件：选入 " + res.Count(r => r.StartsWith("✔")) + " 个。请在浏览器里看每一行的文件和类型对不对，再自己保存。");
                }
                catch (Exception ex) { Log("✘ " + ex.Message); }
                if (!IsDisposed) BeginInvoke(new Action(() => { Busy(false); if (cur == p) ShowProject(p); }));
            }) { IsBackground = true };
            th.Start();
        }
    }

    // ===================================================== 网址和账号
    public static class JwSettingsDialog
    {
        public static void Show(IWin32Window owner, Store st)
        {
            var portal = JwPortal.Load();
            using (var f = new Form { Text = "交委系统网址和账号", Size = new Size(Ui.S(820), Ui.S(600)), StartPosition = FormStartPosition.CenterParent, Font = Ui.F(9.5f), BackColor = Color.White, MinimizeBox = false, MaximizeBox = false, FormBorderStyle = FormBorderStyle.Sizable })
            {
                int y = Ui.S(14), x = Ui.S(16);
                Func<string, int, Label> L = (t, yy) => { var l = new Label { Text = t, AutoSize = true, Location = new Point(x, yy + Ui.S(4)) }; f.Controls.Add(l); return l; };
                L("系统名称：", y);
                var name = new TextBox { Text = portal.Name, Location = new Point(Ui.S(110), y), Width = Ui.S(300) }; f.Controls.Add(name);
                y += Ui.S(36);
                L("登录网址：", y);
                var url = new TextBox { Text = portal.Url, Location = new Point(Ui.S(110), y), Width = Ui.S(540), Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right }; f.Controls.Add(url);
                var bDef = new Button { Text = "恢复通知地址", Location = new Point(Ui.S(660), y - 1), AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right }; f.Controls.Add(bDef);
                bDef.Click += (s, e) => url.Text = JwPortal.NoticeUrl;
                y += Ui.S(32);
                var hint = new Label { Text = "网址、账号以交委最新通知为准，改了立即生效，不用重装。换了网址主机后，第一次登录会再请你确认。", AutoSize = true, ForeColor = Ui.Sub, Location = new Point(x, y) }; f.Controls.Add(hint);
                y += Ui.S(30);
                L("账号（一个乡镇可以有几个账号；“对应项目”没设的项目用默认账号）：", y);
                y += Ui.S(28);
                var lv = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, Location = new Point(x, y), Size = new Size(Ui.S(770), Ui.S(300)), Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom };
                lv.Columns.Add("用户名", Ui.S(140)); lv.Columns.Add("显示名", Ui.S(170)); lv.Columns.Add("密码", Ui.S(80)); lv.Columns.Add("状态", Ui.S(110)); lv.Columns.Add("对应项目", Ui.S(250));
                f.Controls.Add(lv);
                Action refresh = () =>
                {
                    lv.Items.Clear();
                    foreach (var a in portal.Accounts)
                    {
                        var it = new ListViewItem(a.User) { Tag = a };
                        it.SubItems.Add(a.Display); it.SubItems.Add(a.PwData != "" ? "已保存" : "未保存");
                        it.SubItems.Add((a.Disabled ? "停用" : "在用") + (a.User == portal.DefaultUser ? " · 默认" : ""));
                        it.SubItems.Add(a.Projects.Count == 0 ? "" : a.Projects.Count + " 个：" + string.Join("、", a.Projects.Select(id => { var p = st.Lib.Projects.FirstOrDefault(q => q.Id == id); return p != null ? p.Name : id; }).Take(3)));
                        if (a.Disabled) it.ForeColor = Ui.Sub;
                        lv.Items.Add(it);
                    }
                };
                refresh();
                Func<JwAccount> sel = () => lv.SelectedItems.Count == 0 ? null : (JwAccount)lv.SelectedItems[0].Tag;
                y += Ui.S(310);
                var row = new FlowLayoutPanel { Location = new Point(x - Ui.S(3), y), Size = new Size(Ui.S(780), Ui.S(40)), Anchor = AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right };
                f.Controls.Add(row);
                Action<string, Action> B = (t, a) => { var b = new Button { Text = t, AutoSize = true, Height = Ui.S(30) }; b.Click += (s, e) => a(); row.Controls.Add(b); };
                B("新增…", () => { var a = new JwAccount(); if (EditAccount(f, st, a, portal, true)) { portal.Accounts.Add(a); if (portal.DefaultUser == "") portal.DefaultUser = a.User; refresh(); } });
                B("修改…", () => { var a = sel(); if (a != null && EditAccount(f, st, a, portal, false)) refresh(); });
                B("删除", () => { var a = sel(); if (a != null && Ui.Ask("删除账号 " + a.User + "？（本机保存的密码也一起删掉）")) { portal.Accounts.Remove(a); if (portal.DefaultUser == a.User) portal.DefaultUser = ""; refresh(); } });
                B("停用 / 启用", () => { var a = sel(); if (a != null) { a.Disabled = !a.Disabled; refresh(); } });
                B("设为默认", () => { var a = sel(); if (a != null) { portal.DefaultUser = a.User; refresh(); } });
                var ok = new Button { Text = "保存", DialogResult = DialogResult.OK, AutoSize = true, Height = Ui.S(30) };
                var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true, Height = Ui.S(30) };
                row.Controls.Add(new Label { Width = Ui.S(60) }); row.Controls.Add(ok); row.Controls.Add(cancel);
                f.AcceptButton = ok; f.CancelButton = cancel;
                lv.DoubleClick += (s, e) => { var a = sel(); if (a != null && EditAccount(f, st, a, portal, false)) refresh(); };
                if (f.ShowDialog(owner) != DialogResult.OK) return;
                string u = url.Text.Trim();
                Uri tmp;
                if (!Uri.TryCreate(u, UriKind.Absolute, out tmp) || (tmp.Scheme != "https" && tmp.Scheme != "http")) { Ui.Info("网址不对，没保存：" + u); return; }
                if (u != portal.Url || name.Text.Trim() != portal.Name) portal.SessionStamp++;
                portal.Name = name.Text.Trim() == "" ? "以奖代补系统" : name.Text.Trim();
                portal.Url = u;
                portal.Save();
            }
        }

        static bool EditAccount(IWin32Window owner, Store st, JwAccount a, JwPortal portal, bool isNew)
        {
            using (var f = new Form { Text = isNew ? "新增账号" : "修改账号", Size = new Size(Ui.S(560), Ui.S(560)), StartPosition = FormStartPosition.CenterParent, Font = Ui.F(9.5f), BackColor = Color.White, MinimizeBox = false, MaximizeBox = false, FormBorderStyle = FormBorderStyle.FixedDialog })
            {
                int x = Ui.S(16), y = Ui.S(16);
                Func<string, TextBox> T = (label) =>
                {
                    f.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(x, y + Ui.S(4)) });
                    var tb = new TextBox { Location = new Point(Ui.S(110), y), Width = Ui.S(400) }; f.Controls.Add(tb); y += Ui.S(36); return tb;
                };
                var user = T("用户名："); user.Text = a.User;
                var disp = T("显示名："); disp.Text = a.Display;
                var pw = T("密码："); pw.UseSystemPasswordChar = true;
                f.Controls.Add(new Label { Text = isNew ? "密码只用本机当前 Windows 用户能解开的方式加密保存，不进档案库、不进安装包、不写记录。" : "密码留空 = 不改。密码只加密保存在本机。", AutoSize = false, Size = new Size(Ui.S(510), Ui.S(36)), ForeColor = Ui.Sub, Location = new Point(x, y) });
                y += Ui.S(40);
                f.Controls.Add(new Label { Text = "对应项目（这些项目用这个账号填报，可不选）：", AutoSize = true, Location = new Point(x, y) });
                y += Ui.S(24);
                var cl = new CheckedListBox { Location = new Point(x, y), Size = new Size(Ui.S(510), Ui.S(240)), CheckOnClick = true, IntegralHeight = false };
                foreach (var p in st.RealProjects) cl.Items.Add(new KeyValuePair<string, string>(p.Id, p.Name), a.Projects.Contains(p.Id));
                cl.DisplayMember = "Value";
                f.Controls.Add(cl);
                y += Ui.S(250);
                var ok = new Button { Text = "确定", Location = new Point(Ui.S(340), y), AutoSize = true, Height = Ui.S(30) };
                var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(Ui.S(430), y), AutoSize = true, Height = Ui.S(30) };
                f.Controls.Add(ok); f.Controls.Add(cancel); f.CancelButton = cancel;
                ok.Click += (s, e) =>
                {
                    string u = user.Text.Trim();
                    if (u == "") { Ui.Info("请填用户名。"); return; }
                    if (portal.Accounts.Any(o => o != a && o.User == u)) { Ui.Info("已有这个用户名。"); return; }
                    if (isNew && pw.Text == "" && !Ui.Ask("没填密码。以后登录时要自己在浏览器里输密码。继续吗？")) return;
                    f.DialogResult = DialogResult.OK;
                };
                if (f.ShowDialog(owner) != DialogResult.OK) return false;
                if (portal.DefaultUser == a.User && a.User != user.Text.Trim()) portal.DefaultUser = user.Text.Trim();
                a.User = user.Text.Trim(); a.Display = disp.Text.Trim();
                if (pw.Text != "") a.Password = pw.Text;
                a.Projects = cl.CheckedItems.Cast<KeyValuePair<string, string>>().Select(kv => kv.Key).ToList();
                // 一个项目只对应一个账号
                foreach (var o in portal.Accounts.Where(o => o != a)) o.Projects.RemoveAll(id => a.Projects.Contains(id));
                return true;
            }
        }
    }
}
