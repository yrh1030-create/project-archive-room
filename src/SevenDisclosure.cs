using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using IWin32Window = System.Windows.Forms.IWin32Window;

namespace DangAn
{
    /// <summary>
    /// 农村公路建设信息“七公开”电子公告栏：AI 查档案整理七项公示内容并挑出佐证原件，
    /// 软件套政府公告样式生成 HTML（网页可直接展示）和 PDF（16:9，适合电子屏和打印），佐证材料以原件缩略图附后。
    /// </summary>
    public static class SevenDisclosure
    {
        public class Section { public string No = "", Category = "", Item = ""; public List<string> Points = new List<string>(); public List<string> Evidence = new List<string>(); }
        public class Progress { public string Name = "", Status = "", Level = ""; }
        public class Data
        {
            public string Title = "", Org = "", AsOf = "", Note = "";
            public List<KeyValuePair<string, string>> Info = new List<KeyValuePair<string, string>>();
            public List<Section> Sections = new List<Section>();
            public List<Progress> Progress = new List<Progress>();
            public List<string> Missing = new List<string>();
        }
        public class Result { public string Html, Pdf; public Data Data; public int Thumbs; public List<string> Log = new List<string>(); }

        static readonly string[][] Defs =
        {
            new[] { "一", "建设计划公开", "建设依据及年度计划", "建设计划|计划通知|可研|可行性研究|批复|施工图|建设规模|概算|建设内容" },
            new[] { "二", "补助政策公开", "资金来源及补助安排", "资金来源|补助|下达资金|资金指标|补助标准|自筹" },
            new[] { "三", "招投标信息公开", "采购、开标及成交情况", "采购公告|采购方式|最高限价|开标|评标|成交通知书|中标|成交金额|采购意向|履约保证金" },
            new[] { "四", "施工管理公开", "参建单位、开工及施工进度", "参建单位|项目经理|技术负责人|开工令|开工|工期|施工进度|现场人员|任命" },
            new[] { "五", "质量监管公开", "监理监督、社会监督及现场检查", "监理|总监|监督|义务监督员|踏勘|现场检查|工地例会|质量" },
            new[] { "六", "资金使用公开", "资金指标、支付及保证金", "支付|国库集中支付|预付款|工程款|保证金|已支付|拨付" },
            new[] { "七", "工程验收公开", "交工、竣工验收情况", "交工验收|竣工验收|验收|检测|鉴定|评定" },
        };

