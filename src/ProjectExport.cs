using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DangAn
{
    /// <summary>
    /// 导出项目资料包（给上级查阅）：按分类建文件夹（01 立项及计划、04 合同协议……）放资料，
    /// 附《资料目录.xlsx》和《项目基本情况.docx》；可再打成 zip。原文件不动。
    /// </summary>
    public static class ProjectExport
    {
        public class Result { public string Folder, Zip; public int Files, Skipped; public long Bytes; public List<string> Log = new List<string>(); }

        static string Safe(string s) { return Regex.Replace(s ?? "", @"[\\/:*?""<>|\r\n]", "_").Trim(); }

        /// <summary>cats 为空表示全部分类。</summary>
        public static Result Run(Store st, Project p, string outParent, IList<string> cats, bool zip, Action<string> progress)
        {
            var r = new Result();
            string name = Safe(p.Name) + "_资料汇编_" + DateTime.Now.ToString("yyyyMMdd");
            string dir = Path.Combine(outParent, name);
            for (int i = 2; Directory.Exists(dir); i++) dir = Path.Combine(outParent, name + "(" + i + ")");
            Directory.CreateDirectory(dir);
            r.Folder = dir;

            // 拆出的发票副本、拆分记录是软件生成的，不重复放；同名同大小的副本只放一份
            var files = Store.SortForDisplay(st.FilesOf(p).Where(f => !f.Rel.Contains(Invoices.OutFolder) && (cats == null || cats.Count == 0 || cats.Contains(f.Cat)))).ToList();
            var seen = new HashSet<string>();
            var rows = new List<IList<object>>();
            int n = 0;
            foreach (var g in files.GroupBy(f => f.Cat).OrderBy(g => g.Key))
            {
                string catDir = Path.Combine(dir, Safe(g.Key));   // “04 合同协议”：带序号，按办理顺序排
                Directory.CreateDirectory(catDir);
                foreach (var f in g)
                {
                    string key = Regex.Replace(f.Name, @"\(\d+\)(?=\.[^.]+$)", "") + "|" + f.Size;
                    if (!seen.Add(key)) { r.Skipped++; continue; }
                    string src = st.FullPath(f);
                    if (!File.Exists(src)) { r.Log.Add("找不到：" + f.Rel); continue; }
                    string dst = Path.Combine(catDir, f.Name);
                    for (int i = 2; File.Exists(dst); i++) dst = Path.Combine(catDir, Path.GetFileNameWithoutExtension(f.Name) + "(" + i + ")" + Path.GetExtension(f.Name));
                    try { File.Copy(src, dst); } catch (Exception ex) { r.Log.Add("复制失败：" + f.Name + "（" + ex.Message + "）"); continue; }
                    n++; r.Files++; r.Bytes += f.Size;
                    rows.Add(new List<object> { (double)n, Cats.Title(g.Key), Path.GetFileName(dst), f.Time.Length >= 10 ? f.Time.Substring(0, 10) : f.Time, Ui.Size(f.Size), f.Rel });
                    if (progress != null && n % 20 == 0) progress("正在导出资料：" + n + " / " + files.Count);
                }
            }
            Store.WriteXlsx(Path.Combine(dir, "资料目录.xlsx"), "资料目录", new[] { "序号", "分类", "文件名", "日期", "大小", "档案中的位置" }, rows);
            Docx.Write(Path.Combine(dir, "项目基本情况.docx"), Summary(st, p, files.Count == 0 ? 0 : r.Files));

            if (zip)
            {
                if (progress != null) progress("正在打包 zip……");
                string z = dir + ".zip";
                if (File.Exists(z)) File.Delete(z);
                ZipFile.CreateFromDirectory(dir, z, CompressionLevel.Optimal, true, Encoding.UTF8);
                r.Zip = z;
            }
            return r;
        }

        /// <summary>项目基本情况（Word 正文，用 Docx 的简单标记）。</summary>
        static string Summary(Store st, Project p, int fileCount)
        {
            var info = st.Info(p);
            var sb = new StringBuilder();
            sb.AppendLine("# " + p.Name + "基本情况");
            sb.AppendLine("## 一、项目概况");
            sb.AppendLine("|栏目|内容|");
            sb.AppendLine("|档号|" + p.Code + "|");
            sb.AppendLine("|项目类型|" + p.Type + "|");
            sb.AppendLine("|办理进展|" + info.Stage + "|");
            foreach (var f in p.Fields)
            {
                if (string.IsNullOrWhiteSpace(f.Value) || f.Name == "项目名称") continue;
                if (Regex.IsMatch(f.Name, "整理|口径|其他说明|重大事项|填报部门|决策／审批")) continue;   // 内部整理笔记不放进上报材料
                sb.AppendLine("|" + f.Name.Replace("／", "/") + "|" + f.Value.Replace("|", "／").Replace("\n", " ") + "|");
            }
            if (p.Contracts.Count > 0)
            {
                sb.AppendLine("## 二、合同情况");
                sb.AppendLine("|合同类型|合同相对方|合同金额（元）|签订日期|已付（元）|");
                foreach (var c in p.Contracts)
                    sb.AppendLine("|" + c.Kind + "|" + c.Party + "|" + c.Amount.ToString("#,##0.00") + "|" + c.Date + "|" + c.Paid.ToString("#,##0.00") + "|");
                sb.AppendLine("|合计||" + p.Contracts.Sum(c => c.Amount).ToString("#,##0.00") + "||" + p.Contracts.Sum(c => c.Paid).ToString("#,##0.00") + "|");
            }
            sb.AppendLine("## " + (p.Contracts.Count > 0 ? "三" : "二") + "、资料目录");
            sb.AppendLine("本资料包共 " + fileCount + " 份资料，按以下分类存放，详见《资料目录.xlsx》：");
            foreach (var kv in info.CatCount.OrderBy(k => k.Key)) sb.AppendLine("- " + Cats.Title(kv.Key) + "：" + kv.Value + " 份");
            if (info.Missing.Count > 0) sb.AppendLine("暂缺资料：" + string.Join("、", info.Missing.Select(Cats.Title)) + "。");
            sb.AppendLine("整理日期：" + DateTime.Now.ToString("yyyy年M月d日"));
            return sb.ToString();
        }
    }
}
