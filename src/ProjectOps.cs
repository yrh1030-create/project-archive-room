using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;

namespace DangAn
{
    /// <summary>判断文件夹名是“项目名”还是“项目里的分类文件夹”（如“1项目合同”“2项目人员”）。</summary>
    public static class FolderNames
    {
        static readonly Regex Lead = new Regex(@"^\s*[\(（\[【]?\s*\d{1,3}(?![\d年])\s*[\)）\]】]?\s*[\.、．\-_—\s]*");   // 序号，不含“2023年”这类年份
        const string CatKw = "合同|人员|资料|文件|图纸|图片|照片|影像|凭证|发票|财务|资金|债券|收支|招标|投标|中标|验收|结算|决算|审计|会议|纪要|立项|批复|可研|设计|施工|监理|检测|其他|附件|档案|证明|材料|报告|台账|计划|申请|预算|概算|变更|签证|收方|竣工|交工|安全|质量|进度|往来|函件|通知|管理|基本情况|前期|手续|资质|证书";
        const string ProjKw = "工程|公路|道路|大桥|桥梁|桥|整治|改造|拓宽|建设|通畅|抢险|养护|水毁|治理|提升|硬化|通组|设施|村|街道|社区|镇|乡|片区|路段|路";

        public static bool IsCategory(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string t = name.Trim().TrimStart('(', '（', '[', '【');
            bool numbered = t.Length > 0 && char.IsDigit(t[0]) && !Regex.IsMatch(t, @"^\d{4}\s*年");
            string n = Lead.Replace(name, "").Trim();
            if (Regex.IsMatch(n, ProjKw)) return false;
            if (n.Length <= 14 && Regex.IsMatch(n, CatKw)) return true;
            return numbered && n.Length <= 8;
        }

        static readonly Regex NoPrefix = new Regex(@"^\s*[\(（\[【]?\s*\d{1,4}(?![\d年])\s*[\)）\]】]?\s*号?\s*[\.、．\-_—－\s]*");

        /// <summary>项目名的“身份”：去掉“188号--”这类编号、地区前缀、括号说明和标点。两个文件夹身份相同就是同一个项目。</summary>
        public static string Key(string name)
        {
            string s = NoPrefix.Replace(name ?? "", "");
            s = Regex.Replace(s, @"[（(][^）)]*[）)]|涪陵区|重庆市|李渡街道办事处|李渡街道|李渡|\s|[\-—－_、.．,，:：]", "");
            return s;
        }

        /// <summary>去掉“188号--”这类编号，作为新项目的名称。</summary>
        public static string CleanName(string name)
        {
            string s = NoPrefix.Replace(name ?? "", "").Trim();
            return s.Length >= 2 ? s : (name ?? "").Trim();
        }

        public static bool IsProjectLike(string name)
        {
            return !IsCategory(name) && Regex.IsMatch(name ?? "", ProjKw + "|项目");
        }
    }

    /// <summary>项目、文件的删除 / 移除 / 替换。删除一律进 Windows 回收站，可找回。</summary>
    public static class ProjectOps
    {
        static void Recycle(string file)
        {
            for (int i = 0; ; i++)
            {
                try { FileSystem.DeleteFile(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); return; }
                catch (IOException)
                {
                    // 文件可能刚被识别、预览占着：释放一下再试
                    if (i >= 3) throw;
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    System.Threading.Thread.Sleep(400);
                }
            }
        }
        static void RecycleDir(string dir) { FileSystem.DeleteDirectory(dir, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); }

