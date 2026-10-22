using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace DangAn
{
    /// <summary>AI 从合同、凭证、回单、发票原文里整理出的一条合同 / 一笔收支，待人工核对。</summary>
    public class AiConItem { public Contract C; public Contract Existing; public bool Use; public string Status = "", Evidence = ""; }
    public class AiPayItem { public Payment P; public bool Use; public string Status = "", Evidence = ""; }
    public class AiFinPlan { public List<AiConItem> Contracts = new List<AiConItem>(); public List<AiPayItem> Payments = new List<AiPayItem>(); public List<string> Notes = new List<string>(); }

    /// <summary>调用用户设置的 AI 接口，把项目里的合同、收支整理进合同台账和财务。</summary>
    public static class AiFinance
    {
        const int Budget = 90000;

        const string System =
@"你是乡镇（街道）工程项目的财务档案整理员。根据【项目资料】，整理出本项目的合同台账和收支明细。只输出 JSON，不要输出别的文字。

一、contracts：本项目实际签订的合同、协议（施工、监理、设计、勘察、检测、测量、造价咨询/预算审核/结算审核、招标代理等；补充协议单独一条）。
  - kind：合同类型，如“施工合同”“监理合同”“设计合同”“预算审核合同”“招标代理合同”“施工补充协议”；
  - party：乙方 / 承包人 / 受托人全称；amount：签约合同价（元，数字）；date：签订日期 yyyy-MM-dd（看“订立时间”“签订日期”、签字盖章页、封面落款；原文只有年月就写 yyyy-MM，不要自己补日；找不到或识别不清写空）；
  - 签订日期是手写、识别成乱码或空白（如“2026年 月 日”“生年彐月工日”）时，date 写空，并在 note 里写“签订日期手写/空白，见原件第N页”（N 取摘录里【第N页】的页码）。
  - content：合同内容一句话；source：合同原件路径（照抄【文件清单】里的路径）；evidence：原文摘录（金额、乙方所在的那句话，60 字以内）。
  - 中标/成交通知书、采购公告不是合同，但没有合同原件时可以据此登记，并在 evidence 里写明“据成交通知书”。
二、payments：有付款依据的收支。支出的依据是记账凭证、国库集中支付凭证、银行付款回单、支付审批单；收入是上级资金拨付到账的凭证。
  - 只有发票、没有付款依据的，不算已付款，不要列入 payments，写进 notes；
  - date：付款日期；dir：“支出”或“收入”；amount：金额（元）；summary：摘要（如“支付施工预付款”）；party：收款单位；
  - contract：对应合同的 kind（与 contracts 或【已登记的合同】一致）；voucher_no：凭证号；voucher_file / invoice_file：凭证、发票文件路径（照抄清单，没有就空）；evidence：原文摘录。
三、【已登记的合同】【已登记的收支】里已有的，不要重复输出；已登记的内容如果有错，在 notes 里说明。
四、只依据给出的资料，不要编造金额和日期；拿不准的写进 notes。
五、【登记信息】是以前整理时手工登记的，可能有错。与合同、成交通知书、凭证等原件原文冲突时，一律以原文为准；金额以大写为准（大写、小写不一致时按大写），并在 notes 里指出登记信息哪里错了。

输出格式：
{""contracts"":[{""kind"":"""",""party"":"""",""amount"":0,""date"":"""",""content"":"""",""note"":"""",""source"":"""",""evidence"":""""}],
 ""payments"":[{""date"":"""",""dir"":""支出"",""amount"":0,""summary"":"""",""party"":"""",""contract"":"""",""voucher_no"":"""",""voucher_file"":"""",""invoice_file"":"""",""evidence"":""""}],
 ""notes"":[""""]}";

        static readonly Regex ContractFile = new Regex("合同|协议|中标|成交|委托书");
        static readonly Regex PayFile = new Regex("凭证|回单|支付|拨付|付款|拨款|国库|到账|明细");
        static readonly string[] ContractWords = { "签约合同价", "合同金额", "合同价款", "合同总价", "大写", "小写", "乙方", "承包人", "受托人", "签订", "订立时间", "签约日期", "预付款", "付款方式", "支付" };

        public static AiFinPlan Analyze(Store st, AiConfig cfg, Project p, Action<string> log)
        {
            log("整理项目资料……");
            string user = Context(st, p);
            log("交给 AI（" + cfg.Provider + " · " + cfg.Model + "）阅读合同、凭证、发票，约 " + (user.Length / 1000) + " 千字……");
            var o = AiClient.ParseJson(AiClient.Chat(cfg, System, user, true, 8000));
            var plan = new AiFinPlan();
            var files = st.FilesOf(p).ToList();

            foreach (var x in Arr(o, "contracts"))
            {
                var c = new Contract
                {
                    Kind = S(x, "kind"), Party = S(x, "party"), Amount = N(x, "amount"), Date = S(x, "date"),
                    Content = S(x, "content"), Source = Resolve(files, S(x, "source")), Note = S(x, "note") != "" ? "AI 整理：" + S(x, "note") : "AI 整理"
                };
                if (c.Kind == "" || (c.Party == "" && c.Amount == 0)) continue;
                var it = new AiConItem { C = c, Evidence = S(x, "evidence") };
                it.Existing = p.Contracts.FirstOrDefault(e => SameContract(e, c));
                if (it.Existing == null) { it.Status = "新增"; it.Use = true; }
                else
                {
                    var blanks = new List<string>();
                    if (it.Existing.Party == "" && c.Party != "") blanks.Add("相对方");
                    if (it.Existing.Amount == 0 && c.Amount > 0) blanks.Add("金额");
                    if (it.Existing.Date == "" && c.Date != "") blanks.Add("日期");
                    if (it.Existing.Source == "" && c.Source != "") blanks.Add("原件");
                    if (it.Existing.Date == "" && c.Date == "" && S(x, "note").Contains("日期")) blanks.Add("日期说明");
                    bool diff = it.Existing.Amount > 0 && c.Amount > 0 && Math.Abs(it.Existing.Amount - c.Amount) >= 1;
                    it.Status = "已有" + (blanks.Count > 0 ? "，补上：" + string.Join("、", blanks) : "") + (diff ? "；台账金额 " + Ui.Money(it.Existing.Amount) + "，原文 " + Ui.Money(c.Amount) + "（勾选则改成原文）" : "");
                    it.Use = blanks.Count > 0 && !diff;
                    if (blanks.Count == 0 && !diff) continue;
                }
                plan.Contracts.Add(it);
            }

            foreach (var x in Arr(o, "payments"))
            {
                var pay = new Payment
                {
                    Date = S(x, "date"), Dir = S(x, "dir").Contains("收") ? "收入" : "支出", Amount = N(x, "amount"), Summary = S(x, "summary"),
                    Party = S(x, "party"), Contract = S(x, "contract"), VoucherNo = S(x, "voucher_no"),
                    VoucherFile = Resolve(files, S(x, "voucher_file")), InvoiceFile = Resolve(files, S(x, "invoice_file")), Source = "AI整理"
                };
                if (pay.Amount <= 0) continue;
                var it = new AiPayItem { P = pay, Evidence = S(x, "evidence") };
                bool dup = p.Payments.Any(e => e.Dir == pay.Dir && Math.Abs(e.Amount - pay.Amount) < 0.01 && (e.Date == pay.Date || e.Date == "" || pay.Date == ""));
                it.Status = dup ? "已有同额同日的记录" : "新增"; it.Use = !dup;
                plan.Payments.Add(it);
            }
            foreach (var n in Arr0(o, "notes")) { var t = Convert.ToString(n).Trim(); if (t != "") plan.Notes.Add(t); }
            log("AI 整理出合同 " + plan.Contracts.Count + " 条、收支 " + plan.Payments.Count + " 笔。请核对后写入。");
            return plan;
        }

        /// <summary>把勾选的写进台账。返回写入条数。</summary>
        public static int Apply(Store st, Project p, AiFinPlan plan)
        {
            int n = 0;
            var touched = new List<Contract>();
            foreach (var it in plan.Contracts.Where(x => x.Use))
            {
                if (it.Existing == null) { p.Contracts.Add(it.C); touched.Add(it.C); n++; continue; }
                var e = it.Existing;
                if (e.Party == "") e.Party = it.C.Party;
                if (e.Date == "") e.Date = it.C.Date;
                if (e.Source == "") e.Source = it.C.Source;
                if (e.Content == "") e.Content = it.C.Content;
                if (e.Date == "" && it.C.Date == "" && it.C.Note.Contains("日期") && !e.Note.Contains("日期")) e.Note = (e.Note + "；" + it.C.Note.Replace("AI 整理：", "")).Trim('；');
                if (it.C.Amount > 0 && (e.Amount == 0 || Math.Abs(e.Amount - it.C.Amount) >= 1)) { e.Amount = it.C.Amount; touched.Add(e); }
                n++;
            }
            foreach (var it in plan.Payments.Where(x => x.Use))
            {
                var con = p.Contracts.FirstOrDefault(c => c.Kind == it.P.Contract) ?? p.Contracts.FirstOrDefault(c => it.P.Contract != "" && (c.Kind.Contains(it.P.Contract) || it.P.Contract.Contains(c.Kind)));
                if (con != null) { it.P.Contract = con.Kind; if (it.P.Party == "") it.P.Party = con.Party; }
                p.Payments.Add(it.P); n++;
            }
            p.Payments = p.Payments.OrderBy(x => x.Dir == "收入" ? 0 : 1).ThenBy(x => x.Date == "" ? "9999" : x.Date).ToList();
            // 新登记 / 改了金额的合同：按收支明细算已付、未付
            foreach (var c in touched)
            {
                var paid = p.Payments.Where(x => x.Dir != "收入" && x.Contract == c.Kind).Sum(x => x.Amount);
                if (paid > 0) { c.Paid = Math.Round(paid, 2); c.Unpaid = Math.Max(0, Math.Round(c.Amount - paid, 2)); }
                else if (c.Paid == 0) c.Unpaid = c.Amount;
            }
            Finance.AutoLink(st, p);
            return n;
        }

        static bool SameContract(Contract a, Contract b)
        {
            string ka = Regex.Replace(a.Kind, "合同|协议|书", ""), kb = Regex.Replace(b.Kind, "合同|协议|书", "");
            bool kind = ka != "" && kb != "" && (ka.Contains(kb) || kb.Contains(ka));
            bool party = a.Party != "" && b.Party != "" && (a.Party.Contains(b.Party) || b.Party.Contains(a.Party) || Lcs(a.Party, b.Party) >= 6);
            bool amt = a.Amount > 0 && b.Amount > 0 && Math.Abs(a.Amount - b.Amount) < 1;
            return (kind && (party || amt || a.Party == "")) || (party && amt);
        }

        static int Lcs(string a, string b)
        {
            int best = 0; var prev = new int[b.Length + 1];
            for (int i = 1; i <= a.Length; i++)
            {
                var cur = new int[b.Length + 1];
                for (int j = 1; j <= b.Length; j++) if (a[i - 1] == b[j - 1]) { cur[j] = prev[j - 1] + 1; best = Math.Max(best, cur[j]); }
                prev = cur;
            }
            return best;
        }

        /// <summary>AI 给的路径（相对项目文件夹或只有文件名）→ 档案里的相对路径。</summary>
        public static string Resolve(List<FileRec> files, string s)
        {
            s = (s ?? "").Trim().Replace('/', '\\').Trim('\\');
            if (s == "") return "";
            var f = files.FirstOrDefault(x => string.Equals(x.Rel, s, StringComparison.OrdinalIgnoreCase))
                 ?? files.FirstOrDefault(x => x.Rel.EndsWith("\\" + s, StringComparison.OrdinalIgnoreCase))
                 ?? files.FirstOrDefault(x => string.Equals(x.Name, Path.GetFileName(s), StringComparison.OrdinalIgnoreCase));
            return f == null ? "" : f.Rel;
        }

        static string Rel(Project p, string rel)
        {
            foreach (var f in p.Folders) if (rel.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase)) return rel.Substring(f.Length + 1);
            return rel;
        }

        static string Context(Store st, Project p)
        {
            var sb = new StringBuilder();
            sb.AppendLine("项目：" + p.Name + "（" + p.Type + "）");
            sb.AppendLine("【登记信息】（以前手工登记，可能有误，仅供参考）");
            foreach (var k in new[] { "建设内容及规模", "投资概算／可研投资", "中标／施工单位", "施工合同／成交金额", "决策／采购形式" })
                if (p.Get(k) != "") sb.AppendLine(k + "：" + p.Get(k));
            sb.AppendLine("\n【已登记的合同】（金额单位：元；可能有误，以原文为准）");
            if (p.Contracts.Count == 0) sb.AppendLine("（无）");
            foreach (var c in p.Contracts) sb.AppendLine(c.Kind + "｜" + c.Party + "｜" + c.Amount.ToString("0.00") + "｜" + c.Date + "｜已付 " + c.Paid.ToString("0.00"));
            sb.AppendLine("\n【已登记的收支】");
            if (p.Payments.Count == 0) sb.AppendLine("（无）");
            foreach (var x in p.Payments) sb.AppendLine(x.Date + "｜" + x.Dir + "｜" + x.Amount.ToString("0.00") + "｜" + x.Summary + "｜" + x.Contract);
            sb.AppendLine("\n【已识别的发票】");
            foreach (var i in st.InvoicesOf(p).Where(i => i.Members == null || i.Members.Count == 0))
                sb.AppendLine(Rel(p, i.Rel) + "｜" + i.Date + "｜" + i.Seller + "｜" + i.Amount.ToString("0.00") + " 元｜号码 " + i.Number);

            var files = st.FilesOf(p).OrderBy(f => f.Cat).ThenBy(f => f.Rel).ToList();
            sb.AppendLine("\n【文件清单】（分类｜路径）");
            int listBudget = 14000, used = 0;
            foreach (var f in files)
            {
                string line = Cats.Short(f.Cat) + "｜" + Rel(p, f.Rel);
                if ((used += line.Length) > listBudget) { sb.AppendLine("……其余省略"); break; }
                sb.AppendLine(line);
            }

            sb.AppendLine("\n【合同原文摘录】");
            var texts = new Dictionary<string, string>();
            lock (st.Sync) foreach (var f in files) { string t; if (st.Texts.TryGetValue(f.Rel, out t) && !string.IsNullOrWhiteSpace(t)) texts[f.Rel] = t; }
            int left = (Budget - sb.Length) * 6 / 10;
            var seen = new HashSet<string>();
            // 写明金额（大写、签约合同价、成交金额）的文件排在前面：扫描版 PDF 常常没识别出金额页，同名 .doc 里却有
            foreach (var f in files.Where(f => texts.ContainsKey(f.Rel) && (f.Cat == "04 合同协议" || ContractFile.IsMatch(f.Name)) && !f.Rel.Contains(Invoices.OutFolder))
                                   .OrderByDescending(f => Regex.Matches(texts[f.Rel], "签约合同价|成交金额|中标金额|合同金额|合同价款|大写").Count > 0 ? 1 : 0)
                                   .ThenBy(f => f.Ext == ".pdf" ? 0 : 1))
            {
                string t = Squeeze(texts[f.Rel]);
                string sig = t.Length > 200 ? t.Substring(0, 200) : t;
                if (!seen.Add(sig)) continue;   // 内容相同的副本只给一份
                string part = "〔" + Rel(p, f.Rel) + "〕\n" + Windows(t, ContractWords, 1200, 2600) + (t.Length > 3200 ? "\n……（结尾）" + t.Substring(t.Length - 600) : "") + "\n";
                if (part.Length > left) { if (left < 800) break; part = part.Substring(0, left); }
                sb.Append(part); left -= part.Length;
            }

            sb.AppendLine("\n【付款依据原文摘录（凭证、回单、支付审批、拨款）】");
            left = Budget - sb.Length;
            foreach (var f in files.Where(f => texts.ContainsKey(f.Rel) && (f.Cat == "09 资金支付" || PayFile.IsMatch(f.Rel)) && !ContractFile.IsMatch(f.Name)))
            {
                string t = Squeeze(texts[f.Rel]);
                string part = "〔" + Rel(p, f.Rel) + "〕\n" + (t.Length > 900 ? t.Substring(0, 900) + "…" : t) + "\n";
                if (part.Length > left) break;
                sb.Append(part); left -= part.Length;
            }
            return sb.ToString();
        }

        static string Squeeze(string t) { return Regex.Replace(t ?? "", @"[ \t　]+", " ").Replace("\r", ""); }

        /// <summary>开头一段 + 关键词前后的片段。</summary>
        static string Windows(string t, string[] words, int head, int max)
        {
            if (t.Length <= max) return t;
            var spans = new List<int[]> { new[] { 0, Math.Min(head, t.Length) } };
            foreach (var w in words)
            {
                int i = 0, n = 0;
                while (n < 3 && (i = t.IndexOf(w, i, StringComparison.Ordinal)) >= 0) { spans.Add(new[] { Math.Max(0, i - 120), Math.Min(t.Length, i + 180) }); i += w.Length; n++; }
            }
            spans = spans.OrderBy(s => s[0]).ToList();
            var merged = new List<int[]>();
            foreach (var s in spans)
                if (merged.Count > 0 && s[0] <= merged[merged.Count - 1][1]) merged[merged.Count - 1][1] = Math.Max(merged[merged.Count - 1][1], s[1]);
                else merged.Add(new[] { s[0], s[1] });
            var sb = new StringBuilder();
            foreach (var s in merged) { if (sb.Length > max) break; sb.Append(s[0] > 0 ? "…" : "").Append(t.Substring(s[0], s[1] - s[0])); }
            return sb.ToString();
        }

        static IEnumerable<Dictionary<string, object>> Arr(Dictionary<string, object> o, string k) { return Arr0(o, k).OfType<Dictionary<string, object>>(); }
        static IEnumerable<object> Arr0(Dictionary<string, object> o, string k) { object v; return o.TryGetValue(k, out v) && v is object[] ? (object[])v : new object[0]; }
        static string S(Dictionary<string, object> o, string k) { object v; return o.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture).Trim() : ""; }
        static double N(Dictionary<string, object> o, string k)
        {
            object v; if (!o.TryGetValue(k, out v) || v == null) return 0;
            double d; return double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture).Replace(",", "").Replace("元", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? Math.Round(d, 2) : 0;
        }
    }

    /// <summary>AI 整理合同和收支：结果先列出来，人工勾选核对后再写进台账。</summary>
    public class AiFinanceForm : Form
    {
        readonly Store st; readonly Project p;
        TextBox log; DataGridView cg, pg; Button bApply; Label status;
        AiFinPlan plan; bool busy, applied;

        public static bool Run(IWin32Window owner, Store st, Project p)
        {
            if (p.IsRoot) return false;
            if (!AiConfig.Load().Ready)
            {
                if (Ui.Ask("还没有设置 AI 接口（DeepSeek 等）的密钥。现在去设置吗？")) AiSettingsDialog.Show(owner);
                if (!AiConfig.Load().Ready) return false;
            }
            using (var f = new AiFinanceForm(st, p)) { f.ShowDialog(owner); return f.applied; }
        }

        AiFinanceForm(Store st, Project p)
        {
            this.st = st; this.p = p;
            Text = "AI 整理合同和收支 — " + p.Name; Font = Ui.F(9.5f); BackColor = Ui.Bg; ShowIcon = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; Size = new Size(Ui.S(1200), Ui.S(800)); MinimumSize = new Size(Ui.S(900), Ui.S(560));
            status = Ui.Lbl("AI 正在阅读本项目的合同、凭证、回单和发票……", 10f, true, Ui.Text); status.Dock = DockStyle.Top; status.AutoSize = false; status.Height = Ui.S(36); status.Padding = new Padding(Ui.S(16), Ui.S(10), 0, 0);
            log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Top, Height = Ui.S(110), BorderStyle = BorderStyle.None, BackColor = Color.White, Font = Ui.F(9f) };

            cg = MakeGrid(new[] { "写入", "状态", "合同类型", "相对方", "金额（元）", "签订日期", "原件", "依据（原文）" }, new[] { 50, 150, 120, 200, 110, 90, 200, 300 });
            pg = MakeGrid(new[] { "写入", "状态", "日期", "收/支", "金额（元）", "摘要", "对应合同", "凭证", "依据（原文）" }, new[] { 50, 120, 90, 50, 110, 220, 110, 200, 260 });
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Ui.Bg };
            split.Panel1.Controls.Add(cg); split.Panel1.Controls.Add(Head("合同台账（勾选的写入；已有的只补空白项）"));
            split.Panel2.Controls.Add(pg); split.Panel2.Controls.Add(Head("收支明细（只列有付款凭证、回单的；只有发票的不算已付）"));

            var bottom = Ui.Row(); bottom.Dock = DockStyle.Bottom; bottom.Height = Ui.S(56); bottom.Padding = new Padding(Ui.S(12), Ui.S(10), 0, 0);
            bApply = Ui.Btn("写入合同台账和财务", true, "check"); bApply.Enabled = false; bApply.Click += (s, e) => Apply();
            var bClose = Ui.Btn("关闭", false); bClose.Click += (s, e) => Close();
            bottom.Controls.AddRange(new Control[] { bApply, bClose, Ui.Lbl("内容可直接改；写入后可在项目页继续修改。", 8.5f, false, Ui.Sub) });
            var mid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(16), 0, Ui.S(16), 0) };
            mid.Controls.Add(split); mid.Controls.Add(log);
            Controls.Add(mid); Controls.Add(bottom); Controls.Add(status);
            Shown += (s, e) => { split.SplitterDistance = split.Height / 2; Start(); };
            FormClosing += (s, e) => { if (busy) e.Cancel = !Ui.Ask("AI 还在整理，确定关闭？"); };
        }

        static Control Head(string t) { var l = Ui.Lbl(t, 10f, true, Ui.Text); l.Dock = DockStyle.Top; l.AutoSize = false; l.Height = Ui.S(32); l.Padding = new Padding(0, Ui.S(8), 0, 0); return l; }

        static DataGridView MakeGrid(string[] heads, int[] widths)
        {
            var g = Ui.Grid(); g.Dock = DockStyle.Fill; g.AllowUserToAddRows = false;
            for (int i = 0; i < heads.Length; i++)
            {
                DataGridViewColumn c = i == 0 ? (DataGridViewColumn)new DataGridViewCheckBoxColumn() : new DataGridViewTextBoxColumn();
                c.HeaderText = heads[i]; c.Width = Ui.S(widths[i]); c.SortMode = DataGridViewColumnSortMode.NotSortable;
                if (heads[i].Contains("（元）")) { c.DefaultCellStyle.Format = "#,##0.00"; c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; c.ValueType = typeof(double); }
                if (heads[i].StartsWith("依据") || heads[i] == "状态") { c.DefaultCellStyle.ForeColor = Ui.Sub; c.ReadOnly = true; }
                g.Columns.Add(c);
            }
            g.Columns[heads.Length - 1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            g.DataError += (s, e) => e.ThrowException = false;
            return g;
        }

        void Log(string s) { if (IsDisposed) return; if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; } log.AppendText(s + Environment.NewLine); }

        void Start()
        {
            busy = true;
            var cfg = AiConfig.Load();
            AiClient.TokensIn = AiClient.TokensOut = 0;
            var th = new Thread(() =>
            {
                AiFinPlan r = null; string err = null; var t0 = DateTime.Now;
                try { r = AiFinance.Analyze(st, cfg, p, Log); }
                catch (Exception ex) { err = ex is AiException ? ex.Message : ex.GetType().Name + "：" + ex.Message; }
                int secs = (int)(DateTime.Now - t0).TotalSeconds;
                if (IsDisposed) return;
                BeginInvoke(new Action(() =>
                {
                    busy = false;
                    if (err != null) { status.Text = "没有完成：" + err; status.ForeColor = Ui.Red; Log(err); return; }
                    plan = r; Fill();
                    foreach (var n in plan.Notes) Log("· " + n);
                    status.Text = "AI 整理出合同 " + plan.Contracts.Count + " 条、收支 " + plan.Payments.Count + " 笔（用时 " + secs + " 秒）。请逐条核对后写入。";
                    status.ForeColor = Ui.Green;
                    bApply.Enabled = plan.Contracts.Count + plan.Payments.Count > 0;
                }));
            });
            th.IsBackground = true; th.Start();
        }

        void Fill()
        {
            foreach (var it in plan.Contracts)
            {
                int i = cg.Rows.Add(it.Use, it.Status, it.C.Kind, it.C.Party, it.C.Amount, it.C.Date, Path.GetFileName(it.C.Source), it.Evidence);
                cg.Rows[i].Tag = it;
            }
            foreach (var it in plan.Payments)
            {
                int i = pg.Rows.Add(it.Use, it.Status, it.P.Date, it.P.Dir, it.P.Amount, it.P.Summary, it.P.Contract, Path.GetFileName(it.P.VoucherFile), it.Evidence);
                pg.Rows[i].Tag = it;
            }
        }

        static double D(object v) { if (v is double) return (double)v; double d; double.TryParse(Convert.ToString(v).Replace(",", ""), out d); return d; }

        void Apply()
        {
            cg.EndEdit(); pg.EndEdit();
            foreach (DataGridViewRow r in cg.Rows)
            {
                var it = (AiConItem)r.Tag; it.Use = Convert.ToBoolean(r.Cells[0].Value);
                it.C.Kind = Convert.ToString(r.Cells[2].Value).Trim(); it.C.Party = Convert.ToString(r.Cells[3].Value).Trim(); it.C.Amount = D(r.Cells[4].Value); it.C.Date = Convert.ToString(r.Cells[5].Value).Trim();
            }
            foreach (DataGridViewRow r in pg.Rows)
            {
                var it = (AiPayItem)r.Tag; it.Use = Convert.ToBoolean(r.Cells[0].Value);
                it.P.Date = Convert.ToString(r.Cells[2].Value).Trim(); it.P.Dir = Convert.ToString(r.Cells[3].Value).Contains("收") ? "收入" : "支出";
                it.P.Amount = D(r.Cells[4].Value); it.P.Summary = Convert.ToString(r.Cells[5].Value).Trim(); it.P.Contract = Convert.ToString(r.Cells[6].Value).Trim();
            }
            int n = AiFinance.Apply(st, p, plan);
            st.Save(); applied = true;
            Ui.Info("已写入 " + n + " 条。合同台账、财务里都能看到，也可以继续修改。");
            Close();
        }
    }
}
