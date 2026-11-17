using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DangAn
{
    /// <summary>交委页面上的字段（只用截图里出现的标签、分组和单位）。Kind：text / number / date / select（选项以系统为准，不编造）/ picker（带“+”的选择器，交给人选）。</summary>
    public class JwFieldDef
    {
        public string Module, Group, Label, Unit, Kind, Hint;
        public JwFieldDef(string module, string group, string label, string unit, string kind, string hint) { Module = module; Group = group; Label = label; Unit = unit; Kind = kind; Hint = hint; }
    }

    public static class JwExtract
    {
        public const string Bid = "招投标信息", Acc = "质量及验收";

        public static readonly JwFieldDef[] Defs =
        {
            new JwFieldDef(Bid, "招投项目信息", "招标类型", "", "select", "决策／采购形式或招标文件里的招标类型"),
            new JwFieldDef(Bid, "招投项目信息", "计划项目名称", "", "picker", "页面用“+”从计划库挑选，由人选"),
            new JwFieldDef(Bid, "招投项目信息", "计划总投资", "万元", "number", "投资概算／可研投资（档案多为元，换算成万元）"),
            new JwFieldDef(Bid, "招投项目信息", "实际总投资", "万元", "number", "结算、决算或审计审定金额；没有就留空，不能用发票加总"),
            new JwFieldDef(Bid, "招投项目信息", "计划里程", "公里", "number", "计划或设计文件里的建设里程"),
            new JwFieldDef(Bid, "施工招标", "招标方式", "", "select", "公开招标、竞争性磋商、询价、直接委托等，以档案原文为准"),
            new JwFieldDef(Bid, "施工招标", "中标单位", "", "text", "中标/成交通知书、施工合同乙方全称"),
            new JwFieldDef(Bid, "施工招标", "中标时间", "", "date", "中标/成交通知书日期"),
            new JwFieldDef(Bid, "施工招标", "合同签订日期", "", "date", "施工合同签订日期（合同末页、协议书落款）"),
            new JwFieldDef(Bid, "施工招标", "计划开工日期", "", "date", "合同约定的开工日期"),
            new JwFieldDef(Bid, "施工招标", "计划完工日期", "", "date", "合同约定的竣工/完工日期"),
            new JwFieldDef(Bid, "施工招标", "合同工期", "天", "number", "合同写明的工期天数（不要用日期差代替）"),
            new JwFieldDef(Bid, "施工招标", "项目经理", "", "text", "施工单位项目经理"),
            new JwFieldDef(Bid, "施工招标", "证书编号", "", "text", "施工项目经理（建造师）证书编号，不要和监理证书混"),
            new JwFieldDef(Bid, "施工招标", "合同建设里程", "公里", "number", "施工合同或设计文件里的建设里程"),
            new JwFieldDef(Bid, "施工招标", "施工合同金额", "万元", "number", "施工合同签约合同价（大写为准，换算成万元）"),
            new JwFieldDef(Bid, "监理招标", "总监理工程师", "", "text", "监理合同或人员资料里的总监"),
            new JwFieldDef(Bid, "监理招标", "证书编号", "", "text", "总监理工程师的证书/注册号"),
            new JwFieldDef(Acc, "合同信息", "项目名称", "", "picker", "页面用“+”挑选，由人选"),
            new JwFieldDef(Acc, "合同信息", "验收项目别名", "", "text", "档案里的简称或别名；没有就留空"),
            new JwFieldDef(Acc, "合同信息", "验收方式", "", "select", "交工、竣工是否分开验收等，以验收文件为准"),
            new JwFieldDef(Acc, "交工验收信息", "组织验收单位", "", "text", "交工验收证书上的组织单位"),
            new JwFieldDef(Acc, "交工验收信息", "验收时间", "", "date", "交工验收日期"),
            new JwFieldDef(Acc, "交工验收信息", "交工验收里程", "公里", "number", "交工验收里程"),
            new JwFieldDef(Acc, "交工验收信息", "验收结论", "", "select", "交工验收结论（合格等，以原文为准）"),
            new JwFieldDef(Acc, "竣工验收信息", "是否已竣工", "", "select", "有竣工验收资料为是，没有为否；查不到留空"),
            new JwFieldDef(Acc, "竣工验收信息", "组织验收单位", "", "text", "竣工验收证书上的组织单位（可能与交工不同）"),
            new JwFieldDef(Acc, "竣工验收信息", "验收时间", "", "date", "竣工验收日期"),
            new JwFieldDef(Acc, "竣工验收信息", "竣工验收里程", "公里", "number", "竣工验收里程"),
            new JwFieldDef(Acc, "竣工验收信息", "实际完成投资", "万元", "number", "决算或审计审定的实际完成投资；只有发票的不算"),
            new JwFieldDef(Acc, "竣工验收信息", "中央补助资金", "万元", "number", "资金下达文件、台账里的中央补助"),
            new JwFieldDef(Acc, "竣工验收信息", "市级补助资金", "万元", "number", "资金下达文件、台账里的市级补助"),
            new JwFieldDef(Acc, "竣工验收信息", "区县自筹资金", "万元", "number", "区县自筹部分"),
            new JwFieldDef(Acc, "竣工验收信息", "工程质量结论", "", "select", "竣工验收质量结论，以原文为准"),
        };

        const string Prompt =
@"你是乡镇（街道）农村公路项目的填报员，要把本项目的信息填进交委“以奖代补”系统的“招投标信息”和“质量及验收”两页。根据【项目档案】逐项找出要填的值。只输出 JSON。

规则：
1. 只写档案里查得到的，每个值都要写 source（文件名 + 原文摘录，60 字以内）；查不到的 value 写空字符串，不要编造，不要凭常识推。
2. 金额栏一律填“万元”（档案多为元，要除以 10000，如 1691000元 → 169.1），同时在 raw 里写档案原值（如“1691000元”）。里程填公里，工期填天。有单位的栏，value 只写数字（如 169.1、3.182、120），单位不要写进 value。
3. 合同金额以合同原文为准，大写与小写不一致以大写为准；“实际总投资”“实际完成投资”要用结算、决算或审计审定的金额，只有发票、付款的不能算；补助资金要有资金下达文件或台账依据。
4. 合同工期按合同写明的天数填，不要用开工、完工日期相减代替。
5. 施工“证书编号”（项目经理 / 建造师）和监理“证书编号”（总监）要分开，不要填串。交工、竣工的“组织验收单位”“验收时间”要分别对应各自的验收文件。
6. 下拉类字段（招标类型、招标方式、验收方式、验收结论、是否已竣工、工程质量结论）写档案原文里的说法（如“竞争性磋商”“合格”），系统选项以后再对，不要自己套用常见选项。
7. “计划项目名称”“项目名称”是页面上用“+”挑选的，不用填。
8. confidence 写 高 / 中 / 低：原文直接写明的为高，需要换算或推断的为中，拿不准的为低。
9. 日期统一写 yyyy-MM-dd；原文只有年月就写 yyyy-MM。

输出格式：
{""fields"":[{""module"":""招投标信息"",""group"":""施工招标"",""label"":""中标单位"",""value"":"""",""raw"":"""",""source"":"""",""confidence"":""高""}],
 ""notes"":[""需要人核对的地方""]}";

        public static JwProjectData Run(Store st, AiConfig cfg, Project p, Action<string> log)
        {
            var data = JwProjectData.Load(st, p.Id);
            var plan = new AiPlan { Keywords = new List<string> { "中标通知书", "成交通知书", "签约合同价", "合同工期", "开工日期", "竣工日期", "项目经理", "建造师", "总监理工程师", "注册号", "证书编号", "交工验收", "竣工验收", "验收结论", "质量等级", "决算", "审定", "中央", "市级", "补助", "自筹", "里程", "公里", "招标方式", "采购方式" } };
            if (log != null) log("整理项目资料：" + p.Name);
            var sb = new StringBuilder();
            sb.AppendLine("今天是 " + DateTime.Now.ToString("yyyy-MM-dd") + "。");
            sb.AppendLine("【要填的字段】（模块｜分组｜字段｜单位｜找什么）");
            foreach (var d in Defs.Where(x => x.Kind != "picker")) sb.AppendLine(d.Module + "｜" + d.Group + "｜" + d.Label + "｜" + d.Unit + "｜" + d.Hint);
            sb.AppendLine("\n【项目档案】");
            sb.AppendLine(AiFiller.Dossier(st, p, plan, 70000));
            if (log != null) log("AI 正在抽取招投标、质量及验收字段：" + p.Name);
            var o = AiClient.ParseJson(AiClient.Chat(cfg, Prompt, sb.ToString(), true, 12000));

            var got = new List<JwField>();
            object fo;
            if (o.TryGetValue("fields", out fo) && fo is object[])
                foreach (var x in ((object[])fo).OfType<Dictionary<string, object>>())
                {
                    var f = new JwField { Module = S(x, "module"), Group = S(x, "group"), Label = S(x, "label"), Value = S(x, "value"), Raw = S(x, "raw"), Source = S(x, "source"), Confidence = S(x, "confidence") };
                    var def = Defs.FirstOrDefault(d => d.Module == f.Module && d.Group == f.Group && d.Label == f.Label);
                    if (def == null || def.Kind == "picker") continue;
                    f.Unit = def.Unit;
                    if (f.Value != "" && f.Source == "") { f.Confidence = "低"; f.Source = "（AI 没给出依据，请核对）"; }
                    if (def.Kind == "number" && f.Value != "") Number(def, f);
                    // 万元栏防错位：值大于 10 万的，多半没换算
                    double v;
                    if (def.Unit == "万元" && double.TryParse(f.Value, out v) && v > 100000) { f.Confidence = "低"; f.Source = "【疑似没换算成万元】" + f.Source; }
                    got.Add(f);
                }
            // 合并：人改过的保留；字段齐全（没抽到的留空占位）
            var merged = new List<JwField>();
            foreach (var d in Defs.Where(x => x.Kind != "picker"))
            {
                var old = data.Fields.FirstOrDefault(f => f.Module == d.Module && f.Group == d.Group && f.Label == d.Label);
                var nw = got.FirstOrDefault(f => f.Module == d.Module && f.Group == d.Group && f.Label == d.Label) ?? new JwField { Module = d.Module, Group = d.Group, Label = d.Label, Unit = d.Unit };
                merged.Add(old != null && old.Edited ? old : nw);
            }
            data.Fields = merged;
            data.Notes.Clear();
            object no;
            if (o.TryGetValue("notes", out no) && no is object[]) foreach (var n in (object[])no) { var t = Convert.ToString(n).Trim(); if (t != "") data.Notes.Add(t); }
            data.Extracted = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            data.Save(st);
            if (log != null) log("抽取完成：" + p.Name + "，有值 " + data.Fields.Count(f => f.Value != "") + " / " + data.Fields.Count + " 项");
            return data;
        }

        /// <summary>按缺件预检结果准备附件清单（人改过的、已上传的保留）。</summary>
        public static void FillAttachments(Store st, Project p, JwProjectData data, JwPrecheck.Result pre)
        {
            foreach (var s in pre.Slots)
                foreach (var f in s.Files)
                {
                    if (data.Attachments.Any(a => a.Rel == f.Rel && a.Kind == s.Kind)) continue;
                    data.Attachments.Add(new JwAttach { Module = s.Module, Kind = s.Kind, Rel = f.Rel, OrigSize = f.Size, Why = s.Why, Confidence = s.Confidence, Use = s.Files.IndexOf(f) == 0 || s.Kind.Contains("纪要") });
                }
            // 原件已不在本项目的去掉（没传过的）
            data.Attachments.RemoveAll(a => a.Uploaded == "" && !st.FilesOf(p).Any(f => f.Rel == a.Rel));
        }

        /// <summary>数字栏只留数字：去掉 AI 带上的单位；写的是“元”的换算成万元，“米”换算成公里。</summary>
        static void Number(JwFieldDef def, JwField f)
        {
            string s = f.Value.Replace(",", "").Replace("，", "").Replace(" ", "").Replace("¥", "").Replace("￥", "");
            var m = Regex.Match(s, @"^(-?\d+(?:\.\d+)?)(万元|万|元|公里|km|千米|米|m|天|日历天|日|个月)?$", RegexOptions.IgnoreCase);
            if (!m.Success) { f.Confidence = "低"; f.Source = "【不是数字，请改】" + f.Source; return; }
            decimal n = decimal.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            string u = m.Groups[2].Value.ToLowerInvariant();
            if (def.Unit == "万元" && u == "元") { n = n / 10000m; if (f.Confidence == "高") f.Confidence = "中"; if (f.Raw == "") f.Raw = f.Value; }
            if (def.Unit == "公里" && (u == "米" || u == "m")) { n = n / 1000m; if (f.Confidence == "高") f.Confidence = "中"; if (f.Raw == "") f.Raw = f.Value; }
            if (def.Unit == "天" && u == "个月") { f.Confidence = "低"; f.Source = "【原文按月，请换成天数】" + f.Source; return; }
            f.Value = n.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        }

        static string S(Dictionary<string, object> o, string k) { object v; return o.TryGetValue(k, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture).Trim() : ""; }
    }
}