        /// <summary>删除项目。recycle=true：项目文件夹移到回收站；否则文件夹保留，但以后不再扫描、不再当成项目。</summary>
        public static List<string> DeleteProjects(Store st, IEnumerable<Project> projects, bool recycle)
        {
            var log = new List<string>();
            foreach (var p in projects.Where(x => !x.IsRoot).ToList())
            {
                foreach (var folder in p.Folders.ToList())
                {
                    string prefix = folder + "\\";
                    // 文件夹里有归到其他项目、或共用给其他项目的资料时，不删文件夹
                    bool others = st.Lib.Files.Any(f => f.Rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && f.Project != p.Id && f.Project != Store.RootProjectId)
                        || st.Lib.SharedWith.Any(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && kv.Value.Any(x => x != p.Id));
                    string full = Path.Combine(st.Lib.Root, folder);
                    if (recycle && !others && Directory.Exists(full))
                    {
                        try { RecycleDir(full); log.Add("✓ 已把文件夹“" + folder + "”移到回收站"); continue; }
                        catch (Exception ex) { log.Add("× 文件夹“" + folder + "”没能移到回收站：" + ex.Message + "，改为不再扫描"); }
                    }
                    if (!st.Lib.Excludes.Any(e => string.Equals(e.Trim(), folder, StringComparison.OrdinalIgnoreCase))) st.Lib.Excludes.Add(folder);
                    log.Add("✓ 文件夹“" + folder + "”保留，以后不再扫描" + (others ? "（里面有其他项目的资料，所以没有删除文件夹）" : ""));
                }
                foreach (var k in st.Lib.FileProject.Where(kv => kv.Value == p.Id).Select(kv => kv.Key).ToList()) st.Lib.FileProject.Remove(k);
                foreach (var k in st.Lib.SharedWith.Keys.ToList())
                {
                    st.Lib.SharedWith[k].Remove(p.Id);
                    if (st.Lib.SharedWith[k].Count == 0) st.Lib.SharedWith.Remove(k);
                }
                st.Lib.Invoices.RemoveAll(i => i.Project == p.Id);
                st.Lib.Projects.Remove(p);
                log.Add("✓ 已删除项目“" + p.Name + "”");
            }
            st.Scan(null);
            st.Save();
            return log;
        }

        /// <summary>误识别出来的“项目”（名字或文件夹像“1项目合同”“20项目验收”这类分类文件夹）。</summary>
        public static List<Project> LooksMisrecognized(Store st)
        {
            return st.RealProjects.Where(p => FolderNames.IsCategory(p.Name)).ToList();   // 误识别的项目以分类文件夹名为项目名
        }

        /// <summary>把几个项目合并成一个（已有同名项目就并入它，否则新建）。文件夹、合同、收支、栏目、归属与共用设置都并过去。</summary>
        /// <summary>名字去掉编号、前缀后相同（或一个包含另一个且足够长）的项目，分成一组组，供一键合并。</summary>
        public static List<List<Project>> DuplicateGroups(Store st)
        {
            var list = st.RealProjects.ToList();
            Func<Project, List<string>> keys = p => new[] { p.Name }.Concat(p.Folders).Select(FolderNames.Key).Where(k => k.Length >= 4).Distinct().ToList();
            var groups = new List<List<Project>>();
            var used = new HashSet<Project>();
            foreach (var p in list)
            {
                if (used.Contains(p)) continue;
                var kp = keys(p);
                var g = new List<Project> { p };
                foreach (var q in list)
                {
                    if (q == p || used.Contains(q)) continue;
                    var kq = keys(q);
                    if (kp.Any(a => kq.Any(b => a == b || (Math.Min(a.Length, b.Length) >= 7 && (a.Contains(b) || b.Contains(a))))))
                        g.Add(q);
                }
                if (g.Count > 1) { groups.Add(g); foreach (var x in g) used.Add(x); }
            }
            return groups;
        }

        public static Project MergeInto(Store st, List<Project> parts, string name)
        {
            parts = parts.Where(p => !p.IsRoot).ToList();
            var target = st.RealProjects.FirstOrDefault(p => p.Name == name && !parts.Contains(p))
                      ?? parts.FirstOrDefault(p => p.Name == name);
            if (target == null)
            {
                target = new Project { Id = st.NewProjectId(), Name = name, Type = Store.GuessType(name) };
                target.Code = target.Id; target.Required = Store.DefaultRequired(target.Type);
                target.Fields.Add(new Field("项目名称", name, ""));
                st.Lib.Projects.Add(target);
            }
            foreach (var p in parts)
            {
                if (p == target) continue;
                foreach (var f in p.Folders) if (!target.Folders.Contains(f)) target.Folders.Add(f);
                target.Contracts.AddRange(p.Contracts);
                target.Payments.AddRange(p.Payments);
                foreach (var f in p.Fields) if (f.Name != "项目名称" && target.Get(f.Name) == "" && f.Value != "") target.Set(f.Name, f.Value, f.Source);
                if (!string.IsNullOrEmpty(p.Note)) target.Note = (target.Note + "\n" + p.Note).Trim();
                foreach (var k in st.Lib.FileProject.Where(kv => kv.Value == p.Id).Select(kv => kv.Key).ToList()) st.Lib.FileProject[k] = target.Id;
                foreach (var l in st.Lib.SharedWith.Values) if (l.Remove(p.Id) && !l.Contains(target.Id)) l.Add(target.Id);
                foreach (var i in st.Lib.Invoices.Where(i => i.Project == p.Id)) i.Project = target.Id;
                foreach (var f in st.Lib.Files.Where(f => f.Project == p.Id)) f.Project = target.Id;
                st.Lib.Projects.Remove(p);
            }
            target.Name = name;
            st.Scan(null);
            st.Save();
            return target;
        }

