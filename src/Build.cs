using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DangAn
{
    /// <summary>一次性：根据种子配置和以前的整理成果（work_ledger / work_major）建立档案库。</summary>
    public static class Builder
    {
        static Dictionary<string, object> D(object o) { return o as Dictionary<string, object>; }
        static IEnumerable<object> A(object o) { var a = o as IEnumerable; if (a == null || o is string) return new object[0]; return a.Cast<object>(); }
        static string S(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : ""; }
        static double N(Dictionary<string, object> d, string k) { object v; if (d != null && d.TryGetValue(k, out v) && v != null) { try { return Convert.ToDouble(v); } catch { } } return 0; }
        static object Json(string path) { return Store.Ser().DeserializeObject(File.ReadAllText(path, Encoding.UTF8)); }

        public static void Run(string root, string seedPath, Action<string> log)
        {
            var st = Store.Create(root);
            // 重建时保留已经识别过的文字，避免重复 OCR
            if (File.Exists(st.LibPath))
            {
                try
                {
                    var prev = Store.Load(st.DbDir);
                    foreach (var kv in prev.Texts) st.Texts[kv.Key] = kv.Value;
                    st.Lib.TextTried = prev.Lib.TextTried;
                    st.Lib.Invoices = prev.Lib.Invoices ?? new List<InvoiceRec>();   // 保留已拆出的发票登记
                }
                catch { }
            }
            var seed = D(Json(seedPath));
            string ledgerDir = Path.Combine(root, "work_ledger"), majorDir = Path.Combine(root, "work_major");
            Dictionary<string, object> major = File.Exists(Path.Combine(majorDir, "major_data.json")) ? D(Json(Path.Combine(majorDir, "major_data.json"))) : null;
            Dictionary<string, object> ledger = File.Exists(Path.Combine(ledgerDir, "ledger_data.json")) ? D(Json(Path.Combine(ledgerDir, "ledger_data.json"))) : null;

            foreach (var po in A(seed["projects"]))
            {
                var s = D(po);
                var p = new Project { Id = S(s, "id"), Name = S(s, "name"), Type = S(s, "type") };
                p.Code = p.Id;
                foreach (var f in A(s["folders"])) p.Folders.Add(Convert.ToString(f));
                p.Required = Store.DefaultRequired(p.Type);
                p.Set("项目名称", p.Name, "");
                // 重大经济事项基本情况表的字段
                string mname = S(s, "major");
                if (major != null && mname.Length > 0)
                {
                    foreach (var mp in A(major["projects"]))
                    {
                        var m = D(mp);
                        if (S(m, "name") != mname) continue;
                        foreach (var fo in A(m["fields"]))
                        {
                            var arr = A(fo).Select(x => Convert.ToString(x)).ToArray();
                            if (arr.Length < 2) continue;
                            string name = arr[0];
                            if (name == "项目名称") continue;
                            p.Set(name, arr[1], arr.Length > 2 ? arr[2] : "");
                        }
                    }
                }
                var rows = A(s["rows"]).Select(x => Convert.ToInt32(x)).ToList();
                if (major != null && rows.Count > 0)
                {
                    foreach (var ro in A(major["rows"]))
                    {
                        var r = D(ro);
                        if (!rows.Contains((int)N(r, "row"))) continue;
                        string src = "重大经济事项执行情况表第" + S(r, "row") + "行";
                        if (p.Get("决策／采购形式") == "") p.Set("决策／采购形式", S(r, "decision"), src);
                        p.Set("重大事项：起讫／执行节点", S(r, "period"), src);
                        p.Set("重大事项：金额（元）", N(r, "amount").ToString("0.00"), src);
                        p.Set("重大事项：说明", S(r, "note"), src);
                        if (S(r, "voucher") != "") p.Set("记账凭证", S(r, "voucher"), src);
                    }
                }
                // 合同台账
                var lnames = A(s["ledger"]).Select(x => Convert.ToString(x)).ToList();
                if (ledger != null && lnames.Count > 0)
                {
                    foreach (var ro in A(ledger["rows"]))
                    {
                        var r = D(ro);
                        if (!lnames.Contains(S(r, "project"))) continue;
                        var c = new Contract
                        {
                            Kind = S(r, "kind"), Party = S(r, "party"),
                            Amount = N(r, "h") != 0 ? N(r, "h") : N(r, "g"),
                            Date = S(r, "date"), Paid = N(r, "paid"), Unpaid = N(r, "unpaid"),
                            Final = S(r, "finalText"), Content = S(r, "content"), Note = S(r, "note"), Source = S(r, "sourceText")
                        };
                        string lp = S(r, "project");
                        if (lnames.Count > 1 && lp != p.Name) c.Kind = lp + (c.Kind.Length > 0 ? "—" + c.Kind : "");
                        p.Contracts.Add(c);
                    }
                }
                st.Lib.Projects.Add(p);
            }

            if (seed.ContainsKey("rootFolders")) foreach (var rf in A(seed["rootFolders"])) st.Proj(Store.RootProjectId).Folders.Add(Convert.ToString(rf));
            if (seed.ContainsKey("assign")) foreach (var kv in D(seed["assign"])) st.Lib.FileProject[kv.Key] = Convert.ToString(kv.Value);
            if (seed.ContainsKey("share")) foreach (var kv in D(seed["share"])) st.Lib.SharedWith[kv.Key] = A(kv.Value).Select(x => Convert.ToString(x)).ToList();
            log("导入以前识别的文字…");
            ImportLegacyTexts(st, root, ledgerDir, majorDir, log);
            log("扫描全部资料…");
            var res = st.Scan(log);
            log("登记文件 " + st.Lib.Files.Count + " 份；新项目 " + st.Lib.Projects.Count(p => !p.IsRoot) + " 个");
            foreach (var p in st.RealProjects) Complete(st, p);
            foreach (var p in st.RealProjects) Finance.Import(st, p);
            st.TextsDirty = true;
            st.Save();
            log("完成：" + st.LibPath);
        }

        static void ImportLegacyTexts(Store st, string root, string ledgerDir, string majorDir, Action<string> log)
        {
            var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var ocrPages = new Dictionary<string, SortedDictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
            var upstreamIds = new Dictionary<int, string>();
            Action<string, string> add = (path, text) =>
            {
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(text) || text.StartsWith("ERROR")) return;
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
                string rel = path.Substring(root.TrimEnd('\\').Length + 1);
                text = Ocr.Normalize(text.Replace("\r\n", "\n").Replace('\r', '\n'));
                if (text.Trim().Length < 4) return;
                string old;
                if (!texts.TryGetValue(rel, out old) || old.Trim().Length < text.Trim().Length) texts[rel] = text;
            };
            foreach (var dir in new[] { ledgerDir, majorDir })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var jf in Directory.GetFiles(dir, "*.json"))
                {
                    object o;
                    try { o = Json(jf); } catch { continue; }
                    IEnumerable<object> items = o is Dictionary<string, object> ? D(o).Values.Cast<object>() : A(o);
                    bool upstream = Path.GetFileName(jf) == "upstream.json";
                    foreach (var it in items)
                    {
                        var d = D(it);
                        if (d == null) continue;
                        string path = S(d, "path");
                        if (path.Length > 0)
                        {
                            string text = S(d, "text");
                            if (text.Trim().Length == 0 && d.ContainsKey("pages"))
                                text = string.Join("\n", A(d["pages"]).Select(pg => S(D(pg), "text")));
                            add(path, text);
                            if (upstream && d.ContainsKey("id")) upstreamIds[(int)N(d, "id")] = path;
                        }
                        string outp = S(d, "out");
                        if (outp.Length > 0)
                        {
                            string src = S(d, "source");
                            if (src.Length == 0 && Path.GetFileName(outp).StartsWith("U"))
                            {
                                string pathU; if (upstreamIds.TryGetValue((int)N(d, "id"), out pathU)) src = pathU;
                            }
                            if (src.Length == 0 || !File.Exists(outp)) continue;
                            string t = File.ReadAllText(outp, Encoding.UTF8);
                            if (t.StartsWith("ERROR")) continue;
                            SortedDictionary<int, string> pages;
                            if (!ocrPages.TryGetValue(src, out pages)) ocrPages[src] = pages = new SortedDictionary<int, string>();
                            pages[(int)N(d, "p")] = t;
                        }
                    }
                }
            }
            // upstream_ocr_queue 可能先于 upstream.json 被读到：补一遍
            string uq = Path.Combine(majorDir, "upstream_ocr_queue.json");
            if (File.Exists(uq))
                foreach (var it in A(Json(uq)))
                {
                    var d = D(it); string src;
                    if (d == null || !upstreamIds.TryGetValue((int)N(d, "id"), out src)) continue;
                    string outp = S(d, "out");
                    if (!File.Exists(outp)) continue;
                    SortedDictionary<int, string> pages;
                    if (!ocrPages.TryGetValue(src, out pages)) ocrPages[src] = pages = new SortedDictionary<int, string>();
                    pages[(int)N(d, "p")] = File.ReadAllText(outp, Encoding.UTF8);
                }
            foreach (var kv in ocrPages) add(kv.Key, string.Join("\n", kv.Value.Select(x => "【第" + x.Key + "页】\n" + x.Value)));
            foreach (var kv in texts)
                st.Texts[kv.Key] = kv.Value.Length > Store.MaxText ? kv.Value.Substring(0, Store.MaxText) : kv.Value;
            log("导入识别文字 " + st.Texts.Count + " 份");
        }

        /// <summary>补齐标准字段，并从中标通知书、合同中自动识别单位和金额（标注“待核对”）。</summary>
        public static void Complete(Store st, Project p)
        {
            var fields = new List<Field>();
            foreach (var name in Store.StdFields)
            {
                var f = p.Fields.FirstOrDefault(x => x.Name == name);
                if (name == "中标／施工单位" && f == null) f = new Field(name, "", "");
                fields.Add(f ?? new Field(name, "", ""));
            }
            foreach (var f in p.Fields) if (!Store.StdFields.Contains(f.Name)) fields.Add(f);
            p.Fields = fields;
            if (p.Get("项目名称") == "") p.Set("项目名称", p.Name, "");

            var con = p.Contracts.FirstOrDefault(c => c.Kind.Contains("施工"));
            if (p.Get("中标／施工单位") == "" && con != null && con.Party != "")
                p.Set("中标／施工单位", con.Party, "合同台账");
            if (p.Get("施工合同／成交金额") == "" && con != null && con.Amount > 0)
                p.Set("施工合同／成交金额", con.Amount.ToString("0.00") + "元", "合同台账");

            // 中标通知书 / 施工合同 里找中标人和金额
            if (p.Get("中标／施工单位") == "" || p.Get("施工合同／成交金额") == "")
            {
                Func<FileRec, int> rank = f => f.Name.Contains("中标通知") || f.Name.Contains("成交通知") ? 0 : f.Name.Contains("施工合同") ? 1
                    : f.Name.Contains("合同") && !Regex.IsMatch(f.Name, "监理|检测|设计|审核|代理|廉政|模板|咨询|测量|勘察|一审|二审|评审") ? 2 : 9;
                foreach (var f in st.FilesOf(p).Where(f => rank(f) < 9).OrderBy(rank))
                {
                    string t; if (!st.Texts.TryGetValue(f.Rel, out t)) continue;
                    t = t.Replace(" ", "").Replace("\u3000", "");
                    if (p.Get("中标／施工单位") == "")
                    {
                        var m = Regex.Match(t, @"(?:中标人|成交人|中标单位|成交单位|中标公司|承包人|施工单位|乙方)(?:名称)?(?:（全称）|\(全称\))?[：:\n]*([^\s：:，,。；;\n|（(X]{4,40}?(?:集团(?:有限责任公司|股份有限公司|有限公司)?|公司|研究院|设计院|事务所|合作社)(?:[\u4e00-\u9fa5]{2,6}分公司)?)");
                        if (m.Success) p.Set("中标／施工单位", m.Groups[1].Value, "自动识别（待核对）：" + f.Name);
                    }
                    if (p.Get("施工合同／成交金额") == "")
                    {
                        var m = Regex.Match(t, @"(?:中标价|中标金额|中标额|中标总价|成交价|成交金额|合同价款|合同金额|签约合同价|合同总价|合同价)[^0-9￥¥X\n]{0,40}[￥¥]?([0-9][0-9,，.]*\.?[0-9]*)\s*(万元|元)");
                        if (m.Success) p.Set("施工合同／成交金额", m.Groups[1].Value.Replace("，", ",") + m.Groups[2].Value, "自动识别（待核对）：" + f.Name);
                    }
                }
            }
        }
    }
}
