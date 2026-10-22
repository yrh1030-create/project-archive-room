using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace DangAn
{
    /// <summary>调用 Windows 自带的中文文字识别：扫描版 PDF、照片。</summary>
    public static class Ocr
    {
        [ThreadStatic] static OcrEngine engine;

        static OcrEngine Engine()
        {
            if (engine == null)
            {
                engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("zh-Hans-CN"));
                if (engine == null) engine = OcrEngine.TryCreateFromUserProfileLanguages();
            }
            return engine;
        }

        public static bool Available()
        {
            try { return Engine() != null; } catch { return false; }
        }

        static T Wait<T>(IAsyncOperation<T> op)
        {
            while (op.Status == AsyncStatus.Started) Thread.Sleep(2);
            if (op.Status != AsyncStatus.Completed) throw new Exception("识别失败：" + op.Status + " " + (op.ErrorCode != null ? op.ErrorCode.Message : ""));
            return op.GetResults();
        }
        static void WaitW<T>(IAsyncOperationWithProgress<T, T> op)
        {
            while (op.Status == AsyncStatus.Started) Thread.Sleep(2);
            if (op.Status != AsyncStatus.Completed) throw new Exception("识别失败：" + op.Status);
        }
        static void WaitA(IAsyncAction op)
        {
            while (op.Status == AsyncStatus.Started) Thread.Sleep(2);
            if (op.Status != AsyncStatus.Completed) throw new Exception("渲染失败：" + op.Status);
        }

        /// <summary>识别 PDF：前 maxPages 页，再加最后 2 页（合同的签订日期、盖章页一般在最后）。</summary>
        public static string Pdf(string path, int maxPages) { return Pdf(path, maxPages, 2); }

        public static string Pdf(string path, int head, int tail)
        {
            var doc = OpenPdf(path);
            int count = (int)doc.PageCount, h = Math.Min(head, count);
            var idx = Enumerable.Range(0, h).ToList();
            for (int i = Math.Max(h, count - tail); i < count; i++) idx.Add(i);
            var sb = Render(doc, idx);
            if (idx.Count < count) sb.Append("（共 " + count + " 页，识别了前 " + h + " 页和最后 " + (idx.Count - h) + " 页）");
            return sb.ToString();
        }

        /// <summary>只识别最后几页（以前只识别了前面若干页的文件，补上尾页）。</summary>
        public static string PdfTail(string path, int skipHead, int tail, out int count)
        {
            var doc = OpenPdf(path);
            count = (int)doc.PageCount;
            var idx = new List<int>();
            for (int i = Math.Max(skipHead, count - tail); i < count; i++) idx.Add(i);
            return Render(doc, idx).ToString();
        }

        static StringBuilder Render(PdfDocument doc, List<int> pages)
        {
            var sb = new StringBuilder();
            foreach (int pi in pages)
            {
                uint i = (uint)pi;
                using (var page = doc.GetPage(i))
                using (var ms = new InMemoryRandomAccessStream())
                {
                    double w = page.Size.Width, h = page.Size.Height;
                    double target = Math.Min(2200, OcrEngine.MaxImageDimension);
                    double scale = target / Math.Max(w, h);
                    var opt = new PdfPageRenderOptions();
                    opt.DestinationWidth = (uint)Math.Max(1, w * scale);
                    opt.DestinationHeight = (uint)Math.Max(1, h * scale);
                    WaitA(page.RenderToStreamAsync(ms, opt));
                    string t = Recognize(ms);
                    sb.Append("【第").Append(i + 1).Append("页】\n").Append(t).Append('\n');
                }
            }
            return sb;
        }

        /// <summary>逐页识别，返回每页文字（最多 maxPages 页）。</summary>
        public static List<string> PdfPages(string path, int maxPages, out int pageCount)
        {
            var doc = OpenPdf(path);
            pageCount = (int)doc.PageCount;
            var list = new List<string>();
            int n = (int)Math.Min(doc.PageCount, (uint)maxPages);
            for (uint i = 0; i < n; i++)
                using (var page = doc.GetPage(i))
                using (var ms = new InMemoryRandomAccessStream())
                {
                    double w = page.Size.Width, h = page.Size.Height;
                    double scale = Math.Min(2200, OcrEngine.MaxImageDimension) / Math.Max(w, h);
                    var opt = new PdfPageRenderOptions();
                    opt.DestinationWidth = (uint)Math.Max(1, w * scale);
                    opt.DestinationHeight = (uint)Math.Max(1, h * scale);
                    WaitA(page.RenderToStreamAsync(ms, opt));
                    list.Add(Recognize(ms));
                }
            return list;
        }

        /// <summary>识别 PDF 的某一页（从 0 起）。</summary>
        public static string PdfPage(string path, int index)
        {
            var doc = OpenPdf(path);
            using (var page = doc.GetPage((uint)index))
            using (var ms = new InMemoryRandomAccessStream())
            {
                double w = page.Size.Width, h = page.Size.Height;
                double scale = Math.Min(2200, OcrEngine.MaxImageDimension) / Math.Max(w, h);
                var opt = new PdfPageRenderOptions();
                opt.DestinationWidth = (uint)Math.Max(1, w * scale);
                opt.DestinationHeight = (uint)Math.Max(1, h * scale);
                WaitA(page.RenderToStreamAsync(ms, opt));
                return Recognize(ms);
            }
        }

        public static int PdfPageCount(string path)
        {
            return (int)OpenPdf(path).PageCount;
        }

        /// <summary>把 PDF 某一页渲染成 JPEG（dpi 为输出分辨率），同时返回页面尺寸（磅）。</summary>
        public static byte[] RenderJpeg(string path, int pageIndex, int dpi, out int pxW, out int pxH, out double ptW, out double ptH)
        {
            var doc = OpenPdf(path);
            using (var page = doc.GetPage((uint)pageIndex))
            using (var ms = new InMemoryRandomAccessStream())
            {
                double w = page.Size.Width, h = page.Size.Height;   // DIP（1/96 英寸）
                ptW = w * 72 / 96; ptH = h * 72 / 96;
                var opt = new PdfPageRenderOptions();
                opt.DestinationWidth = (uint)(w * dpi / 96); opt.DestinationHeight = (uint)(h * dpi / 96);
                opt.BitmapEncoderId = BitmapEncoder.JpegEncoderId;
                WaitA(page.RenderToStreamAsync(ms, opt));
                pxW = (int)opt.DestinationWidth; pxH = (int)opt.DestinationHeight;
                uint size = (uint)ms.Size;
                var reader = new DataReader(ms.GetInputStreamAt(0));
                Wait(reader.LoadAsync(size));
                var bytes = new byte[size];
                reader.ReadBytes(bytes);
                return bytes;
            }
        }

        /// <summary>先把文件读进内存再交给系统：识别完不占着文件，随后可以马上替换、删除。</summary>
        static InMemoryRandomAccessStream Load(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var ms = new InMemoryRandomAccessStream();
            var w = new DataWriter(ms);
            w.WriteBytes(bytes);
            Wait(w.StoreAsync());
            w.DetachStream();
            ms.Seek(0);
            return ms;
        }

        static PdfDocument OpenPdf(string path) { return Wait(PdfDocument.LoadFromStreamAsync(Load(path))); }

        public static string Image(string path)
        {
            using (var s = Load(path)) return Recognize(s);
        }

        static string Recognize(IRandomAccessStream s)
        {
            var dec = Wait(BitmapDecoder.CreateAsync(s));
            uint max = OcrEngine.MaxImageDimension;
            var tr = new BitmapTransform();
            uint w = dec.OrientedPixelWidth, h = dec.OrientedPixelHeight;
            if (w > max || h > max)
            {
                double k = (double)max / Math.Max(w, h);
                tr.ScaledWidth = (uint)(dec.PixelWidth * k); tr.ScaledHeight = (uint)(dec.PixelHeight * k);
            }
            using (var bmp = Wait(dec.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, tr, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage)))
            {
                var r = Wait(Engine().RecognizeAsync(bmp));
                var sb = new StringBuilder();
                foreach (var line in r.Lines)
                {
                    string prev = null;
                    foreach (var word in line.Words)
                    {
                        string t = word.Text;
                        if (prev != null && !(Tight(prev[prev.Length - 1]) || Tight(t[0]))) sb.Append(' ');
                        sb.Append(t); prev = t;
                    }
                    sb.Append('\n');
                }
                return sb.ToString();
            }
        }

        static bool Tight(char c) { return c > 0x2E80 || char.IsDigit(c) || c == '.' || c == ','; }

        /// <summary>早期识别结果中汉字之间带空格，合并掉，便于搜索。</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = Regex.Replace(s, @"(?<=[⺀-￯0-9.,])[ \t]+(?=[⺀-￯0-9.,])", "");
            return s;
        }
    }

    /// <summary>老版 .doc / .xls / .wps 借助本机 Word、Excel（或 WPS）读取文字。</summary>
    public class OfficeReader : IDisposable
    {
        object word, excel;

        static object Create(string progId)
        {
            var t = Type.GetTypeFromProgID(progId);
            return t == null ? null : Activator.CreateInstance(t);
        }
        static object Get(object o, string p) { return o.GetType().InvokeMember(p, BindingFlags.GetProperty, null, o, null); }
        static void SetP(object o, string p, object v) { o.GetType().InvokeMember(p, BindingFlags.SetProperty, null, o, new[] { v }); }
        static object Call(object o, string m, params object[] a) { return o.GetType().InvokeMember(m, BindingFlags.InvokeMethod, null, o, a); }

        public string Doc(string path)
        {
            if (word == null)
            {
                word = Create("Word.Application") ?? Create("KWps.Application");
                if (word == null) return null;
                try { SetP(word, "Visible", false); SetP(word, "DisplayAlerts", 0); } catch { }
            }
            var docs = Get(word, "Documents");
            // Open(FileName, ConfirmConversions, ReadOnly, AddToRecentFiles, PasswordDocument)
            object d = Call(docs, "Open", path, false, true, false, "\u0001");
            try { return Convert.ToString(Get(Get(d, "Content"), "Text")); }
            finally { try { Call(d, "Close", 0); } catch { } }
        }

        public string Xls(string path)
        {
            if (excel == null)
            {
                excel = Create("Excel.Application") ?? Create("Ket.Application");
                if (excel == null) return null;
                try { SetP(excel, "Visible", false); SetP(excel, "DisplayAlerts", false); SetP(excel, "AutomationSecurity", 3); } catch { }
            }
            var books = Get(excel, "Workbooks");
            object wb = Call(books, "Open", path, 0, true, Type.Missing, "\u0001");
            var sb = new StringBuilder();
            try
            {
                foreach (var ws in (System.Collections.IEnumerable)Get(wb, "Worksheets"))
                {
                    sb.Append(Get(ws, "Name")).Append('\n');
                    var v = Get(Get(ws, "UsedRange"), "Value2") as object[,];
                    if (v == null) continue;
                    for (int r = v.GetLowerBound(0); r <= v.GetUpperBound(0); r++)
                    {
                        var cells = new List<string>();
                        for (int c = v.GetLowerBound(1); c <= v.GetUpperBound(1); c++)
                        {
                            var x = v[r, c];
                            if (x != null && Convert.ToString(x).Trim().Length > 0) cells.Add(Convert.ToString(x));
                        }
                        if (cells.Count > 0) sb.Append(string.Join(" | ", cells)).Append('\n');
                        if (sb.Length > Store.MaxText) return sb.ToString();
                    }
                }
            }
            finally { try { Call(wb, "Close", false); } catch { } }
            return sb.ToString();
        }

        public void Dispose()
        {
            try { if (word != null) Call(word, "Quit", 0); } catch { }
            try { if (excel != null) Call(excel, "Quit"); } catch { }
            if (word != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(word);
            if (excel != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(excel);
            word = excel = null;
        }
    }

    public static class TextJob
    {
        public static readonly string[] OcrExts = { ".pdf", ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };
        public static readonly string[] OfficeExts = { ".doc", ".xls", ".wps", ".et" };
        public const int MaxPdfPages = 12;

        public static List<FileRec> Pending(Store st)
        {
            return st.Lib.Files.Where(f => (OcrExts.Contains(f.Ext) || OfficeExts.Contains(f.Ext)) && f.Size < 200L * 1024 * 1024
                && ((!st.Texts.ContainsKey(f.Rel) && !Tried(st, f)) || NeedsTail(st, f))).ToList();
        }

        static readonly System.Text.RegularExpressions.Regex OldCut = new System.Text.RegularExpressions.Regex(@"（共 (\d+) 页，仅识别前 (\d+) 页）");

        /// <summary>以前只识别了前 12 页的长 PDF：还要补识别最后 2 页。</summary>
        static bool NeedsTail(Store st, FileRec f)
        {
            string t; return f.Ext == ".pdf" && st.Texts.TryGetValue(f.Rel, out t) && t != null && t.Contains("仅识别前 ");
        }

        static string AddTail(string path, string old)
        {
            var m = OldCut.Match(old);
            int head = m.Success ? int.Parse(m.Groups[2].Value) : MaxPdfPages, count;
            string tail;
            try { tail = Ocr.PdfTail(path, head, 2, out count); }
            catch { return OldCut.Replace(old, "（共 $1 页，识别了前 $2 页；尾页识别失败）"); }
            int extra = Math.Max(0, Math.Min(2, count - head));
            return OldCut.Replace(old, "") + tail + (count > head + extra ? "（共 " + count + " 页，识别了前 " + head + " 页和最后 " + extra + " 页）" : "");
        }

        static bool Tried(Store st, FileRec f)
        {
            string v; return st.Lib.TextTried.TryGetValue(f.Rel, out v) && v == f.Size + "|" + f.Time;
        }

        /// <summary>识别尚无文字的文件。可随时取消；每 50 份自动保存。</summary>
        public static int Run(Store st, CancellationToken ct, Action<int, int, string> progress)
        {
            var list = Pending(st);
            int done = 0, ok = 0;
            object lk = st.Sync;
            var office = list.Where(f => OfficeExts.Contains(f.Ext)).ToList();
            var ocr = list.Where(f => OcrExts.Contains(f.Ext)).ToList();
            Action<FileRec, string> finish = (f, t) =>
            {
                lock (lk)
                {
                    done++;
                    if (!string.IsNullOrEmpty(t) && t.Trim().Length > 5)
                    {
                        st.Texts[f.Rel] = t.Length > Store.MaxText ? t.Substring(0, Store.MaxText) : t;
                        st.TextsDirty = true; ok++;
                    }
                    st.Lib.TextTried[f.Rel] = f.Size + "|" + f.Time;
                    if (progress != null) progress(done, list.Count, f.Name);
                    if (done % 50 == 0) { try { st.Save(); } catch { } }
                }
            };
            if (office.Count > 0)
            {
                using (var rd = new OfficeReader())
                    foreach (var f in office)
                    {
                        if (ct.IsCancellationRequested) break;
                        string t = null;
                        try { t = f.Ext == ".xls" || f.Ext == ".et" ? rd.Xls(st.FullPath(f)) : rd.Doc(st.FullPath(f)); } catch { }
                        if (t != null) t = t.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\a', ' ');
                        finish(f, t);
                    }
            }
            if (ocr.Count > 0 && Ocr.Available())
            {
                var po = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2)), CancellationToken = ct };
                try
                {
                    Parallel.ForEach(ocr, po, f =>
                    {
                        string t = null;
                        string old = null;
                        lock (lk) st.Texts.TryGetValue(f.Rel, out old);
                        try
                        {
                            if (f.Ext == ".pdf" && old != null && old.Contains("仅识别前 ")) t = AddTail(st.FullPath(f), old);
                            else t = f.Ext == ".pdf" ? Ocr.Pdf(st.FullPath(f), MaxPdfPages) : Ocr.Image(st.FullPath(f));
                        }
                        catch { t = old; }
                        finish(f, t);
                    });
                }
                catch (OperationCanceledException) { }
            }
            lock (lk) st.Save();
            return ok;
        }
    }
}
