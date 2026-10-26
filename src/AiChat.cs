using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DangAn
{
    /// <summary>
    /// AI 助手：用用户设置的 AI 接口（DeepSeek 等）对话，AI 可调用工具查档案、读 Word/Excel/PDF、
    /// 填写或修改 Excel、生成或修改 Word。生成的文件都是新文件，原文件不动。
    /// </summary>
    public class AiChat
    {
        readonly Store st;
        readonly List<object> messages = new List<object>();
        public readonly List<string> Outputs = new List<string>();
        public Action<string> OnTool;          // 工具执行时的提示（界面显示成灰色小字）
        public Action<string> OnOutput;        // 生成了新文件
        /// <summary>AI 提交的档案修改（合同台账、关键信息、收支），等用户确认后才写入。</summary>
        public readonly List<PendingChange> Pending = new List<PendingChange>();
        public const int MaxRounds = 30, ToolResultMax = 12000, OldToolMax = 1200;
        readonly List<string> attached = new List<string>();   // 本次对话收到过的所有附件

        public static string OutDir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "项目档案室 AI 输出", DateTime.Now.ToString("yyyy-MM-dd"));
                Directory.CreateDirectory(d);
                return d;
            }
        }

        public AiChat(Store st) { this.st = st; Reset(); }

        public void Reset()
        {
            messages.Clear();
            messages.Add(Msg("system", SystemPrompt()));
        }

        string SystemPrompt()
        {
            return "你是“项目档案室”软件里的 AI 助手，服务对象是乡镇（街道）负责农村公路等工程项目的工作人员。今天是 " + DateTime.Now.ToString("yyyy-MM-dd") + "。\n"
                + "档案库里有 " + st.RealProjects.Count() + " 个项目、" + st.Lib.Files.Count + " 份资料。你可以调用工具查档案、读文件、生成或修改 Word 和 Excel。\n"
                + "规则：\n"
                + "1. 档案里的事实（金额、日期、单位、文号）必须用工具查到原文再回答，不要凭记忆或推测；查不到就说查不到。\n"
                + "2. 项目登记信息、合同台账是手工登记的，可能有误；与合同、通知书等原文冲突时以原文为准，金额以大写为准，并提醒用户。\n"
                + "3. 用户上传的附件用 read_file 读取。要用档案把上级发来的 Excel 表填好，用 fill_excel_from_archive；按用户要求改 Excel 的个别格子，用 write_excel。\n"
                + "   fill_excel_from_archive 填好的结果就是最终结果，不要再用 write_excel 往这张表追加或改写，除非用户明确提出要改哪几格。\n"
                + "   用户要把某个项目的资料整理好给上级查阅、报送时，用 export_project_files 导出按分类的资料包。\n"
                + "4. 要写材料（情况说明、报告、函、通知等），用 create_word 生成 Word；要列清单、汇总，用 create_excel 生成 Excel；修改用户的 Word，用 edit_word。\n"
                + "5. 生成、修改的都是新文件，原文件不动。完成后告诉用户文件名和主要内容。\n"
                + "6. 只有发票不等于已付款：说“已支付”必须有记账凭证、国库支付凭证、银行回单等付款依据，否则只能说“已开票”或“已报账申请”。\n"
                + "7. 附件路径照抄“附件N：”后面的完整路径，也可以直接写“附件1”。\n"
                + "8. 汇总很多项目时，先用 list_projects 里的台账数据，表里加一列“数据来源”（如“据合同台账”“据合同原文”），台账与登记信息不一致的标出来；只对有疑问的再去查原文，不要每个项目都逐一翻原文。\n"
                + "9. 所有数字计算（元换万元、比例、合计、差额、核对）都用 calculate 工具算，不要心算。\n"
                + "10. 用户要你把信息登记、补全、更正到项目的关键信息、合同台账、财务收支时：先查原文核实，再用 set_project_info / upsert_contract / add_payment / remove_record 提交。"
                + "这些修改不会马上生效，会列在对话下方等用户点“确认写入”；每条都写明依据（文件名和原文摘录）。只有发票、没有付款凭证的，不登记为已付款。提交后告诉用户请在下方核对确认。\n"
                + "11. 回答用简洁的中文；少用 Markdown 表格和符号，需要表格时生成 Excel。";
        }

        static Dictionary<string, object> Msg(string role, string content) { return new Dictionary<string, object> { { "role", role }, { "content", content } }; }

        /// <summary>发一条用户消息，返回 AI 的最终回答（中间自动执行工具）。</summary>
        public string Send(AiConfig cfg, string text, IList<string> attachments)
        {
            // 以前几轮查到的资料只留开头，免得每轮都把整段原文再发一遍（省 token）
            foreach (var m in messages.OfType<Dictionary<string, object>>())
                if (Convert.ToString(m["role"]) == "tool")
                {
                    string c = Convert.ToString(m["content"]);
                    if (c.Length > OldToolMax) m["content"] = c.Substring(0, OldToolMax) + "\n……（早先查到的内容已省略，需要时再查）";
                }
            var sb = new StringBuilder(text);
            if (attachments != null && attachments.Count > 0)
            {
                sb.Append("\n\n【本条消息的附件】");
                foreach (var a in attachments)
                {
                    if (!attached.Contains(a)) attached.Add(a);
                    sb.Append("\n附件").Append(attached.IndexOf(a) + 1).Append("：").Append(a);
                }
            }
            messages.Add(Msg("user", sb.ToString()));
            for (int round = 0; round < MaxRounds; round++)
            {
                if (round == MaxRounds - 6)
                    messages.Add(Msg("system", "提示：处理步骤快用完了。请不要再查新资料，用已经查到的内容马上完成任务（该生成的文件现在生成），然后回答。"));
                var reply = AiClient.Complete(cfg, messages, Tools, 6000);
                object tc;
                var calls = reply.TryGetValue("tool_calls", out tc) ? tc as object[] : null;
                var keep = new Dictionary<string, object> { { "role", "assistant" }, { "content", reply.ContainsKey("content") && reply["content"] != null ? reply["content"] : "" } };
                if (calls != null && calls.Length > 0) keep["tool_calls"] = calls;
                messages.Add(keep);
                if (calls == null || calls.Length == 0) return Convert.ToString(keep["content"]);
                foreach (Dictionary<string, object> call in calls)
                {
                    var fn = (Dictionary<string, object>)call["function"];
                    string name = Convert.ToString(fn["name"]), args = Convert.ToString(fn.ContainsKey("arguments") ? fn["arguments"] : "{}");
                    string result;
                    try { result = Run(cfg, name, string.IsNullOrWhiteSpace(args) ? new Dictionary<string, object>() : AiClient.ParseJson(args)); }
                    catch (Exception ex)
                    {
                        var e = ex; while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
                        result = "出错：" + (e is AiException ? e.Message : e.Message);
                    }
                    if (result.Length > ToolResultMax) result = result.Substring(0, ToolResultMax) + "\n……（内容太长，已截断）";
                    messages.Add(new Dictionary<string, object> { { "role", "tool" }, { "tool_call_id", call["id"] }, { "content", result } });
                }
            }
            return "（处理步骤太多，已停止。可以把任务拆小一点再试。）";
        }

        // ================== 工具定义
        static Dictionary<string, object> Fn(string name, string desc, Dictionary<string, object> props, params string[] required)
        {
            return new Dictionary<string, object> {
                { "type", "function" },
                { "function", new Dictionary<string, object> {
                    { "name", name }, { "description", desc },
                    { "parameters", new Dictionary<string, object> { { "type", "object" }, { "properties", props }, { "required", required } } } } } };
        }
        static Dictionary<string, object> P(string type, string desc) { return new Dictionary<string, object> { { "type", type }, { "description", desc } }; }
        static Dictionary<string, object> Arr(string desc, Dictionary<string, object> item) { return new Dictionary<string, object> { { "type", "array" }, { "description", desc }, { "items", item } }; }
        static Dictionary<string, object> Obj(Dictionary<string, object> props) { return new Dictionary<string, object> { { "type", "object" }, { "properties", props } }; }
        static Dictionary<string, object> Props(params object[] kv) { var d = new Dictionary<string, object>(); for (int i = 0; i < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1]; return d; }

        static readonly object[] Tools =
        {
            Fn("calculate", "精确计算算式（加减乘除、括号、百分号），如 1691000*22.5% 或 (380700-380475)/10000。所有数字计算都用它，不要心算。", Props("expression", P("string", "算式"))),
            Fn("list_projects", "列出档案库里的全部项目（编号、名称、类型、办理进展、合同数、资料数）。", Props()),
            Fn("project_detail", "查看一个项目的登记信息、合同台账、收支明细、发票、文件清单。", Props("project_id", P("string", "项目编号，如 P013")), "project_id"),
            Fn("search_archive", "在档案全文里搜索（文件名和识别出的文字），返回命中的文件路径和原文片段。多个词用空格分开表示同时包含。", Props("query", P("string", "搜索词"), "project_id", P("string", "只在某个项目里搜（可选）")), "query"),
            Fn("read_file", "读取文件文字：档案里的文件用相对路径（search_archive 返回的），附件用完整路径。Excel 按“行号｜列=内容”列出，便于定位单元格。", Props("path", P("string", "文件路径"), "max_chars", P("integer", "最多读多少字，默认 12000")), "path"),
            Fn("write_excel", "把若干单元格的值写进一张 Excel 表的副本（原表不动；改过的格子标黄）。默认只写空白格，已有内容的格子不改；用户明确要求修改某格时才设 overwrite=true。", Props(
                "path", P("string", "原表完整路径"),
                "overwrite", P("boolean", "是否允许改已有内容的格子（只有用户明确要求改时才设 true）"),
                "cells", Arr("要写的格子", Obj(Props("sheet", P("string", "工作表名，可省略表示第一张"), "cell", P("string", "单元格，如 M51"), "value", P("string", "要写的内容"), "note", P("string", "依据或说明，写进批注（可选）")))),
                "output_name", P("string", "新文件名（可选）")), "path", "cells"),
            Fn("fill_excel_from_archive", "用档案自动填写一张上级发来的 Excel 表：自动认出表里哪几行是本单位的项目，查档案只填空白格，找不到依据的留空。生成新文件。", Props("path", P("string", "表格完整路径")), "path"),
            Fn("create_word", "生成一份 Word 文档（.docx）。content 用简单标记：“# ”大标题，“## ”“### ”小标题，“- ”列表，“|a|b|”表格行，**粗体**，其余每行一段。", Props(
                "file_name", P("string", "文件名，如 2025年安全生产工程情况说明"), "content", P("string", "正文")), "file_name", "content"),
            Fn("create_excel", "生成一张 Excel 表（.xlsx）。", Props(
                "file_name", P("string", "文件名"), "sheet_name", P("string", "工作表名"),
                "headers", Arr("表头", P("string", "")), "rows", Arr("数据行，每行是字符串数组", Arr("", P("string", "")))), "file_name", "headers", "rows"),
            Fn("export_project_files", "导出一个项目的资料包给上级查阅：按分类建文件夹（01 立项及计划、04 合同协议……）复制资料，附资料目录 Excel 和项目基本情况 Word，可打 zip。", Props(
                "project_id", P("string", "项目编号"),
                "categories", Arr("只导出这些分类（可省略=全部），用分类名或关键字，如 合同、资金支付、验收", P("string", "")),
                "zip", P("boolean", "是否打包 zip，默认是")), "project_id"),
            Fn("set_project_info", "提交对项目关键信息（登记栏目）的补充或更正，等用户确认后写入。栏目名用 project_detail 里列出的名称。", Props(
                "project_id", P("string", "项目编号"),
                "fields", Arr("要改的栏目", Obj(Props("name", P("string", "栏目名，如 施工合同／成交金额"), "value", P("string", "新内容"), "source", P("string", "依据：文件名 + 原文摘录"))))), "project_id", "fields"),
            Fn("upsert_contract", "提交合同台账的新增或修改（按合同类型对上已有的一条就修改，否则新增），等用户确认后写入。金额单位元。", Props(
                "project_id", P("string", "项目编号"), "kind", P("string", "合同类型，如 施工合同"), "match_kind", P("string", "要修改的已有合同类型（与 kind 不同时填，可选）"),
                "party", P("string", "合同相对方全称"), "amount", P("number", "合同金额（元）"), "date", P("string", "签订日期"),
                "paid", P("number", "已付（元，有付款凭证才填）"), "unpaid", P("number", "未付（元）"), "final", P("string", "审定/决算金额或说明"),
                "content", P("string", "合同内容"), "note", P("string", "备注"), "source", P("string", "合同原件路径（search_archive 返回的）")), "project_id", "kind"),
            Fn("add_payment", "提交一笔收支记录（需有付款凭证、回单或拨款到账依据），等用户确认后写入。", Props(
                "project_id", P("string", "项目编号"), "date", P("string", "日期"), "dir", P("string", "支出 或 收入"), "amount", P("number", "金额（元）"),
                "summary", P("string", "摘要"), "party", P("string", "对方单位"), "contract", P("string", "对应合同类型"), "voucher_no", P("string", "凭证号"),
                "voucher_file", P("string", "凭证文件路径"), "invoice_file", P("string", "发票文件路径"), "note", P("string", "备注/依据")), "project_id", "dir", "amount"),
            Fn("remove_record", "提交删除一条错误或重复的合同 / 收支记录，等用户确认。", Props(
                "project_id", P("string", "项目编号"), "type", P("string", "contract 或 payment"),
                "kind", P("string", "合同类型（删合同时）"), "date", P("string", "日期（删收支时）"), "amount", P("number", "金额（删收支时）"), "reason", P("string", "为什么删")), "project_id", "type", "reason"),
            Fn("edit_word", "修改一份 Word（.doc/.docx）的副本：把原文里的文字替换成新文字（原文件不动）。", Props(
                "path", P("string", "原文件完整路径"),
                "replacements", Arr("替换", Obj(Props("find", P("string", "原文里要找的文字（要和原文完全一致）"), "replace", P("string", "换成的文字")))),
                "output_name", P("string", "新文件名（可选）")), "path", "replacements"),
        };

        // ================== 工具执行
        string Run(AiConfig cfg, string name, Dictionary<string, object> a)
        {
            switch (name)
            {
                case "calculate":
                    {
                        string ex = S(a, "expression");
                        string e2 = Regex.Replace(ex.Replace("×", "*").Replace("÷", "/").Replace("，", "").Replace(",", "").Replace("（", "(").Replace("）", ")"), @"(\d+(\.\d+)?)%", "($1/100)");
                        if (!Regex.IsMatch(e2, @"^[\d\s\.\+\-\*/\(\)]+$")) return "算式里只能有数字和 + - * / ( ) %：" + ex;
                        Tool("计算：" + ex);
                        var v = Convert.ToDecimal(new System.Data.DataTable().Compute(Regex.Replace(e2, @"(\d+(\.\d+)?)", "$1"), ""));
                        return ex + " = " + Math.Round(v, 6).ToString("0.######", CultureInfo.InvariantCulture);
                    }
                case "list_projects":
                    Tool("查看项目清单");
                    return "编号｜名称｜类型｜进展｜资料数｜登记的施工单位｜合同台账（类型/相对方/金额元；手工登记，可能有误）\n" + string.Join("\n", st.RealProjects.Select(p =>
                    {
                        var i = st.Info(p);
                        return p.Id + "｜" + p.Name + "｜" + p.Type + "｜" + i.Stage + "｜" + i.FileCount + " 份｜" + p.Get("中标／施工单位") + "｜"
                            + (p.Contracts.Count == 0 ? "无" : string.Join("；", p.Contracts.Select(c => c.Kind + "/" + c.Party + "/" + c.Amount.ToString("0.##"))));
                    }));
                case "project_detail":
                    {
                        var p = st.Proj(S(a, "project_id"));
                        if (p == null) p = st.RealProjects.FirstOrDefault(x => x.Name.Contains(S(a, "project_id")));
                        if (p == null) return "没有这个项目编号。先用 list_projects 查看。";
                        Tool("查看项目：" + p.Name);
                        return AiFiller.Dossier(st, p, new AiPlan(), 10000)
                            + "\n—— 关键信息栏目（含空栏，set_project_info 用这些名称） ——\n" + string.Join("、", p.Fields.Select(f => f.Name + (string.IsNullOrWhiteSpace(f.Value) ? "（空）" : "")));
                    }
                case "search_archive":
                    {
                        string q = S(a, "query"), pid = S(a, "project_id");
                        Tool("搜索档案：" + q);
                        var hits = st.Search(q, pid == "" ? null : pid, null, false, 20);
                        if (hits.Count == 0) return "没有找到包含“" + q + "”的资料。可以换个说法或少几个词再搜。";
                        return string.Join("\n", hits.Select(h => { var p = st.Proj(h.File.Project); return "【" + h.File.Rel + "】（" + (p == null ? "" : p.Name) + "）" + h.Snippet; }));
                    }
                case "read_file": return ReadFile(S(a, "path"), a.ContainsKey("max_chars") ? Math.Max(1000, Math.Min(40000, (int)Num(a["max_chars"]))) : 12000);
                case "write_excel": return WriteExcel(a);
                case "fill_excel_from_archive": return FillExcel(cfg, S(a, "path"));
                case "create_word":
                    {
                        string path = NewPath(S(a, "file_name"), ".docx");
                        Tool("生成 Word：" + Path.GetFileName(path));
                        Docx.Write(path, S(a, "content"));
                        Output(path);
                        return "已生成：" + path;
                    }
                case "create_excel": return CreateExcel(a);
                case "edit_word": return EditWord(a);
                case "set_project_info": return ArchiveEdits.SetInfo(st, this, a);
                case "export_project_files": return ExportPack(a);
                case "upsert_contract": return ArchiveEdits.UpsertContract(st, this, a);
                case "add_payment": return ArchiveEdits.AddPayment(st, this, a);
                case "remove_record": return ArchiveEdits.Remove(st, this, a);
            }
            return "没有这个工具：" + name;
        }

        void Tool(string s) { if (OnTool != null) OnTool(s); }
        void Output(string path) { Outputs.Add(path); if (OnOutput != null) OnOutput(path); }

        static string S(Dictionary<string, object> o, string k) { object v; return o.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture).Trim() : ""; }
        static double Num(object v) { double d; return double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d : 0; }

        static string NewPath(string name, string ext)
        {
            name = Regex.Replace(string.IsNullOrWhiteSpace(name) ? "AI生成" : name.Trim(), @"[\\/:*?""<>|\r\n]", "");
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - ext.Length);
            if (name.Length > 80) name = name.Substring(0, 80);
            string dir = OutDir, path = Path.Combine(dir, name + ext);
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, name + "(" + i + ")" + ext);
            return path;
        }

        /// <summary>附件 / 档案文件的完整路径。</summary>
        string Full(string path)
        {
            path = (path ?? "").Trim().Trim('"', '“', '”');
            if (path == "") throw new AiException("没有给文件路径。");
            // 附件：可以写“附件1”，或路径被 AI 转述得略有出入（括号全半角、空格）时按文件名对上
            var am = Regex.Match(path, @"^附件\s*(\d+)");
            if (am.Success) { int k = int.Parse(am.Groups[1].Value) - 1; if (k >= 0 && k < attached.Count) return Exists(attached[k]); }
            Func<string, string> norm = x => Regex.Replace(Path.GetFileName(x) ?? "", @"[\s()（）\[\]【】]", "").ToLowerInvariant();
            var byName = attached.FirstOrDefault(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase) || norm(x) == norm(path));
            if (byName != null) return Exists(byName);
            if (attached.Count == 1 && !File.Exists(path) && Path.IsPathRooted(path) && string.Equals(Path.GetExtension(path), Path.GetExtension(attached[0]), StringComparison.OrdinalIgnoreCase)) return Exists(attached[0]);
            if (Path.IsPathRooted(path)) { if (File.Exists(path)) return path; throw new AiException("找不到文件：" + path); }
            string full = Path.Combine(st.Lib.Root, path);
            if (File.Exists(full)) return full;
            var f = st.Lib.Files.FirstOrDefault(x => x.Rel.EndsWith(path, StringComparison.OrdinalIgnoreCase) || x.Name == Path.GetFileName(path));
            if (f != null) return st.FullPath(f);
            throw new AiException("找不到文件：" + path + "（档案里的文件用 search_archive 返回的路径）");
        }

        static string Exists(string f)
        {
            if (!File.Exists(f)) throw new AiException("附件已经不在了（可能被微信等清理了临时文件）：" + Path.GetFileName(f) + "。请用户重新添加这个附件。");
            return f;
        }

        string ReadFile(string path, int max)
        {
            string full = Full(path), ext = Path.GetExtension(full).ToLowerInvariant();
            Tool("读取：" + Path.GetFileName(full));
            string text = null;
            if (ext == ".xls" || ext == ".xlsx" || ext == ".xlsm" || ext == ".et")
            {
                var sheets = ExcelIO.Read(full);
                var sb = new StringBuilder();
                foreach (var s in sheets) { bool cut; sb.AppendLine(AiFiller.TableText(s, 200, max, out cut)); if (sb.Length > max) break; }
                text = sb.ToString();
            }
            else
            {
                string rel = full.StartsWith(st.Lib.Root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ? full.Substring(st.Lib.Root.TrimEnd('\\').Length + 1) : null;
                if (rel != null) lock (st.Sync) st.Texts.TryGetValue(rel, out text);
                if (string.IsNullOrEmpty(text))
                {
                    if (ext == ".docx" || ext == ".txt" || ext == ".csv" || ext == ".md") text = Store.ExtractText(full, ext);
                    else if (ext == ".doc" || ext == ".wps") using (var rd = new OfficeReader()) text = rd.Doc(full);
                    else if (ext == ".pdf") text = Ocr.Pdf(full, 30);
                    else if (TextJob.OcrExts.Contains(ext)) text = Ocr.Image(full);
                }
            }
            if (string.IsNullOrWhiteSpace(text)) return "这个文件没有读出文字（可能是图片太模糊，或格式不支持）。";
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return "〔" + Path.GetFileName(full) + "，共 " + text.Length + " 字〕\n" + (text.Length > max ? text.Substring(0, max) + "\n……（后面还有 " + (text.Length - max) + " 字）" : text);
        }

        string ExportPack(Dictionary<string, object> a)
        {
            string id = S(a, "project_id");
            var p = st.Proj(id) ?? st.RealProjects.FirstOrDefault(x => id != "" && x.Name.Contains(id));
            if (p == null || p.IsRoot) return "没有这个项目：" + id;
            List<string> cats = null;
            object co;
            if (a.TryGetValue("categories", out co) && co is object[] && ((object[])co).Length > 0)
            {
                var want = ((object[])co).Select(x => Convert.ToString(x).Trim()).Where(x => x != "").ToList();
                cats = Cats.All.Where(c => want.Any(w => Cats.Title(c).Contains(w) || c.Contains(w) || w.Contains(Cats.Short(c)))).ToList();
                if (cats.Count == 0) return "没有对上的分类。可用的分类：" + string.Join("、", Cats.All.Select(Cats.Title));
            }
            bool zip = !a.ContainsKey("zip") || Convert.ToString(a["zip"]).ToLowerInvariant() != "false";
            Tool("导出资料包：" + p.Name + (cats == null ? "（全部分类）" : "（" + string.Join("、", cats.Select(Cats.Short)) + "）"));
            var r = ProjectExport.Run(st, p, OutDir, cats, zip, null);
            Output(r.Zip ?? r.Folder);
            return "已导出 " + r.Files + " 份资料（" + Ui.Size(r.Bytes) + "）到：" + r.Folder + (r.Zip != null ? "\n压缩包：" + r.Zip : "") + (r.Skipped > 0 ? "\n重复副本 " + r.Skipped + " 份未重复放" : "")
                + "\n内含按分类的文件夹、资料目录.xlsx、项目基本情况.docx。";
        }

        string WriteExcel(Dictionary<string, object> a)
        {
            string src = Full(S(a, "path"));
            var cells = new List<AiCell>();
            object co;
            if (a.TryGetValue("cells", out co) && co is object[])
                foreach (var c in ((object[])co).OfType<Dictionary<string, object>>())
                {
                    string cell = S(c, "cell").Replace("$", "").ToUpperInvariant();
                    if (!Regex.IsMatch(cell, @"^[A-Z]{1,3}\d{1,6}$")) continue;
                    cells.Add(new AiCell { Sheet = S(c, "sheet"), Cell = cell, Value = S(c, "value"), Source = S(c, "note") == "" ? "" : "AI 助手：" + S(c, "note") });
                }
            if (cells.Count == 0) return "没有有效的单元格（格式如 M51）。";
            string ext = Path.GetExtension(src);
            string dst = NewPath(S(a, "output_name") != "" ? S(a, "output_name") : Path.GetFileNameWithoutExtension(src) + "_AI修改", ext);
            Tool("写入 Excel：" + cells.Count + " 格 → " + Path.GetFileName(dst));
            bool overwrite = a.ContainsKey("overwrite") && Convert.ToString(a["overwrite"]).ToLowerInvariant() == "true";
            List<string> skipped;
            int n = ExcelIO.Write(src, dst, cells, !overwrite, out skipped);
            Output(dst);
            return "已写入 " + n + " 格，保存为：" + dst + (skipped.Count > 0 ? "\n没有写的格子：" + string.Join("；", skipped.Take(30)) : "");
        }

        string FillExcel(AiConfig cfg, string path)
        {
            string src = Full(path);
            Tool("用档案填表：" + Path.GetFileName(src));
            var sheets = ExcelIO.Read(src);
            var plan = AiFiller.Run(st, cfg, sheets, s => Tool(s.Trim()));
            if (plan.Cells.Count == 0) return "没有可填的格子：" + (plan.Blocks.Count == 0 ? "表里没有找到与档案项目对应的行。" : "对应的格子已有内容，或档案里找不到依据。") + (plan.Skipped.Count > 0 ? "\n留空原因：\n" + string.Join("\n", plan.Skipped.Take(20)) : "");
            string dst = NewPath(Path.GetFileNameWithoutExtension(src) + "_AI填写", Path.GetExtension(src));
            List<string> sk;
            ExcelIO.Write(src, dst, plan.Cells, true, out sk);
            Output(dst);
            return "已填写 " + plan.Cells.Count + " 格，保存为：" + dst + "\n" + string.Join("\n", plan.Cells.Select(c => c.Sheet + "!" + c.Cell + "〔" + c.Header + "〕＝" + c.Value + "（依据：" + AiClient.Cut(c.Source, 80) + "）"))
                + (plan.Skipped.Count > 0 ? "\n留空：\n" + string.Join("\n", plan.Skipped.Take(20)) : "");
        }

        string CreateExcel(Dictionary<string, object> a)
        {
            var headers = ((a.ContainsKey("headers") ? a["headers"] as object[] : null) ?? new object[0]).Select(x => Convert.ToString(x)).ToList();
            var rows = new List<IList<object>>();
            foreach (var r in (a.ContainsKey("rows") ? a["rows"] as object[] : null) ?? new object[0])
            {
                var cells = (r as object[]) ?? new object[] { r };
                rows.Add(cells.Select(x =>
                {
                    string s = Convert.ToString(x, CultureInfo.InvariantCulture) ?? ""; double d;
                    return Regex.IsMatch(s, @"^-?\d+(\.\d+)?$") && s.Length < 15 && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? (object)d : s;
                }).ToList());
            }
            if (headers.Count == 0) return "没有表头。";
            string path = NewPath(S(a, "file_name"), ".xlsx");
            Tool("生成 Excel：" + Path.GetFileName(path));
            Store.WriteXlsx(path, S(a, "sheet_name") == "" ? "Sheet1" : S(a, "sheet_name"), headers, rows);
            Output(path);
            return "已生成：" + path + "（" + rows.Count + " 行）";
        }

        string EditWord(Dictionary<string, object> a)
        {
            string src = Full(S(a, "path")), ext = Path.GetExtension(src).ToLowerInvariant();
            if (ext != ".doc" && ext != ".docx" && ext != ".wps") return "edit_word 只能改 Word 文件。";
            var reps = ((a.ContainsKey("replacements") ? a["replacements"] as object[] : null) ?? new object[0]).OfType<Dictionary<string, object>>()
                .Select(r => Tuple.Create(S(r, "find"), S(r, "replace"))).Where(t => t.Item1 != "").ToList();
            if (reps.Count == 0) return "没有要替换的内容。";
            string dst = NewPath(S(a, "output_name") != "" ? S(a, "output_name") : Path.GetFileNameWithoutExtension(src) + "_AI修改", ext);
            Tool("修改 Word：" + Path.GetFileName(src) + "（" + reps.Count + " 处）");
            File.Copy(src, dst);
            var report = WordEdit.Replace(dst, reps);
            Output(dst);
            return "已保存为：" + dst + "\n" + report;
        }
    }

    /// <summary>用本机 Word / WPS 文字替换文档里的文字。</summary>
    public static class WordEdit
    {
        static object Get(object o, string p) { return o.GetType().InvokeMember(p, BindingFlags.GetProperty, null, o, null); }
        static void SetP(object o, string p, object v) { o.GetType().InvokeMember(p, BindingFlags.SetProperty, null, o, new[] { v }); }
        static object Call(object o, string m, params object[] a) { return o.GetType().InvokeMember(m, BindingFlags.InvokeMethod, null, o, a); }

        public static string Replace(string path, List<Tuple<string, string>> reps)
        {
            object app = null, doc = null;
            var lines = new List<string>();
            try
            {
                foreach (var id in new[] { "Word.Application", "KWps.Application" })
                {
                    var t = Type.GetTypeFromProgID(id);
                    if (t == null) continue;
                    try { app = Activator.CreateInstance(t); break; } catch { }
                }
                if (app == null) throw new AiException("本机没有找到 Word 或 WPS 文字，无法修改 Word。");
                try { SetP(app, "Visible", false); SetP(app, "DisplayAlerts", 0); } catch { }
                doc = Call(Get(app, "Documents"), "Open", path, false, false, false);
                foreach (var r in reps)
                {
                    int n = 0;
                    var rng = Get(doc, "Content");
                    var find = Get(rng, "Find");
                    // Execute(FindText, MatchCase, MatchWholeWord, MatchWildcards, MatchSoundsLike, MatchAllWordForms, Forward, Wrap)
                    while (n < 500 && Convert.ToBoolean(Call(find, "Execute", r.Item1, true, false, false, false, false, true, 0)))
                    {
                        SetP(rng, "Text", r.Item2);
                        Call(rng, "Collapse", 0);   // 折到末尾，继续往后找
                        n++;
                    }
                    lines.Add((n > 0 ? "✓ " : "× 没找到：") + AiClient.Cut(r.Item1, 30) + (n > 1 ? "（" + n + " 处）" : ""));
                }
                Call(doc, "Save");
            }
            finally
            {
                try { if (doc != null) Call(doc, "Close", 0); } catch { }
                try { if (app != null) Call(app, "Quit", 0); } catch { }
                try { if (app != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(app); } catch { }
            }
            return string.Join("\n", lines);
        }
    }

    /// <summary>生成简单的 Word 文档（不需要装 Office）。</summary>
    public static class Docx
    {
        const string Body = "仿宋", Head = "黑体", Title = "宋体";

        static string Esc(string s) { return SecurityElement.Escape(s ?? ""); }

        static string Runs(string text, string font, int halfPts, bool bold)
        {
            var sb = new StringBuilder();
            var parts = Regex.Split(text, @"(\*\*[^*]+\*\*)");
            foreach (var part in parts)
            {
                if (part == "") continue;
                bool b = bold || (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4);
                string t = b && part.StartsWith("**") ? part.Substring(2, part.Length - 4) : part;
                sb.Append("<w:r><w:rPr><w:rFonts w:ascii=\"" + font + "\" w:hAnsi=\"" + font + "\" w:eastAsia=\"" + font + "\"/>" + (b ? "<w:b/>" : "") + "<w:sz w:val=\"" + halfPts + "\"/><w:szCs w:val=\"" + halfPts + "\"/></w:rPr><w:t xml:space=\"preserve\">" + Esc(t) + "</w:t></w:r>");
            }
            return sb.ToString();
        }

        static string Para(string text, string font, int halfPts, bool bold, string align, bool indent, int after)
        {
            return "<w:p><w:pPr>" + (align != null ? "<w:jc w:val=\"" + align + "\"/>" : "") + "<w:spacing w:after=\"" + after + "\" w:line=\"" + (halfPts >= 40 ? 600 : 560) + "\" w:lineRule=\"exact\"/>"
                + (indent ? "<w:ind w:firstLineChars=\"200\" w:firstLine=\"640\"/>" : "") + "</w:pPr>" + Runs(text, font, halfPts, bold) + "</w:p>";
        }

        static string Table(List<string[]> rows)
        {
            int cols = rows.Max(r => r.Length);
            var sb = new StringBuilder("<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/><w:jc w:val=\"center\"/><w:tblBorders>");
            foreach (var b in new[] { "top", "left", "bottom", "right", "insideH", "insideV" }) sb.Append("<w:" + b + " w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"000000\"/>");
            sb.Append("</w:tblBorders></w:tblPr><w:tblGrid>");
            for (int i = 0; i < cols; i++) sb.Append("<w:gridCol/>");
            sb.Append("</w:tblGrid>");
            for (int r = 0; r < rows.Count; r++)
            {
                sb.Append("<w:tr>");
                for (int c = 0; c < cols; c++)
                {
                    string v = c < rows[r].Length ? rows[r][c] : "";
                    sb.Append("<w:tc><w:tcPr><w:vAlign w:val=\"center\"/></w:tcPr><w:p><w:pPr><w:jc w:val=\"center\"/><w:spacing w:after=\"0\"/></w:pPr>" + Runs(v, r == 0 ? Head : Body, 24, false) + "</w:p></w:tc>");
                }
                sb.Append("</w:tr>");
            }
            return sb.Append("</w:tbl>").ToString();
        }

        public static void Write(string path, string content)
        {
            var body = new StringBuilder();
            var table = new List<string[]>();
            Action flush = () => { if (table.Count > 0) { body.Append(Table(table)).Append(Para("", Body, 32, false, null, false, 0)); table.Clear(); } };
            foreach (var raw in (content ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.TrimStart().StartsWith("|"))
                {
                    var cells = line.Trim().Trim('|').Split('|').Select(x => x.Trim()).ToArray();
                    if (cells.All(x => Regex.IsMatch(x, @"^:?-{2,}:?$"))) continue;   // 表格分隔行
                    table.Add(cells); continue;
                }
                flush();
                if (line.Trim() == "") continue;
                if (line.StartsWith("# ")) body.Append(Para(line.Substring(2).Trim(), Title, 44, true, "center", false, 240));
                else if (line.StartsWith("## ")) body.Append(Para(line.Substring(3).Trim(), Head, 32, false, null, true, 0));
                else if (line.StartsWith("### ")) body.Append(Para(line.Substring(4).Trim(), Body, 32, true, null, true, 0));
                else if (Regex.IsMatch(line, @"^\s*[-*•]\s+")) body.Append(Para(Regex.Replace(line, @"^\s*[-*•]\s+", ""), Body, 32, false, null, true, 0));
                else body.Append(Para(line.Trim(), Body, 32, false, null, true, 0));
            }
            flush();
            string doc = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>"
                + body + "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"2098\" w:right=\"1474\" w:bottom=\"1984\" w:left=\"1588\" w:header=\"851\" w:footer=\"992\" w:gutter=\"0\"/></w:sectPr></w:body></w:document>";
            if (File.Exists(path)) File.Delete(path);
            using (var z = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Action<string, string> add = (n, x) => { using (var w = new StreamWriter(z.CreateEntry(n).Open(), new UTF8Encoding(false))) w.Write(x); };
                add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
                add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
                add("word/document.xml", doc);
            }
        }
    }
}
