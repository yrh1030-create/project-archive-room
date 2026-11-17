using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace DangAn
{
    /// <summary>
    /// 字段映射（%APPDATA%\项目档案室\jw-mapping.json），和网址账号分开存。默认按页面上的中文标签找栏位；
    /// 实机核对后可给个别栏位配 PageLabel（页面上的实际叫法）或 Selector（CSS 选择器），改了不用重装。
    /// </summary>
    public class JwMapping
    {
        public class Item
        {
            public string Module { get; set; }
            public string Group { get; set; }
            public string Label { get; set; }
            public string PageLabel { get; set; }
            public string Selector { get; set; }
            public bool Skip { get; set; }
            /// <summary>下拉选项对照：档案里的说法 → 页面上的选项（实机核对后填）。</summary>
            public Dictionary<string, string> Values { get; set; }
            public Item() { Module = Group = Label = PageLabel = Selector = ""; }
        }

        public string Note { get; set; }
        /// <summary>附件区“新增”按钮上的字。</summary>
        public string AddButton { get; set; }
        /// <summary>附件区附近的文字，用来在多个“新增”里挑附件区那个。</summary>
        public string AttachArea { get; set; }
        /// <summary>业务类别 → 系统附件类型下拉里的选项（实机核对后填）。没填的按类别名包含匹配。</summary>
        public Dictionary<string, string> TypeOptions { get; set; }
        public string SaveButton { get; set; }
        /// <summary>判断表单是否已保存的隐藏栏（有值 = 已保存）。系统要求先保存表单才能传附件。</summary>
        public string SavedField { get; set; }
        /// <summary>只有两项都为 true 才会自动点“保存草稿”；默认都不点，由使用人自己保存。</summary>
        public bool AutoSave { get; set; }
        public bool SaveConfirmed { get; set; }
        /// <summary>建设进度默认只读，不填。</summary>
        public bool ProgressWritable { get; set; }
        /// <summary>档案里查不到时用的默认值（字段名 → 值），如“验收方式”现在一般是交竣工合并验收。</summary>
        public Dictionary<string, string> Defaults { get; set; }
        public List<Item> Fields { get; set; }

        public JwMapping()
        {
            Note = "交委以奖代补系统字段映射。默认按页面中文标签找栏位；实机核对后可给个别字段填 PageLabel（页面上的叫法）或 Selector（CSS 选择器）。AutoSave、SaveConfirmed 都为 true 才会自动点保存草稿；软件任何情况下都不点审核、上报、提交。";
            AddButton = "新增"; AttachArea = "附件"; SaveButton = "保存";
            TypeOptions = DefaultTypes();
            SavedField = "#id";
            Fields = new List<Item>();
            Defaults = new Dictionary<string, string> { { "验收方式", "交(竣)工合并验收信息" } };
        }

        /// <summary>业务类别 → 系统“文件类型”选项（按实际页面）。</summary>
        static Dictionary<string, string> DefaultTypes()
        {
            return new Dictionary<string, string> {
                { "施工许可（或备案表）", "施工许可" }, { "中标通知书", "中标通知书" }, { "质量监督通知书（或备案表）", "质量监督通知书" },
                { "施工合同", "施工合同或协议书" }, { "设计合同", "设计合同" }, { "监理合同", "监理合同" },
                { "交工验收资料", "交工验收证书" }, { "竣工验收资料", "竣工验收报告" }, { "验收会议纪要", "会议纪要" }, { "质量检测报告", "其它交竣工证明材料" } };
        }

        static string FilePath { get { return Path.Combine(JwPortal.Dir, "jw-mapping.json"); } }

        public static JwMapping Load()
        {
            JwMapping m = null;
            try { if (File.Exists(FilePath)) m = new JavaScriptSerializer().Deserialize<JwMapping>(File.ReadAllText(FilePath, Encoding.UTF8)); } catch { }
            bool fresh = m == null;
            if (m == null) m = new JwMapping();
            if (m.Fields == null) m.Fields = new List<Item>();
            if (m.TypeOptions == null) m.TypeOptions = new Dictionary<string, string>();
            foreach (var kv in DefaultTypes()) if (!m.TypeOptions.ContainsKey(kv.Key)) { m.TypeOptions[kv.Key] = kv.Value; fresh = true; }
            if (m.SavedField == null) { m.SavedField = "#id"; fresh = true; }
            if (m.Defaults == null) { m.Defaults = new Dictionary<string, string> { { "验收方式", "交(竣)工合并验收信息" } }; fresh = true; }
            // 补齐骨架：每个字段一行，方便实机核对后改
            foreach (var d in JwExtract.Defs.Where(x => x.Kind != "picker"))
                if (!m.Fields.Any(f => f.Module == d.Module && f.Group == d.Group && f.Label == d.Label))
                { m.Fields.Add(new Item { Module = d.Module, Group = d.Group, Label = d.Label }); fresh = true; }
            if (fresh) m.Save();
            return m;
        }

        public void Save()
        {
            var s = new JavaScriptSerializer().Serialize(this);
            File.WriteAllText(FilePath, Pretty(s), new UTF8Encoding(false));
        }

        public static string Path0 { get { return FilePath; } }

        public Item For(JwField f) { return Fields.FirstOrDefault(x => x.Module == f.Module && x.Group == f.Group && x.Label == f.Label); }

        /// <summary>简单缩进，方便人用记事本改。</summary>
        static string Pretty(string json)
        {
            var sb = new StringBuilder(); int ind = 0; bool q = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\')) q = !q;
                if (q) { sb.Append(c); continue; }
                if (c == '{' || c == '[') { sb.Append(c); sb.Append("\r\n"); sb.Append(' ', ++ind * 2); }
                else if (c == '}' || c == ']') { sb.Append("\r\n"); sb.Append(' ', --ind * 2); sb.Append(c); }
                else if (c == ',') { sb.Append(",\r\n"); sb.Append(' ', ind * 2); }
                else if (c == ':') sb.Append(": ");
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>表单还没保存，不能传附件。</summary>
    public class NotSavedException : IOException { public NotSavedException(string m) : base(m) { } }

    /// <summary>一个栏位的填写结果。</summary>
    public class JwFillResult
    {
        public string Group = "", Label = "", Value = "", Now = "", Old = "", Why = "";
        public bool Ok, Kept, Same;
        public List<string> Options = new List<string>();
        public override string ToString()
        {
            string g = Group + "·" + Label;
            if (Same) return "✔ " + g + "：页面已是“" + Now + "”";
            if (Ok) return "✔ " + g + "：" + (Old != "" ? "“" + Old + "” → " : "") + "“" + Now + "”" + (Why != "" ? "（" + Why + "）" : "");
            return (Kept ? "◆ " : "✘ ") + g + "：" + Why + (Options.Count > 0 ? "（页面可选：" + string.Join("、", Options.Where(o => o != "").Take(20)) + "）" : "");
        }
    }

    /// <summary>
    /// 用一个单独的 Edge（或 Chrome）窗口打开交委系统：独立的用户资料目录，不碰使用人自己的浏览器。
    /// 通过浏览器自带的调试接口（只在本机 127.0.0.1）填写当前页；账号密码由软件填，验证码由人输入、登录由人点。
    /// 只会点附件区的“新增”，以及映射里确认过的“保存草稿”；带审核、上报、提交等字样的按钮一律不点。证书提示交给人处理，不忽略证书错误。
    /// </summary>
    public class JwBrowser
    {
        public const string Forbidden = "审核|上报|提交|报送|送审|退回|删除|作废";
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static string ProfileDir { get { var d = Path.Combine(JwPortal.Dir, "jw-browser"); Directory.CreateDirectory(d); return d; } }

        /// <summary>找本机的 Edge，没有就找 Chrome（都是 Chromium 内核、会自动更新）。</summary>
        public static string FindBrowser()
        {
            var cands = new List<string>();
            foreach (var exe in new[] { "msedge.exe", "chrome.exe" })
            {
                foreach (var root in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
                    try
                    {
                        using (var k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                            if (k != null) { var v = Convert.ToString(k.GetValue("")); if (!string.IsNullOrEmpty(v)) cands.Add(v.Trim('"')); }
                    }
                    catch { }
                string sub = exe == "msedge.exe" ? @"Microsoft\Edge\Application\msedge.exe" : @"Google\Chrome\Application\chrome.exe";
                foreach (var sf in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.LocalApplicationData })
                    cands.Add(Path.Combine(Environment.GetFolderPath(sf), sub));
            }
            return cands.FirstOrDefault(File.Exists);
        }

        // ------------------------------------------------------------ 启动、连接
        static int ReadPort()
        {
            try
            {
                var f = Path.Combine(ProfileDir, "DevToolsActivePort");
                if (!File.Exists(f)) return 0;
                int p; return int.TryParse(File.ReadAllLines(f)[0].Trim(), out p) ? p : 0;
            }
            catch { return 0; }
        }

        static string Http(int port, string path, string method = "GET")
        {
            var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + path);
            req.Proxy = null; req.Timeout = 3000; req.Method = method;
            using (var r = req.GetResponse()) using (var sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
        }

        static bool Alive(int port) { if (port <= 0) return false; try { Http(port, "/json/version"); return true; } catch { return false; } }

        int port;

        /// <summary>确保专用浏览器开着，并有一个停在该网站的标签页（没有就打开 url）。</summary>
        public void Start(string url)
        {
            port = ReadPort();
            if (Alive(port))
            {
                string host = JwPortal.HostOf(url);
                if (!Pages().Any(p => JwPortal.HostOf(p.Url) == host))
                    using (var b = new Cdp(BrowserWs())) b.Call("Target.createTarget", new Dictionary<string, object> { { "url", url } });
                return;
            }
            string exe = FindBrowser();
            if (exe == null) throw new IOException("本机没找到 Edge 或 Chrome 浏览器。Windows 10/11 一般自带 Edge；如被卸载，请到微软官网装回 Edge 后再用。");
            try { File.Delete(Path.Combine(ProfileDir, "DevToolsActivePort")); } catch { }
            var psi = new ProcessStartInfo(exe, "--remote-debugging-port=0 --user-data-dir=\"" + ProfileDir + "\" --no-first-run --no-default-browser-check --disable-features=Translate --new-window \"" + url + "\"") { UseShellExecute = false };
            Process.Start(psi);
            var end = DateTime.Now.AddSeconds(20);
            while (DateTime.Now < end)
            {
                Thread.Sleep(300);
                port = ReadPort();
                if (Alive(port)) return;
            }
            throw new IOException("浏览器启动后连不上（20 秒内没准备好）。请关掉这个专用浏览器窗口再试一次。");
        }

        public bool Running { get { if (port <= 0) port = ReadPort(); return Alive(port); } }

        string BrowserWs()
        {
            var o = (Dictionary<string, object>)Js.DeserializeObject(Http(port, "/json/version"));
            return Convert.ToString(o["webSocketDebuggerUrl"]);
        }

        public class PageInfo { public string Id, Url, Title, Ws; }

        public List<PageInfo> Pages()
        {
            var res = new List<PageInfo>();
            var arr = (object[])Js.DeserializeObject(Http(port, "/json/list"));
            foreach (var x in arr.OfType<Dictionary<string, object>>())
            {
                if (Convert.ToString(x["type"]) != "page") continue;
                object ws; x.TryGetValue("webSocketDebuggerUrl", out ws);
                res.Add(new PageInfo { Id = Convert.ToString(x["id"]), Url = Convert.ToString(x["url"]), Title = Convert.ToString(x["title"]), Ws = Convert.ToString(ws) });
            }
            return res;
        }

        /// <summary>连上停在该网站的标签页（最近用过的在前）。</summary>
        Cdp Connect(string host, out PageInfo page)
        {
            if (!Running) throw new IOException("专用浏览器没开。请先点“打开交委系统并登录”。");
            var pages = Pages();
            page = pages.FirstOrDefault(p => JwPortal.HostOf(p.Url) == host && p.Ws != "");
            if (page == null)
                throw new IOException("专用浏览器里没有停在 " + host + " 的页面。" + (pages.Count > 0 ? "当前页面：" + pages[0].Url : "") + "（如果浏览器提示证书或“不安全”，请先自己确认这是交委系统再继续。）");
            var c = new Cdp(page.Ws);
            try { c.Call("Page.enable", null); } catch { }
            c.Eval(Helper);
            return c;
        }

        // ------------------------------------------------------------ 登录
        /// <summary>在登录页填用户名和密码，验证码框标橙色等人输入；登录按钮由人点。confirmHost：网址主机第一次用时问使用人。</summary>
        public string Login(JwPortal portal, JwAccount acc, Func<string, bool> confirmHost)
        {
            string host = JwPortal.HostOf(portal.Url);
            if (host == "") throw new IOException("交委系统网址不对：" + portal.Url);
            if (!portal.ConfirmedHosts.Contains(host))
            {
                if (confirmHost == null || !confirmHost(host)) return "已取消：没确认 " + host + " 是交委系统，没发送账号。";
                portal.ConfirmedHosts.Add(host); portal.Save();
            }
            Start(portal.Url);
            // 等登录页出来（证书提示要人处理，所以多等一会儿）
            var end = DateTime.Now.AddSeconds(90);
            string last = ""; bool navigated = false;
            while (DateTime.Now < end)
            {
                try
                {
                    PageInfo pg;
                    using (var c = Connect(host, out pg))
                    {
                        if (JwPortal.HostOf(pg.Url) != host) throw new IOException("页面主机变了，未发送账号。");
                        var r = (Dictionary<string, object>)Js.DeserializeObject(Convert.ToString(c.Eval("__jw.login(" + Js.Serialize(acc.User) + "," + Js.Serialize(acc.Password) + ")")));
                        if (Convert.ToBoolean(r["ok"]))
                        {
                            JwLog.Write(host, "", acc.User, "登录", "已填用户名和密码，等使用人输入验证码并点登录");
                            return "已在专用浏览器里填好账号 " + acc.User + (Convert.ToBoolean(r["captcha"]) ? "。请在橙色框里输入验证码，然后自己点“登录”。" : "。请自己点“登录”。");
                        }
                        last = Convert.ToString(r["why"]);
                        // 停在系统别的页面上：转到登录页再试一次；转过去还没有密码框，多半已经登录
                        if (!navigated && pg.Url.Split('?')[0] != portal.Url.Split('?')[0])
                        {
                            navigated = true;
                            c.Call("Page.navigate", new Dictionary<string, object> { { "url", portal.Url } });
                        }
                        else if (navigated && DateTime.Now > end.AddSeconds(-75)) return "登录页上没有密码框，可能已经登录了。请看浏览器。";
                    }
                }
                catch (Exception ex) { last = ex.Message; }
                Thread.Sleep(1500);
            }
            return "没能自动填账号：" + last + "。可以在浏览器里手工登录，登录后照样能用“填入当前页”。";
        }

        /// <summary>当前交委页面上有没有这段文字（用来提醒是不是打开了别的项目）。连不上返回 null。</summary>
        public bool? PageHasText(JwPortal portal, string text)
        {
            try { PageInfo pg; using (var c = Connect(JwPortal.HostOf(portal.Url), out pg)) return Convert.ToBoolean(c.Eval("__jw.hasText(" + Js.Serialize(text) + ")")); }
            catch { return null; }
        }

        // ------------------------------------------------------------ 填字段
        public List<JwFillResult> Fill(JwPortal portal, string projectName, string user, string module, List<JwField> fields, JwMapping map, bool overwrite)
        {
            if (module == "建设进度" && !map.ProgressWritable) throw new InvalidOperationException("建设进度默认只读，不自动填写。");
            string host = JwPortal.HostOf(portal.Url);
            var groups = JwExtract.Defs.Where(d => d.Module == module).Select(d => d.Group).Distinct().ToList();
            // 以前查过资料的项目也按默认值补（不改存档，只在这次填入时用）
            fields = fields.Select(x => new JwField { Module = x.Module, Group = x.Group, Label = x.Label, Unit = x.Unit, Value = x.Value, Edited = x.Edited }).ToList();
            JwExtract.ApplyDefaults(fields, map);
            JwExtract.Derive(fields);
            var items = new List<Dictionary<string, object>>();
            var res = new List<JwFillResult>();
            int hidden = 0;
            foreach (var f in fields.Where(x => x.Module == module))
            {
                var m = map.For(f);
                if (m != null && m.Skip) continue;
                var def = JwExtract.Defs.FirstOrDefault(d => d.Module == f.Module && d.Group == f.Group && d.Label == f.Label);
                if (f.Value == "") { res.Add(new JwFillResult { Group = f.Group, Label = f.Label, Why = def != null && def.Kind == "auto" ? "页面由选项目带出；档案里没查到，无法核对" : "档案里没查到，留空没填" }); continue; }
                if (def != null && def.Kind == "date" && System.Text.RegularExpressions.Regex.IsMatch(f.Value, @"^\d{4}-\d{2}$"))
                { res.Add(new JwFillResult { Group = f.Group, Label = f.Label, Why = "档案只写到月（" + f.Value + "），请在页面上自己选具体日期" }); continue; }
                string val = f.Value, alias;
                // 映射里配了“档案说法 → 页面选项”的，换成页面选项
                if (m != null && m.Values != null && m.Values.TryGetValue(val, out alias) && !string.IsNullOrEmpty(alias)) val = alias;
                items.Add(new Dictionary<string, object> { { "group", f.Group }, { "label", f.Label }, { "value", val }, { "pageLabel", m != null ? m.PageLabel : "" }, { "selector", m != null ? m.Selector : "" }, { "kind", def != null ? def.Kind : "" } });
            }
            PageInfo pg;
            using (var c = Connect(host, out pg))
            {
                var chk = (Dictionary<string, object>)Js.DeserializeObject(Convert.ToString(c.Eval("__jw.pageHas(" + Js.Serialize(groups) + ")")));
                var found = ((object[])chk["found"]).Select(Convert.ToString).ToList();
                if (found.Count == 0)
                    throw new IOException("当前页面上没看到“" + module + "”的分组（" + string.Join("、", groups) + "）。请先在浏览器里打开这个项目的“" + module + "”页面再点填入。" + (Convert.ToBoolean(chk["progress"]) ? "（建设进度页不自动填。）" : ""));
                // 先填第一组（招标类型、验收方式会切换页面上显示哪几组），等页面变完再填其余
                var first = items.Where(i => (string)i["group"] == groups[0]).ToList();
                var rest = items.Where(i => (string)i["group"] != groups[0]).ToList();
                var all = new List<object>();
                foreach (var part in new[] { first, rest })
                {
                    if (part.Count == 0) continue;
                    all.AddRange((object[])Js.DeserializeObject(Convert.ToString(c.Eval("__jw.fill(" + Js.Serialize(part) + "," + Js.Serialize(groups) + "," + (overwrite ? "true" : "false") + ")"))));
                    if (part == first) Thread.Sleep(800);
                }
                {
                    foreach (var o in all.OfType<Dictionary<string, object>>())
                    {
                        if (B(o, "skip")) { hidden++; continue; }
                        var r = new JwFillResult { Group = S(o, "group"), Label = S(o, "label"), Now = S(o, "now"), Old = S(o, "old"), Why = S(o, "why"), Ok = B(o, "ok"), Kept = B(o, "kept"), Same = B(o, "same") };
                        r.Value = items.First(i => (string)i["group"] == r.Group && (string)i["label"] == r.Label)["value"] as string;
                        object op; if (o.TryGetValue("options", out op) && op is object[]) r.Options = ((object[])op).Select(Convert.ToString).ToList();
                        if (B(o, "ro") && r.Ok) r.Why = "日期控件，已直接写入，请看一眼页面是否认";
                        if (B(o, "fuzzy") && r.Ok) r.Why = "要填“" + r.Value + "”，页面没有一模一样的，按最接近的选项选了，请看一眼";
                        if (B(o, "dflt") && r.Ok) r.Why = (r.Why != "" ? r.Why + "；" : "") + "原来是页面默认选项";
                        if (B(o, "check")) Check(r);
                        res.Add(r);
                    }
                }
            }
            if (hidden > 0) res.Add(new JwFillResult { Group = "提示", Label = "隐藏的组", Same = false, Kept = true, Why = hidden + " 项在页面上没显示的组里（如合并验收时的交工、竣工两组），没填" });
            JwLog.Write(host, projectName, user, module, "填入 " + res.Count(r => r.Ok) + " 项，未填 " + res.Count(r => !r.Ok) + " 项：" + string.Join("；", res.Select(r => r.ToString())));
            return res;
        }

        // ------------------------------------------------------------ 附件
        /// <summary>逐个附件：找空的上传框（没有就点附件区“新增”加一行），选文件、选附件类型。只选入页面，不点提交。</summary>
        public List<string> Attach(JwPortal portal, string projectName, string user, List<JwAttach> list, JwMapping map, Action<string> log)
        {
            string host = JwPortal.HostOf(portal.Url);
            var res = new List<string>();
            PageInfo pg;
            using (var c = Connect(host, out pg))
            {
                if (!string.IsNullOrEmpty(map.SavedField))
                {
                    var sv = c.Eval("__jw.savedVal(" + Js.Serialize(map.SavedField) + ")");
                    if (sv != null && Convert.ToString(sv) == "")
                        throw new NotSavedException("这一页还没保存过。系统要求先保存表单才能传附件：请先点页面上方橙色的“保存”，再点一次“填入当前页”（或“选入附件”）。");
                }
                foreach (var a in list)
                {
                    string name = Path.GetFileName(a.Pdf);
                    try
                    {
                        if (!File.Exists(a.Pdf)) throw new IOException("上传副本不在了，请重新“准备附件 PDF”");
                        if (new FileInfo(a.Pdf).Length > JwPdf.Limit) throw new IOException("超过 5MB，没选入");
                        int idx = Convert.ToInt32(c.Eval("__jw.pickFile()"));
                        if (idx < 0)
                        {
                            int before = Convert.ToInt32(c.Eval("__jw.files()"));
                            var add = (Dictionary<string, object>)Js.DeserializeObject(Convert.ToString(c.Eval("__jw.clickAdd(" + Js.Serialize(map.AddButton) + "," + Js.Serialize(map.AttachArea) + ")")));
                            if (!B(add, "ok")) throw new IOException(S(add, "why"));
                            var end = DateTime.Now.AddSeconds(6);
                            while (DateTime.Now < end && Convert.ToInt32(c.Eval("__jw.files()")) <= before) Thread.Sleep(250);
                            idx = Convert.ToInt32(c.Eval("__jw.pickFile()"));
                            if (idx < 0) throw new IOException("点了“" + map.AddButton + "”，但页面上没出现新的上传框（可能弹出了选择窗口，请看浏览器）");
                        }
                        string oid = c.EvalObject("__jw.target()");
                        c.Call("DOM.setFileInputFiles", new Dictionary<string, object> { { "files", new[] { a.Pdf } }, { "objectId", oid } });
                        string opt;
                        if (!map.TypeOptions.TryGetValue(a.Kind, out opt) || string.IsNullOrEmpty(opt)) opt = "";
                        string key = Regex.Replace(a.Kind, "（.*?）|\\(.*?\\)", "");
                        if (a.Kind.Contains("纪要") || a.Why.Contains("会议纪要")) key = "中标通知书";
                        var t = (Dictionary<string, object>)Js.DeserializeObject(Convert.ToString(c.Eval("__jw.setType(" + Js.Serialize(opt) + "," + Js.Serialize(key) + ")")));
                        c.Eval("__jw.done()");
                        a.Uploaded = DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " 已选入页面";
                        string msg = "✔ " + a.Kind + "：" + name + "（" + Mb(new FileInfo(a.Pdf).Length) + "）已选入；";
                        if (B(t, "ok")) msg += "类型选“" + S(t, "now") + "”";
                        else
                        {
                            object op; var opts = t.TryGetValue("options", out op) && op is object[] ? ((object[])op).Select(Convert.ToString).Where(x => x != "").ToList() : new List<string>();
                            msg += S(t, "why") + (opts.Count > 0 ? "，请手工选（可选：" + string.Join("、", opts.Take(20)) + "）" : "");
                        }
                        res.Add(msg);
                    }
                    catch (Exception ex) { res.Add("✘ " + a.Kind + "：" + name + "：" + ex.Message); }
                    if (log != null) log(res[res.Count - 1]);
                }
                if (map.AutoSave && map.SaveConfirmed && res.Any(r => r.StartsWith("✔")))
                {
                    var s = (Dictionary<string, object>)Js.DeserializeObject(Convert.ToString(c.Eval("__jw.clickText(" + Js.Serialize(map.SaveButton) + ")")));
                    res.Add(B(s, "ok") ? "已点“" + map.SaveButton + "”（保存草稿，没有提交）" : "没点保存：" + S(s, "why"));
                }
            }
            JwLog.Write(host, projectName, user, "附件", string.Join("；", res));
            return res;
        }

        /// <summary>选项目后系统自动带出的栏：不填，只把页面值和档案值对一下。</summary>
        static void Check(JwFillResult r)
        {
            double a, b;
            string page = r.Now.Trim();
            if (page == "") { r.Kept = true; r.Why = "页面还没带出（先点“+”选计划项目），档案是 " + r.Value; return; }
            bool same = double.TryParse(page, out a) && double.TryParse(r.Value, out b) ? Math.Abs(a - b) < 0.005 : page == r.Value;
            if (same) { r.Same = true; return; }
            r.Kept = true; r.Why = "页面带出的是 " + page + "，档案是 " + r.Value + "，不一致，请核对（软件不改这一栏）";
        }

        static string Mb(long n) { return (n / 1048576.0).ToString("0.00") + "MB"; }
        static string S(Dictionary<string, object> o, string k) { object v; return o.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : ""; }
        static bool B(Dictionary<string, object> o, string k) { object v; return o.TryGetValue(k, out v) && v is bool && (bool)v; }

        // ------------------------------------------------------------ 页面里的助手脚本
        /// <summary>注入到页面的脚本：按标签找栏位（含同源 iframe）、按分组区分同名栏、填值、找上传框、点“新增”。带审核/上报/提交字样的一律不点。</summary>
        public static readonly string Helper = @"(function(){
if(window.__jw&&window.__jw.v===4)return 'ok';
var J={v:4};
var BAD=/" + Forbidden + @"/;
var UNIT=/^[（(]?(万元|元|公里|km|千米|天|日历天|日)?[）)]?$/;
function norm(s){return (s||'').replace(/[\s　 ：:＊*]/g,'');}
function docs(){var out=[document],i=0;while(i<out.length){var d=out[i++],fr=d.querySelectorAll('iframe,frame');for(var k=0;k<fr.length;k++){try{var cd=fr[k].contentDocument;if(cd&&cd.documentElement&&out.indexOf(cd)<0)out.push(cd);}catch(e){}}}return out;}
function vis(el){if(!el)return false;try{var s=el.ownerDocument.defaultView.getComputedStyle(el);if(s.display==='none'||s.visibility==='hidden')return false;}catch(e){}return el.getClientRects().length>0;}
function isCtrl(el){var t=el.tagName;if(t==='SELECT'||t==='TEXTAREA')return true;if(t!=='INPUT')return false;return !/^(hidden|button|submit|reset|image|file)$/i.test(el.type||'');}
function okCtrl(el){return isCtrl(el)&&(el.tagName==='SELECT'||vis(el));}
function firstCtrl(el){var q=el.querySelectorAll('input,select,textarea');for(var i=0;i<q.length;i++)if(okCtrl(q[i]))return q[i];return null;}
function textOf(el){if(el.querySelector('input,select,textarea,table,iframe'))return null;var t=norm(el.textContent);return t.length>0&&t.length<=40?t:null;}
var TAGS='td,th,label,span,div,font,b,strong,p,dt,dd,li,legend,h1,h2,h3,h4,h5,h6,a,em,i';
function findText(d,pred){var all=d.querySelectorAll(TAGS),res=[];for(var i=0;i<all.length;i++){var t=textOf(all[i]);if(t&&pred(t)&&vis(all[i]))res.push(all[i]);}
 return res.filter(function(a){for(var j=0;j<res.length;j++)if(res[j]!==a&&a.contains(res[j]))return false;return true;});}
function before(a,b){return !!(a.compareDocumentPosition(b)&4);}
function labelMatch(t,L){return t===L||(t.indexOf(L)===0&&UNIT.test(t.substr(L.length)));}
function groupOf(el,heads){var best=null,bn='';for(var i=0;i<heads.length;i++){var h=heads[i];if(h.el.ownerDocument!==el.ownerDocument)continue;if(h.el!==el&&before(h.el,el)&&(!best||before(best,h.el))){best=h.el;bn=h.name;}}return bn;}
function ctrlFor(lab){
 if(lab.tagName==='LABEL'&&lab.htmlFor){var fid=lab.htmlFor,pn=lab.parentElement;for(var up0=0;up0<4&&pn;up0++){var cs0=pn.querySelectorAll('[id]');for(var q0=0;q0<cs0.length;q0++)if(cs0[q0].id===fid&&isCtrl(cs0[q0]))return cs0[q0];pn=pn.parentElement;}}
 var n=lab;
 for(var up=0;up<4&&n&&n.tagName!=='BODY'&&n.tagName!=='TR'&&n.tagName!=='FORM';up++){
  var s=n.nextElementSibling,stop=false;
  while(s){if(okCtrl(s))return s;var q=firstCtrl(s);if(q)return q;var t=norm(s.textContent);if(t.length>0&&!UNIT.test(t)){stop=true;break;}s=s.nextElementSibling;}
  if(stop)break;
  n=n.parentElement;
 }
 return null;
}
function fire(el,names){var w=el.ownerDocument.defaultView;names.forEach(function(n){var e;try{e=new w.Event(n,{bubbles:true});}catch(x){e=el.ownerDocument.createEvent('HTMLEvents');e.initEvent(n,true,true);}el.dispatchEvent(e);});}
function mark(el,c){try{el.style.outline='2px solid '+(c||'#2e7d32');el.style.outlineOffset='1px';}catch(e){}}
function setRaw(el,v){var w=el.ownerDocument.defaultView,proto=el.tagName==='TEXTAREA'?w.HTMLTextAreaElement.prototype:w.HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,v);fire(el,['input','change','keyup','blur']);}
function radioText(r){var d=r.ownerDocument;if(r.id){var l=d.querySelector('label[for=""'+r.id+'""]');if(l)return l.textContent.trim();}if(r.parentElement&&r.parentElement.tagName==='LABEL')return r.parentElement.textContent.trim();var s=r.nextSibling;while(s&&!(s.textContent||'').trim())s=s.nextSibling;return s?(s.textContent||'').trim():(r.value||'');}
function emptyish(t){return /^(请选择|全部|无|-+|)$/.test(norm(t));}
function loose(s){return norm(s).replace(/[（）()【】\[\]、，,。.\-—_]/g,'').replace(/信息$/,'');}
function setVal(el,v,over,kind){
 var w=el.ownerDocument.defaultView;
 if(kind==='auto'){var cur=el.tagName==='SELECT'?(el.selectedIndex>=0?el.options[el.selectedIndex].text.trim():''):(el.value||'');return {ok:false,check:true,now:cur};}
 if(el.disabled)return {ok:false,why:'这一栏是灰的，不能编辑',now:el.value||''};
 if(el.tagName==='SELECT'){
  var opts=[],idx=-1,near=[],lv=loose(v);
  for(var i=0;i<el.options.length;i++){var t=el.options[i].text.trim(),lt=loose(t);opts.push(t);if(norm(t)===norm(v))idx=i;else if(lt.length>=2&&lv.length>=2&&(lt.indexOf(lv)>=0||lv.indexOf(lt)>=0))near.push(i);}
  var fuzzy=false;if(idx<0&&near.length===1){idx=near[0];fuzzy=true;}
  var si=el.selectedIndex,old=si>=0?el.options[si].text.trim():'';
  if(idx<0)return {ok:false,why:'下拉里没有“'+v+'”这个选项，没选',options:opts,old:old};
  if(si===idx)return {ok:true,now:old,same:true};
  var dflt=emptyish(old)||si<=0;   // 第一项是页面默认，不算人选过
  if(!dflt&&!over)return {ok:false,kept:true,why:'页面已选“'+old+'”，没覆盖',old:old};
  el.selectedIndex=idx;fire(el,['input','change']);try{if(w.layui&&w.layui.form)w.layui.form.render('select');}catch(x){}
  mark(el);return {ok:true,now:el.options[idx].text.trim(),old:emptyish(old)?'':old,fuzzy:fuzzy,dflt:dflt&&!emptyish(old)};
 }
 var ty=(el.type||'').toLowerCase();
 if(ty==='radio'||ty==='checkbox'){
  var rs=(el.form||el.ownerDocument).querySelectorAll('input[type=""'+ty+'""]'),names=[];
  for(var i2=0;i2<rs.length;i2++){var r=rs[i2];if(r.name!==el.name)continue;var t2=radioText(r);names.push(t2);if(norm(t2)===norm(v)){if(r.checked)return {ok:true,now:t2,same:true};r.click();mark(r.parentElement||r);return {ok:true,now:t2};}}
  return {ok:false,why:'单选里没有“'+v+'”，没选',options:names};
 }
 var old2=el.value||'';
 if(old2===v)return {ok:true,now:v,same:true};
 if(el.readOnly&&kind!=='date')return {ok:false,why:'这一栏是只读的（一般由点“+”选项目带出），没填',now:old2};
 if(old2!==''&&!over)return {ok:false,kept:true,why:'页面已有“'+old2+'”，没覆盖',old:old2};
 var ro=el.readOnly;setRaw(el,v);mark(el);
 return {ok:true,now:el.value,old:old2,ro:ro};
}
function heads(groups){var hs=[];docs().forEach(function(d){groups.forEach(function(g){findText(d,function(t){var G=norm(g);return t===G||(t.indexOf(G)===0&&/^[（(]/.test(t.substr(G.length)));}).forEach(function(el){hs.push({el:el,name:g});});});});return hs;}
J.pageHas=function(groups){var hs=heads(groups),f=[];hs.forEach(function(h){if(f.indexOf(h.name)<0)f.push(h.name);});
 var prog=false;docs().forEach(function(d){if(findText(d,function(t){return t==='建设进度'||t==='进度信息';}).length)prog=true;});
 return JSON.stringify({found:f,progress:prog});};
J.fill=function(items,groups,over){
 var ds=docs(),hs=heads(groups),fg={},out=[];hs.forEach(function(h){fg[h.name]=1;});
 items.forEach(function(it){
  var r={group:it.group,label:it.label};
  try{
   var el=null;
   if(it.selector){ds.some(function(d){el=d.querySelector(it.selector);return !!el;});if(!el){r.ok=false;r.why='映射里配的选择器在页面上找不到';}}
   else{
    var L=norm(it.pageLabel||it.label),cs=[];
    ds.forEach(function(d){cs=cs.concat(findText(d,function(t){return labelMatch(t,L);}));});
    cs=cs.filter(function(c){return !!ctrlFor(c);});
    if(!fg[it.group]){r.ok=false;r.skip=true;r.why='页面上没显示“'+it.group+'”这一组，跳过';out.push(r);return;}
    if(cs.length>0){var g=cs.filter(function(c){return groupOf(c,hs)===it.group;});cs=g;}
    if(cs.length===0){r.ok=false;r.why='页面上没找到“'+it.label+'”这一栏'+(fg[it.group]?'':'（也没看到“'+it.group+'”分组）');}
    else if(cs.length>1){r.ok=false;r.why='页面上有 '+cs.length+' 个“'+it.label+'”，分不清是哪一组的，没填（可在映射里配置）';}
    else el=ctrlFor(cs[0]);
   }
   if(el){var s=setVal(el,it.value,over,it.kind);for(var k in s)r[k]=s[k];}
  }catch(e){r.ok=false;r.why='出错：'+e.message;}
  out.push(r);
 });
 return JSON.stringify(out);
};
J.login=function(u,p){
 var ds=docs(),pw=null,d0=null;
 ds.some(function(d){var q=d.querySelectorAll('input[type=password]');for(var i=0;i<q.length;i++)if(vis(q[i])){pw=q[i];d0=d;return true;}return false;});
 if(!pw)return JSON.stringify({ok:false,why:'当前页面没有密码框（可能还没打开登录页，或已经登录）'});
 var ins=[].slice.call(d0.querySelectorAll('input')).filter(function(x){var t=(x.type||'text').toLowerCase();return /^(text|tel|email|number|)$/.test(t)&&vis(x);});
 var cap=null,user=null,i;
 for(i=0;i<ins.length;i++){var key=(ins[i].name+' '+ins[i].id+' '+(ins[i].placeholder||'')+' '+(ins[i].className||'')).toLowerCase();if(/yzm|captcha|verif|checkcode|validat|randcode|imgcode|vcode|验证码|校验码/.test(key)){cap=ins[i];break;}}
 for(i=0;i<ins.length;i++)if(ins[i]!==cap&&before(ins[i],pw))user=ins[i];
 if(!user)return JSON.stringify({ok:false,why:'没认出用户名输入框'});
 if(!cap)for(i=0;i<ins.length;i++)if(ins[i]!==user&&before(pw,ins[i])){cap=ins[i];break;}
 setRaw(user,u);setRaw(pw,p);
 if(cap){mark(cap,'#e65100');try{cap.focus();}catch(e){}}
 return JSON.stringify({ok:true,captcha:!!cap});
};
function fileInputs(){var a=[];docs().forEach(function(d){a=a.concat([].slice.call(d.querySelectorAll('input[type=file]')));});return a;}
J.files=function(){return fileInputs().length;};
J.pickFile=function(){var a=fileInputs(),pick=-1;a.forEach(function(x){x.removeAttribute('data-jw-target');});
 for(var i=0;i<a.length;i++){var x=a[i];if(x.getAttribute('data-jw-used')||(x.files&&x.files.length)||x.disabled)continue;var row=x.closest('tr')||x.parentElement;if(row&&!vis(row)&&!vis(x))continue;pick=i;}
 if(pick>=0)a[pick].setAttribute('data-jw-target','1');return pick;};
J.target=function(){var a=fileInputs();for(var i=0;i<a.length;i++)if(a[i].getAttribute('data-jw-target'))return a[i];return null;};
function clickables(d){return [].slice.call(d.querySelectorAll('button,a,input[type=button],input[type=submit],span,div,i,li')).filter(function(e){if(!vis(e))return false;var t=e.tagName==='INPUT'?e.value:e.textContent;t=norm(t);return t.length>0&&t.length<=12;});}
function textOfBtn(e){return norm(e.tagName==='INPUT'?e.value:e.textContent);}
J.clickAdd=function(text,area){
 var c=[];docs().forEach(function(d){clickables(d).forEach(function(e){if(textOfBtn(e)===norm(text))c.push(e);});});
 c=c.filter(function(a){for(var j=0;j<c.length;j++)if(c[j]!==a&&a.contains(c[j]))return false;return true;});
 if(!c.length)return JSON.stringify({ok:false,why:'页面上没找到“'+text+'”按钮'});
 var best=null,bd=99;c.forEach(function(e){var n=e,dpt=0;while(n&&dpt<10){if(norm(n.textContent).indexOf(norm(area))>=0){if(dpt<bd){bd=dpt;best=e;}break;}n=n.parentElement;dpt++;}});
 if(!best){if(c.length>1)return JSON.stringify({ok:false,why:'页面上有 '+c.length+' 个“'+text+'”按钮，分不清哪个是附件区的'});best=c[0];}
 if(BAD.test(textOfBtn(best)))return JSON.stringify({ok:false,why:'按钮带审核/上报/提交字样，不点'});
 setTimeout(function(){best.click();},0);return JSON.stringify({ok:true});
};
J.setType=function(opt,key){
 var x=J.target();if(!x)return JSON.stringify({ok:false,why:'找不到刚才的上传框'});
 var n=x,sel=null;for(var i=0;i<6&&n&&!sel;i++){n=n.parentElement;if(n){var s=n.querySelectorAll('select');for(var k=0;k<s.length;k++){sel=s[k];break;}}}
 if(!sel)return JSON.stringify({ok:true,now:'（这一行没有类型下拉）'});
 var opts=[],idx=-1;for(var i2=0;i2<sel.options.length;i2++){var t=sel.options[i2].text.trim();opts.push(t);if(opt&&norm(t)===norm(opt))idx=i2;}
 if(idx<0&&!opt){var hit=[];for(var j=0;j<opts.length;j++)if(key&&norm(opts[j]).indexOf(norm(key))>=0)hit.push(j);if(hit.length===1)idx=hit[0];}
 if(idx<0)return JSON.stringify({ok:false,why:'附件类型没自动选',options:opts});
 sel.selectedIndex=idx;fire(sel,['input','change']);try{var w=sel.ownerDocument.defaultView;if(w.layui&&w.layui.form)w.layui.form.render('select');}catch(e){}
 mark(sel);return JSON.stringify({ok:true,now:opts[idx]});
};
J.done=function(){var x=J.target();if(x){x.setAttribute('data-jw-used','1');x.removeAttribute('data-jw-target');mark(x.parentElement||x);}return 1;};
J.clickText=function(text){
 if(BAD.test(norm(text)))return JSON.stringify({ok:false,why:'带审核/上报/提交字样，不点'});
 var c=[];docs().forEach(function(d){clickables(d).forEach(function(e){if(textOfBtn(e)===norm(text))c.push(e);});});
 c=c.filter(function(a){for(var j=0;j<c.length;j++)if(c[j]!==a&&a.contains(c[j]))return false;return true;});
 if(c.length!==1)return JSON.stringify({ok:false,why:'页面上“'+text+'”按钮有 '+c.length+' 个，没点'});
 setTimeout(function(){c[0].click();},0);return JSON.stringify({ok:true});
};
J.savedVal=function(sel){var v=null;docs().some(function(d){var e=d.querySelector(sel);if(e){v=e.value||'';return true;}return false;});return v;};
J.hasText=function(s){var n=norm(s);return docs().some(function(d){try{var t=d.body?d.body.innerText:'';[].forEach.call(d.querySelectorAll('input'),function(x){if(x.type!=='password')t+=' '+(x.value||'');});return norm(t).indexOf(n)>=0;}catch(e){return false;}});};
window.__jw=J;return 'ok';
})()";
    }

    /// <summary>浏览器调试接口的最小客户端（只连本机 127.0.0.1）。</summary>
    public class Cdp : IDisposable
    {
        readonly ClientWebSocket ws = new ClientWebSocket();
        readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        readonly Dictionary<int, Dictionary<string, object>> got = new Dictionary<int, Dictionary<string, object>>();
        readonly object gate = new object(), sendGate = new object();
        int seq; bool closed;
        volatile string dialog;

        public Cdp(string url)
        {
            if (!ws.ConnectAsync(new Uri(url), CancellationToken.None).Wait(10000)) throw new IOException("连不上浏览器");
            new Thread(Loop) { IsBackground = true }.Start();
        }

        void Loop()
        {
            var buf = new byte[1 << 16];
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var ms = new MemoryStream(); WebSocketReceiveResult r;
                    do
                    {
                        r = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result;
                        if (r.MessageType == WebSocketMessageType.Close) return;
                        ms.Write(buf, 0, r.Count);
                    } while (!r.EndOfMessage);
                    var o = (Dictionary<string, object>)js.DeserializeObject(Encoding.UTF8.GetString(ms.ToArray()));
                    object id, m;
                    if (o.TryGetValue("id", out id)) lock (gate) { got[Convert.ToInt32(id)] = o; Monitor.PulseAll(gate); }
                    else if (o.TryGetValue("method", out m))
                    {
                        if ((string)m == "Page.javascriptDialogOpening")
                        {
                            var p = o["params"] as Dictionary<string, object>; object msg = null;
                            if (p != null) p.TryGetValue("message", out msg);
                            dialog = Convert.ToString(msg); lock (gate) Monitor.PulseAll(gate);
                        }
                        else if ((string)m == "Page.javascriptDialogClosed") dialog = null;
                    }
                }
            }
            catch { }
            finally { lock (gate) { closed = true; Monitor.PulseAll(gate); } }
        }

        public Dictionary<string, object> Call(string method, Dictionary<string, object> prm, int timeoutMs = 20000)
        {
            int id = Interlocked.Increment(ref seq);
            var msg = js.Serialize(new Dictionary<string, object> { { "id", id }, { "method", method }, { "params", prm ?? new Dictionary<string, object>() } });
            lock (sendGate) ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)), WebSocketMessageType.Text, true, CancellationToken.None).Wait(10000);
            var end = DateTime.Now.AddMilliseconds(timeoutMs);
            lock (gate)
            {
                while (!got.ContainsKey(id))
                {
                    if (closed) throw new IOException("和浏览器的连接断了（窗口被关了？）");
                    if (dialog != null) throw new IOException("页面弹出了提示框：“" + dialog + "”，请在浏览器里处理后再试");
                    var left = end - DateTime.Now;
                    if (left <= TimeSpan.Zero) throw new TimeoutException("浏览器没响应（" + method + "）");
                    Monitor.Wait(gate, left);
                }
                var r = got[id]; got.Remove(id);
                object err;
                if (r.TryGetValue("error", out err)) { var e = err as Dictionary<string, object>; throw new IOException("浏览器报错：" + (e != null ? Convert.ToString(e["message"]) : "")); }
                object res; r.TryGetValue("result", out res);
                return res as Dictionary<string, object> ?? new Dictionary<string, object>();
            }
        }

        Dictionary<string, object> Run(string expr, bool byValue)
        {
            var r = Call("Runtime.evaluate", new Dictionary<string, object> { { "expression", expr }, { "returnByValue", byValue }, { "awaitPromise", true } });
            object ex;
            if (r.TryGetValue("exceptionDetails", out ex))
            {
                var d = ex as Dictionary<string, object>; object e2 = null, desc = null;
                if (d != null && d.TryGetValue("exception", out e2) && e2 is Dictionary<string, object>) ((Dictionary<string, object>)e2).TryGetValue("description", out desc);
                throw new IOException("页面脚本出错：" + Convert.ToString(desc ?? (d != null && d.ContainsKey("text") ? d["text"] : "")));
            }
            return r["result"] as Dictionary<string, object>;
        }

        public object Eval(string expr) { object v; var r = Run(expr, true); return r != null && r.TryGetValue("value", out v) ? v : null; }

        public string EvalObject(string expr)
        {
            object v; var r = Run(expr, false);
            if (r == null || !r.TryGetValue("objectId", out v)) throw new IOException("页面上没找到要操作的元素");
            return Convert.ToString(v);
        }

        public void Dispose()
        {
            try { ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait(1000); } catch { }
            try { ws.Dispose(); } catch { }
        }
    }
}
