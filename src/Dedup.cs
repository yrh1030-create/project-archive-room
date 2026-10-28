using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DangAn
{
    /// <summary>
    /// 合同台账、收支明细查重：同一笔被两张明细表重复导入、AI 和手工各登记了一次等情况。
    /// 每组保留信息最全的一条，把其余几条上有、它没有的内容（凭证号、凭证/发票文件、关联合同……）补过来，再删掉其余几条。
    /// </summary>
    public static class Dedup
    {
        public class Group<T> { public T Keep; public List<T> Drop = new List<T>(); public string Why = ""; }

        static string Core(string s) { return Regex.Replace(s ?? "", @"[（(][^）)]*[）)]|涪陵区|李渡街道|李渡|街道办事处|项目|工程|\s|[，,。、:：\-—_]", ""); }

        static int Lcs(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
            int best = 0; var prev = new int[b.Length + 1];
            for (int i = 1; i <= a.Length; i++)
            {
                var cur = new int[b.Length + 1];
                for (int j = 1; j <= b.Length; j++) if (a[i - 1] == b[j - 1]) { cur[j] = prev[j - 1] + 1; best = Math.Max(best, cur[j]); }
                prev = cur;
            }
            return best;
        }

        // ---------------- 收支 ----------------
        /// <summary>同一笔：收支方向、金额相同，日期相同（或一边没填），凭证号相同（或一边没填），摘要相近。</summary>
        public static bool SamePayment(Payment a, Payment b)
        {
            if (a.Dir != b.Dir || Math.Abs(a.Amount - b.Amount) >= 0.01 || a.Amount == 0) return false;
            if (a.Date != "" && b.Date != "" && a.Date != b.Date) return false;
            string va = a.VoucherNo.Replace("-", "").Replace(" ", ""), vb = b.VoucherNo.Replace("-", "").Replace(" ", "");
            if (va != "" && vb != "" && va != vb) return false;   // 两张不同的凭证：是两笔
            string sa = Core(a.Summary), sb = Core(b.Summary);
            if (sa == "" || sb == "" || sa.Contains(sb) || sb.Contains(sa)) return true;
            return Lcs(sa, sb) >= Math.Min(6, Math.Min(sa.Length, sb.Length));
        }

        static int Info(Payment x)
        {
            return (x.VoucherNo != "" ? 4 : 0) + (x.VoucherFile != "" ? 3 : 0) + (x.InvoiceFile != "" ? 3 : 0) + (x.Contract != "" ? 2 : 0) + (x.Date != "" ? 2 : 0)
                + (x.Party != "" ? 1 : 0) + (x.Note != "" ? 1 : 0) + (x.Source == "手工" ? 1 : 0);
        }

        public static List<Group<Payment>> Payments(Project p)
        {
            var res = new List<Group<Payment>>();
            var used = new HashSet<Payment>();
            foreach (var a in p.Payments)
            {
                if (used.Contains(a)) continue;
                var same = p.Payments.Where(b => b != a && !used.Contains(b) && SamePayment(a, b)).ToList();
                if (same.Count == 0) continue;
                var all = new List<Payment> { a }; all.AddRange(same);
                var keep = all.OrderByDescending(Info).First();
                var g = new Group<Payment> { Keep = keep, Drop = all.Where(x => x != keep).ToList() };
                g.Why = all.Count + " 笔：" + (keep.Date == "" ? "日期不详" : keep.Date) + " " + keep.Dir + " " + keep.Amount.ToString("#,##0.00") + " 元";
                var srcs = all.Select(x => System.IO.Path.GetFileName(x.Source)).Distinct().ToList();
                if (srcs.Count > 1) g.Why += "（来自 " + string.Join("、", srcs) + "）";
                foreach (var x in all) used.Add(x);
                res.Add(g);
            }
            return res;
        }

        // ---------------- 合同 ----------------
        static string Party(string s) { return Regex.Replace(s ?? "", @"[（(][^）)]*[）)]|\s|有限责任公司|股份有限公司|有限公司|集团|公司", ""); }
        static string Kind(string s) { return Regex.Replace(s ?? "", @"[（(][^）)]*[）)]|合同|协议|书|事项|\s", ""); }

        /// <summary>同一份：相对方相同、合同类型对得上，且金额相同（或一边金额没填）。</summary>
        public static bool SameContract(Contract a, Contract b)
        {
            string pa = Party(a.Party), pb = Party(b.Party);
            if (pa == "" || pb == "" || !(pa == pb || (Math.Min(pa.Length, pb.Length) >= 4 && (pa.Contains(pb) || pb.Contains(pa))))) return false;
            // 类型也要对得上：同一家单位的“预算审核”和“招标代理”金额碰巧相同，是两份合同
            string ka = Kind(a.Kind), kb = Kind(b.Kind);
            bool kindOk = ka == "" || kb == "" || ka.Contains(kb) || kb.Contains(ka);
            if (!kindOk) return false;
            if (a.Amount > 0 && b.Amount > 0) return Math.Abs(a.Amount - b.Amount) < 1;
            return ka != "" && kb != "";
        }

        static int Info(Contract c)
        {
            return (c.Amount > 0 ? 4 : 0) + (c.Source != "" ? 3 : 0) + (c.Date != "" ? 2 : 0) + (c.Paid > 0 ? 2 : 0) + (c.Final != "" ? 1 : 0) + (c.Content != "" ? 1 : 0)
                + (c.Note.StartsWith("AI") ? 0 : 1);
        }

        public static List<Group<Contract>> Contracts(Project p)
        {
            var res = new List<Group<Contract>>();
            var used = new HashSet<Contract>();
            foreach (var a in p.Contracts)
            {
                if (used.Contains(a)) continue;
                var same = p.Contracts.Where(b => b != a && !used.Contains(b) && SameContract(a, b)).ToList();
                if (same.Count == 0) continue;
                var all = new List<Contract> { a }; all.AddRange(same);
                var keep = all.OrderByDescending(Info).First();
                res.Add(new Group<Contract> { Keep = keep, Drop = all.Where(x => x != keep).ToList(), Why = all.Count + " 条：" + keep.Party + " " + keep.Amount.ToString("#,##0.00") + " 元（" + string.Join(" / ", all.Select(x => x.Kind).Distinct()) + "）" });
                foreach (var x in all) used.Add(x);
            }
            return res;
        }

        // ---------------- 合并 ----------------
        public static void Merge(Project p, Group<Payment> g)
        {
            var k = g.Keep;
            foreach (var d in g.Drop)
            {
                if (k.Date == "") k.Date = d.Date;
                if (k.VoucherNo == "") k.VoucherNo = d.VoucherNo;
                if (k.VoucherFile == "") k.VoucherFile = d.VoucherFile;
                if (k.InvoiceFile == "") k.InvoiceFile = d.InvoiceFile;
                if (k.Contract == "") k.Contract = d.Contract;
                if (k.Party == "") k.Party = d.Party;
                if (k.Note == "") k.Note = d.Note;
                p.Payments.Remove(d);
            }
        }

        public static void Merge(Project p, Group<Contract> g)
        {
            var k = g.Keep;
            foreach (var d in g.Drop)
            {
                if (k.Amount == 0) k.Amount = d.Amount;
                if (k.Date == "") k.Date = d.Date;
                if (k.Source == "") k.Source = d.Source;
                if (k.Paid == 0 && d.Paid > 0) { k.Paid = d.Paid; k.Unpaid = d.Unpaid; }
                if (k.Final == "") k.Final = d.Final;
                if (k.Content == "") k.Content = d.Content;
                foreach (var pay in p.Payments.Where(x => x.Contract == d.Kind)) pay.Contract = k.Kind;   // 挂在被删那条上的收支改挂保留的
                p.Contracts.Remove(d);
            }
        }

        public static int Count(Project p) { return Payments(p).Sum(g => g.Drop.Count) + Contracts(p).Sum(g => g.Drop.Count); }

        public static string Desc(Payment x) { return (x.Date == "" ? "日期不详" : x.Date) + " " + x.Dir + " " + x.Amount.ToString("#,##0.00") + " 元 " + AiClient.Cut(x.Summary, 30) + (x.VoucherNo != "" ? "（凭证 " + x.VoucherNo + "）" : ""); }
        public static string Desc(Contract c) { return c.Kind + "｜" + c.Party + "｜" + c.Amount.ToString("#,##0.00") + " 元" + (c.Date != "" ? "｜" + c.Date : ""); }
    }
}