        /// <summary>把资料从某项目里移除（文件保留）：共用的只取消共用；本项目自己的从此不再显示。</summary>
        public static void RemoveFromProject(Store st, FileRec f, Project p)
        {
            List<string> shared;
            st.Lib.SharedWith.TryGetValue(f.Rel, out shared);
            if (f.Project != p.Id)
            {
                if (shared != null) { shared.Remove(p.Id); if (shared.Count == 0) st.Lib.SharedWith.Remove(f.Rel); }
            }
            else if (shared != null && shared.Count > 0)
            {
                // 本项目的资料同时共用给了别的项目：交给那个项目，本项目不再显示
                st.Lib.FileProject[f.Rel] = shared[0]; f.Project = shared[0];
                shared.RemoveAt(0); if (shared.Count == 0) st.Lib.SharedWith.Remove(f.Rel);
            }
            else
            {
                if (!st.Lib.Hidden.Contains(f.Rel)) st.Lib.Hidden.Add(f.Rel);
                st.Lib.Files.Remove(f);
            }
            foreach (var pay in p.Payments)
            {
                if (pay.VoucherFile == f.Rel) pay.VoucherFile = "";
                if (pay.InvoiceFile == f.Rel) pay.InvoiceFile = "";
            }
        }

        /// <summary>
        /// 某笔支出的发票换成新票（发票重开）：新票挂上，旧票从项目里移除（文件保留，可恢复）；
        /// 旧票是从合同、凭证里拆出来的，记住来源页，以后不再拆回来。
        /// </summary>
        public static void SwapInvoice(Store st, Project p, Payment pay, string newRel)
        {
            string old = pay.InvoiceFile;
            pay.InvoiceFile = newRel;
            if (old == "" || string.Equals(old, newRel, StringComparison.OrdinalIgnoreCase)) return;
            if (p.Payments.Any(x => x != pay && x.InvoiceFile == old)) return;   // 旧票还挂在别的笔上
            RetireInvoice(st, p, old);
        }

        /// <summary>旧发票作废：从项目里移除（文件保留，可恢复）；拆出来的记住来源页，以后不再拆回来。</summary>
        public static void RetireInvoice(Store st, Project p, string old)
        {
            var rec = st.Lib.Invoices.FirstOrDefault(i => string.Equals(i.Rel, old, StringComparison.OrdinalIgnoreCase));
            if (rec != null && !string.Equals(rec.Source, rec.Rel, StringComparison.OrdinalIgnoreCase) && rec.Page > 0)
            {
                string key = rec.Source + "|" + rec.Page;
                if (!st.Lib.SkipInvoicePages.Contains(key)) st.Lib.SkipInvoicePages.Add(key);
            }
            var f = st.Lib.Files.FirstOrDefault(x => string.Equals(x.Rel, old, StringComparison.OrdinalIgnoreCase));
            if (f != null) RemoveFromProject(st, f, p);
        }

        /// <summary>新加进来的发票，看是不是本项目某张旧发票的重开票：同一销售方，金额相差不到 1%，号码不同。</summary>
        public static InvoiceRec ReissueOf(Store st, Project p, InvoiceRec inv)
        {
            if (inv == null || inv.Amount <= 0) return null;
            return st.InvoicesOf(p).Where(o => o.Rel != inv.Rel && (o.Members == null || o.Members.Count == 0) && o.Amount > 0
                    && Math.Abs(o.Amount - inv.Amount) <= Math.Max(1, o.Amount * 0.01)
                    && (o.Seller == "" || inv.Seller == "" || o.Seller == inv.Seller)
                    && !(o.Number != "" && o.Number == inv.Number))
                .OrderBy(o => Math.Abs(o.Amount - inv.Amount)).FirstOrDefault();
        }