        const string Prompt =
@"你是乡镇（街道）农村公路项目的信息公开专员，要为一个项目编写“农村公路建设信息‘七公开’电子公告栏”。根据【项目档案】整理，只输出 JSON。

要求：
1. 只写档案里查得到的事实（文号、日期、金额、单位、人员），不编造；某一项查不到就写“暂无相关资料，待补充”，并把缺的内容写进 missing。
2. 七项固定为：一、建设计划公开（建设依据及年度计划）；二、补助政策公开（资金来源及补助安排）；三、招投标信息公开（采购、开标及成交情况）；四、施工管理公开（参建单位、开工及施工进度）；五、质量监管公开（监理监督、社会监督及现场检查）；六、资金使用公开（资金指标、支付及保证金）；七、工程验收公开（交工、竣工验收情况）。
3. 每项 points 写 2–4 条，每条 25–70 字，语言规范简洁，像政府公告；金额写“万元”保留到需要的位数，文号用〔〕。
4. 合同金额以合同、成交通知书原文为准（大写为准）；“已支付”必须有国库支付凭证、银行回单等付款依据，只有发票的不写已支付。
5. 每项 evidence 选 1–3 份最能佐证的原件（照抄【可作佐证的文件】里的路径），只选正式签章版（优先 PDF、照片），不要选草稿、草件、征求意见稿、模板；同一份文件尽量不在多项重复使用；优先：计划/批复通知、资金下达通知、采购公告/成交通知书/开标一览表、开工令/人员任命、监理合同/现场检查照片、国库支付凭证/保证金回单、验收证书。
6. info 填：建设单位、建设地点、建设里程、开工日期、当前进度、成交金额（查不到写“待补充”）。
7. progress 写 4–8 个“施工进度及监督动态”小格（如 路面工程/已完成、护栏/施工中、工程验收/尚未开展、工程款支付/38.07万元），level 用 done（已完成）/ doing（进行中）/ todo（未开展）/ info（信息）。
8. 不要写个人身份证号、住址；监理、监督人员姓名和联系电话可以写（属于公开监督渠道）。

输出格式：
{""title"":""项目正式名称"",""org"":""建设单位全称"",
 ""info"":{""建设单位"":"""",""建设地点"":"""",""建设里程"":"""",""开工日期"":"""",""当前进度"":"""",""成交金额"":""""},
 ""sections"":[{""no"":""一"",""points"":[""…""],""evidence"":[""路径""]}],
 ""progress"":[{""name"":""路面工程"",""status"":""已完成"",""level"":""done""}],
 ""note"":""一句话说明（如：本公告栏根据项目现有资料更新，验收信息在验收后补充公开）"",
 ""missing"":[""…""]}";

        public static Result Build(Store st, AiConfig cfg, Project p, string asOf, string outDir, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(asOf)) asOf = DateTime.Now.ToString("yyyy年M月d日");
            var plan = new AiPlan { Keywords = Defs.SelectMany(df => df[3].Split('|')).Distinct().ToList() };
            if (log != null) log("整理项目资料……");
            string dossier = AiFiller.Dossier(st, p, plan, 80000);
            string user = "今天是 " + DateTime.Now.ToString("yyyy-MM-dd") + "，公告截至 " + asOf + "。\n" + EvidenceList(st, p) + "\n【项目档案】\n" + dossier;
            if (log != null) log("AI 正在按“七公开”整理公示内容……");
            var o = AiClient.ParseJson(AiClient.Chat(cfg, Prompt, user, true, 16000));

            var files = st.FilesOf(p).ToList();
            var d = new Data { AsOf = asOf };
            d.Title = S(o, "title") != "" ? S(o, "title") : p.Name;
            d.Org = S(o, "org") != "" ? S(o, "org") : p.Get("建设／实施单位");
            object io;
            var info = o.TryGetValue("info", out io) ? io as Dictionary<string, object> : null;
            foreach (var k in new[] { "建设单位", "建设地点", "建设里程", "开工日期", "当前进度", "成交金额" })
                d.Info.Add(new KeyValuePair<string, string>(k, info != null && info.ContainsKey(k) && Convert.ToString(info[k]).Trim() != "" ? Convert.ToString(info[k]).Trim() : "待补充"));
            var secs = Arr(o, "sections").OfType<Dictionary<string, object>>().ToList();
            foreach (var def in Defs)
            {
                var so = secs.FirstOrDefault(x => S(x, "no") == def[0]) ?? (secs.Count == 7 ? secs[Array.IndexOf(Defs, def)] : null);
                var s = new Section { No = def[0], Category = def[1], Item = def[2] };
                if (so != null)
                {
                    s.Points = Arr(so, "points").Select(x => Convert.ToString(x).Trim()).Where(x => x != "").ToList();
                    s.Evidence = Arr(so, "evidence").Select(x => AiFinance.Resolve(files, Convert.ToString(x))).Where(x => x != "").Distinct().Take(3).ToList();
                }
                if (s.Points.Count == 0) s.Points.Add("暂无相关资料，待补充。");
                d.Sections.Add(s);
            }
            foreach (var x in Arr(o, "progress").OfType<Dictionary<string, object>>().Take(8))
                d.Progress.Add(new Progress { Name = S(x, "name"), Status = S(x, "status"), Level = S(x, "level") });
            d.Note = S(o, "note");
            d.Missing = Arr(o, "missing").Select(x => Convert.ToString(x).Trim()).Where(x => x != "").ToList();

            var r = new Result { Data = d };
            if (log != null) log("正在制作佐证材料缩略图……");
            var thumbs = Thumbs(st, d, r.Log);
            r.Thumbs = thumbs.Count;

            Directory.CreateDirectory(outDir);
            string baseName = Safe(d.Title) + "_七公开电子公告栏_" + DateTime.Now.ToString("yyyyMMdd");
            r.Html = Path.Combine(outDir, baseName + ".html");
            for (int i = 2; File.Exists(r.Html); i++) r.Html = Path.Combine(outDir, baseName + "(" + i + ").html");
            File.WriteAllText(r.Html, Html(d, thumbs), new UTF8Encoding(true));
            if (log != null) log("正在生成 PDF……");
            string pdf = Path.ChangeExtension(r.Html, ".pdf");
            string err = ToPdf(r.Html, pdf);
            if (err == null) r.Pdf = pdf; else r.Log.Add("PDF 没生成：" + err);
            return r;
        }

