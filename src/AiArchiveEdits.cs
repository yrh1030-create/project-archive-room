using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DangAn
{
    /// <summary>AI 助手提交的一条档案修改：先列给用户看，确认后才执行 Apply。</summary>
    public class PendingChange
    {
        public string ProjectId = "", ProjectName = "", Area = "", Desc = "", Source = "";
        public Action Apply;
        public bool Use = true;
        public override string ToString() { return "【" + ProjectName + "】" + Area + "｜" + Desc + (Source != "" ? "　依据：" + AiClient.Cut(Source, 60) : ""); }
    }

    /// <summary>AI 助手修改关键信息、合同台账、收支的工具。只登记待确认的修改，不直接写档案库。</summary>
    public static class ArchiveEdits
    {
        static string S(Dictionary<string, object> o, string k) { object v; return o.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture).Trim() : ""; }
        static bool Has(Dictionary<string, object> o, string k) { object v; return o.TryGetValue(k, out v) && v != null && Convert.ToString(v, CultureInfo.InvariantCulture).Trim() != ""; }
        static double N(Dictionary<string, object> o, string k)
        {
            double d; return double.TryParse(S(o, k).Replace(",", "").Replace("元", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? Math.Round(d, 2) : 0;
        }
        static string M(double v) { return v.ToString("#,##0.00"); }

        static Project Find(Store st, Dictionary<string, object> a)
        {
            string id = S(a, "project_id");
            var p = st.Proj(id) ?? st.RealProjects.FirstOrDefault(x => id != "" && (x.Name == id || x.Name.Contains(id)));
            if (p == null || p.IsRoot) throw new AiException("没有这个项目：" + id + "。先用 list_projects 查编号。");
            return p;
        }

        static string Add(AiChat chat, Project p, string area, string desc, string source, Action apply)
        {
            chat.Pending.Add(new PendingChange { ProjectId = p.Id, ProjectName = p.Name, Area = area, Desc = desc, Source = source, Apply = apply });
            return "已提交，等用户确认后写入：" + area + "｜" + desc;
        }

        static string Tag(string source) { return "[AI 助手 " + DateTime.Now.ToString("yyyy-MM-dd") + "] " + source; }

        public static string SetInfo(Store st, AiChat chat, Dictionary<string, object> a)
        {
            var p = Find(st, a);
            object fo; var list = a.TryGetValue("fields", out fo) ? fo as object[] : null;
            if (list == null || list.Length == 0) return "没有要改的栏目。";
            var res = new List<string>();
            foreach (var f in list.OfType<Dictionary<string, object>>())
            {
                string name = S(f, "name"), val = S(f, "value"), src = S(f, "source");
                if (name == "" || val == "") continue;
                string old = p.Get(name);
                if (old == val) { res.Add("“" + name + "”与登记一致，不用改"); continue; }
                if (src == "") { res.Add("“" + name + "”没有写依据，未提交；请查原文后带上依据再提交"); continue; }
                string n = name, v = val, s = src;
                res.Add(Add(chat, p, "关键信息", name + "：" + (old == "" ? "（空）" : AiClient.Cut(old, 40)) + " → " + AiClient.Cut(val, 80), src, () => p.Set(n, v, Tag(s))));
            }
            return string.Join("\n", res);
        }

        public static string UpsertContract(Store st, AiChat chat, Dictionary<string, object> a)
        {
            var p = Find(st, a);
            string kind = S(a, "kind"), match = S(a, "match_kind"), src = S(a, "source");
            if (kind == "") return "缺少合同类型。";
            if (src != "") { var r = AiFinance.Resolve(st.FilesOf(p).ToList(), src); if (r != "") src = r; }
            string key = match != "" ? match : kind;
            var hits = p.Contracts.Where(c => c.Kind == key).ToList();
            if (hits.Count == 0) hits = p.Contracts.Where(c => c.Kind.Contains(key) || key.Contains(c.Kind)).ToList();
            if (hits.Count > 1 && Has(a, "party")) hits = hits.Where(c => c.Party == S(a, "party") || c.Party == "").ToList();
            if (hits.Count > 1) return "台账里有 " + hits.Count + " 条“" + key + "”，请用 match_kind 写出准确的合同类型（" + string.Join("、", hits.Select(c => c.Kind + "/" + c.Party)) + "）。";
            var e = hits.FirstOrDefault();
            if (e == null)
            {
                var c = new Contract { Kind = kind, Party = S(a, "party"), Amount = N(a, "amount"), Date = S(a, "date"), Paid = N(a, "paid"), Final = S(a, "final"), Content = S(a, "content"), Note = S(a, "note"), Source = src };
                c.Unpaid = Has(a, "unpaid") ? N(a, "unpaid") : Math.Max(0, Math.Round(c.Amount - c.Paid, 2));
                if (c.Party == "" && c.Amount == 0) return "新增合同至少要有相对方或金额。";
                return Add(chat, p, "合同台账", "新增 " + c.Kind + "：" + c.Party + "，" + M(c.Amount) + " 元" + (c.Date != "" ? "，" + c.Date + " 签订" : "") + (c.Paid > 0 ? "，已付 " + M(c.Paid) : ""), S(a, "note") != "" ? S(a, "note") : src,
                    () => p.Contracts.Add(c));
            }
            var diffs = new List<string>(); var sets = new List<Action>();
            Action<string, string, string, Action<string>> txt = (field, label, old, set) =>
            {
                if (!Has(a, field)) return; string v = S(a, field); if (v == old) return;
                diffs.Add(label + " " + (old == "" ? "（空）" : AiClient.Cut(old, 30)) + " → " + AiClient.Cut(v, 40)); sets.Add(() => set(v));
            };
            Action<string, string, double, Action<double>> num = (field, label, old, set) =>
            {
                if (!Has(a, field)) return; double v = N(a, field); if (Math.Abs(v - old) < 0.005) return;
                diffs.Add(label + " " + M(old) + " → " + M(v)); sets.Add(() => set(v));
            };
            if (kind != e.Kind) txt("kind", "类型", e.Kind, v => e.Kind = v);
            txt("party", "相对方", e.Party, v => e.Party = v);
            num("amount", "金额", e.Amount, v => e.Amount = v);
            txt("date", "签订日期", e.Date, v => e.Date = v);
            num("paid", "已付", e.Paid, v => e.Paid = v);
            num("unpaid", "未付", e.Unpaid, v => e.Unpaid = v);
            txt("final", "审定/决算", e.Final, v => e.Final = v);
            txt("content", "内容", e.Content, v => e.Content = v);
            txt("note", "备注", e.Note, v => e.Note = v);
            if (src != "" && src != e.Source) { string s2 = src; diffs.Add("原件 → " + System.IO.Path.GetFileName(src)); sets.Add(() => e.Source = s2); }
            if (diffs.Count == 0) return "“" + e.Kind + "”与台账一致，不用改。";
            return Add(chat, p, "合同台账", "修改 " + e.Kind + "：" + string.Join("；", diffs), S(a, "note") != "" ? S(a, "note") : src, () => { foreach (var x in sets) x(); });
        }

        public static string AddPayment(Store st, AiChat chat, Dictionary<string, object> a)
        {
            var p = Find(st, a);
            var files = st.FilesOf(p).ToList();
            var pay = new Payment
            {
                Date = S(a, "date"), Dir = S(a, "dir").Contains("收") ? "收入" : "支出", Amount = N(a, "amount"), Summary = S(a, "summary"), Party = S(a, "party"),
                Contract = S(a, "contract"), VoucherNo = S(a, "voucher_no"), Note = S(a, "note"), Source = "AI助手",
                VoucherFile = AiFinance.Resolve(files, S(a, "voucher_file")), InvoiceFile = AiFinance.Resolve(files, S(a, "invoice_file"))
            };
            if (pay.Amount <= 0) return "金额不对。";
            if (p.Payments.Any(x => x.Dir == pay.Dir && Math.Abs(x.Amount - pay.Amount) < 0.01 && (x.Date == pay.Date || x.Date == "" || pay.Date == "")))
                return "财务里已有同日同额的一笔，没有重复提交。";
            var con = p.Contracts.FirstOrDefault(c => c.Kind == pay.Contract) ?? p.Contracts.FirstOrDefault(c => pay.Contract != "" && (c.Kind.Contains(pay.Contract) || pay.Contract.Contains(c.Kind)));
            if (con != null) { pay.Contract = con.Kind; if (pay.Party == "") pay.Party = con.Party; }
            return Add(chat, p, "财务", "新增" + pay.Dir + " " + M(pay.Amount) + " 元" + (pay.Date != "" ? "（" + pay.Date + "）" : "（日期不详）") + " " + pay.Summary + (pay.Contract != "" ? "，对应" + pay.Contract : "")
                + (pay.VoucherFile != "" ? "，凭证 " + System.IO.Path.GetFileName(pay.VoucherFile) : ""), pay.Note,
                () =>
                {
                    p.Payments.Add(pay);
                    p.Payments = p.Payments.OrderBy(x => x.Dir == "收入" ? 0 : 1).ThenBy(x => x.Date == "" ? "9999" : x.Date).ToList();
                    Invoices.Link(st, p);
                });
        }

        /// <summary>查出重复的收支、合同，每组提交一条“合并”修改（保留信息最全的一条），等用户确认。</summary>
        public static string Dedupe(Store st, AiChat chat, Dictionary<string, object> a)
        {
            var p = Find(st, a);
            var pays = Dedup.Payments(p); var cons = Dedup.Contracts(p);
            if (pays.Count + cons.Count == 0) return "“" + p.Name + "”没有发现重复的收支或合同。";
            var res = new List<string>();
            foreach (var g in pays) { var gg = g; res.Add(Add(chat, p, "财务", "合并重复（" + g.Why + "）：保留 " + Dedup.Desc(g.Keep) + "；删除 " + string.Join("；", g.Drop.Select(Dedup.Desc)), "同日同额同方向，凭证号不冲突", () => Dedup.Merge(p, gg))); }
            foreach (var g in cons) { var gg = g; res.Add(Add(chat, p, "合同台账", "合并重复（" + g.Why + "）：保留 " + Dedup.Desc(g.Keep) + "；删除 " + string.Join("；", g.Drop.Select(Dedup.Desc)), "同一相对方、同金额", () => Dedup.Merge(p, gg))); }
            double extra = pays.Sum(g => g.Drop.Where(x => x.Dir != "收入").Sum(x => x.Amount));
            return "发现 " + pays.Count + " 组重复收支（多算支出 " + M(extra) + " 元）、" + cons.Count + " 组重复合同，已提交合并，等用户确认：\n" + string.Join("\n", res);
        }

        /// <summary>修改已有的一笔收支（按日期 + 金额找到它）。</summary>
        public static string UpdatePayment(Store st, AiChat chat, Dictionary<string, object> a)
        {
            var p = Find(st, a);
            double amt = N(a, "match_amount"); string date = S(a, "match_date"), vno = S(a, "match_voucher_no");
            var hits = p.Payments.Where(x => Math.Abs(x.Amount - amt) < 0.01 && (date == "" || x.Date == date) && (vno == "" || x.VoucherNo == vno)).ToList();
            if (hits.Count != 1) return hits.Count == 0 ? "没找到 " + (date == "" ? "" : date + " ") + M(amt) + " 元这一笔。" : "有 " + hits.Count + " 笔符合，请再给出日期或凭证号（match_date / match_voucher_no）；如果是重复记录，用 dedupe_records。";
            var x0 = hits[0]; var files = st.FilesOf(p).ToList();
            var diffs = new List<string>(); var sets = new List<Action>();
            Action<string, string, string, Action<string>> txt = (field, label, old, set) =>
            {
                if (!Has(a, field)) return; string v = S(a, field); if (v == old) return;
                diffs.Add(label + " " + (old == "" ? "（空）" : AiClient.Cut(old, 30)) + " → " + AiClient.Cut(v, 40)); sets.Add(() => set(v));
            };
            txt("date", "日期", x0.Date, v => x0.Date = v);
            txt("dir", "收/支", x0.Dir, v => x0.Dir = v.Contains("收") ? "收入" : "支出");
            if (Has(a, "amount") && Math.Abs(N(a, "amount") - x0.Amount) >= 0.01) { double v = N(a, "amount"); diffs.Add("金额 " + M(x0.Amount) + " → " + M(v)); sets.Add(() => x0.Amount = v); }
            txt("summary", "摘要", x0.Summary, v => x0.Summary = v);
            txt("contract", "关联合同", x0.Contract, v => x0.Contract = v);
            txt("voucher_no", "凭证号", x0.VoucherNo, v => x0.VoucherNo = v);
            if (Has(a, "voucher_file")) { string r = AiFinance.Resolve(files, S(a, "voucher_file")); if (r != "" && r != x0.VoucherFile) { diffs.Add("凭证 → " + System.IO.Path.GetFileName(r)); sets.Add(() => x0.VoucherFile = r); } }
            if (Has(a, "invoice_file")) { string r = AiFinance.Resolve(files, S(a, "invoice_file")); if (r != "" && r != x0.InvoiceFile) { diffs.Add("发票 → " + System.IO.Path.GetFileName(r)); sets.Add(() => x0.InvoiceFile = r); } }
            txt("note", "备注", x0.Note, v => x0.Note = v);
            if (diffs.Count == 0) return "这一笔没有要改的。";
            return Add(chat, p, "财务", "修改 " + Dedup.Desc(x0) + "：" + string.Join("；", diffs), S(a, "reason"), () => { foreach (var s in sets) s(); });
        }

        public static string Remove(Store st, AiChat chat, Dictionary<string, object> a)
        {
            var p = Find(st, a);
            string reason = S(a, "reason");
            if (S(a, "type").Contains("pay") || S(a, "type").Contains("收") || S(a, "type").Contains("支"))
            {
                double amt = N(a, "amount"); string date = S(a, "date");
                var hits = p.Payments.Where(x => Math.Abs(x.Amount - amt) < 0.01 && (date == "" || x.Date == date)).ToList();
                if (hits.Count != 1) return hits.Count == 0 ? "没找到这笔收支。" : "有 " + hits.Count + " 笔同额的，请写明日期。";
                var x0 = hits[0];
                return Add(chat, p, "财务", "删除 " + x0.Date + " " + x0.Dir + " " + M(x0.Amount) + " 元 " + x0.Summary, reason, () => p.Payments.Remove(x0));
            }
            string kind = S(a, "kind");
            var cs = p.Contracts.Where(c => c.Kind == kind).ToList();
            if (cs.Count != 1) return cs.Count == 0 ? "台账里没有“" + kind + "”。" : "有 " + cs.Count + " 条“" + kind + "”，无法确定删哪条。";
            var c0 = cs[0];
            return Add(chat, p, "合同台账", "删除 " + c0.Kind + "：" + c0.Party + "，" + M(c0.Amount) + " 元", reason, () => p.Contracts.Remove(c0));
        }
    }
}
