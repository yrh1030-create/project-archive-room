using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DangAn
{
    /// <summary>
    /// 交委填报的缺件预检：通知要求的 6 类附件（施工许可、中标通知书、质量监督通知书、施工合同、设计合同、监理合同），
    /// 未走交委监管程序的暂免施工许可、质监通知书；直接委托没有中标通知书时，以会议纪要代替。
    /// 另找质量及验收用的交工 / 竣工验收资料。按文件名、档案分类、合同台账和原文开头综合判断，每项写依据。
    /// </summary>
    public static class JwPrecheck
    {
        public class Slot
        {
            public string Module = "招投标信息", Kind = "", Status = "", Why = "", Confidence = "";
            public bool Required;
            public List<FileRec> Files = new List<FileRec>();
        }

        public class Result
        {
            public Project P;
            public List<Slot> Slots = new List<Slot>();
            public bool Direct;          // 直接委托
            public int Missing { get { return Slots.Count(s => s.Required && s.Files.Count == 0); } }
            public string Status { get { return Missing == 0 ? "可填报" : Slots.Any(s => s.Files.Count > 0) ? "缺件（已有的可先传）" : "缺件"; } }
        }

        class Rule { public string Kind, Name, Exclude, Cats, Prefer, Avoid; public bool NeedSupervised; public string ContractKind; }

        static readonly Rule[] Notice =
        {
            new Rule { Kind = "施工许可（或备案表）", Name = "施工许可|施工备案|开工许可|施工报建|施工.*备案表", Exclude = "申请表$", Cats = "01|06|11|07", NeedSupervised = true },
            new Rule { Kind = "中标通知书", Name = "中标通知|成交通知", Exclude = "公告|公示|投标文件|响应文件|结果", Cats = "03", Prefer = "施工", Avoid = "中介超市|一审|二审|招标代理|代理|审计|造价|咨询|设计|监理|检测|可研|测量|勘察" },
            new Rule { Kind = "质量监督通知书（或备案表）", Name = "质量监督|质监通知|质监备案|监督备案|工程质量监督", Exclude = "", Cats = "07|06|01|11", NeedSupervised = true },
            new Rule { Kind = "施工合同", Name = "施工合同", Exclude = "补充|报监|检测", Cats = "04", ContractKind = "施工" },
            new Rule { Kind = "设计合同", Name = "设计合同|勘察设计合同", Exclude = "施工图", Cats = "04", ContractKind = "设计" },
            new Rule { Kind = "监理合同", Name = "监理合同", Exclude = "规划|日志|月报|细则|草", Cats = "04", ContractKind = "监理" },
        };

        static readonly Rule[] Accept =
        {
            new Rule { Kind = "交工验收资料", Name = "交工验收|交工证书|交工.*鉴定", Exclude = "申请|通知", Cats = "07" },
            new Rule { Kind = "竣工验收资料", Name = "竣工验收|竣工证书|竣工.*鉴定", Exclude = "申请|通知", Cats = "07|08" },
            new Rule { Kind = "质量检测报告", Name = "检测报告|质量鉴定|质量检测|质量评定", Exclude = "合同|协议", Cats = "07" },
            new Rule { Kind = "验收会议纪要", Name = "验收.*纪要|纪要.*验收|验收会议", Exclude = "", Cats = "05|07" },
        };

        static readonly string[] OkExt = { ".pdf", ".doc", ".docx", ".wps", ".xls", ".xlsx", ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

        public static Result Check(Store st, Project p)
        {
            var r = new Result { P = p };
            var files = st.FilesOf(p).Where(f => OkExt.Contains(f.Ext) && !f.Rel.Contains(Invoices.OutFolder)).ToList();
            string decision = p.Get("决策／采购形式");
            r.Direct = Regex.IsMatch(decision, "直接委托|直接发包|直接选取|直接指定|党工委|会议研究|集体研究|会议确定|会议决定") && !Regex.IsMatch(decision, "公开招标|竞争性|询价|比选");

            foreach (var ru in Notice)
            {
                var s = new Slot { Kind = ru.Kind, Required = !ru.NeedSupervised || p.Supervised };
                Fill(st, p, files, ru, s);
                if (s.Files.Count == 0 && ru.Kind == "中标通知书" && (r.Direct || !files.Any(f => f.Cat == "03 招标采购")))
                {
                    // 直接委托、没有中标通知书：页面规定把相关会议纪要传到“中标通知书”一项
                    var minutes = files.Where(f => f.Cat == "05 会议纪要及决策" || Regex.IsMatch(f.Name, "纪要|会议")).ToList();
                    var hit = minutes.Where(f => Regex.IsMatch(Head(st, f) + f.Name, "施工|中标|委托|确定|实施单位")).ToList();
                    if (hit.Count == 0) hit = minutes;
                    if (hit.Count > 0)
                    {
                        s.Files = Store.SortForDisplay(hit).Take(3).ToList();
                        s.Status = "以会议纪要代替（直接委托，待审核）"; s.Confidence = "中";
                        s.Why = "按页面规定：直接委托缺少中标通知书，以相关会议纪要上传到“中标通知书”一项";
                        r.Slots.Add(s); continue;
                    }
                }
                s.Status = s.Files.Count > 0 ? "已找到" : s.Required ? "缺失" : "本项目暂不要求（未走交委监管程序）";
                r.Slots.Add(s);
            }
            foreach (var ru in Accept)
            {
                var s = new Slot { Module = "质量及验收", Kind = ru.Kind, Required = false };
                Fill(st, p, files, ru, s);
                s.Status = s.Files.Count > 0 ? "已找到" : "未找到";
                r.Slots.Add(s);
            }
            return r;
        }

        static string Head(Store st, FileRec f)
        {
            string t; lock (st.Sync) st.Texts.TryGetValue(f.Rel, out t);
            t = Regex.Replace(t ?? "", @"\s+", "");
            return t.Length > 300 ? t.Substring(0, 300) : t;
        }

        static void Fill(Store st, Project p, List<FileRec> files, Rule ru, Slot s)
        {
            var name = new Regex(ru.Name); var ex = ru.Exclude == "" ? null : new Regex(ru.Exclude);
            // 合同台账里登记了原件的，最可靠
            if (ru.ContractKind != null)
                foreach (var c in p.Contracts.Where(c => c.Kind.Contains(ru.ContractKind) && !c.Kind.Contains("补充")))
                {
                    var src = (c.Source ?? "").Split('；', ';', '\n')[0].Trim();
                    var f = files.FirstOrDefault(x => string.Equals(x.Rel, src, StringComparison.OrdinalIgnoreCase));
                    if (f != null && !s.Files.Contains(f)) { s.Files.Add(f); s.Why = "合同台账“" + c.Kind + "”登记的原件"; s.Confidence = "高"; }
                }
            var scored = new List<KeyValuePair<FileRec, int>>();
            foreach (var f in files)
            {
                if (s.Files.Contains(f)) continue;
                bool byName = name.IsMatch(f.Name) && (ex == null || !ex.IsMatch(f.Name));
                string head = Head(st, f);
                bool byText = head.Length > 0 && name.IsMatch(head.Length > 80 ? head.Substring(0, 80) : head) && (ex == null || !ex.IsMatch(f.Name));
                if (!byName && !byText) continue;
                int sc = (byName ? 2 : 0) + (byText ? 2 : 0) + (Regex.IsMatch(f.Cat, "^(" + ru.Cats + ") ") ? 1 : 0) + (f.Ext == ".pdf" ? 1 : 0) - (Regex.IsMatch(f.Name, @"\(\d\)|副本") ? 1 : 0);
                // 施工的中标通知书：同一项目常有设计、监理、审计等服务采购的成交通知书，要分开
                if (ru.Prefer != null && Regex.IsMatch(f.Name + head, ru.Prefer)) sc += 2;
                if (ru.Avoid != null && Regex.IsMatch(f.Name, ru.Avoid) && !Regex.IsMatch(f.Name, ru.Prefer ?? "$^")) sc -= 4;
                if (ru.Avoid != null && sc <= 1) continue;   // 只像服务采购的，不当施工中标通知书
                scored.Add(new KeyValuePair<FileRec, int>(f, sc));
            }
            foreach (var kv in scored.OrderByDescending(x => x.Value).Take(s.Files.Count > 0 ? 1 : 3))
            {
                s.Files.Add(kv.Key);
                if (s.Why == "") { s.Why = "文件名" + (kv.Value >= 4 ? "和原文开头" : "") + "符合“" + ru.Kind + "”"; s.Confidence = kv.Value >= 5 ? "高" : kv.Value >= 3 ? "中" : "低"; }
            }
        }

        /// <summary>缺件清单导出 Excel。</summary>
        public static void Export(string path, List<Result> rs)
        {
            var heads = new List<string> { "项目编号", "项目名称", "走交委监管程序", "总体状态" };
            heads.AddRange(Notice.Select(n => n.Kind));
            heads.AddRange(Accept.Select(n => "验收：" + n.Kind));
            heads.Add("说明");
            var rows = new List<IList<object>>();
            foreach (var r in rs)
            {
                var row = new List<object> { r.P.Id, r.P.Name, r.P.Supervised ? "是" : "否", r.Status };
                foreach (var s in r.Slots) row.Add(s.Files.Count > 0 ? s.Status + "：" + string.Join("；", s.Files.Select(f => f.Name)) : s.Status);
                row.Add("通知要求 6 类，页面提示“四个附件均需上传”，两者具体如何对应待实机确认；已有的附件可先传。" + (r.Direct ? "直接委托项目。" : ""));
                rows.Add(row);
            }
            Store.WriteXlsx(path, "交委缺件清单", heads, rows);
        }
    }

    /// <summary>上传前把附件变成 ≤5MB 的 PDF：Word/Excel 转 PDF、图片转 PDF，超过的按页重新渲染压缩，仍超就按页拆分。原件不动，副本放 %APPDATA%\项目档案室\jw-cache\。</summary>
    public static class JwPdf
    {
        public const long Limit = 5L * 1024 * 1024;
        const long Target = (long)(4.8 * 1024 * 1024);
        public static string CacheDir { get { var d = Path.Combine(JwPortal.Dir, "jw-cache"); Directory.CreateDirectory(d); return d; } }

        public class Out { public string Path; public long Size; public string Level; }

        /// <summary>返回一份或多份（拆分时）不超过 5MB 的 PDF。失败时抛出带中文说明的异常。</summary>
        public static List<Out> Prepare(string src)
        {
            string ext = System.IO.Path.GetExtension(src).ToLowerInvariant(), name = System.IO.Path.GetFileNameWithoutExtension(src);
            string hash = Store.Sha1(src).Replace("-", "").Substring(0, 12);
            string pdf;
            if (ext == ".pdf") pdf = src;
            else
            {
                pdf = System.IO.Path.Combine(CacheDir, hash + "_" + Safe(name) + ".pdf");
                if (!File.Exists(pdf))
                {
                    if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".tif" || ext == ".tiff") ImageToPdf(src, pdf);
                    else
                    {
                        string tmp = SevenDisclosure.OfficeToPdf(src);
                        if (tmp == null) throw new IOException("“" + System.IO.Path.GetFileName(src) + "”转不成 PDF：本机没有 Word / Excel / WPS，或文件打不开。请改传已有的 PDF。");
                        File.Copy(tmp, pdf, true); try { File.Delete(tmp); } catch { }
                    }
                }
            }
            long size = new FileInfo(pdf).Length;
            if (size <= Limit) return new List<Out> { new Out { Path = pdf, Size = size, Level = pdf == src ? "未压缩（不超过 5MB）" : "已转 PDF，未压缩" } };
            // 超过 5MB：从轻到重按页重新渲染（分辨率、JPEG 质量逐级降低），尽量整份不拆
            foreach (var lv in new[] { new[] { 150, 80 }, new[] { 130, 70 }, new[] { 110, 60 }, new[] { 100, 50 } })
            {
                int dpi = lv[0], q = lv[1];
                string outp = System.IO.Path.Combine(CacheDir, hash + "_" + dpi + "dpi_q" + q + "_" + Safe(name) + ".pdf");
                if (!File.Exists(outp)) Render(pdf, outp, dpi, q, 0, Ocr.PdfPageCount(pdf));
                long s2 = new FileInfo(outp).Length;
                if (s2 <= Target) return new List<Out> { new Out { Path = outp, Size = s2, Level = "压缩到 " + dpi + "dpi" + (dpi <= 110 ? "（较低，上传前看一眼文字和印章是否清楚）" : "") } };
            }
            // 最强一档仍超：按页拆成几份，每份 ≤5MB
            var res = new List<Out>();
            int n = Ocr.PdfPageCount(pdf), start = 0, part = 1;
            while (start < n)
            {
                int end = start + 1;
                string outp = null;
                while (true)
                {
                    string tryp = System.IO.Path.Combine(CacheDir, hash + "_100dpi_q50_p" + (start + 1) + "-" + end + "_" + Safe(name) + ".pdf");
                    if (!File.Exists(tryp)) Render(pdf, tryp, 100, 50, start, end);
                    if (new FileInfo(tryp).Length > Target && end > start + 1) break;
                    outp = tryp;
                    if (end >= n || new FileInfo(tryp).Length > Target) break;
                    end++;
                }
                if (outp == null) throw new IOException("“" + System.IO.Path.GetFileName(src) + "”第 " + (start + 1) + " 页压缩后仍超过 5MB，需手工处理。");
                var m = Regex.Match(outp, @"_p(\d+)-(\d+)_");
                int e2 = int.Parse(m.Groups[2].Value);
                res.Add(new Out { Path = outp, Size = new FileInfo(outp).Length, Level = "拆分第 " + part++ + " 份（第 " + (start + 1) + "–" + e2 + " 页，100dpi）" });
                start = e2;
            }
            return res;
        }

        static string Safe(string s) { s = Regex.Replace(s ?? "", @"[\\/:*?""<>|\s]", "_"); return s.Length > 40 ? s.Substring(0, 40) : s; }

        static void Render(string pdf, string outp, int dpi, int quality, int from, int to)
        {
            var pages = new List<Tuple<byte[], int, int, double, double>>();
            for (int i = from; i < to; i++)
            {
                int w, h; double pw, ph;
                pages.Add(Tuple.Create(Jpeg(Ocr.RenderJpeg(pdf, i, dpi, out w, out h, out pw, out ph), quality), w, h, pw, ph));
            }
            Invoices.WriteImagePdf(outp, pages);
        }

        /// <summary>按指定质量重新编码 JPEG（系统渲染默认质量很高，文件大）。</summary>
        static byte[] Jpeg(byte[] src, int quality)
        {
            try
            {
                using (var ms = new MemoryStream(src))
                using (var im = Image.FromStream(ms))
                using (var outp = new MemoryStream())
                {
                    var enc = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    var ps = new EncoderParameters(1); ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                    im.Save(outp, enc, ps);
                    return outp.Length < src.Length ? outp.ToArray() : src;
                }
            }
            catch { return src; }
        }

        static void ImageToPdf(string img, string outp)
        {
            using (var im = Image.FromFile(img))
            {
                double k = Math.Min(1.0, 2200.0 / Math.Max(im.Width, im.Height));
                using (var bmp = new Bitmap(im, (int)(im.Width * k), (int)(im.Height * k)))
                using (var ms = new MemoryStream())
                {
                    var enc = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    var ps = new EncoderParameters(1); ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 82L);
                    bmp.Save(ms, enc, ps);
                    // 按 150dpi 换算成页面尺寸（磅）
                    double pw = bmp.Width * 72.0 / 150, ph = bmp.Height * 72.0 / 150;
                    Invoices.WriteImagePdf(outp, new List<Tuple<byte[], int, int, double, double>> { Tuple.Create(ms.ToArray(), bmp.Width, bmp.Height, pw, ph) });
                }
            }
        }
    }
}