        static readonly Regex EvidenceName = new Regex("通知|批复|计划|下达|公告|成交|中标|开标|招标情况|合同|协议|开工令|任命|通讯录|例会|纪要|监理|监督|踏勘|检查|照片|现场|凭证|回单|支付|保证金|验收|检测|鉴定|结算|决算|审计|申请表");

        /// <summary>可作佐证的文件清单：按名字挑出通知、批复、成交通知书、开工令、合同、凭证、验收等，草稿排在后面。</summary>
        static string EvidenceList(Store st, Project p)
        {
            var root = st.Lib.Root.TrimEnd('\\') + "\\";
            var files = st.FilesOf(p).Where(f => !f.Rel.Contains(Invoices.OutFolder) && EvidenceName.IsMatch(f.Name)
                    && (f.Ext == ".pdf" || f.Ext == ".jpg" || f.Ext == ".jpeg" || f.Ext == ".png" || f.Ext == ".doc" || f.Ext == ".docx" || f.Ext == ".xls" || f.Ext == ".xlsx"))
                .OrderBy(f => Regex.IsMatch(f.Name, "草|稿|模板|样本|副本") ? 1 : 0).ThenBy(f => f.Ext == ".pdf" || f.Ext == ".jpg" || f.Ext == ".png" ? 0 : 1).ThenBy(f => f.Cat).Take(160).ToList();
            var sb = new StringBuilder("【可作佐证的文件】（分类｜路径）\n");
            foreach (var f in files)
            {
                string rel = f.Rel;
                foreach (var fd in p.Folders) if (rel.StartsWith(fd + "\\", StringComparison.OrdinalIgnoreCase)) { rel = rel.Substring(fd.Length + 1); break; }
                sb.Append(Cats.Short(f.Cat)).Append("｜").Append(rel).Append('\n');
            }
            return sb.ToString();
        }

        // ---------------- 佐证材料缩略图 ----------------
        class Thumb { public string Caption, DataUri; }

        static List<Thumb> Thumbs(Store st, Data d, List<string> log)
        {
            var list = new List<Thumb>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in d.Sections)
                foreach (var rel in s.Evidence)
                {
                    if (list.Count >= 42) return list;
                    if (!used.Add(rel)) continue;   // 同一份文件只放一次（放在第一次用到它的那一项）
                    string full = Path.Combine(st.Lib.Root, rel), ext = Path.GetExtension(rel).ToLowerInvariant();
                    string label = Regex.Replace(Path.GetFileNameWithoutExtension(rel), @"^\d+[\.、_\-\s]*", "");
                    if (label.Length > 24) label = label.Substring(0, 24) + "…";
                    try
                    {
                        string pdf = ext == ".pdf" ? full : (ext == ".doc" || ext == ".docx" || ext == ".wps" || ext == ".xls" || ext == ".xlsx" || ext == ".et") ? OfficeToPdf(full) : null;
                        if (pdf != null)
                        {
                            int n = Ocr.PdfPageCount(pdf), take = Math.Min(n, 4);
                            for (int i = 0; i < take && list.Count < 42; i++)
                            {
                                int w, h; double pw, ph;
                                var jpg = Ocr.RenderJpeg(pdf, i, 72, out w, out h, out pw, out ph);
                                list.Add(new Thumb { Caption = s.Category + "佐证材料（" + label + (n > 1 ? "第" + (i + 1) + "页，共" + n + "页" : "") + "）", DataUri = "data:image/jpeg;base64," + Convert.ToBase64String(jpg) });
                            }
                        }
                        else if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp")
                            list.Add(new Thumb { Caption = s.Category + "佐证材料（" + label + "）", DataUri = "data:image/jpeg;base64," + Convert.ToBase64String(Shrink(full, 900)) });
                    }
                    catch (Exception ex) { log.Add("佐证材料没做成：" + Path.GetFileName(rel) + "（" + ex.Message + "）"); }
                }
            return list;
        }

