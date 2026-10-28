using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DangAn
{
    /// <summary>财务：从收支明细表导入每一笔，自动关联合同、记账凭证和发票原件；汇总统计。</summary>
    public static class Finance
    {
        public class Summary
        {
            public double Budget, ContractTotal, Actual, Income, Paid, ContractPaid, Unpaid, Invoiced;
            public int InvoiceCount;
            public int PayCount, IncomeCount, WithVoucher, WithInvoice;
            public string PaidBasis = "";
        }

        public static Summary Sum(Store st, Project p)
        {
            var s = new Summary();
            s.Budget = Store.FirstMoney(p.Get("投资概算／可研投资"));
            s.ContractTotal = p.Contracts.Sum(c => c.Amount);
            s.ContractPaid = p.Contracts.Sum(c => c.Paid);
            s.Unpaid = p.Contracts.Sum(c => c.Unpaid);
            s.Actual = Store.FirstMoney(p.Get("实际投资额（元）"));
            var outs = p.Payments.Where(x => x.Dir != "收入").ToList();
            var ins = p.Payments.Where(x => x.Dir == "收入").ToList();
            s.PayCount = outs.Count; s.IncomeCount = ins.Count;
            s.Income = ins.Sum(x => x.Amount);
            if (outs.Count > 0) { s.Paid = outs.Sum(x => x.Amount); s.PaidBasis = "按收支明细"; }
            else { s.Paid = s.ContractPaid; s.PaidBasis = s.ContractPaid > 0 ? "按合同台账" : ""; }
            var singles = st.InvoicesOf(p).Where(i => i.Members == null || i.Members.Count == 0).ToList();
            s.Invoiced = singles.Sum(i => i.Amount); s.InvoiceCount = singles.Count;
            s.WithVoucher = outs.Count(x => x.VoucherFile != "");
            s.WithInvoice = outs.Count(x => x.InvoiceFile != "");
            return s;
        }

        // ---------------- 导入明细表 ----------------
        static readonly Regex VoucherRe = new Regex(@"^[记收付转银现][-－—\s]?\d+$|凭证|^暂");
        static bool IsNum(string s) { double d; return double.TryParse(s.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out d); }
        static double Num(string s) { double d; double.TryParse(s.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out d); return d; }

        static string ToDate(string cell)
        {
            double d;
            if (double.TryParse(cell, NumberStyles.Any, CultureInfo.InvariantCulture, out d) && d > 36000 && d < 60000)
                return DateTime.FromOADate(d).ToString("yyyy-MM-dd");
            var m = Regex.Match(cell, @"^(20\d\d)[-/.年](\d{1,2})[-/.月](\d{1,2})");
            if (m.Success) return m.Groups[1].Value + "-" + m.Groups[2].Value.PadLeft(2, '0') + "-" + m.Groups[3].Value.PadLeft(2, '0');
            return null;
        }

        static string Batch(string s)
        {
            var m = Regex.Match(s ?? "", @"20\d\d年第[一二三四]批");
            return m.Success ? m.Value : null;
        }

        /// <summary>解析一张收入/支出明细表的文字。</summary>
        public static List<Payment> Parse(string text, string dir, string source)
        {
            var list = new List<Payment>();
            if (string.IsNullOrEmpty(text)) return list;
            double unit = text.Contains("万元") ? 10000 : 1;
            bool started = false;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (!started) { if (line.Contains("摘要") && line.Contains("金额")) started = true; continue; }
                if (line.StartsWith("合") || line.Length == 0) { if (line.StartsWith("合")) break; continue; }
                var cells = line.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (cells.Count < 2 || !Regex.IsMatch(cells[0], @"^\d{1,3}$")) continue;
                var p = new Payment { Dir = dir, Source = source };
                bool hasAmount = false;
                var notes = new List<string>();
                for (int i = 1; i < cells.Count; i++)
                {
                    string c = cells[i];
                    string d = p.Date == "" && p.Summary == "" ? ToDate(c) : null;
                    if (d != null) { p.Date = d; continue; }
                    if (p.VoucherNo == "" && p.Summary == "" && VoucherRe.IsMatch(c) && c.Length <= 14) { p.VoucherNo = c.Replace("－", "-").Replace(" ", ""); continue; }
                    if (p.Summary == "" && !IsNum(c)) { p.Summary = c; continue; }
                    if (p.Summary != "" && !hasAmount && IsNum(c)) { p.Amount = Math.Round(Num(c) * unit, 2); hasAmount = true; continue; }
                    if (hasAmount && !IsNum(c)) notes.Add(c);
                }
                if (p.Summary == "" || !hasAmount || p.Amount == 0) continue;
                if (p.VoucherNo.StartsWith("暂")) { p.Note = p.VoucherNo; p.VoucherNo = ""; }
                if (notes.Count > 0) p.Note = (p.Note + " " + string.Join("；", notes)).Trim();
                if (Regex.IsMatch(p.Summary, "一、二批|第一、二批")) p.Note = ("多批合计金额，不能直接作为单个项目的数额 " + p.Note).Trim();
                list.Add(p);
            }
            return list;
        }

        /// <summary>重新从本项目的收支明细表导入（保留手工录入和已手工关联的文件）。返回导入笔数。</summary>
        public static int Import(Store st, Project p)
        {
            Func<Payment, bool> keep = x => x.Source == "手工" || x.Source.StartsWith("AI");   // 手工录入、AI 整理的不随明细表重导而丢失
            var oldAuto = p.Payments.Where(x => !keep(x)).ToList();
            var manual = p.Payments.Where(keep).ToList();
            var result = new List<Payment>();
            var seen = new HashSet<string>();
            string myBatch = Batch(p.Name);
            var sheets = st.FilesOf(p).Where(f => (f.Ext == ".xlsx" || f.Ext == ".xls") && Regex.IsMatch(f.Name, "支出明细|收入明细|支付明细|拨款明细"))
                           .OrderBy(f => f.Rel.Length);
            foreach (var f in sheets)
            {
                string text;
                lock (st.Sync) st.Texts.TryGetValue(f.Rel, out text);
                string dir = f.Name.Contains("收入") || f.Name.Contains("拨款") ? "收入" : "支出";
                foreach (var pay in Parse(text, dir, f.Rel))
                {
                    var b = Batch(pay.Summary);
                    if (myBatch != null && b != null && b != myBatch) continue;   // 合并表里别的批次
                    string key = pay.Dir + "|" + pay.Date + "|" + pay.VoucherNo + "|" + pay.Amount.ToString("0.00") + "|" + (pay.VoucherNo == "" ? pay.Summary : "");
                    if (!seen.Add(key)) continue;
                    // 同一笔出现在两张明细表里（一张带凭证号，一张是汇总表）：合并成一笔
                    var twin = result.FirstOrDefault(x => Dedup.SamePayment(x, pay));
                    if (twin != null)
                    {
                        if (twin.VoucherNo == "" && pay.VoucherNo != "") { twin.VoucherNo = pay.VoucherNo; twin.Source = pay.Source; }
                        if (twin.Date == "") twin.Date = pay.Date;
                        if (twin.Note == "") twin.Note = pay.Note;
                        continue;
                    }
                    // 保留以前手工改过的关联
                    var prev = oldAuto.FirstOrDefault(o => o.Dir == pay.Dir && o.Date == pay.Date && o.VoucherNo == pay.VoucherNo && Math.Abs(o.Amount - pay.Amount) < 0.01);
                    if (prev != null) { pay.VoucherFile = prev.VoucherFile; pay.InvoiceFile = prev.InvoiceFile; pay.Contract = prev.Contract; pay.Party = prev.Party; if (prev.Note != "" && pay.Note == "") pay.Note = prev.Note; }
                    result.Add(pay);
                }
            }
            result = result.OrderBy(x => x.Dir == "收入" ? 0 : 1).ThenBy(x => x.Date == "" ? "9999" : x.Date).ToList();
            p.Payments = result.Concat(manual).ToList();
            AutoLink(st, p);
            return result.Count;
        }

        // ---------------- 自动关联 ----------------
        static readonly string[][] ContractKeys = {
            new[] { "监理", "监理" }, new[] { "检测", "检测" }, new[] { "招标代理", "招标代理|代理" },
            new[] { "预算审核", "预算审核|造价咨询|预算" }, new[] { "一审", "一审|结算审核|竣工结算审核" }, new[] { "设计", "设计|勘察" },
            new[] { "测量", "测量|地形图" }, new[] { "施工", "工程款|进度款|预付款|工程预付|尾款" }
        };

        static readonly string[][] FeeTypeMap = {
            new[] { "检测", "检测|试验" }, new[] { "监理", "监理" }, new[] { "设计", "设计|勘察" }, new[] { "造价", "造价|预算|审核|一审|二审|结算审" },
            new[] { "招标", "招标|代理" }, new[] { "测量", "测量|地形图" }, new[] { "工程款", "工程款|进度款|预付款|施工" }, new[] { "补偿", "补偿|占地|用地" }, new[] { "迁改", "迁改|改迁|迁建" }
        };

        static List<string> FeeTypes(string s)
        {
            var r = new List<string>();
            foreach (var t in FeeTypeMap) if (t[1].Split('|').Any(k => (s ?? "").Contains(k))) r.Add(t[0]);
            return r;
        }

        static int Lcs(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
            int best = 0;
            var prev = new int[b.Length + 1];
            for (int i = 1; i <= a.Length; i++)
            {
                var cur = new int[b.Length + 1];
                for (int j = 1; j <= b.Length; j++)
                    if (a[i - 1] == b[j - 1]) { cur[j] = prev[j - 1] + 1; if (cur[j] > best) best = cur[j]; }
                prev = cur;
            }
            return best;
        }

        /// <summary>界面上显示的简短摘要（去掉重复的项目名）。</summary>
        public static string Short(Project p, string s)
        {
            var r = Core(p, s).Trim(' ', '-', '—');
            r = Regex.Replace(r, @"^(工程项目|项目)", "");
            return r.Length >= 2 ? r : (s ?? "");
        }

        static string Core(Project p, string s)
        {
            // 去掉摘要里重复的项目名，只留“第一次工程款”“设计费”这类关键内容
            s = s ?? "";
            string stem = Regex.Replace(p.Name, @"（.*?）|\(.*?\)", "");
            foreach (var cut in new[] { p.Name, stem + "项目", stem, stem.Replace("工程", ""), "涪陵区", "李渡街道办事处", "李渡街道", "（连二塘至深沟子段）", "（新龙村傅家垭口至工农煤矿段）" }) s = s.Replace(cut, "");
            s = Regex.Replace(s, @"[（(][^）)]*通组公路通畅工程[）)]", "");
            s = Regex.Replace(s, @"涪陵区?20\d\d年乡村振兴产业路通畅工程", "");
            return s.Trim();
        }

        static IEnumerable<string> DateTokens(string date)
        {
            DateTime d;
            if (!DateTime.TryParse(date, out d)) yield break;
            yield return d.Month + "月" + d.Day + "号";
            yield return d.Month + "月" + d.Day + "日";
            yield return d.ToString("yyyyMMdd");
            yield return d.ToString("yyyy.M.d");
        }

        public static void AutoLink(Store st, Project p)
        {
            // 合同：先按合同名称相似度，再按关键词
            foreach (var pay in p.Payments.Where(x => x.Dir != "收入" && x.Contract == ""))
            {
                string core = Core(p, pay.Summary);
                Contract best = null; int bl = 0;
                foreach (var c in p.Contracts)
                {
                    string kind = c.Kind.Contains("—") ? c.Kind.Substring(c.Kind.LastIndexOf('—') + 1) : c.Kind;
                    kind = Regex.Replace(Core(p, kind), @"[（(].*?[）)]", "").Replace("合同", "").Replace("事项", "");
                    int l = Lcs(core, kind);
                    if (l > bl) { bl = l; best = c; }
                }
                if (best != null && bl >= 5) { pay.Contract = best.Kind; if (pay.Party == "") pay.Party = best.Party; }
            }
            foreach (var pay in p.Payments.Where(x => x.Dir != "收入" && x.Contract == ""))
                foreach (var ck in ContractKeys)
                {
                    if (!ck[1].Split('|').Any(k => pay.Summary.Contains(k))) continue;
                    var con = p.Contracts.FirstOrDefault(c => c.Kind.Contains(ck[0]));
                    if (con != null) { pay.Contract = con.Kind; if (pay.Party == "") pay.Party = con.Party; }
                    break;
                }
            // 文件
            var files = st.FilesOf(p).Where(f => f.Ext != ".xlsx" && f.Ext != ".xls" && f.Ext != ".rar" && f.Ext != ".zip" && f.Ext != ".txt").ToList();
            var vouchers = files.Where(f => f.Cat == "09 资金支付" || Regex.IsMatch(f.Rel, "凭证|支付|拨付")).Where(f => !f.Name.Contains("发票") && !Regex.IsMatch(f.Name, "合理性|明细")).ToList();
            var invoices = files.Where(f => (f.Name.Contains("发票") || f.Name.Contains("票据")) && !f.Rel.Contains(Invoices.OutFolder)).ToList();
            var usedV = new Dictionary<string, string>();   // 凭证文件 → 已用于哪天（同一天的几笔可共用一张凭证）
            foreach (var x in p.Payments.Where(x => x.VoucherFile != "")) usedV[x.VoucherFile] = x.Date;
            var usedI = new HashSet<string>(p.Payments.Select(x => x.InvoiceFile).Where(x => x != ""));
            var cands = new List<Tuple<int, Payment, FileRec, bool>>();
            foreach (var pay in p.Payments)
            {
                string core = Core(p, pay.Summary);
                var dts = DateTokens(pay.Date).ToList();
                Func<FileRec, int> score = f =>
                {
                    string hay = Core(p, f.Rel.Substring(f.Rel.IndexOf('\\') + 1));
                    int sc = 0;
                    bool dateHit = dts.Any(t => hay.Contains(t));
                    if (dateHit) sc += 6;
                    int l = Lcs(core, hay);
                    if (l >= 2) sc += l;
                    if (pay.VoucherNo != "" && hay.Replace("-", "").Contains(pay.VoucherNo.Replace("-", ""))) sc += 6;
                    if (pay.Dir == "收入" && Regex.IsMatch(hay, "收入|到账|拨款")) sc += 3;
                    // “第一次 / 第二次…”必须一致
                    var o1 = Regex.Matches(core, "第[一二三四五六七八九十0-9]+[次期]").Cast<Match>().Select(m => m.Value.Replace("期", "次")).ToList();
                    var o2 = Regex.Matches(hay, "第[一二三四五六七八九十0-9]+[次期]").Cast<Match>().Select(m => m.Value.Replace("期", "次")).ToList();
                    // 文件名写明了费用类型（检测/设计/监理…）时，必须和这笔的类型一致
                    string near = Path.GetFileName(Path.GetDirectoryName(f.Rel) ?? "") + "\\" + f.Name;
                    var tf = FeeTypes(Core(p, near)); var tp = FeeTypes(Core(p, pay.Summary));
                    if (tf.Count > 0 && tp.Count > 0 && !tf.Intersect(tp).Any()) return 0;
                    if (tf.Count > 0 && tp.Count == 0) return 0;
                    if (o1.Count > 0 && o2.Count > 0 && !o1.Intersect(o2).Any()) return 0;
                    bool ordMatch = o1.Count > 0 && o1.Intersect(o2).Any();
                    if (o1.Count > 0 && o2.Count == 0 && !dateHit) return 0;
                    if (!dateHit && !(l >= 6 || (ordMatch && l >= 3))) return 0;
                    if (ordMatch) sc += 4;
                    if (Regex.IsMatch(f.Name, "凭证|支付|回单|付款|拨款")) sc += 2;
                    return sc;
                };
                if (pay.VoucherFile == "") foreach (var f in vouchers) { int sc = score(f); if (sc >= 8) cands.Add(Tuple.Create(sc, pay, f, true)); }
                if (pay.InvoiceFile == "" && pay.Dir != "收入") foreach (var f in invoices) { int sc = score(f); if (sc >= 6) cands.Add(Tuple.Create(sc, pay, f, false)); }
            }
            foreach (var c in cands.OrderByDescending(x => x.Item1))
            {
                var pay = c.Item2; var f = c.Item3;
                if (c.Item4)
                {
                    if (pay.VoucherFile != "") continue;
                    string usedDate;
                    if (usedV.TryGetValue(f.Rel, out usedDate) && (usedDate == "" || usedDate != pay.Date)) continue;
                    pay.VoucherFile = f.Rel; usedV[f.Rel] = pay.Date;
                }
                else
                {
                    if (pay.InvoiceFile != "" || usedI.Contains(f.Rel)) continue;
                    pay.InvoiceFile = f.Rel; usedI.Add(f.Rel);
                }
            }
            Invoices.Link(st, p);   // 拆出的单张发票按凭证文件夹 / 金额关联
        }

        /// <summary>本项目里像凭证/发票、却还没挂到任何一笔上的文件。</summary>
        public static void LinkSplitInvoices(Store st, Project p) { Invoices.Link(st, p); }

        public static List<FileRec> Unlinked(Store st, Project p)
        {
            var used = new HashSet<string>(p.Payments.SelectMany(x => new[] { x.VoucherFile, x.InvoiceFile }).Where(x => x != ""), StringComparer.OrdinalIgnoreCase);
            // 已并入“发票合集”且合集已挂上的单张发票，也算已关联
            foreach (var inv in st.Lib.Invoices.Where(i => i.Project == p.Id && i.Members != null && i.Members.Count > 0 && used.Contains(i.Rel)))
                foreach (var m in inv.Members) used.Add(m);
            return Store.SortForDisplay(st.FilesOf(p).Where(f => !used.Contains(f.Rel) && f.Ext != ".xlsx" && f.Ext != ".xls" && f.Ext != ".rar" && f.Ext != ".zip"
                && (f.Name.Contains("发票") || Regex.IsMatch(f.Name, "凭证|记账|付款单|支付单")))).ToList();
        }
    }
}
