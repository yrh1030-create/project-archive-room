using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DangAn
{
    public class ImportForm : Form
    {
        readonly MainForm m; readonly string[] paths; Store St { get { return m.St; } }
        ComboBox proj; CheckBox byCat; TextBox log; Button go, cancel;

        public ImportForm(MainForm m, string[] paths, Project target)
        {
            this.m = m; this.paths = paths;
            Text = "导入资料"; Font = Ui.F(9.5f); BackColor = Color.White; ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.Sizable;
            Size = new Size(Ui.S(760), Ui.S(560)); MinimumSize = new Size(Ui.S(600), Ui.S(420));

            var top = new Panel { Height = Ui.S(150), Padding = new Padding(Ui.S(18)) };
            var t = Ui.Lbl("准备导入 " + paths.Length + " 项：" + string.Join("、", paths.Take(3).Select(Path.GetFileName)) + (paths.Length > 3 ? " 等" : ""), 10.5f, true, Ui.Text);
            t.Location = new Point(Ui.S(18), Ui.S(16)); t.MaximumSize = new Size(Ui.S(700), 0);
            var l = Ui.Lbl("归入项目：", 9.5f, false, Ui.Sub); l.Location = new Point(Ui.S(18), Ui.S(62));
            proj = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(Ui.S(100), Ui.S(58)), Width = Ui.S(520) };
            foreach (var p in St.RealProjects) proj.Items.Add(p);
            proj.Items.Add("＋ 新建项目…");
            proj.DisplayMember = "Name";
            var guess = target ?? Guess();
            if (guess != null && proj.Items.Contains(guess)) proj.SelectedItem = guess;
            proj.SelectedIndexChanged += (s, e) =>
            {
                if (proj.SelectedItem is string)
                {
                    var n = Ui.Prompt("新建项目", "项目名称：", paths.Length == 1 ? Path.GetFileNameWithoutExtension(paths[0]) : "");
                    if (string.IsNullOrEmpty(n)) { proj.SelectedIndex = -1; return; }
                    var p = new Project { Id = St.NewProjectId(), Name = n, Type = Store.GuessType(n) };
                    p.Code = p.Id; p.Required = Store.DefaultRequired(p.Type);
                    Builder.Complete(St, p);
                    St.Lib.Projects.Add(p);
                    proj.Items.Insert(proj.Items.Count - 1, p);
                    proj.SelectedItem = p;
                }
            };
            byCat = new CheckBox { Text = "按资料分类建子文件夹存放（如“04 合同协议”）；不勾选则保持原有文件夹结构", AutoSize = true, Location = new Point(Ui.S(18), Ui.S(100)), Checked = paths.All(File.Exists) && !paths.Any(Store.IsArchive) };
            top.Controls.AddRange(new Control[] { t, l, proj, byCat });

            log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = Color.FromArgb(250, 250, 251), Font = Ui.F(9f), BorderStyle = BorderStyle.FixedSingle };
            log.Text = "说明：资料会复制到该项目的文件夹里（原文件保留不动）；压缩包自动解压；内容完全相同的文件只录一次。\r\n";
            var bottom = new Panel { Height = Ui.S(60), Dock = DockStyle.Bottom, Padding = new Padding(Ui.S(14), Ui.S(10), Ui.S(14), Ui.S(10)) };
            var br = Ui.Row(); br.Dock = DockStyle.Right;
            go = Ui.Btn("开始导入", true); go.Click += (s, e) => Run();
            cancel = Ui.Btn("关闭", false); cancel.Click += (s, e) => Close();
            br.Controls.AddRange(new Control[] { go, cancel });
            bottom.Controls.Add(br);
            var mid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(18), 0, Ui.S(18), 0) };
            log.Dock = DockStyle.Fill; mid.Controls.Add(log);
            Controls.Add(bottom);
            Ui.Stack(this, mid, top);
        }

        Project Guess()
        {
            Project best = null; int bestScore = 0;
            foreach (var path in paths)
            {
                string n = Path.GetFileNameWithoutExtension(path.TrimEnd('\\'));
                string dir = Path.GetFileName(Path.GetDirectoryName(path.TrimEnd('\\')) ?? "");
                foreach (var p in St.RealProjects)
                {
                    int score = 0;
                    foreach (var key in new[] { p.Name }.Concat(p.Folders))
                    {
                        if (n.Contains(key) || key.Contains(n) && n.Length >= 4) score = Math.Max(score, 100 + key.Length);
                        else if (dir.Length >= 4 && (dir.Contains(key) || key.Contains(dir))) score = Math.Max(score, 50 + key.Length);
                        else score = Math.Max(score, Common(n, key));
                    }
                    if (score > bestScore) { bestScore = score; best = p; }
                }
            }
            return bestScore >= 6 ? best : null;
        }

        static int Common(string a, string b)
        {
            // 最长公共子串长度，用于“2023年第一批通组公路xx.pdf”这类文件名
            int best = 0;
            for (int i = 0; i < a.Length; i++)
                for (int j = 0; j < b.Length; j++)
                {
                    int k = 0;
                    while (i + k < a.Length && j + k < b.Length && a[i + k] == b[j + k]) k++;
                    if (k > best) best = k;
                }
            return best;
        }

        void Run()
        {
            var p = proj.SelectedItem as Project;
            if (p == null) { Ui.Info("请先选择要归入的项目。"); return; }
            go.Enabled = false; cancel.Enabled = false; proj.Enabled = false; byCat.Enabled = false;
            bool cat = byCat.Checked;
            Task.Factory.StartNew(() =>
            {
                List<string> res;
                try
                {
                    res = St.Import(paths, p, cat, s => BeginInvoke(new Action(() => Text = s)));
                    St.Scan(null);
                    St.Save();
                }
                catch (Exception ex) { res = new List<string> { "× 出错：" + ex.Message }; }
                BeginInvoke(new Action(() =>
                {
                    int ok = res.Count(x => x.StartsWith("✓")), dup = res.Count(x => x.StartsWith("·")), bad = res.Count(x => x.StartsWith("×"));
                    log.AppendText("\r\n" + string.Join("\r\n", res) + "\r\n\r\n完成：导入 " + ok + " 项，重复跳过 " + dup + " 项" + (bad > 0 ? "，失败 " + bad + " 项" : "") + "。\r\n");
                    Text = "导入完成"; cancel.Enabled = true; cancel.Text = "完成";
                }));
            });
        }
    }
}
