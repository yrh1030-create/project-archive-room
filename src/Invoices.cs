using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DangAn
{
    /// <summary>从合同、支付凭证等混合扫描件里找出发票页，单独拆成 PDF，并按凭证文件夹、金额关联到收支记录。原文件不动。</summary>
    public static class Invoices
    {
        public const string OutFolder = "发票（从合同凭证中拆出）";
        const string CacheFile = "发票识别缓存.json";
        static readonly string[] Kw = { "发票代码", "发票号码", "价税合计", "开票日期", "纳税人识别号", "销售方", "购买方", "税务局", "开票人", "税额", "电子发票", "专用发票", "普通发票", "收款人", "复核" };
        static readonly string[] Strong = { "价税合计", "发票代码", "发票号码", "开票日期" };

        public static bool IsInvoicePage(string t)
        {
            if (string.IsNullOrEmpty(t)) return false;
            string s = t.Replace(" ", "");
            if (!s.Contains("发票") && !s.Contains("税务局")) return false;
            int strong = Strong.Count(k => s.Contains(k));
            int n = Kw.Count(k => s.Contains(k));
            return strong >= 1 && n >= 4;
        }

        static double Money(string v) { double d; double.TryParse(v.Replace(",", "").Replace("，", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out d); return d; }

        /// <summary>OCR 把小数点识别成“·”“．”等，统一成“.”；去掉空格。</summary>
        static string Norm(string t)
        {
            string s = (t ?? "").Replace(" ", "");
            s = Regex.Replace(s, @"(?<=\d)[·．。•‧](?=\d)", ".");
            return s;
        }

        public static InvoiceRec Parse(string text)
        {
            var r = new InvoiceRec();
            string s = Norm(text).Replace("\n", " ");
            var m = Regex.Match(s, @"小写[）)]?[^0-9]{0,6}?([0-9][0-9,]*\.[0-9]{2})");
            if (!m.Success) m = Regex.Match(s, @"[（(]小写[）)]\s?[¥￥Y]?\s?([0-9][0-9,]*\.[0-9]{2})");
            if (m.Success) r.Amount = Money(m.Groups[1].Value);
            if (r.Amount == 0)
            {
                var all = Regex.Matches(s, @"[¥￥]\s?([0-9][0-9,]*\.[0-9]{2})").Cast<Match>().Select(x => Money(x.Groups[1].Value)).ToList();
                if (all.Count > 0) r.Amount = all.Max();
            }
            m = Regex.Match(s, @"(20\d\d)年(\d{1,2})月(\d{1,2})[日曰]");
            if (!m.Success) m = Regex.Match(s, @"(20\d\d)[-.](\d{1,2})[-.](\d{1,2})");
            if (m.Success) r.Date = m.Groups[1].Value + "-" + m.Groups[2].Value.PadLeft(2, '0') + "-" + m.Groups[3].Value.PadLeft(2, '0');
            m = Regex.Match(s, @"发票号码[:：]?\s?([0-9]{8,20})");
            if (!m.Success) m = Regex.Match(s, @"[№Nn][oO°º]?\s?([0-9]{8})");
            if (m.Success) r.Number = m.Groups[1].Value;
            // 销售方：发票上出现的单位名里，排除购买方（街道办）和银行、税务
            foreach (Match x in Regex.Matches(s, @"([\u4e00-\u9fa5（）()]{2,30}?(?:有限责任公司|股份有限公司|有限公司|分公司|事务所|研究院|设计院|合作社|检测中心))"))
            {
                var name = Regex.Replace(x.Groups[1].Value, "^(名称|称|销售方|销方|售|单位)", "");
                if (name.Length < 4 || Regex.IsMatch(name, "李渡|人民政府|办事处|街道|银行|税务|全能王")) continue;
                r.Seller = name; break;
            }
            return r;
        }

        static string Fingerprint(string t)
        {
            var s = Regex.Replace(t ?? "", @"[^\u4e00-\u9fa50-9]", "");
            return s.Length > 160 ? s.Substring(0, 160) : s;
        }

        /// <summary>清除本项目以前拆出的发票（只删本程序生成、登记在案的文件），用于重新拆分。</summary>
        public static void Reset(Store st, Project p)
        {
            foreach (var x in st.Lib.Invoices.Where(i => i.Project == p.Id).ToList())
            {
                string full = Path.Combine(st.Lib.Root, x.Rel);
                if (x.Rel.Contains(OutFolder)) try { if (File.Exists(full)) File.Delete(full); } catch { }
                st.Texts.Remove(x.Rel); st.TextsDirty = true;
                foreach (var pay in p.Payments.Where(y => y.InvoiceFile == x.Rel)) pay.InvoiceFile = "";
                st.Lib.Invoices.Remove(x);
            }
            string csv = Path.Combine(st.ProjectFolder(p), OutFolder, "拆分记录.csv");
            try { if (File.Exists(csv)) File.Delete(csv); } catch { }
        }

        // ---------------- 生成只含图片的 PDF ----------------
        public static void WriteImagePdf(string path, List<Tuple<byte[], int, int, double, double>> pages)
        {
            var offsets = new List<long>();
            using (var fs = File.Create(path))
            {
                Action<string> W = s => { var b = Encoding.ASCII.GetBytes(s); fs.Write(b, 0, b.Length); };
                W("%PDF-1.4\n");
                int n = pages.Count;
                // 对象编号：1 目录，2 页树，之后每页 3 个对象（页、图像、内容）
                Action<int> Begin = id => { while (offsets.Count < id) offsets.Add(0); offsets[id - 1] = fs.Position; W(id + " 0 obj\n"); };
                Begin(1); W("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                Begin(2); W("<< /Type /Pages /Count " + n + " /Kids [" + string.Join(" ", Enumerable.Range(0, n).Select(i => (3 + i * 3) + " 0 R")) + "] >>\nendobj\n");
                for (int i = 0; i < n; i++)
                {
                    var pg = pages[i];
                    int pid = 3 + i * 3, iid = pid + 1, cid = pid + 2;
                    string pw = pg.Item4.ToString("0.##", CultureInfo.InvariantCulture), ph = pg.Item5.ToString("0.##", CultureInfo.InvariantCulture);
                    Begin(pid); W("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + pw + " " + ph + "] /Resources << /XObject << /Im0 " + iid + " 0 R >> >> /Contents " + cid + " 0 R >>\nendobj\n");
                    Begin(iid); W("<< /Type /XObject /Subtype /Image /Width " + pg.Item2 + " /Height " + pg.Item3 + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + pg.Item1.Length + " >>\nstream\n");
                    fs.Write(pg.Item1, 0, pg.Item1.Length); W("\nendstream\nendobj\n");
                    string content = "q " + pw + " 0 0 " + ph + " 0 0 cm /Im0 Do Q";
                    Begin(cid); W("<< /Length " + content.Length + " >>\nstream\n" + content + "\nendstream\nendobj\n");
                }
                long xref = fs.Position;
                W("xref\n0 " + (offsets.Count + 1) + "\n0000000000 65535 f \n");
                foreach (var o in offsets) W(o.ToString("0000000000") + " 00000 n \n");
                W("trailer\n<< /Size " + (offsets.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            }
        }

        static string Safe(string s, int max)
        {
            s = Regex.Replace(s ?? "", @"[\\/:*?""<>|\s]", "");
            return s.Length > max ? s.Substring(0, max) : s;
        }

        // ---------------- 处理一个项目 ----------------
        public class Result { public int Scanned, Pages, Found, Written, Linked; }

        public static Result Process(Store st, Project p, Action<string> log, CancellationToken ct)
        {
            var res = new Result();
            if (p.IsRoot) return res;
            // 候选：PDF，且名字带“发票”，或在资金支付类，或已识别文字里出现发票特征
            var cands = new List<FileRec>();
            var seenCopy = new HashSet<string>();
            foreach (var f in Store.SortForDisplay(st.FilesOf(p)))
            {
                if (f.Ext != ".pdf" || f.Rel.Contains(OutFolder)) continue;
                string t; lock (st.Sync) st.Texts.TryGetValue(f.Rel, out t);
                int kw = t == null ? 0 : Kw.Count(k => t.Replace(" ", "").Contains(k));
                if (!(f.Name.Contains("发票") || kw >= 2 || f.Cat == "09 资金支付" || (f.Cat == "04 合同协议" && kw >= 1))) continue;
                string copyKey = Regex.Replace(f.Name, @"\(\d+\)(?=\.pdf$)", "") + "|" + f.Size;
                if (!seenCopy.Add(copyKey)) continue;
                cands.Add(f);
            }
            res.Scanned = cands.Count;
            var cache = LoadCache(st);
            var found = new List<Tuple<FileRec, int, string>>();   // 文件，页序（0 起），页文字
            int done = 0;
            object lk = new object();
            var po = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2)), CancellationToken = ct };
            try
            {
                Parallel.ForEach(cands, po, f =>
                {
                    string key = f.Rel + "|" + f.Size;
                    List<string> pages;
                    lock (lk) cache.TryGetValue(key, out pages);
                    if (pages == null)
                    {
                        try { int pc; pages = Ocr.PdfPages(st.FullPath(f), 80, out pc); } catch { pages = new List<string>(); }
                        lock (lk) cache[key] = pages;
                    }
                    lock (lk)
                    {
                        res.Pages += pages.Count;
                        for (int i = 0; i < pages.Count; i++) if (IsInvoicePage(pages[i])) found.Add(Tuple.Create(f, i, pages[i]));
                        done++;
                        if (log != null) log("识别发票：" + p.Name + " " + done + "/" + cands.Count + " " + f.Name);
                    }
                });
            }
            catch (OperationCanceledException) { }
            SaveCache(st, cache);

            // 拆出
            string outDir = Path.Combine(st.ProjectFolder(p), OutFolder);
            var skip = new HashSet<string>(st.Lib.SkipInvoicePages ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var keys = new HashSet<string>(st.Lib.Invoices.Where(x => x.Project == p.Id).Select(x => x.Number != "" ? "N" + x.Number : "A" + x.Amount.ToString("0.00") + "|" + x.Date + "|" + x.Seller));
            var prints = new HashSet<string>();
            var csv = new StringBuilder();
            foreach (var hit in found.OrderBy(x => x.Item1.Rel).ThenBy(x => x.Item2))
            {
                if (skip.Contains(hit.Item1.Rel + "|" + (hit.Item2 + 1))) continue;   // 已被换成重开的新票 / 已删除的旧票
                if (st.Lib.Invoices.Any(x => string.Equals(x.Rel, hit.Item1.Rel, StringComparison.OrdinalIgnoreCase))) continue;   // 本身就是单独登记的发票
                var inv = Parse(hit.Item3);
                // 去重：同一张发票常被扫进好几份文件（合同副本、凭证）
                string key = inv.Number != "" ? "N" + inv.Number : "A" + inv.Amount.ToString("0.00") + "|" + inv.Date + "|" + inv.Seller;
                if (inv.Number == "" && inv.Amount == 0) key = "T" + Fingerprint(hit.Item3);
                if (!prints.Add(Fingerprint(hit.Item3)) || !keys.Add(key)) continue;
                res.Found++;
                inv.Source = hit.Item1.Rel; inv.Page = hit.Item2 + 1; inv.Project = p.Id;
                string name = "发票_" + (inv.Date != "" ? inv.Date : "日期不详") + "_" + (inv.Seller != "" ? Safe(inv.Seller, 24) : Safe(Path.GetFileNameWithoutExtension(hit.Item1.Name), 20))
                    + "_" + (inv.Amount > 0 ? inv.Amount.ToString("0.00") + "元" : "金额待核") + ".pdf";
                Directory.CreateDirectory(outDir);
                string full = Path.Combine(outDir, name);
                int k2 = 2;
                while (File.Exists(full) && !st.Lib.Invoices.Any(x => st.FullPath(new FileRec { Rel = x.Rel }) == full && x.Source == inv.Source && x.Page == inv.Page))
                    full = Path.Combine(outDir, Path.GetFileNameWithoutExtension(name) + "(" + k2++ + ").pdf");
                if (!File.Exists(full))
                {
                    try
                    {
                        int w, h; double pw, ph;
                        var jpg = Ocr.RenderJpeg(st.FullPath(hit.Item1), hit.Item2, 200, out w, out h, out pw, out ph);
                        WriteImagePdf(full, new List<Tuple<byte[], int, int, double, double>> { Tuple.Create(jpg, w, h, pw, ph) });
                        res.Written++;
                    }
                    catch (Exception ex) { if (log != null) log("拆分失败：" + hit.Item1.Name + " 第" + inv.Page + "页 " + ex.Message); continue; }
                }
                inv.Rel = full.Substring(st.Lib.Root.TrimEnd('\\').Length + 1);
                lock (st.Sync)
                {
                    st.Lib.Invoices.RemoveAll(x => x.Rel == inv.Rel);
                    st.Lib.Invoices.Add(inv);
                    st.Texts[inv.Rel] = "【拆自：" + inv.Source + " 第" + inv.Page + "页】\n" + hit.Item3;
                    st.TextsDirty = true;
                }
                csv.AppendLine(string.Join(",", new[] { Path.GetFileName(full), inv.Source, inv.Page.ToString(), inv.Amount.ToString("0.00"), inv.Date, inv.Seller, inv.Number }.Select(x => "\"" + (x ?? "").Replace("\"", "'") + "\"")));
            }
            if (csv.Length > 0)
            {
                string log2 = Path.Combine(outDir, "拆分记录.csv");
                bool exists = File.Exists(log2);
                using (var w = new StreamWriter(log2, true, new UTF8Encoding(true)))
                {
                    if (!exists) w.WriteLine("发票文件,来源文件,来源页码,价税合计（元）,开票日期,销售方,发票号码");
                    w.Write(csv.ToString());
                }
            }
            st.Scan(null);
            res.Linked = Link(st, p);
            st.Save();
            return res;
        }

        /// <summary>把拆出的发票挂到收支记录：同一凭证文件夹 / 来源文件优先，其次金额一致。返回新挂上的笔数。</summary>
        public static int Link(Store st, Project p)
        {
            var invs = st.InvoicesOf(p).Where(x => File.Exists(Path.Combine(st.Lib.Root, x.Rel))).ToList();
            if (invs.Count == 0) return 0;
            var used = new HashSet<string>(p.Payments.Select(x => x.InvoiceFile).Where(x => x != ""));
            int n = 0;
            Func<string, string> dirOf = r => Path.GetDirectoryName(r) ?? "";
            var outs = p.Payments.Where(x => x.Dir != "收入").ToList();
            // 已经挂的是“合同及发票”这类混合文件 → 换成拆出的单张发票
            foreach (var pay in outs.Where(x => x.InvoiceFile != ""))
            {
                var fromIt = invs.Where(x => x.Source == pay.InvoiceFile && !used.Contains(x.Rel)).OrderBy(x => Math.Abs(x.Amount - pay.Amount)).FirstOrDefault();
                if (fromIt != null) { used.Remove(pay.InvoiceFile); pay.InvoiceFile = fromIt.Rel; used.Add(fromIt.Rel); n++; }
            }
            // 1）同一凭证文件夹 / 同一文件里的发票
            foreach (var pay in outs.Where(x => x.InvoiceFile == "" && x.VoucherFile != ""))
            {
                string vd = dirOf(pay.VoucherFile);
                var near = invs.Where(x => !used.Contains(x.Rel) && (x.Source == pay.VoucherFile || dirOf(x.Source) == vd)).ToList();
                near = near.Where(x => x.Members == null || x.Members.Count == 0).ToList();
                var pick = near.FirstOrDefault(x => Math.Abs(x.Amount - pay.Amount) <= 1);
                bool smallFolder = st.FilesOf(p).Count(f => dirOf(f.Rel) == vd) <= 6;   // 一笔一个文件夹的那种
                if (pick == null && near.Count == 1 && smallFolder) pick = near[0];
                if (pick == null && near.Count > 1 && smallFolder)
                {
                    // 一笔款分开了好几张发票（如预付款开成 10 万一张）：合成一份“发票合集”
                    pick = Combine(st, p, near, pay);
                    if (pick != null) foreach (var x in near) used.Add(x.Rel);
                }
                if (pick != null) { pay.InvoiceFile = pick.Rel; used.Add(pick.Rel); n++; }
            }
            // 2）金额一致（±1 元）；同额多张时取开票日期最接近、且不晚于付款日的
            foreach (var pay in outs.Where(x => x.InvoiceFile == "" && x.Amount > 0))
            {
                var same = invs.Where(x => !used.Contains(x.Rel) && Math.Abs(x.Amount - pay.Amount) <= 1).ToList();
                if (same.Count == 0) continue;
                DateTime pd;
                InvoiceRec pick = same[0];
                if (same.Count > 1 && DateTime.TryParse(pay.Date, out pd))
                    pick = same.OrderBy(x => { DateTime d; return DateTime.TryParse(x.Date, out d) ? (d <= pd.AddDays(7) ? (pd - d).TotalDays : 9999) : 5000; }).First();
                pay.InvoiceFile = pick.Rel; used.Add(pick.Rel); n++;
            }
            return n;
        }

        /// <summary>把几张发票合成一个多页 PDF，登记为一条“合集”记录。</summary>
        static InvoiceRec Combine(Store st, Project p, List<InvoiceRec> parts, Payment pay)
        {
            try
            {
                parts = parts.OrderBy(x => x.Date == "" ? "9999" : x.Date).ThenBy(x => x.Number).ToList();
                double sum = parts.Sum(x => x.Amount);
                var first = parts[0];
                string seller = parts.Select(x => x.Seller).FirstOrDefault(x => x != "") ?? "";
                string name = "发票合集_" + (first.Date != "" ? first.Date : "日期不详") + "_" + Safe(seller != "" ? seller : Finance.Short(p, pay.Summary), 24) + "_" + parts.Count + "张_合计" + sum.ToString("0.00") + "元.pdf";
                string full = Path.Combine(st.ProjectFolder(p), OutFolder, name);
                if (!File.Exists(full))
                {
                    var pages = new List<Tuple<byte[], int, int, double, double>>();
                    foreach (var x in parts)
                    {
                        int w, h; double pw, ph;
                        pages.Add(Tuple.Create(Ocr.RenderJpeg(Path.Combine(st.Lib.Root, x.Source), x.Page - 1, 200, out w, out h, out pw, out ph), w, h, pw, ph));
                    }
                    WriteImagePdf(full, pages);
                }
                var rec = new InvoiceRec { Rel = full.Substring(st.Lib.Root.TrimEnd('\\').Length + 1), Source = first.Source, Page = 0, Amount = sum, Date = first.Date, Seller = seller, Number = "合集" + parts.Count + "张", Project = p.Id, Members = parts.Select(x => x.Rel).ToList() };
                lock (st.Sync)
                {
                    st.Lib.Invoices.RemoveAll(x => x.Rel == rec.Rel);
                    st.Lib.Invoices.Add(rec);
                    st.Texts[rec.Rel] = "发票合集（" + parts.Count + " 张，合计 " + sum.ToString("0.00") + " 元）\n" + string.Join("\n", parts.Select(x => x.Date + " " + x.Seller + " " + x.Amount.ToString("0.00") + " 元 " + x.Number));
                    st.TextsDirty = true;
                }
                return rec;
            }
            catch { return null; }
        }

        /// <summary>
        /// 把一份单独的发票文件（如重开的新票、补进来的电子发票）登记为发票：识别金额、日期、销售方。
        /// 不是发票、或一份文件里有多张发票（交给“拆分发票”）时返回 null。
        /// </summary>
        public static InvoiceRec RegisterFile(Store st, Project p, string rel)
        {
            string full = Path.Combine(st.Lib.Root, rel), ext = Path.GetExtension(rel).ToLowerInvariant();
            var pages = new List<string>();
            try
            {
                if (ext == ".pdf") { int pc; pages = Ocr.PdfPages(full, 6, out pc); }
                else if (TextJob.OcrExts.Contains(ext)) pages.Add(Ocr.Image(full));
            }
            catch { }
            var hits = Enumerable.Range(0, pages.Count).Where(i => IsInvoicePage(pages[i])).ToList();
            if (hits.Count != 1) return null;
            var rec = Parse(pages[hits[0]]);
            rec.Rel = rel; rec.Source = rel; rec.Page = hits[0] + 1; rec.Project = p.Id;
            lock (st.Sync)
            {
                st.Lib.Invoices.RemoveAll(x => string.Equals(x.Rel, rel, StringComparison.OrdinalIgnoreCase));
                st.Lib.Invoices.Add(rec);
                if (!st.Texts.ContainsKey(rel)) { st.Texts[rel] = string.Join("\n", pages); st.TextsDirty = true; }
            }
            return rec;
        }

        public static Dictionary<string, List<string>> LoadCache(Store st)
        {
            string path = Path.Combine(st.DbDir, CacheFile);
            try { if (File.Exists(path)) return Store.Ser().Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(path, Encoding.UTF8)); } catch { }
            return new Dictionary<string, List<string>>();
        }

        public static void SaveCache(Store st, Dictionary<string, List<string>> cache)
        {
            try { File.WriteAllText(Path.Combine(st.DbDir, CacheFile), Store.Ser().Serialize(cache), new UTF8Encoding(false)); } catch { }
        }
    }
}
