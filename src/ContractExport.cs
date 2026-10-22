using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace DangAn
{
    /// <summary>
    /// 一键导出“经济合同 PDF 包”：pdf\ 下每份合同一个 PDF（去掉夹带的发票、凭证、回单等无关页；Word、照片转成 PDF），
    /// 外加《合同PDF清单.xlsx》和可离线浏览的《合同预览.html》。
    /// </summary>
    public static class ContractExport
    {
        class Row { public int No; public Project P; public Contract C; public string Source = ""; }

        class Doc
        {
            public string Source, File = "", Title, Kind = "合同", Project, Removed = "";
            public List<Row> Rows = new List<Row>();
            public long Size; public int Pages;
        }

        public class Result { public string Folder; public int Docs, Rows, Missing, Cleaned; public List<string> Log = new List<string>(); }

        static readonly string[] Exts = { ".pdf", ".doc", ".docx", ".wps", ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

        // ---------------- 找合同原件 ----------------
        static string KindKey(string kind)
        {
            string k = kind.Contains("—") ? kind.Substring(kind.LastIndexOf('—') + 1) : kind;
            k = Regex.Replace(k, @"[（(].*?[）)]|合同|协议|事项|服务|申请表登记", "");
            return k.Trim();
        }

        static int Lcs(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
            int best = 0; var prev = new int[b.Length + 1];
            for (int i = 1; i <= a.Length; i++)
            {
                var cur = new int[b.Length + 1];
                for (int j = 1; j <= b.Length; j++) if (a[i - 1] == b[j - 1]) { cur[j] = prev[j - 1] + 1; if (cur[j] > best) best = cur[j]; }
                prev = cur;
            }
            return best;
        }

        static string Resolve(Store st, Project p, Contract c)
        {
            // 1) 台账里登记的来源文件
            if (!string.IsNullOrEmpty(c.Source))
            {
                // 可能登记了多个文件（用；、换行等分隔），逐个试，优先 PDF
                var cands = Regex.Split(c.Source, @"[;；\r\n|]+").Select(x => x.Trim().Trim('"')).Where(x => x.Length > 0)
                    .OrderBy(x => x.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? 0 : 1);
                foreach (var raw in cands)
                {
                    try
                    {
                        string rel = raw;
                        if (Path.IsPathRooted(rel) && rel.StartsWith(st.Lib.Root, StringComparison.OrdinalIgnoreCase)) rel = rel.Substring(st.Lib.Root.TrimEnd('\\').Length + 1);
                        if (File.Exists(Path.Combine(st.Lib.Root, rel)) && Exts.Contains(Path.GetExtension(rel).ToLowerInvariant())) return rel;
                    }
                    catch (ArgumentException) { }   // 不是合法路径，跳过
                }
            }
            // 2) 在项目资料里找：合同类文件，名字里有合同类型或签订方
            string key = KindKey(c.Kind);
            FileRec best = null; int bs = 0;
            foreach (var f in st.FilesOf(p))
            {
                if (!Exts.Contains(f.Ext) || f.Rel.Contains(Invoices.OutFolder)) continue;
                bool contractish = f.Cat == "04 合同协议" || Regex.IsMatch(f.Name, "合同|协议");
                if (!contractish) continue;
                int s = 0;
                if (key.Length >= 2 && f.Name.Contains(key)) s += 10 + key.Length;
                else if (key.Length >= 2) { int l = Lcs(key, f.Name); if (l >= 2) s += l * 2; }
                if (!string.IsNullOrEmpty(c.Party) && c.Party.Length >= 4) { int l = Lcs(c.Party, f.Name); if (l >= 4) s += l; }
                if (s == 0) continue;
                if (f.Ext == ".pdf") s += 3;
                if (Regex.IsMatch(f.Name, @"\(\d\)|副本")) s -= 2;
                if (s > bs) { bs = s; best = f; }
            }
            return best != null && bs >= 6 ? best.Rel : "";
        }

        // ---------------- 无关页识别 ----------------
        static readonly Regex Suspicious = new Regex("发票|记账凭证|回单|付款申请|支付申请|拨付申请|请款单|报销");

        /// <summary>这一页是不是夹带进来的发票、凭证、回单、付款申请；不是返回 null。只看页首几行判断凭证类，避免误删合同条款页。</summary>
        public static string PageKind(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return null;
            if (Invoices.IsInvoicePage(t)) return "发票";
            var lines = t.Replace(" ", "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith("【第")).Take(6).ToList();
            string head = string.Join("", lines), s = t.Replace(" ", "");
            if (head.Contains("记账凭证") || (s.Contains("会计主管") && s.Contains("记账") && s.Contains("出纳"))) return "记账凭证";
            if (Regex.IsMatch(head, "回单|转账凭证|收款通知|业务凭证")) return "银行回单";
            if (Regex.IsMatch(head, "付款申请|支付申请|拨付申请|请款单|报销单|资金审签|支付审批")) return "支付申请";
            return null;
        }

        // ---------------- Word → PDF ----------------
        class WordConverter : IDisposable
        {
            object app; bool tried;
            static object Call(object o, string m, params object[] a) { return o.GetType().InvokeMember(m, BindingFlags.InvokeMethod, null, o, a); }
            static object Get(object o, string p) { return o.GetType().InvokeMember(p, BindingFlags.GetProperty, null, o, null); }
            public bool Convert(string src, string dst)
            {
                if (app == null)
                {
                    if (tried) return false;
                    tried = true;
                    var t = Type.GetTypeFromProgID("Word.Application") ?? Type.GetTypeFromProgID("KWps.Application");
                    if (t == null) return false;
                    try { app = Activator.CreateInstance(t); app.GetType().InvokeMember("Visible", BindingFlags.SetProperty, null, app, new object[] { false }); } catch { app = null; return false; }
                    try { app.GetType().InvokeMember("DisplayAlerts", BindingFlags.SetProperty, null, app, new object[] { 0 }); } catch { }
                }
                object d = null;
                try
                {
                    d = Call(Get(app, "Documents"), "Open", src, false, true, false, "\u0001");
                    try { Call(d, "ExportAsFixedFormat", dst, 17); }
                    catch { Call(d, "SaveAs", dst, 17); }
                    return File.Exists(dst);
                }
                catch { return false; }
                finally { if (d != null) try { Call(d, "Close", 0); } catch { } }
            }
            public void Dispose()
            {
                if (app != null) { try { Call(app, "Quit", 0); } catch { } try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(app); } catch { } app = null; }
            }
        }

        static byte[] ToJpeg(string img, out int w, out int h)
        {
            using (var bmp = new Bitmap(img))
            using (var flat = new Bitmap(bmp.Width, bmp.Height))
            using (var ms = new MemoryStream())
            {
                using (var g = Graphics.FromImage(flat)) { g.Clear(Color.White); g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height); }
                var enc = ImageCodecInfo.GetImageEncoders().First(x => x.MimeType == "image/jpeg");
                var ps = new EncoderParameters(1); ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
                flat.Save(ms, enc, ps);
                w = flat.Width; h = flat.Height;
                return ms.ToArray();
            }
        }

        static string Safe(string s, int max)
        {
            s = Regex.Replace(s ?? "", @"[\\/:*?""<>|\r\n\t]", "");
            return s.Length > max ? s.Substring(0, max) : s;
        }

        // ---------------- 主流程 ----------------
        public static Result Run(Store st, List<Project> projects, string outParent, string title, Action<string> progress)
        {
            var res = new Result();
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
            string folder = Path.Combine(outParent, "经济合同PDF_" + stamp);
            string pdfDir = Path.Combine(folder, "pdf");
            Directory.CreateDirectory(pdfDir);
            res.Folder = folder;

            // 台账序号与“导出合同台账”一致：按项目顺序、合同顺序连续编号
            var rows = new List<Row>(); int no = 0;
            foreach (var p in projects) foreach (var c in p.Contracts) rows.Add(new Row { No = ++no, P = p, C = c });
            res.Rows = rows.Count;
            foreach (var r in rows) { if (progress != null) progress("查找合同原件 " + r.No + "/" + rows.Count); r.Source = Resolve(st, r.P, r.C); }

            // 同一份原件只出一个 PDF
            var docs = new List<Doc>();
            foreach (var g in rows.Where(r => r.Source != "").GroupBy(r => r.Source, StringComparer.OrdinalIgnoreCase))
            {
                var list = g.ToList();
                var d = new Doc { Source = g.Key, Rows = list, Project = list[0].P.Name };
                var projs = list.Select(r => r.P.Name).Distinct().ToList();
                var kinds = list.Select(r => r.C.Kind).Distinct().ToList();
                // 合同类型里已带子项目名（如“2021年农村公路日常养护—服务合同”）时直接用它
                if (projs.Count == 1 && kinds.Count == 1 && kinds[0].Contains("—")) d.Title = kinds[0];
                else d.Title = projs.Count == 1 ? projs[0] + "—" + string.Join("、", kinds) : string.Join("、", projs) + "—" + string.Join("、", kinds) + "（同一份文件）";
                docs.Add(d);
            }
            docs = docs.OrderBy(d => d.Rows.Min(r => r.No)).ToList();
            var cache = Invoices.LoadCache(st);
            bool cacheDirty = false;
            using (var word = new WordConverter())
            {
                int i = 0;
                foreach (var d in docs)
                {
                    i++;
                    if (progress != null) progress("生成合同 PDF " + i + "/" + docs.Count + "：" + d.Title);
                    string src = Path.Combine(st.Lib.Root, d.Source), ext = Path.GetExtension(src).ToLowerInvariant();
                    string name = (i).ToString("000") + "_" + Safe(d.Title, 90);
                    string dst = Path.Combine(pdfDir, name + ".pdf");
                    try
                    {
                        if (ext == ".pdf")
                        {
                            // 原件文字里有发票/凭证迹象，才逐页识别并去掉无关页
                            string text; lock (st.Sync) st.Texts.TryGetValue(d.Source, out text);
                            List<string> pages = null;
                            if (text != null && Suspicious.IsMatch(text))
                            {
                                string key = d.Source + "|" + new FileInfo(src).Length;
                                if (!cache.TryGetValue(key, out pages))
                                {
                                    try { int pc; pages = Ocr.PdfPages(src, 120, out pc); cache[key] = pages; cacheDirty = true; } catch { pages = null; }
                                }
                            }
                            var removed = new List<int>(); var why = new Dictionary<string, int>();
                            if (pages != null)
                                for (int k = 1; k < pages.Count; k++)   // 第 1 页始终保留
                                {
                                    var kind = PageKind(pages[k]);
                                    if (kind == null) continue;
                                    removed.Add(k); int n; why.TryGetValue(kind, out n); why[kind] = n + 1;
                                }
                            int total = Ocr.PdfPageCount(src);
                            if (removed.Count == 0)
                            {
                                File.Copy(src, dst, true);
                                d.Pages = total;
                            }
                            else
                            {
                                var keep = new List<Tuple<byte[], int, int, double, double>>();
                                for (int k = 0; k < total; k++)
                                {
                                    if (removed.Contains(k)) continue;
                                    int w, h; double pw, ph;
                                    keep.Add(Tuple.Create(Ocr.RenderJpeg(src, k, 170, out w, out h, out pw, out ph), w, h, pw, ph));
                                }
                                Invoices.WriteImagePdf(dst, keep);
                                d.Pages = keep.Count;
                                d.Removed = string.Join("、", why.Select(kv => kv.Key + kv.Value + "页")) + "（原第 " + string.Join(",", removed.Select(x => x + 1)) + " 页）";
                                res.Cleaned++;
                            }
                        }
                        else if (ext == ".doc" || ext == ".docx" || ext == ".wps")
                        {
                            if (!word.Convert(src, dst))
                            {
                                dst = Path.Combine(pdfDir, name + ext);   // 本机没有 Word/WPS：原样附上
                                File.Copy(src, dst, true);
                                d.Removed = "本机无法转 PDF，附原 Word 文件";
                            }
                            else try { d.Pages = Ocr.PdfPageCount(dst); } catch { }
                        }
                        else
                        {
                            int w, h; var jpg = ToJpeg(src, out w, out h);
                            double pw = w * 72.0 / 150, ph = h * 72.0 / 150;   // 按 150dpi 定页面大小
                            Invoices.WriteImagePdf(dst, new List<Tuple<byte[], int, int, double, double>> { Tuple.Create(jpg, w, h, pw, ph) });
                            d.Pages = 1;
                        }
                        d.File = "pdf/" + Path.GetFileName(dst);
                        d.Size = new FileInfo(dst).Length;
                    }
                    catch (Exception ex) { res.Log.Add("× " + d.Title + "：" + ex.Message); d.File = ""; }
                }
            }
            if (cacheDirty) Invoices.SaveCache(st, cache);
            docs = docs.Where(d => d.File != "").ToList();
            res.Docs = docs.Count;
            res.Missing = rows.Count(r => r.Source == "");

            // 清单
            var xrows = new List<IList<object>>();
            foreach (var d in docs)
                xrows.Add(new List<object> { Path.GetFileName(d.File), string.Join(",", d.Rows.Select(r => r.No)), d.Kind, d.Pages, d.Removed,
                    d.Title + "（" + string.Join("、", d.Rows.Select(r => r.C.Party).Where(x => x != "").Distinct()) + "）", d.Source });
            foreach (var r in rows.Where(r => r.Source == ""))
                xrows.Add(new List<object> { "（未找到合同原件）", r.No.ToString(), "合同", "", "", r.P.Name + "—" + r.C.Kind + "（" + r.C.Party + "）", "" });
            Store.WriteXlsx(Path.Combine(folder, "合同PDF清单.xlsx"), "合同PDF清单", new[] { "PDF文件", "对应台账序号", "类型", "页数", "已去除的无关页", "合同名称/签订方", "原始路径" }, xrows);

            // 预览网页
            var arr = docs.Select(d => new Dictionary<string, object> {
                { "file", d.File }, { "title", d.Title }, { "nos", d.Rows.Select(r => r.No).ToList() }, { "kind", d.Kind }, { "project", d.Project },
                { "sources", new List<string> { d.Source } }, { "size", d.Size }, { "pages", d.Pages }, { "removed", d.Removed },
                { "rows", d.Rows.Select(r => new Dictionary<string, object> { { "no", r.No }, { "name", r.C.Kind.Contains("—") ? r.C.Kind : r.P.Name + "—" + r.C.Kind }, { "party", r.C.Party },
                    { "date", r.C.Date }, { "content", r.C.Content }, { "amount", r.C.Amount == 0 ? "" : r.C.Amount.ToString("#,##0.##") } }).ToList() }
            }).ToList();
            string tpl;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("preview.html"))
            using (var rd = new StreamReader(s, Encoding.UTF8)) tpl = rd.ReadToEnd();
            string json = Store.Ser().Serialize(arr).Replace("</", "<\\/");
            string html = tpl.Replace("__TITLE__", WebEsc(title)).Replace("__DOCS__", json);
            File.WriteAllText(Path.Combine(folder, "合同预览.html"), html, new UTF8Encoding(false));
            return res;
        }

        static string WebEsc(string s) { return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"); }
    }
}
