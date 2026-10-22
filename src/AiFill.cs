using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DangAn
{
    /// <summary>AI 接口设置：存在本机用户目录（不随档案库分享），密钥用 Windows 账户加密保存。</summary>
    public class AiConfig
    {
        public string Provider { get; set; }
        public string BaseUrl { get; set; }
        public string Model { get; set; }
        public string KeyData { get; set; }
        public AiConfig() { Provider = "DeepSeek"; BaseUrl = "https://api.deepseek.com"; Model = "deepseek-chat"; KeyData = ""; }

        /// <summary>名称、接口地址、默认模型。都是兼容 OpenAI 的 /chat/completions 接口。</summary>
        public static readonly string[][] Presets =
        {
            new[] { "DeepSeek", "https://api.deepseek.com", "deepseek-chat" },
            new[] { "通义千问", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus" },
            new[] { "Kimi（月之暗面）", "https://api.moonshot.cn/v1", "moonshot-v1-128k" },
            new[] { "智谱 GLM", "https://open.bigmodel.cn/api/paas/v4", "glm-4-plus" },
            new[] { "自定义（兼容 OpenAI 接口）", "", "" },
        };

        static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "项目档案室"); } }
        static string FilePath { get { return Path.Combine(Dir, "ai.json"); } }

        [ScriptIgnore]
        public string Key
        {
            get
            {
                if (string.IsNullOrEmpty(KeyData)) return "";
                try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(KeyData), null, DataProtectionScope.CurrentUser)); }
                catch { return ""; }
            }
            set
            {
                KeyData = string.IsNullOrEmpty(value) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
            }
        }

        public bool Ready { get { return Key != "" && BaseUrl != "" && Model != ""; } }

        public static AiConfig Load()
        {
            try { if (File.Exists(FilePath)) return new JavaScriptSerializer().Deserialize<AiConfig>(File.ReadAllText(FilePath, Encoding.UTF8)) ?? new AiConfig(); }
            catch { }
            return new AiConfig();
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false));
        }
    }

    public class AiException : Exception { public AiException(string m) : base(m) { } }

    /// <summary>调用兼容 OpenAI 格式的对话接口（DeepSeek、通义千问、Kimi、智谱等）。</summary>
    public static class AiClient
    {
        public static int TokensIn, TokensOut;

        public static string Chat(AiConfig cfg, string system, string user, bool json, int maxTokens)
        {
            var body = new Dictionary<string, object>
            {
                { "model", cfg.Model },
                { "messages", new object[] {
                    new Dictionary<string, object> { { "role", "system" }, { "content", system } },
                    new Dictionary<string, object> { { "role", "user" }, { "content", user } } } },
                { "temperature", 0.1 },
                { "max_tokens", maxTokens },
            };
            if (json) body["response_format"] = new Dictionary<string, object> { { "type", "json_object" } };
            var msg = Message(Send(cfg, body));
            return msg == null || !msg.ContainsKey("content") ? "" : Convert.ToString(msg["content"]);
        }

        /// <summary>多轮对话（可带工具）：返回 AI 这一轮的 message（可能含 tool_calls）。</summary>
        public static Dictionary<string, object> Complete(AiConfig cfg, List<object> messages, object[] tools, int maxTokens)
        {
            var body = new Dictionary<string, object> { { "model", cfg.Model }, { "messages", messages.ToArray() }, { "temperature", 0.2 }, { "max_tokens", maxTokens } };
            if (tools != null && tools.Length > 0) body["tools"] = tools;
            var msg = Message(Send(cfg, body));
            if (msg == null) throw new AiException("AI 接口没有返回结果。");
            return msg;
        }

        static Dictionary<string, object> Message(Dictionary<string, object> o)
        {
            var choices = o.ContainsKey("choices") ? o["choices"] as object[] : null;
            if (choices == null || choices.Length == 0) throw new AiException("AI 接口没有返回结果。");
            return ((Dictionary<string, object>)choices[0])["message"] as Dictionary<string, object>;
        }

        static Dictionary<string, object> Send(AiConfig cfg, Dictionary<string, object> body)
        {
            if (cfg.Key == "") throw new AiException("还没有填写 AI 接口密钥（设置与备份 → AI 智能填表）。");
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
            string url = cfg.BaseUrl.TrimEnd('/');
            if (!url.EndsWith("/chat/completions")) url += "/chat/completions";
            for (int attempt = 0; ; attempt++)
            {
                try { return Post(url, cfg.Key, body); }
                catch (AiHttpException e)
                {
                    // 部分模型不支持 response_format：去掉后重试
                    if (e.Status == 400 && body.ContainsKey("response_format")) { body.Remove("response_format"); continue; }
                    if ((e.Status == 429 || e.Status >= 500 || e.Status == 0) && attempt < 2) { System.Threading.Thread.Sleep(3000 * (attempt + 1)); continue; }
                    throw new AiException(Explain(e));
                }
            }
        }

        class AiHttpException : Exception
        {
            public int Status; public string Body;
            public AiHttpException(int s, string b, string m) : base(m) { Status = s; Body = b; }
        }

        static string Explain(AiHttpException e)
        {
            string detail = e.Body ?? "";
            var m = Regex.Match(detail, "\"message\"\\s*:\\s*\"([^\"]*)\"");
            if (m.Success) detail = Regex.Unescape(m.Groups[1].Value);
            if (detail.Length > 200) detail = detail.Substring(0, 200);
            switch (e.Status)
            {
                case 401: return "接口密钥不正确或已失效（401）。请到“设置与备份”重新填写。" + detail;
                case 402: return "AI 账户余额不足（402），请先充值。" + detail;
                case 404: return "接口地址或模型名称不对（404）。" + detail;
                case 429: return "请求太频繁或额度用完（429），稍后再试。" + detail;
                case 0: return "连不上 AI 接口：" + e.Message;
            }
            return "AI 接口返回错误（" + e.Status + "）：" + detail;
        }

        static Dictionary<string, object> Post(string url, string key, Dictionary<string, object> body)
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            byte[] data = Encoding.UTF8.GetBytes(ser.Serialize(body));
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST"; req.ContentType = "application/json; charset=utf-8";
            req.Headers["Authorization"] = "Bearer " + key;
            req.Timeout = 300000; req.ReadWriteTimeout = 300000;
            string text;
            try
            {
                using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) text = r.ReadToEnd();
            }
            catch (WebException we)
            {
                var hr = we.Response as HttpWebResponse;
                if (hr == null) throw new AiHttpException(0, null, we.Message);
                string b = "";
                try { using (var r = new StreamReader(hr.GetResponseStream(), Encoding.UTF8)) b = r.ReadToEnd(); } catch { }
                throw new AiHttpException((int)hr.StatusCode, b, we.Message);
            }
            var o = ser.DeserializeObject(text) as Dictionary<string, object>;
            if (o == null) throw new AiException("AI 接口返回内容无法识别。");
            object u;
            if (o.TryGetValue("usage", out u) && u is Dictionary<string, object>)
            {
                var ud = (Dictionary<string, object>)u;
                TokensIn += ToInt(ud, "prompt_tokens"); TokensOut += ToInt(ud, "completion_tokens");
            }
            return o;
        }

        static int ToInt(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToInt32(v) : 0; }

        /// <summary>从回复里取出 JSON 对象（容忍 ```json 包裹和前后多余文字）。</summary>
        public static Dictionary<string, object> ParseJson(string reply)
        {
            string s = reply ?? "";
            int a = s.IndexOf('{'), b = s.LastIndexOf('}');
            if (a < 0 || b <= a) throw new AiException("AI 没有按要求返回 JSON：" + Cut(s, 200));
            try { return (Dictionary<string, object>)new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(s.Substring(a, b - a + 1)); }
            catch (Exception e) { throw new AiException("AI 返回的 JSON 有误：" + e.Message); }
        }

        public static string Cut(string s, int n) { s = s ?? ""; return s.Length <= n ? s : s.Substring(0, n) + "…"; }
    }

    // ===================================================== Excel 读写（用本机 Excel 或 WPS 表格）
    public class SheetData
    {
        public string Name;
        public int Rows, Cols;                 // 有内容的行列数（从 A1 起）
        public string[,] Text;                 // [行-1, 列-1]
        public bool[,] Formula;
        public string Get(int r, int c) { return r >= 1 && c >= 1 && r <= Rows && c <= Cols ? Text[r - 1, c - 1] ?? "" : ""; }
        public bool IsFormula(int r, int c) { return r >= 1 && c >= 1 && r <= Rows && c <= Cols && Formula[r - 1, c - 1]; }
    }

    public static class ExcelIO
    {
        public const int MaxRows = 3000, MaxCols = 60;

        static object Get(object o, string p, params object[] a) { return o.GetType().InvokeMember(p, BindingFlags.GetProperty, null, o, a); }
        static void SetP(object o, string p, object v) { o.GetType().InvokeMember(p, BindingFlags.SetProperty, null, o, new[] { v }); }
        static object Call(object o, string m, params object[] a) { return o.GetType().InvokeMember(m, BindingFlags.InvokeMethod, null, o, a); }

        static object App()
        {
            foreach (var id in new[] { "Excel.Application", "KET.Application", "ET.Application" })
            {
                var t = Type.GetTypeFromProgID(id);
                if (t == null) continue;
                try
                {
                    var app = Activator.CreateInstance(t);
                    try { SetP(app, "Visible", false); } catch { }
                    try { SetP(app, "DisplayAlerts", false); } catch { }
                    return app;
                }
                catch { }
            }
            throw new AiException("本机没有找到 Excel 或 WPS 表格，无法读写 .xls/.xlsx。");
        }

        static void Quit(object app, object wb)
        {
            try { if (wb != null) Call(wb, "Close", false); } catch { }
            try { if (app != null) Call(app, "Quit"); } catch { }
            try { if (wb != null) Marshal.FinalReleaseComObject(wb); } catch { }
            try { if (app != null) Marshal.FinalReleaseComObject(app); } catch { }
            GC.Collect(); GC.WaitForPendingFinalizers();
        }

        public static string ColName(int c)
        {
            string s = "";
            while (c > 0) { int m = (c - 1) % 26; s = (char)('A' + m) + s; c = (c - 1) / 26; }
            return s;
        }

        public static int ColIndex(string s)
        {
            int c = 0;
            foreach (char ch in s.ToUpperInvariant()) c = c * 26 + (ch - 'A' + 1);
            return c;
        }

        static string Show(object v)
        {
            if (v == null) return "";
            if (v is double)
            {
                double d = (double)v;
                if (Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15) return ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
                return Math.Round(d, 6).ToString("0.######", CultureInfo.InvariantCulture);
            }
            if (v is bool) return (bool)v ? "是" : "否";
            if (v is int) return "#错误";
            return Convert.ToString(v).Trim();
        }

        public static List<SheetData> Read(string path)
        {
            var list = new List<SheetData>();
            object app = null, wb = null;
            try
            {
                app = App();
                wb = Call(Get(app, "Workbooks"), "Open", path, 0, true);
                var sheets = Get(wb, "Worksheets");
                int n = Convert.ToInt32(Get(sheets, "Count"));
                for (int i = 1; i <= n; i++)
                {
                    var ws = Get(sheets, "Item", i);
                    if (Convert.ToInt32(Get(ws, "Visible")) != -1) continue;
                    var used = Get(ws, "UsedRange");
                    int r0 = Convert.ToInt32(Get(used, "Row")), c0 = Convert.ToInt32(Get(used, "Column"));
                    int rn = Convert.ToInt32(Get(Get(used, "Rows"), "Count")), cn = Convert.ToInt32(Get(Get(used, "Columns"), "Count"));
                    int rows = Math.Min(r0 + rn - 1, MaxRows), cols = Math.Min(c0 + cn - 1, MaxCols);
                    if (rows < 1 || cols < 1) continue;
                    var range = Get(ws, "Range", "A1", ColName(cols) + rows);
                    var vals = Get(range, "Value2") as object[,];
                    var fmls = Get(range, "Formula") as object[,];
                    var sd = new SheetData { Name = Convert.ToString(Get(ws, "Name")), Text = new string[rows, cols], Formula = new bool[rows, cols] };
                    int lastR = 0, lastC = 0;
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                        {
                            object v = vals == null ? null : vals[r + vals.GetLowerBound(0), c + vals.GetLowerBound(1)];
                            string f = fmls == null ? "" : Convert.ToString(fmls[r + fmls.GetLowerBound(0), c + fmls.GetLowerBound(1)]);
                            sd.Text[r, c] = Show(v);
                            sd.Formula[r, c] = f.StartsWith("=");
                            if (sd.Text[r, c] != "" || sd.Formula[r, c]) { lastR = Math.Max(lastR, r + 1); lastC = Math.Max(lastC, c + 1); }
                        }
                    if (lastR == 0) continue;
                    sd.Rows = lastR; sd.Cols = lastC;
                    list.Add(sd);
                }
            }
            finally { Quit(app, wb); }
            return list;
        }

        /// <summary>把填写结果写进原表的副本：填过的格子标浅黄色，批注里写依据。返回写入格数。</summary>
        public static int Write(string src, string dst, IList<AiCell> cells) { List<string> skipped; return Write(src, dst, cells, false, out skipped); }

        /// <summary>keepFilled：已有内容的格子不改（记进 skipped）。合并单元格只写左上角一次。</summary>
        public static int Write(string src, string dst, IList<AiCell> cells, bool keepFilled, out List<string> skipped)
        {
            object app = null, wb = null; int n = 0;
            skipped = new List<string>();
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                app = App();
                wb = Call(Get(app, "Workbooks"), "Open", src, 0, true);
                var sheets = Get(wb, "Worksheets");
                foreach (var c in cells)
                {
                    var ws = string.IsNullOrEmpty(c.Sheet) ? Get(sheets, "Item", 1) : Get(sheets, "Item", c.Sheet);
                    var cell = Get(ws, "Range", c.Cell);
                    try { cell = Get(Get(cell, "MergeArea"), "Cells", 1, 1); } catch { }
                    string key = Convert.ToString(Get(ws, "Name")) + "!" + Convert.ToString(Get(cell, "Address", false, false));
                    if (!done.Add(key)) { skipped.Add(c.Cell + "（与前面的格子是同一个合并单元格）"); continue; }
                    if (keepFilled)
                    {
                        object cur = Get(cell, "Value2"); bool hasF = Convert.ToBoolean(Get(cell, "HasFormula"));
                        if (hasF || (cur != null && Convert.ToString(cur).Trim() != "")) { skipped.Add(c.Cell + "（原来已有内容：" + AiClient.Cut(Convert.ToString(cur), 20) + "）"); continue; }
                    }
                    double d;
                    string v = c.Value.Trim();
                    if (Regex.IsMatch(v, @"^-?\d+(\.\d+)?$") && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) SetP(cell, "Value2", d);
                    else SetP(cell, "Value2", v);
                    try { SetP(Get(cell, "Interior"), "Color", 204 * 65536 + 242 * 256 + 255); } catch { }   // 浅黄
                    try
                    {
                        Call(cell, "ClearComments");
                        if (c.Source != "") Call(cell, "AddComment", (c.Source.StartsWith("AI") ? "" : "AI 填写依据：") + AiClient.Cut(c.Source, 240));
                    }
                    catch { }
                    n++;
                }
                object fmt = Get(wb, "FileFormat");
                if (File.Exists(dst)) File.Delete(dst);
                Call(wb, "SaveAs", dst, fmt);
            }
            finally { Quit(app, wb); }
            return n;
        }
    }

    /// <summary>一个要填写的单元格。</summary>
    public class AiCell
    {
        public string Sheet = "", Cell = "", Header = "", RowLabel = "", Value = "", Source = "", ProjectId = "", ProjectName = "";
        public int Row, Col;
        public bool Use = true;
    }

    public class AiBlock
    {
        public string Sheet = "", ProjectId = "", Reason = "", Confidence = "";
        public List<int> Rows = new List<int>();
        public int FirstRow;
    }

    public class AiPlan
    {
        public List<SheetData> Sheets = new List<SheetData>();
        public Dictionary<string, Dictionary<int, string>> Headers = new Dictionary<string, Dictionary<int, string>>();   // 表 → 列 → 栏目名
        public Dictionary<string, List<int>> FillCols = new Dictionary<string, List<int>>();
        public Dictionary<string, List<int>> RowCols = new Dictionary<string, List<int>>();                             // 按路段逐行填写的列
        public Dictionary<string, string> Questions = new Dictionary<string, string>();                                 // "表!列字母" → 要回答什么
        public List<AiBlock> Blocks = new List<AiBlock>();
        public List<string> Keywords = new List<string>();
        public List<AiCell> Cells = new List<AiCell>();
        public List<string> Skipped = new List<string>();
    }

    // ===================================================== AI 填表流程
    /// <summary>
    /// ① 把表格内容和档案项目清单交给 AI：认出表头、要填的列、哪几行是本单位哪个项目；
    /// ② 每个项目单独一次：把项目登记信息、合同、收支、文件清单和原文摘录交给 AI，只填空白格；
    /// 找不到依据的格子保持空白。
    /// </summary>
    public static class AiFiller
    {
        const int TableBudget = 70000, ProjectBudget = 60000;

        public static AiPlan Run(Store st, AiConfig cfg, List<SheetData> sheets, Action<string> log)
        {
            var plan = new AiPlan { Sheets = sheets };
            log("① 让 AI 看懂表格结构，并和档案里的 " + st.RealProjects.Count() + " 个项目对应……");
            Analyze(st, cfg, plan, log);
            if (plan.Blocks.Count == 0) { log("表里没有找到和档案项目对得上的行。"); return plan; }
            int i = 0;
            foreach (var g in plan.Blocks.GroupBy(b => b.ProjectId))
            {
                var p = st.Proj(g.Key);
                i++;
                log("② (" + i + "/" + plan.Blocks.Select(b => b.ProjectId).Distinct().Count() + ") 查档案并填写：" + p.Name + "（表中第 " + string.Join("、", g.Select(b => b.Sheet + " " + Span(b.Rows))) + " 行）");
                try
                {
                    int before = plan.Cells.Count;
                    FillProject(st, cfg, plan, p, g.ToList(), log);
                    log("    填了 " + (plan.Cells.Count - before) + " 格。");
                }
                catch (AiException e) { log("    这个项目没填成：" + e.Message); }
            }
            return plan;
        }

        static string Span(List<int> rows) { return rows.Count == 1 ? rows[0].ToString() : rows.Min() + "–" + rows.Max(); }

        // ---------------- ① 分析表格 ----------------
        static string ProjectCatalog(Store st)
        {
            var sb = new StringBuilder();
            foreach (var p in st.RealProjects)
            {
                sb.Append(p.Id).Append("｜").Append(p.Name);
                if (p.Folders.Count > 0) sb.Append("｜文件夹：").Append(string.Join("；", p.Folders));
                foreach (var f in new[] { "建设地点", "建设内容及规模", "中标／施工单位", "立项／上级建设计划" })
                {
                    var v = p.Get(f);
                    if (v != "") sb.Append("｜").Append(f).Append("：").Append(AiClient.Cut(v, 90));
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static string TableText(SheetData s, int cellMax, int budget, out bool truncated)
        {
            var sb = new StringBuilder(); truncated = false;
            sb.AppendLine("工作表「" + s.Name + "」，共 " + s.Rows + " 行，列 A–" + ExcelIO.ColName(s.Cols) + "（每行只列出有内容的格子；公式格显示计算结果并标 ƒ）");
            for (int r = 1; r <= s.Rows; r++)
            {
                var parts = new List<string>();
                for (int c = 1; c <= s.Cols; c++)
                {
                    string t = s.Get(r, c);
                    if (t == "" && !s.IsFormula(r, c)) continue;
                    parts.Add(ExcelIO.ColName(c) + "=" + AiClient.Cut(t.Replace("\r", "").Replace("\n", " "), cellMax) + (s.IsFormula(r, c) ? "ƒ" : ""));
                }
                if (parts.Count == 0) continue;
                string line = "R" + r + "｜" + string.Join("｜", parts);
                if (sb.Length + line.Length > budget) { truncated = true; sb.AppendLine("……（后面的行太多，省略）"); break; }
                sb.AppendLine(line);
            }
            return sb.ToString();
        }

        const string AnalyzeSystem =
@"你是乡镇（街道）项目档案管理员的填表助手。上级发来一张 Excel 表，需要用本单位项目档案里的信息填写。现在做第一步：看懂表格，并找出表中哪些行就是本单位档案里的项目。只输出 JSON，不要输出别的文字。

要求：
1. header_rows：表头所在的行号；columns：每一列的栏目名（列字母→栏目名，多层表头合起来写，如“预付款金额（万元）”）。
2. fill_columns：需要本单位根据档案来填写的列，例如进度、合同情况、是否……、约定条件、金额、日期、单位名称、情况说明、备注。
   不要列入表格本来就给定的基础数据列：序号、项目名称、所在村组、起止点、里程、宽度、长度、上级下达或预估的计划/补助金额，以及公式合计列。
3. blocks：表中确实是指本单位档案里某个项目的行。一个项目常常占连续多行（后面几行项目名称为空，是同一项目的不同路段），把这些行都放进 rows，first_row 是该项目第一行。
   合计、小计行不要放进去。表里其他乡镇、街道、单位的项目不要匹配。项目名称、年份、地点要对得上；拿不准时 confidence 写“低”并在 reason 里说明。
4. questions：给每个 fill_column 写一句话，说明这一栏要回答什么、单位和格式（如“万元，保留2位小数”“填是或否”），表头带日期的写明按该日期时点。
5. row_columns：fill_columns 中确实需要按行（路段）分别填写的列；项目整体的信息（合同、预付款、进度、单位、备注等）只填项目第一行，不要列入。没有就给空数组。
6. keywords：为了在档案原文里找这些栏目的答案，给出 5–15 个检索词（如“预付款”“开工令”“形象进度”）。

输出格式：
{""sheets"":[{""sheet"":""工作表名"",""header_rows"":[3],""columns"":{""A"":""序号"",""B"":""项目名称""},""fill_columns"":[""L"",""M""],""row_columns"":[]}],
 ""questions"":{""工作表名!L"":""……""},
 ""blocks"":[{""sheet"":""工作表名"",""project_id"":""P001"",""rows"":[51,52],""first_row"":51,""confidence"":""高"",""reason"":""……""}],
 ""keywords"":[""……""]}";

        static void Analyze(Store st, AiConfig cfg, AiPlan plan, Action<string> log)
        {
            var sb = new StringBuilder();
            sb.AppendLine("今天是 " + DateTime.Now.ToString("yyyy-MM-dd") + "。");
            sb.AppendLine("【本单位档案里的项目】（项目编号｜名称｜……）");
            sb.AppendLine(ProjectCatalog(st));
            sb.AppendLine("【待填表格】");
            int per = Math.Max(8000, TableBudget / Math.Max(1, plan.Sheets.Count));
            foreach (var s in plan.Sheets)
            {
                bool cut;
                sb.AppendLine(TableText(s, 60, per, out cut));
                if (cut) log("    提示：工作表「" + s.Name + "」行数很多，只交给 AI 前面一部分。");
            }
            var o = AiClient.ParseJson(AiClient.Chat(cfg, AnalyzeSystem, sb.ToString(), true, 8000));

            foreach (var so in Arr(o, "sheets").OfType<Dictionary<string, object>>())
            {
                string name = Str(so, "sheet");
                var sd = plan.Sheets.FirstOrDefault(x => x.Name == name);
                if (sd == null) continue;
                var cols = new Dictionary<int, string>();
                object co;
                if (so.TryGetValue("columns", out co) && co is Dictionary<string, object>)
                    foreach (var kv in (Dictionary<string, object>)co)
                        if (Regex.IsMatch(kv.Key, "^[A-Za-z]{1,3}$")) cols[ExcelIO.ColIndex(kv.Key)] = Convert.ToString(kv.Value);
                plan.Headers[name] = cols;
                plan.FillCols[name] = Arr(so, "fill_columns").Select(Convert.ToString).Where(x => Regex.IsMatch(x ?? "", "^[A-Za-z]{1,3}$"))
                    .Select(ExcelIO.ColIndex).Where(c => c <= sd.Cols + 5).Distinct().ToList();
                plan.RowCols[name] = Arr(so, "row_columns").Select(Convert.ToString).Where(x => Regex.IsMatch(x ?? "", "^[A-Za-z]{1,3}$")).Select(ExcelIO.ColIndex).ToList();
            }
            object qo;
            if (o.TryGetValue("questions", out qo) && qo is Dictionary<string, object>)
                foreach (var kv in (Dictionary<string, object>)qo) plan.Questions[kv.Key] = Convert.ToString(kv.Value);
            plan.Keywords = Arr(o, "keywords").Select(Convert.ToString).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct().Take(20).ToList();

            foreach (var bo in Arr(o, "blocks").OfType<Dictionary<string, object>>())
            {
                var b = new AiBlock { Sheet = Str(bo, "sheet"), ProjectId = Str(bo, "project_id"), Reason = Str(bo, "reason"), Confidence = Str(bo, "confidence") };
                var sd = plan.Sheets.FirstOrDefault(x => x.Name == b.Sheet);
                if (sd == null || st.Proj(b.ProjectId) == null || st.Proj(b.ProjectId).IsRoot) continue;
                foreach (var r in Arr(bo, "rows")) { int ri; if (int.TryParse(Convert.ToString(r), out ri) && ri >= 1 && ri <= sd.Rows) b.Rows.Add(ri); }
                if (b.Rows.Count == 0) continue;
                b.Rows = b.Rows.Distinct().OrderBy(x => x).ToList();
                int fr; b.FirstRow = int.TryParse(Str(bo, "first_row"), out fr) && b.Rows.Contains(fr) ? fr : b.Rows[0];
                plan.Blocks.Add(b);
                log("    第 " + Span(b.Rows) + " 行 → " + st.Proj(b.ProjectId).Name + "（把握：" + (b.Confidence == "" ? "—" : b.Confidence) + "）" + (b.Reason != "" ? "  " + AiClient.Cut(b.Reason, 80) : ""));
            }
            foreach (var kv in plan.FillCols)
                log("    要填的栏目（" + kv.Key + "）：" + string.Join("、", kv.Value.Select(c => ExcelIO.ColName(c) + " " + HeaderOf(plan, kv.Key, c))));
        }

        static string HeaderOf(AiPlan plan, string sheet, int col)
        {
            Dictionary<int, string> h; string s;
            return plan.Headers.TryGetValue(sheet, out h) && h.TryGetValue(col, out s) ? s : ExcelIO.ColName(col) + " 列";
        }

        static IEnumerable<object> Arr(Dictionary<string, object> o, string k)
        {
            object v;
            if (o.TryGetValue(k, out v) && v is object[]) return (object[])v;
            if (v is System.Collections.ArrayList) return ((System.Collections.ArrayList)v).Cast<object>();
            return new object[0];
        }

        static string Str(Dictionary<string, object> o, string k) { object v; return o.TryGetValue(k, out v) && v != null ? Convert.ToString(v).Trim() : ""; }

        // ---------------- ② 按项目填写 ----------------
        const string FillSystem =
@"你是乡镇（街道）项目档案管理员的填表助手。根据下面给出的【项目档案】，填写表格里的空白单元格。只输出 JSON，不要输出别的文字。

规则：
1. 只依据给出的档案内容。档案里找不到依据的格子不要填（放进 skipped 并说明原因），宁可空着，不要编造，不要凭常识推算。
2. 【登记信息】【合同台账】是以前手工登记的，可能有错；与原文摘录冲突时以原文为准，金额以大写为准。
3. 每个填写都要给 source：依据的文件名或档案登记字段，并摘录关键原文（60 字以内）。
3a. 按栏目要求的单位和格式填写：元换算成万元要除以 10000；金额、数量只写数字，不带单位；“是否”类填“是”或“否”；日期写成 2026-05-19 格式；文字说明简明扼要。
4. “备注”“情况说明”类栏目：只写与本表主题直接相关、上级需要知道的说明；没有就不填（不要填“无”）。登记信息里的“其他说明及资料缺项”“整理／期间口径”“重大事项”等是本单位内部整理笔记，不要抄进上报表格。
5. 表头带日期（时点）的栏目，按那个时点的情况填写；只能找到该时点以后的情况时不要倒填，放进 skipped。
6. 一个项目占多行时，项目整体的信息只填在 first_row 那一行，不要把同一内容抄到其他行。
7. 只能填写“待填单元格”清单里列出的格子。

输出格式：
{""fills"":[{""cell"":""M51"",""value"":""是"",""source"":""施工合同.pdf：……""}],
 ""skipped"":[{""cell"":""O51"",""reason"":""……""}]}";

        static void FillProject(Store st, AiConfig cfg, AiPlan plan, Project p, List<AiBlock> blocks, Action<string> log)
        {
            var allowed = new Dictionary<string, AiCell>(StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            sb.AppendLine("今天是 " + DateTime.Now.ToString("yyyy-MM-dd") + "。");
            sb.AppendLine("【表格】");
            foreach (var b in blocks)
            {
                var sd = plan.Sheets.First(x => x.Name == b.Sheet);
                List<int> fc;
                if (!plan.FillCols.TryGetValue(b.Sheet, out fc) || fc.Count == 0) continue;
                sb.AppendLine("工作表「" + b.Sheet + "」栏目：" + string.Join("｜", Enumerable.Range(1, sd.Cols).Select(c => ExcelIO.ColName(c) + "=" + HeaderOf(plan, b.Sheet, c))));
                sb.AppendLine("本项目所在行（first_row = " + b.FirstRow + "）：");
                foreach (int r in b.Rows)
                {
                    var parts = new List<string>();
                    for (int c = 1; c <= sd.Cols; c++) { string t = sd.Get(r, c); if (t != "") parts.Add(ExcelIO.ColName(c) + "=" + AiClient.Cut(t.Replace("\n", " "), 120)); }
                    sb.AppendLine("R" + r + "｜" + string.Join("｜", parts));
                }
                sb.AppendLine("待填单元格：");
                foreach (int c in fc)
                {
                    string q; plan.Questions.TryGetValue(b.Sheet + "!" + ExcelIO.ColName(c), out q);
                    List<int> rc;
                    bool perRow = plan.RowCols.TryGetValue(b.Sheet, out rc) && rc.Contains(c);
                    var empty = (perRow ? b.Rows : new List<int> { b.FirstRow }).Where(r => sd.Get(r, c) == "" && !sd.IsFormula(r, c)).ToList();
                    if (empty.Count == 0) continue;
                    foreach (int r in empty)
                        allowed[b.Sheet + "!" + ExcelIO.ColName(c) + r] = new AiCell
                        {
                            Sheet = b.Sheet, Cell = ExcelIO.ColName(c) + r, Row = r, Col = c, Header = HeaderOf(plan, b.Sheet, c),
                            ProjectId = p.Id, ProjectName = p.Name, RowLabel = RowLabel(sd, r)
                        };
                    sb.AppendLine("  " + string.Join("、", empty.Select(r => ExcelIO.ColName(c) + r)) + "：" + HeaderOf(plan, b.Sheet, c) + (string.IsNullOrEmpty(q) ? "" : "——" + q));
                }
            }
            if (allowed.Count == 0) { log("    这个项目对应的格子都已经有内容，不用填。"); return; }
            sb.AppendLine();
            sb.AppendLine("【项目档案】");
            sb.AppendLine(Dossier(st, p, plan, ProjectBudget));

            var o = AiClient.ParseJson(AiClient.Chat(cfg, FillSystem, sb.ToString(), true, 8000));
            bool multiSheet = blocks.Select(b => b.Sheet).Distinct().Count() > 1;
            foreach (var fo in Arr(o, "fills").OfType<Dictionary<string, object>>())
            {
                string cell = Str(fo, "cell").Replace("$", ""), sheet = blocks[0].Sheet;
                int bang = cell.IndexOf('!');
                if (bang > 0) { sheet = cell.Substring(0, bang).Trim('\'', '「', '」'); cell = cell.Substring(bang + 1); }
                AiCell target;
                if (!allowed.TryGetValue(sheet + "!" + cell, out target) && !(multiSheet == false && allowed.TryGetValue(blocks[0].Sheet + "!" + cell, out target)))
                {
                    log("    忽略 " + cell + "（不在待填范围）");
                    continue;
                }
                string v = Str(fo, "value");
                if (v == "" || v == "无法确定" || v == "未知" || v == "—") continue;
                if (Regex.IsMatch(target.Header, "备注|说明") && Regex.IsMatch(v, "^(无|暂无|没有|/|／|无备注|无需说明)$")) continue;
                target.Value = v; target.Source = Str(fo, "source");
                if (!plan.Cells.Contains(target)) plan.Cells.Add(target);
            }
            foreach (var so in Arr(o, "skipped").OfType<Dictionary<string, object>>())
                plan.Skipped.Add(p.Name + " " + Str(so, "cell") + "：" + Str(so, "reason"));
        }

        static string RowLabel(SheetData sd, int r)
        {
            var parts = new List<string>();
            for (int c = 1; c <= Math.Min(sd.Cols, 6); c++) { string t = sd.Get(r, c); if (t != "") parts.Add(t); }
            return AiClient.Cut(string.Join(" ", parts), 60);
        }

        /// <summary>项目资料包：登记信息、合同、收支、发票、文件清单，以及与栏目相关的原文摘录。</summary>
        public static string Dossier(Store st, Project p, AiPlan plan, int budget)
        {
            var sb = new StringBuilder();
            sb.AppendLine("项目名称：" + p.Name + "（编号 " + p.Id + "，类型 " + p.Type + "）");
            var info = st.Info(p);
            sb.AppendLine("归档进度：" + info.Stage + (info.Missing.Count > 0 ? "；缺少：" + string.Join("、", info.Missing.Select(Cats.Short)) : ""));
            sb.AppendLine("—— 登记信息（以前手工登记，可能有误；与下面原文冲突时以原文为准） ——");
            foreach (var f in p.Fields) if (!string.IsNullOrWhiteSpace(f.Value)) sb.AppendLine(f.Name + "：" + f.Value);
            if (p.Note != "") sb.AppendLine("备注：" + p.Note);
            if (p.Contracts.Count > 0)
            {
                sb.AppendLine("—— 合同台账（金额单位：元；手工登记，可能有误） ——");
                foreach (var c in p.Contracts)
                    sb.AppendLine(c.Kind + "｜乙方：" + c.Party + "｜金额 " + c.Amount.ToString("0.00") + "｜签订 " + c.Date + "｜已付 " + c.Paid.ToString("0.00") + "｜未付 " + c.Unpaid.ToString("0.00")
                        + (c.Final != "" ? "｜结算/决算：" + c.Final : "") + (c.Content != "" ? "｜内容：" + c.Content : "") + (c.Note != "" ? "｜说明：" + c.Note : "") + (c.Source != "" ? "｜原件：" + Path.GetFileName(c.Source.Split(';', '；', '\n')[0].Trim()) : ""));
            }
            var fs = Finance.Sum(st, p);
            sb.AppendLine("—— 资金 ——");
            sb.AppendLine("资金到账 " + fs.Income.ToString("0.00") + " 元；已支付 " + fs.Paid.ToString("0.00") + " 元（" + (fs.PaidBasis == "" ? "无记录" : fs.PaidBasis) + "）；发票 " + fs.InvoiceCount + " 张合计 " + fs.Invoiced.ToString("0.00") + " 元");
            foreach (var x in p.Payments.OrderBy(x => x.Date).Take(80))
                sb.AppendLine("  " + x.Date + " " + x.Dir + " " + x.Amount.ToString("0.00") + " 元｜" + x.Summary + (x.Party != "" ? "｜" + x.Party : "") + (x.VoucherNo != "" ? "｜凭证 " + x.VoucherNo : "") + (x.Contract != "" ? "｜合同：" + x.Contract : ""));
            foreach (var iv in st.Lib.Invoices.Where(i => i.Project == p.Id && (i.Members == null || i.Members.Count == 0)).OrderBy(i => i.Date).Take(60))
                sb.AppendLine("  发票 " + iv.Date + "｜" + iv.Seller + "｜" + iv.Amount.ToString("0.00") + " 元｜" + Path.GetFileName(iv.Rel));

            var files = st.FilesOf(p).ToList();
            var list = new StringBuilder();
            list.AppendLine("—— 档案文件清单（分类｜文件） ——");
            foreach (var f in files.OrderBy(f => f.Cat).ThenBy(f => f.Rel))
            {
                string line = Cats.Short(f.Cat) + "｜" + Rel(p, f.Rel);
                if (list.Length + line.Length > 12000) { list.AppendLine("……共 " + files.Count + " 份，其余省略"); break; }
                list.AppendLine(line);
            }
            sb.Append(list);

            var words = new List<string>(plan.Keywords);
            foreach (var h in plan.FillCols.SelectMany(kv => kv.Value.Select(c => HeaderOf(plan, kv.Key, c))))
                foreach (var w in Regex.Split(Regex.Replace(h, @"[（(][^）)]*[）)]", " "), @"[\s、，,／/中是否的及和与]+"))
                    if (w.Length >= 2 && w.Length <= 8) words.Add(w);
            words = words.Distinct().ToList();
            int left = budget - sb.Length;
            if (left > 2000) sb.Append(Excerpts(st, p, files, words, left));
            return sb.ToString();
        }

        static string Rel(Project p, string rel)
        {
            foreach (var f in p.Folders) if (rel.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase)) return rel.Substring(f.Length + 1);
            return rel;
        }

        static string Excerpts(Store st, Project p, List<FileRec> files, List<string> words, int budget)
        {
            const int Half = 220;
            var hits = new List<Tuple<FileRec, string, List<int>, int>>();   // 文件、全文、命中位置、命中词数
            lock (st.Sync)
            {
                foreach (var f in files)
                {
                    string text;
                    if (!st.Texts.TryGetValue(f.Rel, out text) || string.IsNullOrEmpty(text)) continue;
                    var pos = new List<int>(); int kinds = 0;
                    foreach (var w in words)
                    {
                        int i = 0, n = 0;
                        while (n < 6 && (i = text.IndexOf(w, i, StringComparison.OrdinalIgnoreCase)) >= 0) { pos.Add(i); n++; i += w.Length; }
                        if (n > 0) kinds++;
                    }
                    if (pos.Count > 0) hits.Add(Tuple.Create(f, text, pos, kinds));
                }
            }
            var sb = new StringBuilder();
            sb.AppendLine("—— 档案原文摘录（按检索词：" + string.Join("、", words.Take(30)) + "） ——");
            foreach (var h in hits.OrderByDescending(h => h.Item4).ThenBy(h => h.Item1.Cat == "04 合同协议" ? 0 : 1).ThenByDescending(h => h.Item3.Count))
            {
                var spans = new List<int[]>();
                foreach (int i in h.Item3.OrderBy(x => x))
                {
                    int a = Math.Max(0, i - Half), b = Math.Min(h.Item2.Length, i + Half);
                    if (spans.Count > 0 && a <= spans[spans.Count - 1][1]) spans[spans.Count - 1][1] = Math.Max(spans[spans.Count - 1][1], b);
                    else spans.Add(new[] { a, b });
                }
                var part = new StringBuilder();
                part.AppendLine("【" + Rel(p, h.Item1.Rel) + "】");
                foreach (var s in spans.Take(6))
                    part.AppendLine("…" + Regex.Replace(h.Item2.Substring(s[0], s[1] - s[0]), @"\s+", " ") + "…");
                if (sb.Length + part.Length > budget)
                {
                    if (sb.Length + 600 > budget) break;
                    continue;
                }
                sb.Append(part);
            }
            return sb.ToString();
        }
    }
}