        /// <summary>删除资料文件（进回收站），并清掉所有相关登记。</summary>
        public static void DeleteFile(Store st, FileRec f)
        {
            string full = st.FullPath(f);
            if (File.Exists(full)) Recycle(full);
            Forget(st, f.Rel, null);
        }

        /// <summary>用新文件替换旧版本：新文件放到旧文件所在文件夹，旧文件进回收站；分类、归属、财务关联沿用。返回新文件的相对路径。
        /// 换的是发票（含从合同、凭证里拆出的发票）时，新文件重新识别登记为发票，原来那一页以后不再拆回旧票。</summary>
        public static string ReplaceFile(Store st, FileRec old, string newPath)
        {
            bool wasInvoice = st.Lib.Invoices.Any(i => string.Equals(i.Rel, old.Rel, StringComparison.OrdinalIgnoreCase) || string.Equals(i.Source, old.Rel, StringComparison.OrdinalIgnoreCase))
                || old.Name.Contains("发票");
            var owner = st.Proj(old.Project);
            string oldFull = st.FullPath(old);
            if (string.Equals(Path.GetFullPath(newPath), Path.GetFullPath(oldFull), StringComparison.OrdinalIgnoreCase)) return old.Rel;
            string dir = Path.GetDirectoryName(oldFull);
            string tmp = Path.Combine(dir, "~替换中_" + Guid.NewGuid().ToString("N").Substring(0, 6) + Path.GetExtension(newPath));
            File.Copy(newPath, tmp);                     // 先复制成功，再动旧文件
            if (File.Exists(oldFull)) Recycle(oldFull);
            string target = Path.Combine(dir, Path.GetFileName(newPath));
            if (File.Exists(target))
            {
                string b = Path.Combine(dir, Path.GetFileNameWithoutExtension(target)), ext = Path.GetExtension(target); int i = 2;
                while (File.Exists(b + "(" + i + ")" + ext)) i++;
                target = b + "(" + i + ")" + ext;
            }
            File.Move(tmp, target);
            string newRel = target.Substring(st.Lib.Root.TrimEnd('\\').Length + 1);
            // 从旧文件里拆出过的发票：都已作废，从项目里移除（文件保留），付款关联改挂新文件
            foreach (var x in st.Lib.Invoices.Where(i => string.Equals(i.Source, old.Rel, StringComparison.OrdinalIgnoreCase) && !string.Equals(i.Rel, old.Rel, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                foreach (var pr in st.Lib.Projects) foreach (var pay in pr.Payments) if (pay.InvoiceFile == x.Rel) pay.InvoiceFile = newRel;
                if (!st.Lib.Hidden.Contains(x.Rel)) st.Lib.Hidden.Add(x.Rel);
                st.Lib.Invoices.Remove(x);
            }
            Forget(st, old.Rel, newRel);
            st.Scan(null);
            if (wasInvoice && owner != null && !owner.IsRoot) Invoices.RegisterFile(st, owner, newRel);
            st.Save();
            return newRel;
        }

        /// <summary>清掉某文件的登记；moveTo 不为空时把分类、归属、共用、财务关联转给新文件。</summary>
        static void Forget(Store st, string rel, string moveTo)
        {
            lock (st.Sync)
            {
                st.Lib.Files.RemoveAll(x => string.Equals(x.Rel, rel, StringComparison.OrdinalIgnoreCase));
                if (st.Texts.Remove(rel)) st.TextsDirty = true;
                string v; List<string> l;
                if (st.Lib.FileProject.TryGetValue(rel, out v)) { st.Lib.FileProject.Remove(rel); if (moveTo != null) st.Lib.FileProject[moveTo] = v; }
                if (st.Lib.FileCat.TryGetValue(rel, out v)) { st.Lib.FileCat.Remove(rel); if (moveTo != null) st.Lib.FileCat[moveTo] = v; }
                if (st.Lib.SharedWith.TryGetValue(rel, out l)) { st.Lib.SharedWith.Remove(rel); if (moveTo != null) st.Lib.SharedWith[moveTo] = l; }
                foreach (var gone in st.Lib.Invoices.Where(i => i.Rel == rel && i.Source != rel && i.Page > 0))
                {
                    // 拆出的发票被删除或换掉：记住来源页，以后“拆分发票”不再拆回来
                    string key = gone.Source + "|" + gone.Page;
                    if (!st.Lib.SkipInvoicePages.Contains(key)) st.Lib.SkipInvoicePages.Add(key);
                }
                st.Lib.Invoices.RemoveAll(i => i.Rel == rel);
                foreach (var p in st.Lib.Projects)
                    foreach (var pay in p.Payments)
                    {
                        if (pay.VoucherFile == rel) pay.VoucherFile = moveTo ?? "";
                        if (pay.InvoiceFile == rel) pay.InvoiceFile = moveTo ?? "";
                    }
            }
        }
    }

    /// <summary>一键导入：在选中的文件夹里识别出各个项目文件夹（底下的“1项目文件”“2项目人员”当作分类），并入已有项目或新建项目。</summary>
    public static class BatchImport
    {
        public class Item
        {
            public string Dir, Name, NewName;
            public bool Loose, Include = true;
            public List<string> Paths = new List<string>();   // 要复制的内容
            public int FileCount; public long Size;
            public Project Target;                          // null = 新建项目
        }

        static readonly HashSet<string> Skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "node_modules", "__pycache__", ".git", "$RECYCLE.BIN", "System Volume Information", "项目档案库", "项目档案室软件" };

        /// <summary>
        /// 识别规则（只看下一级，结果可预期）：
        /// ① 选中的文件夹下一级大多是“1项目合同”“2项目人员”这类分类文件夹（或没有子文件夹）→ 整个文件夹是一个项目；
        /// ② 否则下一级每个子文件夹就是一个项目；子文件夹是“2025年”这种归类夹、里面装着好几个项目时，往里展开一层；
        /// ③ 直接放在选中文件夹里的散件、和项目并列的分类文件夹，单列一项，默认不导入。
        /// </summary>
        public static List<Item> Analyze(Store st, string folder)
        {
            var items = new List<Item>();
            var d = new DirectoryInfo(folder.TrimEnd('\\'));
            var subs = Subs(d).Where(x => !st.Excluded(x.Name)).ToList();
            var loose = new List<string>();
            int cats = subs.Count(x => FolderNames.IsCategory(x.Name));
            if (subs.Count == 0 || cats * 2 >= subs.Count) items.Add(Whole(d));
            else
                foreach (var s in subs)
                {
                    var inner = Subs(s);
                    if (FolderNames.IsCategory(s.Name)) loose.Add(s.FullName);
                    else if (!FolderNames.IsProjectLike(s.Name) && inner.Count(x => FolderNames.IsProjectLike(x.Name)) >= 2 && inner.Count(x => FolderNames.IsCategory(x.Name)) * 2 < inner.Count)
                    {
                        foreach (var ss in inner) items.Add(Whole(ss));   // 归类夹：里面每个子文件夹是一个项目
                        try { loose.AddRange(s.GetFiles().Select(x => x.FullName)); } catch { }
                    }
                    else items.Add(Whole(s));
                }
            if (items.Count > 0 && !(subs.Count == 0 || cats * 2 >= subs.Count)) try { loose.AddRange(d.GetFiles().Select(x => x.FullName)); } catch { }
            if (loose.Count > 0) items.Add(new Item { Dir = d.FullName, Name = d.Name, Loose = true, Paths = loose });
            foreach (var it in items)
            {
                foreach (var pth in it.Paths)
                {
                    if (File.Exists(pth)) { it.FileCount++; it.Size += new FileInfo(pth).Length; }
                    else if (Directory.Exists(pth))
                        foreach (var f in SafeFiles(pth)) { it.FileCount++; try { it.Size += new FileInfo(f).Length; } catch { } }
                }
                it.NewName = it.Loose ? it.Name : FolderNames.CleanName(it.Name);
                it.Target = Match(st, it.Name);
                if (it.Loose && it.Target == null) it.Include = false;
            }
            return items.Where(x => x.FileCount > 0).ToList();
        }

        static Item Whole(DirectoryInfo d)
        {
            var it = new Item { Dir = d.FullName, Name = d.Name };
            it.Paths.AddRange(Subs(d).Select(x => x.FullName));
            try { it.Paths.AddRange(d.GetFiles().Select(x => x.FullName)); } catch { }
            return it;
        }

        static IEnumerable<string> SafeFiles(string dir)
        {
            var list = new List<string>();
            try { list.AddRange(Directory.GetFiles(dir)); } catch { }
            try { foreach (var d in Directory.GetDirectories(dir)) if (!Skip.Contains(Path.GetFileName(d))) list.AddRange(SafeFiles(d)); } catch { }
            return list;
        }

        static List<DirectoryInfo> Subs(DirectoryInfo d)
        {
            try { return d.GetDirectories().Where(x => !Skip.Contains(x.Name) && (x.Attributes & FileAttributes.Hidden) == 0).ToList(); }
            catch { return new List<DirectoryInfo>(); }
        }

        static bool HasProjectBelow(DirectoryInfo d, int depth)
        {
            if (depth <= 0) return false;
            foreach (var s in Subs(d)) if (FolderNames.IsProjectLike(s.Name) || HasProjectBelow(s, depth - 1)) return true;
            return false;
        }

        static void Detect(DirectoryInfo d, List<Item> items, int depth)
        {
            var subs = Subs(d);
            int projSubs = subs.Count(x => FolderNames.IsProjectLike(x.Name));
            int catSubs = subs.Count(x => FolderNames.IsCategory(x.Name));
            bool self = (FolderNames.IsProjectLike(d.Name) || (projSubs == 0 && (catSubs > 0 || depth == 0))) && projSubs < 2;
            if (self || depth >= 3)
            {
                var it = new Item { Dir = d.FullName, Name = d.Name };
                it.Paths.AddRange(subs.Select(x => x.FullName));
                try { it.Paths.AddRange(d.GetFiles().Select(x => x.FullName)); } catch { }
                items.Add(it);
                return;
            }
            var loose = new List<string>();
            foreach (var s in subs)
            {
                if (FolderNames.IsProjectLike(s.Name) || HasProjectBelow(s, 2)) Detect(s, items, depth + 1);
                else loose.Add(s.FullName);
            }
            try { loose.AddRange(d.GetFiles().Select(x => x.FullName)); } catch { }
            if (loose.Count > 0) items.Add(new Item { Dir = d.FullName, Name = d.Name, Loose = true, Paths = loose });
        }

        static string Loose(string s)
        {
            return Regex.Replace(FolderNames.Key(s), @"项目|工程|资料|文件|20\d\d年?", "");
        }

        static string Norm(string s)
        {
            // 去掉编号、地区前缀、年份和“农村公路、水毁整治”这类通用词，只留地名、批次等有区分度的部分（年份单独比）
            return Regex.Replace(FolderNames.Key(s), @"项目|工程|资料|文件|20\d\d年?|农村公路|公路|道路|村道|通组|通畅|水毁|整治|抢险|硬化|改造|改建|拓宽|提升|安全生产|基础设施|建设|施工", "");
        }

        /// <summary>两个名字共有的字数 ÷ 较短名字的字数（0～1）。</summary>
        static double Shared(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return 0;
            var pool = b.ToList(); int n = 0;
            foreach (var ch in a) { int i = pool.IndexOf(ch); if (i >= 0) { n++; pool.RemoveAt(i); } }
            return (double)n / Math.Min(a.Length, b.Length);
        }

        static int Lcs(string a, string b)
        {
            int best = 0; var prev = new int[b.Length + 1];
            for (int i = 1; i <= a.Length; i++)
            {
                var cur = new int[b.Length + 1];
                for (int j = 1; j <= b.Length; j++) if (a[i - 1] == b[j - 1]) { cur[j] = prev[j - 1] + 1; if (cur[j] > best) best = cur[j]; }
                prev = cur;
            }
            return best;
        }

        /// <summary>按名称找已有项目（文件夹同名，或去掉“涪陵区/李渡街道/工程”等后高度相似）。</summary>
        public static Project Match(Store st, string name)
        {
            string key = FolderNames.Key(name);
            string n = Norm(name), ln = Loose(name);
            var yn = Regex.Match(name ?? "", @"20\d\d").Value;
            var score = new Dictionary<Project, double>();
            foreach (var p in st.RealProjects)
                foreach (var cand in new[] { p.Name }.Concat(p.Folders))
                {
                    if (string.Equals(cand, name, StringComparison.OrdinalIgnoreCase) || (key.Length >= 4 && FolderNames.Key(cand) == key)) return p;   // 同名（去掉编号后）
                    var yc = Regex.Match(cand, @"20\d\d").Value;
                    if (yn != "" && yc != "" && yn != yc) continue;   // 年份不同，不是同一个项目
                    string c = Norm(cand), lc = Loose(cand);
                    double sc = 0;
                    if (n.Length >= 2 && c.Length >= 2)
                    {
                        int l = Lcs(n, c); double r = (double)l / Math.Min(n.Length, c.Length);
                        if (l >= 3 && r >= 0.75) sc = l + r + Shared(ln, lc);   // 再按两个全名共有的字分高下（如三个“石致路”项目）
                    }
                    else if (n.Length < 2 && c.Length < 2 && yn != "" && yn == yc && ln.Length >= 4 && lc.Length >= 4)
                    {
                        // 名字里只有通用词（如“2025年农村公路安全生产工程”）：同年份、全名高度相似才算
                        int l = Lcs(ln, lc); double r = (double)l / Math.Min(ln.Length, lc.Length);
                        if (r >= 0.8) sc = l + r;
                    }
                    if (sc > 0 && (!score.ContainsKey(p) || score[p] < sc)) score[p] = sc;
                }
            var top = score.OrderByDescending(kv => kv.Value).ToList();
            if (top.Count == 0) return null;
            if (top.Count > 1 && top[1].Value >= top[0].Value - 0.01) return null;   // 同样像两个项目：不自动并入，让用户选
            return top[0].Key;
        }

        public static List<string> Run(Store st, List<Item> items, Action<string> progress)
        {
            var log = new List<string>();
            string root = st.Lib.Root.TrimEnd('\\');
            foreach (var it in items.Where(x => x.Include))
            {
                var p = it.Target;
                // 本来就在档案文件夹第一层的文件夹：直接登记成项目，不再复制一份
                if (!it.Loose && string.Equals(Path.GetDirectoryName(it.Dir.TrimEnd('\\')), root, StringComparison.OrdinalIgnoreCase))
                {
                    string top = Path.GetFileName(it.Dir.TrimEnd('\\'));
                    var owner = st.ProjectForTop(top, false);
                    if (p == null) p = owner;
                    if (p == null)
                    {
                        string nm = string.IsNullOrWhiteSpace(it.NewName) ? FolderNames.CleanName(it.Name) : it.NewName.Trim();
                        p = new Project { Id = st.NewProjectId(), Name = nm, Type = Store.GuessType(nm) };
                        p.Code = p.Id; p.Required = Store.DefaultRequired(p.Type);
                        Builder.Complete(st, p);
                        st.Lib.Projects.Add(p);
                        log.Add("＋ 新建项目：" + p.Name);
                    }
                    foreach (var o in st.Lib.Projects) if (o != p) o.Folders.RemoveAll(f => string.Equals(f, top, StringComparison.OrdinalIgnoreCase));
                    if (!p.Folders.Contains(top)) p.Folders.Add(top);
                    st.Lib.Excludes.RemoveAll(x => string.Equals(x, top, StringComparison.OrdinalIgnoreCase));
                    log.Add("【" + p.Name + "】已登记文件夹“" + top + "”（文件本来就在档案文件夹里，不复制）");
                    continue;
                }
                if (p == null)
                {
                    string name = string.IsNullOrWhiteSpace(it.NewName) ? it.Name : it.NewName.Trim();
                    p = st.RealProjects.FirstOrDefault(x => x.Name == name);
                    if (p == null)
                    {
                        p = new Project { Id = st.NewProjectId(), Name = name, Type = Store.GuessType(name) };
                        p.Code = p.Id; p.Required = Store.DefaultRequired(p.Type);
                        string safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
                        // 根目录已有同名文件夹且属于别的项目时，换个名字
                        string folder = safe; int k = 2;
                        while (st.ProjectForTop(folder, false) != null && st.ProjectForTop(folder, false) != p) folder = safe + "(" + k++ + ")";
                        p.Folders.Add(folder);
                        Builder.Complete(st, p);
                        st.Lib.Projects.Add(p);
                        log.Add("＋ 新建项目：" + p.Name);
                    }
                }
                var lines = st.Import(it.Paths, p, false, progress);
                int ok = lines.Count(x => x.StartsWith("✓") && !x.StartsWith("✓ 已解压")), dup = lines.Count(x => x.StartsWith("·")), bad = lines.Count(x => x.StartsWith("×"));
                log.Add("【" + p.Name + "】导入 " + ok + " 份" + (dup > 0 ? "，重复跳过 " + dup + " 份" : "") + (bad > 0 ? "，失败 " + bad + " 份" : "") + "（来自 " + it.Dir + "）");
                log.AddRange(lines.Where(x => x.StartsWith("×")));
            }
            st.Scan(progress);
            st.Save();
            return log;
        }
    }
}
