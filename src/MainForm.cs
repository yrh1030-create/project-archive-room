using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DangAn
{
    public interface IPage { void Reload(); string Crumb { get; } }

    public class MainForm : Form
    {
        public Store St;
        Panel host;
        Label crumb, statusLbl;
        Button stopOcr;
        readonly Dictionary<string, Control> pages = new Dictionary<string, Control>();
        readonly List<NavItem> navs = new List<NavItem>();
        string current;
        CancellationTokenSource ocrCts;
        public bool OcrRunning { get { return ocrCts != null; } }

        public MainForm(Store st)
        {
            St = st;
            Text = "项目档案室";
            Font = Ui.F(9.5f);
            BackColor = Ui.Bg;
            StartPosition = FormStartPosition.CenterScreen;
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(Ui.S(1400), wa.Width), Math.Min(Ui.S(900), wa.Height));
            MinimumSize = new Size(Ui.S(1000), Ui.S(640));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            AllowDrop = true;
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) =>
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                Control cp;
                if (current == "AI 助手" && pages.TryGetValue("AI 助手", out cp)) ((ChatPage)cp).Attach(files.Where(File.Exists));   // AI 助手页：拖进来的是附件
                else Import(files, CurrentProject);
            };

            SuspendLayout();
            // 左侧导航
            var side = new Panel { Dock = DockStyle.Left, Width = Ui.S(228), BackColor = Ui.Side, Padding = new Padding(Ui.S(14), Ui.S(20), Ui.S(14), Ui.S(14)) };
            side.Paint += (s, e) => { using (var p = new Pen(Color.FromArgb(226, 230, 236))) e.Graphics.DrawLine(p, side.Width - 1, 0, side.Width - 1, side.Height); };
            var logo = new Panel { Height = Ui.S(58), BackColor = Color.Transparent };
            logo.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                AppIcon.Draw(g, new RectangleF(Ui.S(4), (logo.Height - Ui.S(42)) / 2f, Ui.S(42), Ui.S(42)));
                using (var f1 = Ui.F(14f, FontStyle.Bold)) TextRenderer.DrawText(g, "项目档案室", f1, new Point(Ui.S(54), Ui.S(6)), Ui.Text);
                using (var f2 = Ui.F(8.5f)) TextRenderer.DrawText(g, "工程资料 · 一项目一档", f2, new Point(Ui.S(56), Ui.S(34)), Ui.Sub);
            };
            var ws = new Label { Text = "工作空间", ForeColor = Color.FromArgb(140, 148, 160), Font = Ui.F(8.5f), Dock = DockStyle.Top, Height = Ui.S(40), TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(Ui.S(8), 0, 0, Ui.S(6)) };
            var navPanel = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Color.Transparent };
            var navDefs = new[] { new[] { "项目总览", "grid" }, new[] { "全文查资料", "search" }, new[] { "智能填表", "table" }, new[] { "AI 助手", "chat" }, new[] { "设置与备份", "gear" } };
            foreach (var n in navDefs)
            {
                var b = new NavItem(n[0], n[1]) { Width = Ui.S(200), Margin = new Padding(0, 0, 0, Ui.S(2)) };
                string name = n[0];
                b.Click += (s, e) => ShowPage(name);
                navs.Add(b); navPanel.Controls.Add(b);
            }
            var foot = new Card { Dock = DockStyle.Bottom, Height = Ui.S(78), Flat = true, Fill = Color.FromArgb(248, 250, 252), Padding = new Padding(Ui.S(12), Ui.S(10), Ui.S(10), Ui.S(8)) };
            var f1l = Ui.Lbl("● 资料保存在本机", 8.5f, true, Color.FromArgb(22, 150, 90)); f1l.Location = new Point(Ui.S(12), Ui.S(10));
            var f2l = new Label { Text = st.DbDir, ForeColor = Ui.Sub, Font = Ui.F(8f), AutoEllipsis = true, Location = new Point(Ui.S(12), Ui.S(34)), Size = new Size(Ui.S(170), Ui.S(36)), BackColor = Color.Transparent };
            foot.Controls.Add(f1l); foot.Controls.Add(f2l);
            side.Controls.Add(foot);
            var ver = new Label { Text = "版本 " + AppInfo.Version + " · 开发者 " + AppInfo.Developer, Dock = DockStyle.Bottom, Height = Ui.S(30), ForeColor = Color.FromArgb(150, 158, 170), Font = Ui.F(8f), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(Ui.S(6), 0, 0, 0), BackColor = Color.Transparent };
            side.Controls.Add(ver);
            Ui.Stack(side, null, logo, ws, navPanel);

            // 右侧
            var right = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg };
            var top = new Panel { Height = Ui.S(60), BackColor = Ui.Bg, Padding = new Padding(Ui.S(30), 0, Ui.S(24), 0) };
            crumb = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ui.Sub, Font = Ui.F(9.5f), AutoEllipsis = true };
            var topRight = Ui.Row(); topRight.Dock = DockStyle.Right; topRight.Padding = new Padding(0, Ui.S(12), 0, 0);
            var bk = Ui.Btn("备份档案库", false, "backup");
            bk.Click += (s, e) => DoBackup();
            topRight.Controls.Add(bk);
            top.Controls.Add(crumb); top.Controls.Add(topRight);
            var status = new Panel { Height = Ui.S(32), BackColor = Color.White, Padding = new Padding(Ui.S(30), 0, Ui.S(16), 0), Dock = DockStyle.Bottom };
            status.Paint += (s, e) => { using (var p = new Pen(Ui.Line)) e.Graphics.DrawLine(p, 0, 0, status.Width, 0); };
            statusLbl = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ui.Sub, Font = Ui.F(9f), AutoEllipsis = true };
            var sb = (RoundButton)Ui.Btn("暂停识别", false, "pause");
            sb.Style = RoundButton.Kind.Ghost; sb.Dock = DockStyle.Right; sb.Visible = false; sb.Font = Ui.F(9f); sb.Padding = new Padding(Ui.S(8), 0, Ui.S(8), 0); sb.Margin = new Padding(0);
            stopOcr = sb;
            stopOcr.Click += (s, e) => StopOcr();
            status.Controls.Add(statusLbl); status.Controls.Add(stopOcr);
            host = new Panel { BackColor = Ui.Bg, Padding = new Padding(Ui.S(30), Ui.S(4), Ui.S(30), Ui.S(16)) };
            right.Controls.Add(status);
            Ui.Stack(right, host, top);

            Controls.Add(right); Controls.Add(side);
            ResumeLayout();

            Shown += (s, e) => { ShowPage("项目总览"); BackgroundRefresh(); };
            FormClosing += (s, e) =>
            {
                StopOcr();
                Control pv2; if (pages.TryGetValue("项目", out pv2) && pv2.Visible) try { ((ProjectView)pv2).Commit(); } catch { }
                try { St.Save(); if (St.Lib.AutoBackup && !string.IsNullOrEmpty(St.Lib.BackupDir)) St.Backup(); } catch { }
            };
        }

        public void SetStatus(string s)
        {
            if (IsDisposed || Disposing) return;
            try { if (InvokeRequired) BeginInvoke(new Action(() => statusLbl.Text = s)); else statusLbl.Text = s; } catch (InvalidOperationException) { }
        }

        void OnUi(Action a)
        {
            if (IsDisposed || Disposing) return;
            try { BeginInvoke(a); } catch (InvalidOperationException) { }
        }

        public void ShowPage(string name)
        {
            Control page;
            if (!pages.TryGetValue(name, out page))
            {
                switch (name)
                {
                    case "项目总览": page = new OverviewPage(this); break;
                    case "全文查资料": page = new SearchPage(this); break;
                    case "智能填表": page = new FillPage(this); break;
                    case "AI 助手": page = new ChatPage(this); break;
                    default: page = new SettingsPage(this); break;
                }
                page.Dock = DockStyle.Fill; page.Visible = false;
                pages[name] = page; host.Controls.Add(page);
            }
            Control pv;
            if (pages.TryGetValue("项目", out pv) && pv.Visible) ((ProjectView)pv).Commit();
            foreach (var b in navs) { b.Selected = b.Text == name; b.Invalidate(); }
            foreach (var p in pages.Values) if (p != page) p.Visible = false;
            current = name;
            ((IPage)page).Reload();
            page.Visible = true;
            crumb.Text = ((IPage)page).Crumb;
        }

        public void ReloadAll()
        {
            foreach (var kv in pages) if (kv.Key == current) ((IPage)kv.Value).Reload();
            foreach (var kv in pages) if (kv.Key != current) kv.Value.Tag = "dirty";
        }

        /// <summary>在主窗口里打开项目页（概况、关键信息、合同台账、全部资料、备注在同一页）。</summary>
        public void OpenProject(Project p, int tab)
        {
            Control old;
            if (pages.TryGetValue("项目", out old)) { ((ProjectView)old).Commit(); host.Controls.Remove(old); old.Dispose(); }
            var view = new ProjectView(this, p) { Dock = DockStyle.Fill, Visible = false };
            pages["项目"] = view; host.Controls.Add(view);
            foreach (var kv in pages) if (kv.Value != view) kv.Value.Visible = false;
            foreach (var b in navs) { b.Selected = b.Text == "项目总览"; b.Invalidate(); }
            current = "项目";
            view.Reload();
            view.Visible = true;
            crumb.Text = view.Crumb;
            if (tab == 2) view.JumpTo("全部资料"); else if (tab == 3) view.JumpTo("合同台账");
        }

        public void SetCrumb(string s) { crumb.Text = s; }

        public Project CurrentProject
        {
            get { Control v; return current == "项目" && pages.TryGetValue("项目", out v) ? ((ProjectView)v).P : null; }
        }

        public void SearchFor(string q)
        {
            ShowPage("全文查资料");
            ((SearchPage)pages["全文查资料"]).Run(q);
        }

        /// <summary>启动时在后台查一遍新增/删除的文件，然后识别新文件的文字。</summary>
        public void BackgroundRefresh()
        {
            SetStatus("正在检查档案文件夹里的新资料…");
            Task.Factory.StartNew(() =>
            {
                try
                {
                    var r = St.Scan(null);
                    St.Save();
                    OnUi(new Action(() =>
                    {
                        SetStatus("档案库已是最新：共 " + St.Lib.Files.Count + " 份资料" + (r[0] + r[1] > 0 ? "（新增 " + r[0] + "，移除 " + r[1] + "）" : "") + "；全文可搜 " + St.Texts.Count + " 份");
                        ReloadAll();
                        if (TextJob.Pending(St).Count > 0) StartOcr();
                    }));
                }
                catch (Exception ex) { SetStatus("检查失败：" + ex.Message); }
            });
        }

        public void Rescan()
        {
            Cursor = Cursors.WaitCursor;
            try { var r = St.Scan(SetStatus); St.Save(); SetStatus("扫描完成：共 " + St.Lib.Files.Count + " 份，新增 " + r[0] + "，移除 " + r[1]); }
            finally { Cursor = Cursors.Default; }
            ReloadAll();
            if (TextJob.Pending(St).Count > 0) StartOcr();
        }

        public void StartOcr()
        {
            if (ocrCts != null) return;
            int n = TextJob.Pending(St).Count;
            if (n == 0) { SetStatus("所有资料都已识别过文字。"); return; }
            ocrCts = new CancellationTokenSource();
            var ct = ocrCts.Token;
            stopOcr.Visible = true;
            SetStatus("正在识别扫描件文字（共 " + n + " 份，可继续正常使用）…");
            var th = new Thread(() =>
            {
                int ok = 0;
                try { ok = TextJob.Run(St, ct, (d, all, name) => SetStatus("正在识别扫描件文字 " + d + " / " + all + "：" + name)); }
                catch (Exception ex) { SetStatus("识别中断：" + ex.Message); }
                OnUi(new Action(() =>
                {
                    ocrCts = null; stopOcr.Visible = false;
                    SetStatus((ct.IsCancellationRequested ? "已暂停识别。" : "文字识别完成。") + "本次识别 " + ok + " 份；全文可搜 " + St.Texts.Count + " 份");
                }));
            });
            th.IsBackground = true; th.Priority = ThreadPriority.BelowNormal;
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
        }

        public void StopOcr() { if (ocrCts != null) ocrCts.Cancel(); }

        public void Import(string[] paths, Project target)
        {
            if (paths == null || paths.Length == 0) return;
            // 在总览页拖入的全是文件夹：走“一键导入”，自动识别里面的各个项目
            if (target == null && paths.All(Directory.Exists)) { BatchImportFolders(paths); return; }
            using (var f = new ImportForm(this, paths, target)) f.ShowDialog(this);
            ReloadAll();
            if (TextJob.Pending(St).Count > 0) StartOcr();
        }

        /// <summary>导出“经济合同 PDF 包”（pdf 文件夹 + 合同PDF清单.xlsx + 合同预览.html）。</summary>
        public void ExportContractPdfs(List<Project> projects, string title)
        {
            int n = projects.Sum(p => p.Contracts.Count);
            if (n == 0) { Ui.Info("没有合同记录可导出。先在项目的“合同台账”里登记合同。"); return; }
            string outParent;
            using (var d = new FolderBrowserDialog { Description = "选择导出位置（会在里面新建“经济合同PDF_日期”文件夹）", SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                outParent = d.SelectedPath;
            }
            SetStatus("正在导出合同 PDF（共 " + n + " 条合同）…");
            var th = new Thread(() =>
            {
                ContractExport.Result r = null; string err = null;
                try { r = ContractExport.Run(St, projects, outParent, title, SetStatus); }
                catch (Exception ex) { err = ex.Message; }
                OnUi(new Action(() =>
                {
                    if (err != null) { SetStatus("导出失败：" + err); Ui.Info("导出失败：" + err); return; }
                    SetStatus("合同 PDF 已导出：" + r.Docs + " 个 PDF，对应 " + r.Rows + " 条合同" + (r.Missing > 0 ? "，" + r.Missing + " 条没找到原件" : ""));
                    string msg = "已导出到：\n" + r.Folder + "\n\n" + r.Docs + " 个合同 PDF，对应台账 " + r.Rows + " 条（同一份文件只保留一份）"
                        + (r.Cleaned > 0 ? "\n其中 " + r.Cleaned + " 份去掉了夹带的发票、凭证等无关页" : "")
                        + (r.Missing > 0 ? "\n有 " + r.Missing + " 条合同没找到原件，已在清单里标出" : "")
                        + (r.Log.Count > 0 ? "\n\n" + string.Join("\n", r.Log.Take(8)) : "")
                        + "\n\n现在打开“合同预览.html”看看吗？";
                    if (Ui.Ask(msg)) Ui.Open(System.IO.Path.Combine(r.Folder, "合同预览.html"));
                    else Ui.Reveal(r.Folder);
                }));
            });
            th.IsBackground = true; th.SetApartmentState(ApartmentState.STA);   // 转 Word 需要 STA
            th.Start();
        }

        public void BatchImportFolders(string[] folders)
        {
            Cursor = Cursors.WaitCursor;
            BatchImportForm f;
            try { f = new BatchImportForm(this, folders); }
            finally { Cursor = Cursors.Default; }
            using (f) f.ShowDialog(this);
            ReloadAll();
            if (TextJob.Pending(St).Count > 0) StartOcr();
        }

        public void DoBackup()
        {
            if (string.IsNullOrEmpty(St.Lib.BackupDir))
            {
                using (var d = new FolderBrowserDialog { Description = "选择备份位置（可选百度网盘、坚果云等同步文件夹，或U盘）" })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    St.Lib.BackupDir = d.SelectedPath;
                }
            }
            try { var dir = St.Backup(); Ui.Info("已备份到：\n" + dir + "\n\n备份的是档案库（项目登记、分类、合同台账、全文索引），原始资料文件仍在原文件夹。"); }
            catch (Exception ex) { Ui.Info("备份失败：" + ex.Message); }
        }
    }
}