        static byte[] Shrink(string path, int max)
        {
            using (var img = Image.FromFile(path))
            {
                double k = Math.Min(1.0, (double)max / Math.Max(img.Width, img.Height));
                using (var bmp = new Bitmap(img, (int)(img.Width * k), (int)(img.Height * k)))
                using (var ms = new MemoryStream())
                {
                    var enc = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    var ps = new EncoderParameters(1); ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 80L);
                    bmp.Save(ms, enc, ps);
                    return ms.ToArray();
                }
            }
        }

        static object Get(object o, string p) { return o.GetType().InvokeMember(p, BindingFlags.GetProperty, null, o, null); }
        static object Call(object o, string m, params object[] a) { return o.GetType().InvokeMember(m, BindingFlags.InvokeMethod, null, o, a); }

        /// <summary>Word / Excel 转 PDF（用本机 Office 或 WPS），返回临时 PDF 路径；转不了返回 null。</summary>
        public static string OfficeToPdf(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string outPdf = Path.Combine(Path.GetTempPath(), "项目档案室_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".pdf");
            bool excel = ext == ".xls" || ext == ".xlsx" || ext == ".et";
            object app = null, doc = null;
            try
            {
                foreach (var id in excel ? new[] { "Excel.Application", "KET.Application" } : new[] { "Word.Application", "KWps.Application" })
                {
                    var t = Type.GetTypeFromProgID(id); if (t == null) continue;
                    try { app = Activator.CreateInstance(t); break; } catch { }
                }
                if (app == null) return null;
                try { app.GetType().InvokeMember("Visible", BindingFlags.SetProperty, null, app, new object[] { false }); } catch { }
                try { app.GetType().InvokeMember("DisplayAlerts", BindingFlags.SetProperty, null, app, new object[] { excel ? (object)false : 0 }); } catch { }
                if (excel)
                {
                    doc = Call(Get(app, "Workbooks"), "Open", path, 0, true);
                    Call(doc, "ExportAsFixedFormat", 0, outPdf);
                }
                else
                {
                    doc = Call(Get(app, "Documents"), "Open", path, false, true, false);
                    Call(doc, "ExportAsFixedFormat", outPdf, 17);
                }
                return File.Exists(outPdf) ? outPdf : null;
            }
            catch { return null; }
            finally
            {
                try { if (doc != null) Call(doc, "Close", excel ? (object)false : 0); } catch { }
                try { if (app != null) Call(app, "Quit"); } catch { }
                try { if (app != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(app); } catch { }
            }
        }

        // ---------------- HTML ----------------
        static string H(string s) { return WebUtility.HtmlEncode(s ?? ""); }
        static string Safe(string s) { return Regex.Replace(s ?? "", @"[\\/:*?""<>|\r\n]", "_"); }

        const string Css = @"
:root{--red:#a8201a;--red-d:#7f1712;--ink:#1f2329;--sub:#5b6270;--line:#d9d2c5;--paper:#fffdf8;--pink:#fbf1ee;--head:#3d4a5c;--gold:#c79a3b}
*{box-sizing:border-box}
body{margin:0;background:#e9e6df;color:var(--ink);font-family:'Microsoft YaHei','微软雅黑','PingFang SC',sans-serif}
.nav{position:sticky;top:0;z-index:9;background:var(--red);color:#fff;display:flex;align-items:center;gap:6px;padding:10px 24px;box-shadow:0 2px 8px rgba(0,0,0,.18);flex-wrap:wrap}
.nav b{font-family:'STZhongsong','华文中宋','SimSun',serif;font-size:17px;margin-right:12px;letter-spacing:1px}
.nav a{color:#fff;text-decoration:none;font-size:13px;padding:4px 10px;border:1px solid rgba(255,255,255,.35);border-radius:3px}
.nav a:hover{background:rgba(255,255,255,.15)}
.nav .print{margin-left:auto;background:var(--gold);border-color:var(--gold);cursor:pointer}
.page{width:338.67mm;min-height:190.5mm;margin:18px auto;background:var(--paper);padding:7mm 10mm 5mm;position:relative;box-shadow:0 4px 18px rgba(0,0,0,.18);display:flex;flex-direction:column}
.page:before{content:'';position:absolute;left:0;right:0;top:0;height:5px;background:linear-gradient(90deg,var(--red-d),var(--red),var(--gold),var(--red),var(--red-d))}
h1{margin:0;text-align:center;color:var(--red);font-family:'STZhongsong','华文中宋','SimSun',serif;font-size:25px;letter-spacing:1px}
h2{margin:3px 0 8px;text-align:center;color:var(--ink);font-size:18px;font-weight:700;letter-spacing:2px}
h2 small{font-weight:400;color:var(--sub);font-size:13px;letter-spacing:0}
.info{display:grid;grid-template-columns:repeat(3,auto 1fr);border:1px solid var(--line);margin-bottom:8px}
.info div{padding:5px 10px;border-bottom:1px solid var(--line);font-size:13px;display:flex;align-items:center}
.info .k{background:var(--red);color:#fff;font-weight:700;justify-content:center;white-space:nowrap;min-width:84px}
.info .v{background:#f6f1e7;justify-content:center;text-align:center}
.info .v.hl{font-weight:700;color:var(--red-d)}
table{width:100%;border-collapse:collapse;font-size:12px;table-layout:fixed}
th{background:var(--head);color:#fff;font-weight:700;padding:5px;border:1px solid #2f3a49;letter-spacing:1px}
td{border:1px solid var(--line);padding:4px 9px;vertical-align:middle;line-height:1.5}
td.cat{background:var(--pink);color:var(--red);font-weight:700;text-align:center;font-size:15px;width:17%;font-family:'STZhongsong','华文中宋','SimSun',serif}
td.item{text-align:center;font-weight:700;width:19%}
td.txt ul{margin:0;padding-left:1.1em}
td.txt li{margin:1px 0}
tr{break-inside:avoid;page-break-inside:avoid}
.pg-title{color:var(--red);font-weight:700;font-size:15px;margin:12px 0 6px;border-left:4px solid var(--red);padding-left:8px}
.prog{display:grid;grid-template-columns:repeat(4,1fr);border:1px solid var(--line)}
.prog div{padding:6px 8px;text-align:center;border:1px solid var(--line);font-size:12px}
.prog b{display:block;color:var(--red-d);margin-bottom:2px}
.prog .p-done{background:#e8f3ea}.prog .p-doing{background:#fff4dc}.prog .p-todo{background:#eff0f3}.prog .p-info{background:#f6f1e7}
.note{color:var(--sub);font-size:11.5px;text-align:right;margin-top:8px}
.foot{margin-top:auto;padding-top:5px;display:flex;justify-content:space-between;align-items:flex-end;font-size:12px;color:var(--sub);border-top:1px solid var(--line)}
.seal{color:var(--red);font-weight:700;text-align:right;line-height:1.7}
.ev{display:grid;grid-template-columns:repeat(3,1fr);grid-auto-rows:1fr;gap:6mm 7mm;flex:1}
.ev figure{margin:0;display:flex;flex-direction:column;align-items:center;justify-content:flex-end}
.ev img{max-width:100%;max-height:68mm;border:1px solid #cfc8bb;background:#fff;box-shadow:0 1px 4px rgba(0,0,0,.12)}
.ev figcaption{margin-top:5px;font-size:11.5px;text-align:center;color:var(--ink)}
.ev-head{display:flex;justify-content:space-between;align-items:baseline;margin-bottom:6px}
.ev-head span{color:var(--red);font-weight:700;font-size:15px;border-left:4px solid var(--red);padding-left:8px}
.ev-head em{font-style:normal;color:var(--sub);font-size:12px}
@page{size:338.67mm 190.5mm;margin:0}
@media print{body{background:#fff}.nav{display:none}.page{margin:0;box-shadow:none;page-break-after:always;height:190.5mm;overflow:hidden}.page:last-child{page-break-after:auto}}
";

        static string Html(Data d, List<Thumb> thumbs)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.Append("<title>").Append(H(d.Title)).Append(" · 七公开电子公告栏</title><style>").Append(Css).Append("</style></head><body>");
            sb.Append("<div class=\"nav\"><b>农村公路建设信息“七公开”</b>");
            foreach (var s in d.Sections) sb.Append("<a href=\"#s").Append(s.No).Append("\">").Append(H(s.No + "、" + s.Category.Replace("公开", ""))).Append("</a>");
            if (thumbs.Count > 0) sb.Append("<a href=\"#ev\">佐证材料</a>");
            sb.Append("<a class=\"print\" onclick=\"window.print()\">打印 / 另存 PDF</a></div>");

            // 第 1 页：项目概况 + 一至四
            int pageNo = 1;
            Action<IEnumerable<Section>, bool> page = (secs, first) =>
            {
                sb.Append("<section class=\"page\"><h1>").Append(H(d.Title)).Append("</h1><h2>农村公路建设信息“七公开”电子公告栏 <small>（第 ").Append(pageNo++).Append(" 页）</small></h2>");
                if (first)
                {
                    sb.Append("<div class=\"info\">");
                    foreach (var kv in d.Info) sb.Append("<div class=\"k\">").Append(H(kv.Key)).Append("</div><div class=\"v").Append(kv.Key == "成交金额" || kv.Key == "当前进度" ? " hl" : "").Append("\">").Append(H(kv.Value)).Append("</div>");
                    sb.Append("</div>");
                }
                sb.Append("<table><thead><tr><th style=\"width:17%\">公开类别</th><th style=\"width:19%\">公开事项</th><th>公示内容</th></tr></thead><tbody>");
                foreach (var s in secs)
                {
                    sb.Append("<tr id=\"s").Append(s.No).Append("\"><td class=\"cat\">").Append(H(s.No + "、" + s.Category)).Append("</td><td class=\"item\">").Append(H(s.Item)).Append("</td><td class=\"txt\"><ul>");
                    foreach (var pt in s.Points) sb.Append("<li>").Append(H(pt)).Append("</li>");
                    sb.Append("</ul></td></tr>");
                }
                sb.Append("</tbody></table>");
                if (!first)
                {
                    if (d.Progress.Count > 0)
                    {
                        sb.Append("<div class=\"pg-title\">施工进度及监督动态（截至 ").Append(H(d.AsOf)).Append("）</div><div class=\"prog\">");
                        foreach (var g in d.Progress)
                        {
                            string lv = Regex.IsMatch(g.Level, "^(done|doing|todo|info)$") ? g.Level : (Regex.IsMatch(g.Status, "完成|已") ? "done" : Regex.IsMatch(g.Status, "中") ? "doing" : Regex.IsMatch(g.Status, "未|尚") ? "todo" : "info");
                            sb.Append("<div class=\"p-").Append(lv).Append("\"><b>").Append(H(g.Name)).Append("</b>").Append(H(g.Status)).Append("</div>");
                        }
                        for (int i = d.Progress.Count; i % 4 != 0; i++) sb.Append("<div class=\"p-info\"></div>");
                        sb.Append("</div>");
                    }
                    if (d.Note != "") sb.Append("<div class=\"note\">说明：").Append(H(d.Note)).Append("</div>");
                }
                sb.Append("<div class=\"foot\"><span>").Append(H(d.Org)).Append(" · 农村公路建设信息“七公开”</span>");
                if (!first) sb.Append("<span class=\"seal\">").Append(H(d.Org)).Append("<br>").Append(H(d.AsOf)).Append("</span>");   // 落款只放在正文最后一页
                else sb.Append("<span>").Append(H(d.AsOf)).Append("</span>");
                sb.Append("</div></section>");
            };
            page(d.Sections.Take(4), true);
            page(d.Sections.Skip(4), false);

            // 佐证材料：每页 6 张
            for (int i = 0; i < thumbs.Count; i += 6)
            {
                sb.Append("<section class=\"page\"").Append(i == 0 ? " id=\"ev\"" : "").Append("><div class=\"ev-head\"><span>佐证材料</span><em>").Append(H(d.Title)).Append(" · 第 ").Append(pageNo++).Append(" 页</em></div><div class=\"ev\">");
                foreach (var t in thumbs.Skip(i).Take(6)) sb.Append("<figure><img src=\"").Append(t.DataUri).Append("\"><figcaption>").Append(H(t.Caption)).Append("</figcaption></figure>");
                sb.Append("</div></section>");
            }
            sb.Append("</body></html>");
            return sb.ToString();
        }

        // ---------------- PDF ----------------
        /// <summary>HTML 转 PDF：优先用 Edge（排版与网页一致），没有 Edge 时用 Word。成功返回 null，否则返回原因。</summary>
        /// <summary>弹出“选择保存位置”（记住上次选的文件夹）；取消返回 null。须在界面线程调用。</summary>
        public static string PickDir(IWin32Window owner, string projectName)
        {
            var cfg = AiConfig.Load();
            string last = !string.IsNullOrEmpty(cfg.SevenDir) && Directory.Exists(cfg.SevenDir) ? cfg.SevenDir : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "选择“" + projectName + "”七公开公告栏的保存位置（会生成网页版 .html 和 .pdf）", SelectedPath = last, ShowNewFolderButton = true })
            {
                if (d.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK) return null;
                cfg.SevenDir = d.SelectedPath; cfg.Save();
                return d.SelectedPath;
            }
        }

        public static string ToPdf(string html, string pdf)
        {
            var edge = new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\Application\msedge.exe") }.FirstOrDefault(File.Exists);
            if (edge != null)
            {
                try
                {
                    if (File.Exists(pdf)) File.Delete(pdf);
                    string prof = Path.Combine(Path.GetTempPath(), "项目档案室_edge");
                    var psi = new ProcessStartInfo(edge, "--headless --disable-gpu --no-first-run --no-pdf-header-footer --user-data-dir=\"" + prof + "\" --print-to-pdf=\"" + pdf + "\" \"" + new Uri(html).AbsoluteUri + "\"")
                    { UseShellExecute = false, CreateNoWindow = true };
                    using (var pr = Process.Start(psi)) { if (!pr.WaitForExit(180000)) try { pr.Kill(); } catch { } }
                    if (File.Exists(pdf) && new FileInfo(pdf).Length > 1000) return null;
                }
                catch { }
            }
            // 退而求其次：Word 打开 HTML 另存 PDF
            string tmp = OfficeToPdf(html);
            if (tmp != null) { File.Copy(tmp, pdf, true); try { File.Delete(tmp); } catch { } return null; }
            return "本机没有 Edge 浏览器，也没有 Word，可以用浏览器打开网页版后“打印 → 另存为 PDF”";
        }

        static IEnumerable<object> Arr(Dictionary<string, object> o, string k) { object v; return o.TryGetValue(k, out v) && v is object[] ? (object[])v : new object[0]; }
        static string S(Dictionary<string, object> o, string k) { object v; return o.TryGetValue(k, out v) && v != null ? Convert.ToString(v).Trim() : ""; }
    }
}
