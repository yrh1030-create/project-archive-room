using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DangAn
{
    /// <summary>为一笔收支挑选凭证 / 发票文件：本项目文件按“像不像”排序，可搜索，也可从电脑选择（自动收进项目文件夹）。</summary>
    public static class FilePicker
    {
        public static string Pick(IWin32Window owner, Store st, Project p, string title, Payment pay, bool voucher)
        {
            using (var f = new Form())
            {
                f.Text = title + " — " + pay.Summary; f.Font = Ui.F(9.5f); f.BackColor = Color.White; f.ShowIcon = false;
                f.StartPosition = FormStartPosition.CenterParent; f.Size = new Size(Ui.S(1000), Ui.S(680)); f.MinimizeBox = false;
                var head = new Panel { Dock = DockStyle.Top, Height = Ui.S(70), Padding = new Padding(Ui.S(16), Ui.S(14), Ui.S(16), 0) };
                var info = Ui.Lbl((pay.Date == "" ? "" : pay.Date + "  ·  ") + pay.Summary + "  ·  " + Ui.Money(pay.Amount) + " 元" + (pay.VoucherNo != "" ? "  ·  " + pay.VoucherNo : ""), 10f, true, Ui.Text);
                info.Location = new Point(Ui.S(16), Ui.S(8));
                var sb = new SearchBox("搜文件名…", 9.5f) { Location = new Point(Ui.S(16), Ui.S(34)), Width = Ui.S(360) };
                var onlyLikely = new CheckBox { Text = voucher ? "只看像凭证的文件" : "只看像发票的文件", Checked = true, AutoSize = true, Location = new Point(Ui.S(392), Ui.S(40)) };
                head.Controls.AddRange(new Control[] { info, sb, onlyLikely });
                var lv = Ui.List("文件名", 300, "分类", 110, "修改时间", 120, "所在文件夹", 380);
                lv.Dock = DockStyle.Fill; lv.MultiSelect = false; lv.ShowItemToolTips = true;
                var bottom = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(62), Padding = new Padding(Ui.S(16), Ui.S(12), Ui.S(16), Ui.S(10)) };
                var left = Ui.Row(); left.Dock = DockStyle.Left;
                var browse = Ui.Btn("从电脑选择…", false, "upload");
                var preview = Ui.Btn("打开看看", false, "open");
                left.Controls.AddRange(new Control[] { browse, preview });
                var right = Ui.Row(); right.Dock = DockStyle.Right;
                var ok = Ui.Btn("关联这个文件", true, "check"); var no = Ui.Btn("取消", false);
                right.Controls.AddRange(new Control[] { no, ok });
                bottom.Controls.Add(left); bottom.Controls.Add(right);
                f.Controls.Add(lv); f.Controls.Add(head); f.Controls.Add(bottom);
                lv.BringToFront();

                var dateTokens = new List<string>();
                DateTime d;
                if (DateTime.TryParse(pay.Date, out d)) { dateTokens.Add(d.Month + "月" + d.Day + "号"); dateTokens.Add(d.Month + "月" + d.Day + "日"); }
                var keys = Regex.Matches(pay.Summary ?? "", "第[一二三四五六七八九十0-9]+次|设计|监理|检测|招标代理|预算|造价|一审|审核|工程款|进度款|预付款|尾款|补偿|迁改|测量").Cast<Match>().Select(m => m.Value).Distinct().ToList();
                Func<FileRec, int> score = x =>
                {
                    int s = 0;
                    if (dateTokens.Any(t => x.Rel.Contains(t))) s += 10;
                    s += keys.Count(k => x.Rel.Contains(k)) * 3;
                    if (voucher && Regex.IsMatch(x.Name, "凭证|支付|回单|付款|拨款")) s += 4;
                    if (!voucher && Regex.IsMatch(x.Name, "发票|票据")) s += 8;
                    if (x.Cat == "09 资金支付") s += 2;
                    if (!voucher)
                    {
                        var inv = st.Lib.Invoices.FirstOrDefault(i => i.Rel == x.Rel);
                        if (inv != null) { s += 5; if (Math.Abs(inv.Amount - pay.Amount) <= 1) s += 40; }
                    }
                    return s;
                };
                var all = st.FilesOf(p).ToList();
                string picked = null;
                Action fill = () =>
                {
                    string q = sb.Text.Trim();
                    var list = all.Where(x => q == "" || x.Rel.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (onlyLikely.Checked && q == "")
                        list = list.Where(x => voucher ? (x.Cat == "09 资金支付" || Regex.IsMatch(x.Rel, "凭证|支付|回单|拨款")) : Regex.IsMatch(x.Rel, "发票|票据"));
                    lv.BeginUpdate(); lv.Items.Clear();
                    foreach (var x in list.OrderByDescending(score).ThenBy(x => x.Rel).Take(800))
                    {
                        var it = FileMenu.Item(st, x, false);
                        it.SubItems.RemoveAt(2);   // 去掉“大小”列，与表头对齐
                        lv.Items.Add(it);
                    }
                    lv.EndUpdate();
                    if (lv.Items.Count > 0) lv.Items[0].Selected = true;
                };
                sb.Box.TextChanged += (s, e) => fill();
                onlyLikely.CheckedChanged += (s, e) => fill();
                Func<FileRec> sel = () => lv.SelectedItems.Count == 0 ? null : lv.SelectedItems[0].Tag as FileRec;
                ok.Click += (s, e) => { var x = sel(); if (x == null) return; picked = x.Rel; f.DialogResult = DialogResult.OK; };
                lv.DoubleClick += (s, e) => ok.PerformClick();
                no.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                preview.Click += (s, e) => { var x = sel(); if (x != null) Ui.Open(st.FullPath(x)); };
                browse.Click += (s, e) =>
                {
                    using (var od = new OpenFileDialog { Title = "选择凭证 / 发票文件（PDF、图片、OFD 等）" })
                    {
                        if (od.ShowDialog(f) != DialogResult.OK) return;
                        string root = st.Lib.Root.TrimEnd('\\') + "\\";
                        if (od.FileName.StartsWith(root, StringComparison.OrdinalIgnoreCase)) picked = od.FileName.Substring(root.Length);
                        else
                        {
                            // 复制进项目文件夹的“09 资金支付”
                            st.Import(new[] { od.FileName }, p, true, null);
                            st.Save();
                            var hit = st.Lib.Files.LastOrDefault(x => x.Name == Path.GetFileName(od.FileName));
                            if (hit == null) { var dup = st.FindDuplicate(od.FileName); if (dup != null) hit = dup; }
                            if (hit != null) picked = hit.Rel;
                        }
                        if (picked != null) f.DialogResult = DialogResult.OK;
                    }
                };
                f.Shown += (s, e) => { fill(); sb.Box.Focus(); };
                return f.ShowDialog(owner) == DialogResult.OK ? picked : null;
            }
        }
    }

}
