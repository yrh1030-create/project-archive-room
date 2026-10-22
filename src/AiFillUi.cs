using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace DangAn
{
    /// <summary>AI 填上级报表：选表格 → AI 对应项目并查档案填写 → 核对 → 另存为新表格。</summary>
    public class AiFillForm : Form
    {
        readonly Store st; readonly string src;
        TextBox logBox; DataGridView grid; Button bSave, bAgain; Label status;
        AiPlan plan; bool busy;

        public static void Start(IWin32Window owner, Store st)
        {
            var cfg = AiConfig.Load();
            if (!cfg.Ready)
            {
                if (Ui.Ask("还没有设置 AI 接口（DeepSeek 等）的密钥。现在去设置吗？")) AiSettingsDialog.Show(owner);
                cfg = AiConfig.Load();
                if (!cfg.Ready) return;
            }
            string path;
            using (var d = new OpenFileDialog { Title = "选择上级发来的表格", Filter = "Excel 表格|*.xls;*.xlsx;*.xlsm;*.et|所有文件|*.*" })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return;
                path = d.FileName;
            }
            new AiFillForm(st, path).Show(owner);
        }

        public AiFillForm(Store st, string src)
        {
            this.st = st; this.src = src;
            Text = "AI 填表 — " + Path.GetFileName(src);
            Font = Ui.F(9.5f); BackColor = Ui.Bg; StartPosition = FormStartPosition.CenterParent;
            Size = new Size(Ui.S(1180), Ui.S(760)); MinimumSize = new Size(Ui.S(800), Ui.S(500));
            ShowIcon = false;

            var head = new Panel { Dock = DockStyle.Top, Height = Ui.S(70), Padding = new Padding(Ui.S(16), Ui.S(10), Ui.S(16), 0) };
            var t1 = Ui.Lbl(Path.GetFileName(src), 12f, true, Ui.Text); t1.Location = new Point(Ui.S(16), Ui.S(10));
            status = Ui.Lbl("准备中……", 9f, false, Ui.Sub); status.Location = new Point(Ui.S(16), Ui.S(42));
            head.Controls.AddRange(new Control[] { t1, status });

            logBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Top, Height = Ui.S(150), BorderStyle = BorderStyle.None, BackColor = Color.White, Font = Ui.F(9f) };
            grid = Ui.Grid(); grid.Dock = DockStyle.Fill; grid.AllowUserToAddRows = false;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "写入", Width = Ui.S(78) });
            foreach (var c in new[] { new object[] { "单元格", 90 }, new object[] { "栏目", 170 }, new object[] { "所在行", 190 }, new object[] { "填写内容（可直接改）", 320 }, new object[] { "依据（档案原文）", 380 } })
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = (string)c[0], Width = Ui.S((int)c[1]), SortMode = DataGridViewColumnSortMode.NotSortable });
            for (int i = 1; i < grid.Columns.Count; i++) grid.Columns[i].ReadOnly = i != 4;
            grid.Columns[4].DefaultCellStyle.BackColor = Color.FromArgb(255, 251, 235);
            grid.Columns[5].DefaultCellStyle.ForeColor = Ui.Sub;
            grid.Columns[5].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            foreach (int i in new[] { 2, 4, 5 }) grid.Columns[i].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(2)) }; card.Controls.Add(grid);

            var bottom = Ui.Row(); bottom.Dock = DockStyle.Bottom; bottom.Height = Ui.S(56); bottom.Padding = new Padding(Ui.S(12), Ui.S(10), 0, 0);
            bSave = Ui.Btn("写入并另存为新表格…", true, "excel"); bSave.Enabled = false; bSave.Click += (s, e) => SaveAs();
            bAgain = Ui.Btn("重新填写", false, "refresh"); bAgain.Enabled = false; bAgain.Click += (s, e) => Run();
            var bSet = Ui.Btn("AI 接口设置…", false, "gear"); bSet.Click += (s, e) => AiSettingsDialog.Show(this);
            bottom.Controls.AddRange(new Control[] { bSave, bAgain, bSet, Ui.Lbl("只填空白格、原表不动；填过的格子标黄，批注写依据。", 8.5f, false, Ui.Sub) });

            var mid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(16), 0, Ui.S(16), 0) };
            var gap = Ui.Gap(10); gap.Dock = DockStyle.Top;
            mid.Controls.Add(card); mid.Controls.Add(gap); mid.Controls.Add(logBox);
            Controls.Add(mid); Controls.Add(bottom); Controls.Add(head);
            Shown += (s, e) => Run();
            FormClosing += (s, e) => { if (busy && !Ui.Ask("AI 还在填写，确定关闭？")) e.Cancel = true; };
        }

        void Log(string s)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; }
            logBox.AppendText(s + Environment.NewLine);
        }

        void Status(string s) { if (!IsDisposed) BeginInvoke(new Action(() => status.Text = s)); }

        void Run()
        {
            if (busy) return;
            busy = true; bSave.Enabled = bAgain.Enabled = false; grid.Rows.Clear(); logBox.Clear();
            var cfg = AiConfig.Load();
            AiClient.TokensIn = AiClient.TokensOut = 0;
            var th = new Thread(() =>
            {
                AiPlan result = null; string err = null; var t0 = DateTime.Now;
                try
                {
                    Status("正在读取表格……");
                    var sheets = ExcelIO.Read(src);
                    Log("读到 " + sheets.Count + " 个工作表：" + string.Join("、", sheets.Select(x => "「" + x.Name + "」" + x.Rows + " 行")));
                    Status("AI（" + cfg.Provider + " · " + cfg.Model + "）正在对照档案填写，通常需要半分钟到几分钟……");
                    result = AiFiller.Run(st, cfg, sheets, Log);
                }
                catch (Exception ex) { err = ex is AiException ? ex.Message : ex.GetType().Name + "：" + ex.Message; }
                int secs = (int)(DateTime.Now - t0).TotalSeconds;
                if (IsDisposed) return;
                BeginInvoke(new Action(() =>
                {
                    busy = false; bAgain.Enabled = true;
                    if (err != null) { Log("出错：" + err); status.Text = "没有完成：" + err; status.ForeColor = Ui.Red; return; }
                    plan = result; ShowCells(plan);
                    if (plan.Skipped.Count > 0)
                    {
                        Log("—— 找不到依据、保持空白的格子 ——");
                        foreach (var x in plan.Skipped) Log("  " + x);
                    }
                    status.ForeColor = plan.Cells.Count > 0 ? Ui.Green : Ui.Orange;
                    status.Text = "完成：对上 " + plan.Blocks.Select(b => b.ProjectId).Distinct().Count() + " 个项目，填写 " + plan.Cells.Count + " 格，留空 " + plan.Skipped.Count + " 格。用时 " + secs + " 秒（约 " + (AiClient.TokensIn + AiClient.TokensOut) + " tokens）。请核对后写入。";
                }));
            });
            th.IsBackground = true; th.SetApartmentState(ApartmentState.STA); th.Start();
        }

        void ShowCells(AiPlan p)
        {
            grid.Rows.Clear();
            foreach (var c in p.Cells.OrderBy(c => c.Sheet).ThenBy(c => c.Row).ThenBy(c => c.Col))
            {
                int i = grid.Rows.Add(true, (p.Sheets.Count > 1 ? c.Sheet + "!" : "") + c.Cell, c.Header, c.RowLabel, c.Value, c.Source);
                grid.Rows[i].Tag = c;
            }
            bSave.Enabled = p.Cells.Count > 0;
        }

        void SaveAs()
        {
            grid.EndEdit();
            var cells = new List<AiCell>();
            foreach (DataGridViewRow r in grid.Rows)
            {
                var c = (AiCell)r.Tag;
                if (!Convert.ToBoolean(r.Cells[0].Value)) continue;
                c.Value = Convert.ToString(r.Cells[4].Value ?? "").Trim();
                if (c.Value != "") cells.Add(c);
            }
            if (cells.Count == 0) { Ui.Info("没有勾选要写入的格子。"); return; }
            string ext = Path.GetExtension(src), dst;
            using (var d = new SaveFileDialog { Title = "另存为", FileName = Path.GetFileNameWithoutExtension(src) + "_AI填写" + ext, Filter = "Excel 表格|*" + ext, InitialDirectory = Path.GetDirectoryName(src) })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                dst = d.FileName;
            }
            if (string.Equals(Path.GetFullPath(dst), Path.GetFullPath(src), StringComparison.OrdinalIgnoreCase)) { Ui.Info("请另存为一个新文件，不要覆盖原表。"); return; }
            Cursor = Cursors.WaitCursor; bSave.Enabled = false;
            string err = null; int n = 0;
            var th = new Thread(() => { try { List<string> sk; n = ExcelIO.Write(src, dst, cells, true, out sk); } catch (Exception ex) { err = ex.Message; } });
            th.SetApartmentState(ApartmentState.STA); th.Start();
            while (th.IsAlive) { Application.DoEvents(); Thread.Sleep(50); }
            Cursor = Cursors.Default; bSave.Enabled = true;
            if (err != null) { Ui.Info("保存失败：" + err + "\n（文件是否正在 Excel 里打开？）"); return; }
            if (Ui.Ask("已写入 " + n + " 格，保存为：\n" + dst + "\n\n现在打开看看吗？")) Ui.Open(dst);
        }
    }

    /// <summary>AI 接口设置（保存在本机，不随档案库分享）。</summary>
    public static class AiSettingsDialog
    {
        public static void Show(IWin32Window owner)
        {
            var cfg = AiConfig.Load();
            using (var f = new Form { Text = "AI 接口设置", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowIcon = false, ShowInTaskbar = false, StartPosition = FormStartPosition.CenterParent, Font = Ui.F(9.5f), BackColor = Color.White, ClientSize = new Size(Ui.S(620), Ui.S(330)) })
            {
                int y = Ui.S(16), lx = Ui.S(20), fx = Ui.S(130), fw = Ui.S(460);
                Func<string, Label> L = t => { var l = new Label { Text = t, Location = new Point(lx, y + Ui.S(3)), AutoSize = true }; f.Controls.Add(l); return l; };
                L("服务商");
                var prov = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(fx, y), Width = fw };
                foreach (var p in AiConfig.Presets) prov.Items.Add(p[0]);
                f.Controls.Add(prov); y += Ui.S(40);
                L("接口地址");
                var url = new TextBox { Location = new Point(fx, y), Width = fw, Text = cfg.BaseUrl }; f.Controls.Add(url); y += Ui.S(40);
                L("模型");
                var model = new TextBox { Location = new Point(fx, y), Width = fw, Text = cfg.Model }; f.Controls.Add(model); y += Ui.S(40);
                L("API 密钥");
                var key = new TextBox { Location = new Point(fx, y), Width = fw, Text = cfg.Key, UseSystemPasswordChar = true }; f.Controls.Add(key); y += Ui.S(34);
                var show = new CheckBox { Text = "显示密钥", Location = new Point(fx, y), AutoSize = true }; show.CheckedChanged += (s, e) => key.UseSystemPasswordChar = !show.Checked; f.Controls.Add(show); y += Ui.S(30);
                var tip = new Label { Text = "支持 DeepSeek、通义千问、Kimi、智谱等兼容 OpenAI 格式的接口。密钥只加密保存在本机当前 Windows 账户下，不会写进档案库，分享档案库或安装包都不会带出去。", Location = new Point(lx, y), Size = new Size(Ui.S(580), Ui.S(44)), ForeColor = Ui.Sub, Font = Ui.F(8.5f) };
                f.Controls.Add(tip);

                int idx = Array.FindIndex(AiConfig.Presets, p => p[0] == cfg.Provider);
                prov.SelectedIndex = idx < 0 ? AiConfig.Presets.Length - 1 : idx;
                bool loading = true;
                prov.SelectedIndexChanged += (s, e) =>
                {
                    if (loading) return;
                    var p = AiConfig.Presets[prov.SelectedIndex];
                    if (p[1] != "") { url.Text = p[1]; model.Text = p[2]; }
                };
                loading = false;

                Action apply = () => { cfg.Provider = (string)prov.SelectedItem; cfg.BaseUrl = url.Text.Trim(); cfg.Model = model.Text.Trim(); cfg.Key = key.Text.Trim(); };
                var bTest = Ui.Btn("测试连接", false); bTest.Location = new Point(lx, Ui.S(276));
                bTest.Click += (s, e) =>
                {
                    apply(); f.Cursor = Cursors.WaitCursor;
                    string reply = null, err = null;
                    var th = new Thread(() => { try { reply = AiClient.Chat(cfg, "你是测试助手。", "请只回复：连接成功", false, 20); } catch (Exception ex) { err = ex.Message; } });
                    th.IsBackground = true; th.Start();
                    while (th.IsAlive) { Application.DoEvents(); Thread.Sleep(50); }
                    f.Cursor = Cursors.Default;
                    Ui.Info(err == null ? "连接正常。AI 回复：" + AiClient.Cut(reply, 60) : err);
                };
                var bOk = Ui.Btn("保存", true); bOk.Location = new Point(Ui.S(400), Ui.S(276));
                bOk.Click += (s, e) => { apply(); cfg.Save(); f.DialogResult = DialogResult.OK; };
                var bCancel = Ui.Btn("取消", false); bCancel.Location = new Point(Ui.S(500), Ui.S(276)); bCancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.AddRange(new Control[] { bTest, bOk, bCancel });
                f.ShowDialog(owner);
            }
        }
    }
}
