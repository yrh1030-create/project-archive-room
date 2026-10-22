using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;

namespace DangAn
{
    public static class Cats
    {
        public static readonly string[] All = {
            "01 立项及计划", "02 设计及概预算", "03 招标采购", "04 合同协议", "05 会议纪要及决策",
            "06 施工及监理", "07 检测验收", "08 结算决算审计", "09 资金支付", "10 图纸照片", "11 人员及资质", "99 其他"
        };
        // 按优先级排列：先命中者为准
        static readonly string[][] Rules = {
            new[] { "05 会议纪要及决策", "会议纪要|纪要|党工委|会议记录|专题会" },
            new[] { "03 招标采购", "中标|招标|比选|询价|投标|成交|公示|邀请函|限价|清单|采购" },
            new[] { "04 合同协议", "合同|协议" },
            new[] { "08 结算决算审计", "结算|决算|审核报告|一审|二审|审计|财评|定案|审定" },
            new[] { "07 检测验收", "验收|检测|交工|竣工|试验|鉴定" },
            new[] { "11 人员及资质", "人员|项目经理|技术负责人|建造师|资格证|资质|身份证|社保|职称|安全员|施工员|上岗证|营业执照" },
            new[] { "09 资金支付", "支付|凭证|拨付|资金|债券|流水|发票|保证金|总账|余额表|付款|收据|报账|支出" },
            new[] { "02 设计及概预算", "设计|施工图|概算|预算|图纸|勘察|测量|变更|图集|总体设计" },
            new[] { "01 立项及计划", "立项|可研|可行性|工可|建议书|计划|申报|申请|批复|下达|规划|选址|用地|项目基本情况" },
            new[] { "06 施工及监理", "施工|监理|签证|收方|开工|日志|进度|安全|整改|图片|照片|影像" },
        };
        static readonly string[][] Split;
        static Cats()
        {
            Split = new string[Rules.Length][];
            for (int i = 0; i < Rules.Length; i++) Split[i] = Rules[i][1].Split('|');
        }
        static string Match(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Replace("建设计划", "立项计划");   // “建设计划”里含“设计”二字，避免误分到设计类
            for (int i = 0; i < Rules.Length; i++)
                foreach (var k in Split[i]) if (s.IndexOf(k, StringComparison.Ordinal) >= 0) return Rules[i][0];
            return null;
        }
        public static string Classify(string rel)
        {
            string name = Path.GetFileNameWithoutExtension(rel);
            string ext = Path.GetExtension(rel).ToLowerInvariant();
            string c = Match(name);
            if (c != null) return c;
            // 再看所在文件夹（从近到远）
            var parts = rel.Split('\\');
            for (int i = parts.Length - 2; i >= 1; i--) { c = Match(parts[i]); if (c != null) return c; }
            if (ext == ".dwg" || ext == ".dxf" || ext == ".ovobj") return "02 设计及概预算";
            if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".heic" || ext == ".mp4") return "10 图纸照片";
            return "99 其他";
        }
        public static readonly string[] StageNames = { "立项", "设计审批", "招标采购", "签订合同", "施工", "检测验收", "结算决算" };
        public static readonly string[] StageCats = { "01 立项及计划", "02 设计及概预算", "03 招标采购", "04 合同协议", "06 施工及监理", "07 检测验收", "08 结算决算审计" };
        public static string Short(string cat) { return cat.Length > 3 ? cat.Substring(3) : cat; }

        /// <summary>给人看的分组名称。</summary>
        public static string Title(string cat)
        {
            switch (cat.Substring(0, 2))
            {
                case "01": return "上级文件 · 立项计划";
                case "02": return "设计 · 概预算";
                case "03": return "招标采购";
                case "04": return "合同协议";
                case "05": return "会议纪要 · 决策";
                case "06": return "施工 · 监理";
                case "07": return "检测验收";
                case "08": return "结算 · 决算 · 审计";
                case "09": return "资金支付";
                case "10": return "图纸照片";
                case "11": return "人员 · 资质";
                default: return "其他资料";
            }
        }
    }

    public class Store
    {
        public const string RootProjectId = "_root";
        public const string LibFile = "档案库.json";
        public const string TextFile = "全文索引.json";
        public static readonly string[] DefaultExcludes = { "项目档案库", "项目档案室软件", "项目档案室_发布包", "outputs", "work_*", "node_modules", "__pycache__", ".git", "$RECYCLE.BIN" };
        public static readonly string[] StdFields = {
            "项目名称", "项目代码", "建设／实施单位", "建设地点", "建设内容及规模", "立项／上级建设计划", "投资概算／可研投资",
            "资金来源及到位情况", "中标／施工单位", "施工合同／成交金额", "实际投资额（元）", "实际开工时间", "实际竣工／完工时间",
            "工程进度及验收", "决策／采购形式", "其他说明及资料缺项"
        };

        public string DbDir;
        public Library Lib = new Library();
        public Dictionary<string, string> Texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public bool TextsDirty;
        public readonly object Sync = new object();

        public string LibPath { get { return Path.Combine(DbDir, LibFile); } }
        public string TextPath { get { return Path.Combine(DbDir, TextFile); } }

        public static JavaScriptSerializer Ser()
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue; s.RecursionLimit = 1000;
            return s;
        }

        public static Store Load(string dbDir)
        {
            var st = new Store();
            st.DbDir = dbDir;
            st.Lib = Ser().Deserialize<Library>(File.ReadAllText(st.LibPath, Encoding.UTF8));
            if (st.Lib.FileProject == null) st.Lib.FileProject = new Dictionary<string, string>();
            if (st.Lib.FileCat == null) st.Lib.FileCat = new Dictionary<string, string>();
            if (st.Lib.FillFields == null) st.Lib.FillFields = new List<string>();
            if (st.Lib.TextTried == null) st.Lib.TextTried = new Dictionary<string, string>();
            if (st.Lib.SharedWith == null) st.Lib.SharedWith = new Dictionary<string, List<string>>();
            foreach (var p in st.Lib.Projects) if (p.Payments == null) p.Payments = new List<Payment>();
            if (st.Lib.Invoices == null) st.Lib.Invoices = new List<InvoiceRec>();
            foreach (var i in st.Lib.Invoices) if (i.Members == null) i.Members = new List<string>();
            if (st.Lib.Excludes == null || st.Lib.Excludes.Count == 0) st.Lib.Excludes = DefaultExcludes.ToList();
            if (!st.Lib.Excludes.Contains("work_*")) st.Lib.Excludes.Add("work_*");   // 工作临时文件夹不当项目
            if (st.Lib.Hidden == null) st.Lib.Hidden = new List<string>();
            if (st.Lib.SkipInvoicePages == null) st.Lib.SkipInvoicePages = new List<string>();
            // 档案库随文件夹整体搬动（换盘符、拷到别的电脑）时，自动认上一级目录为根目录
            if (!Directory.Exists(st.Lib.Root ?? ""))
            {
                var parent = Directory.GetParent(dbDir);
                if (parent != null) st.Lib.Root = parent.FullName;
            }
            if (File.Exists(st.TextPath))
            {
                var t = Ser().Deserialize<Dictionary<string, string>>(File.ReadAllText(st.TextPath, Encoding.UTF8));
                st.Texts = new Dictionary<string, string>(t, StringComparer.OrdinalIgnoreCase);
            }
            st.EnsureRoot();
            return st;
        }

        public static Store Create(string root)
        {
            var st = new Store();
            st.DbDir = Path.Combine(root, "项目档案库");
            Directory.CreateDirectory(st.DbDir);
            st.Lib.Root = root;
            st.Lib.Excludes = DefaultExcludes.ToList();
            st.EnsureRoot();
            return st;
        }

        public void EnsureRoot()
        {
            if (!Lib.Projects.Any(p => p.Id == RootProjectId))
                Lib.Projects.Add(new Project { Id = RootProjectId, Name = "未归属资料（根目录散件）", Type = "待分配" });
        }

        static void WriteAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Copy(path, path + ".bak", true);
                File.Delete(path);
            }
            File.Move(tmp, path);
        }

        public void Save()
        {
            lock (Sync) SaveCore();
        }

        void SaveCore()
        {
            Directory.CreateDirectory(DbDir);
            Lib.Updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            WriteAtomic(LibPath, Ser().Serialize(Lib));
            if (TextsDirty || !File.Exists(TextPath)) { WriteAtomic(TextPath, Ser().Serialize(Texts)); TextsDirty = false; }
        }

        public string Backup()
        {
            if (string.IsNullOrEmpty(Lib.BackupDir)) throw new Exception("还没有设置备份位置。");
            string dir = Path.Combine(Lib.BackupDir, "项目档案库备份_" + DateTime.Now.ToString("yyyyMMdd_HHmm"));
            Directory.CreateDirectory(dir);
            Save();
            File.Copy(LibPath, Path.Combine(dir, LibFile), true);
            if (File.Exists(TextPath)) File.Copy(TextPath, Path.Combine(dir, TextFile), true);
            return dir;
        }

        // ---------------- 项目与文件 ----------------
        public Project Proj(string id) { return Lib.Projects.FirstOrDefault(p => p.Id == id); }
        public string FullPath(FileRec f) { return Path.Combine(Lib.Root, f.Rel); }
        public IEnumerable<FileRec> FilesOf(Project p) { return Lib.Files.Where(f => InProject(f, p.Id)); }

        /// <summary>文件是否属于（或共用到）某项目。</summary>
        public bool InProject(FileRec f, string pid)
        {
            if (f.Project == pid) return true;
            List<string> l;
            return Lib.SharedWith.Count > 0 && Lib.SharedWith.TryGetValue(f.Rel, out l) && l.Contains(pid);
        }

        /// <summary>除归属项目外，还共用到哪些项目。</summary>
        public List<string> SharedOf(FileRec f)
        {
            List<string> l;
            return Lib.SharedWith.TryGetValue(f.Rel, out l) ? l.Where(x => x != f.Project && Proj(x) != null).ToList() : new List<string>();
        }
        public IEnumerable<Project> RealProjects { get { return Lib.Projects.Where(p => !p.IsRoot); } }

        public string NewProjectId()
        {
            int n = 1;
            while (Lib.Projects.Any(p => p.Id == "P" + n.ToString("000"))) n++;
            return "P" + n.ToString("000");
        }

        public string ProjectFolder(Project p)
        {
            if (p.Folders.Count > 0) return Path.Combine(Lib.Root, p.Folders[0]);
            string safe = string.Join("_", p.Name.Split(Path.GetInvalidFileNameChars()));
            p.Folders.Add(safe);
            return Path.Combine(Lib.Root, safe);
        }

        /// <summary>不参与扫描的顶层文件夹；支持末尾通配符，如“work_*”。</summary>
        public bool Excluded(string topName)
        {
            foreach (var raw in Lib.Excludes)
            {
                var e = raw.Trim();
                if (e.EndsWith("*") ? topName.StartsWith(e.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)
                                    : string.Equals(e, topName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        static readonly HashSet<string> SkipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "node_modules", "__pycache__", ".git", "$RECYCLE.BIN", "System Volume Information" };

        void Walk(string dir, List<string> outp)
        {
            try { foreach (var f in Directory.EnumerateFiles(dir)) outp.Add(f); } catch { }
            try
            {
                foreach (var d in Directory.EnumerateDirectories(dir))
                {
                    if (SkipDirs.Contains(Path.GetFileName(d))) continue;
                    Walk(d, outp);
                }
            }
            catch { }
        }

        public Project ProjectForTop(string top, bool create)
        {
            foreach (var p in Lib.Projects)
                foreach (var f in p.Folders) if (string.Equals(f, top, StringComparison.OrdinalIgnoreCase)) return p;
            if (!create) return null;
            // “1项目合同”“2项目人员”这类是某个项目里的分类文件夹（多半是把单个项目文件夹当成了总目录）：
            // 不各自建项目，统一归到以总目录名命名的那个项目
            if (FolderNames.IsCategory(top))
            {
                string rootName = Path.GetFileName(Lib.Root.TrimEnd('\\'));
                var host = Lib.Projects.FirstOrDefault(p => !p.IsRoot && p.Name == rootName);
                if (host == null)
                {
                    host = new Project { Id = NewProjectId(), Name = rootName, Type = GuessType(rootName) };
                    host.Required = DefaultRequired(host.Type); host.Code = host.Id;
                    host.Fields.Add(new Field("项目名称", rootName, "总目录名称"));
                    Lib.Projects.Add(host);
                }
                host.Folders.Add(top);
                return host;
            }
            var np = new Project { Id = NewProjectId(), Name = top, Type = GuessType(top) };
            np.Folders.Add(top);
            np.Required = DefaultRequired(np.Type);
            np.Code = np.Id;
            np.Fields.Add(new Field("项目名称", top, "文件夹名称"));
            Lib.Projects.Add(np);
            return np;
        }

        public static string GuessType(string name)
        {
            if (Regex(name, "抢险|水毁|灾后|破损")) return "抢险整治";
            if (Regex(name, "养护")) return "日常养护";
            if (Regex(name, "本级财政|补助")) return "财政补助";
            if (Regex(name, "通组|通畅|硬化")) return "通组公路";
            if (Regex(name, "安全")) return "安全设施";
            return "道路工程";
        }
        static bool Regex(string s, string pat) { foreach (var k in pat.Split('|')) if (s.Contains(k)) return true; return false; }

        public static List<string> DefaultRequired(string type)
        {
            if (type == "抢险整治" || type == "日常养护")
                return new List<string> { "04 合同协议", "05 会议纪要及决策", "07 检测验收", "09 资金支付" };
            return new List<string> { "01 立项及计划", "02 设计及概预算", "03 招标采购", "04 合同协议", "07 检测验收", "08 结算决算审计" };
        }

        public string Assign(string rel)
        {
            string pid;
            if (Lib.FileProject.TryGetValue(rel, out pid) && Proj(pid) != null) return pid;
            int i = rel.IndexOf('\\');
            if (i < 0) return RootProjectId;
            var p = ProjectForTop(rel.Substring(0, i), true);
            return p.Id;
        }

        /// <summary>文件归属的项目（不新建项目）；找不到返回 null。</summary>
        public Project OwnerOf(string rel)
        {
            string pid;
            if (Lib.FileProject.TryGetValue(rel, out pid) && Proj(pid) != null) return Proj(pid);
            int i = rel.IndexOf('\\');
            return i < 0 ? Proj(RootProjectId) : ProjectForTop(rel.Substring(0, i), false);
        }

        /// <summary>本项目里被“移除”（文件还在）的资料。</summary>
        public List<string> HiddenOf(Project p)
        {
            return Lib.Hidden.Where(r => { var o = OwnerOf(r); return o != null && o.Id == p.Id && File.Exists(Path.Combine(Lib.Root, r)); }).ToList();
        }

        /// <summary>把移除的资料恢复显示（可同时改归属项目）。</summary>
        public void Unhide(string rel, Project to)
        {
            Lib.Hidden.RemoveAll(x => string.Equals(x, rel, StringComparison.OrdinalIgnoreCase));
            if (to != null) { var o = OwnerOf(rel); if (o == null || o.Id != to.Id) Lib.FileProject[rel] = to.Id; }
            if (!Lib.Files.Any(f => string.Equals(f.Rel, rel, StringComparison.OrdinalIgnoreCase)))
            {
                var fi = new FileInfo(Path.Combine(Lib.Root, rel));
                if (fi.Exists) Lib.Files.Add(new FileRec { Rel = rel, Size = fi.Length, Time = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"), Project = Assign(rel), Cat = CatOf(rel) });
            }
        }

        /// <summary>项目里有效的发票登记（排除已移除、文件已不在的）。</summary>
        public List<InvoiceRec> InvoicesOf(Project p)
        {
            var live = new HashSet<string>(Lib.Files.Select(f => f.Rel), StringComparer.OrdinalIgnoreCase);
            return Lib.Invoices.Where(i => i.Project == p.Id && live.Contains(i.Rel)).ToList();
        }

        public string CatOf(string rel)
        {
            string c;
            if (Lib.FileCat.TryGetValue(rel, out c)) return c;
            return Cats.Classify(rel);
        }

        /// <summary>扫描根目录，更新文件登记，并为新文件提取文字。返回(新增, 删除)。</summary>
        public int[] Scan(Action<string> progress)
        {
            lock (Sync) return ScanCore(progress);
        }

        int[] ScanCore(Action<string> progress)
        {
            string root = Lib.Root.TrimEnd('\\');
            var all = new List<string>();
            try { foreach (var f in Directory.EnumerateFiles(root)) all.Add(f); } catch { }
            foreach (var d in Directory.EnumerateDirectories(root))
            {
                string top = Path.GetFileName(d);
                if (Excluded(top) || SkipDirs.Contains(top)) continue;
                if (progress != null) progress("正在扫描：" + top);
                Walk(d, all);
            }
            var old = new Dictionary<string, FileRec>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Lib.Files) old[f.Rel] = f;
            var list = new List<FileRec>(all.Count);
            var hidden = new HashSet<string>(Lib.Hidden ?? new List<string>(), StringComparer.OrdinalIgnoreCase);   // 用户从项目里移除、不再显示的文件
            int added = 0, n = 0;
            foreach (var full in all)
            {
                n++;
                string rel = full.Substring(root.Length + 1);
                string fname = Path.GetFileName(rel);
                if (fname.StartsWith("~$") || fname.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) || fname.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                if (hidden.Contains(rel)) continue;
                FileInfo fi;
                try { fi = new FileInfo(full); } catch { continue; }
                string time = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                FileRec r;
                bool changed = true;
                if (old.TryGetValue(rel, out r)) { changed = r.Size != fi.Length || r.Time != time; }
                else { r = new FileRec { Rel = rel }; added++; }
                r.Size = fi.Length; r.Time = time;
                r.Project = Assign(rel);
                r.Cat = CatOf(rel);
                list.Add(r);
                if (IsOffice(r.Ext) && (changed || !Texts.ContainsKey(rel)))
                {
                    string t = ExtractText(full, r.Ext);
                    if (!string.IsNullOrEmpty(t)) { Texts[rel] = t; TextsDirty = true; }
                }
                if (progress != null && n % 500 == 0) progress("已登记 " + n + " / " + all.Count + " 份文件");
            }
            var keep = new HashSet<string>(list.Select(f => f.Rel), StringComparer.OrdinalIgnoreCase);
            int removed = Lib.Files.Count(f => !keep.Contains(f.Rel));
            foreach (var k in Texts.Keys.Where(k => !keep.Contains(k)).ToList()) { Texts.Remove(k); TextsDirty = true; }
            foreach (var k in Lib.SharedWith.Keys.Where(k => !keep.Contains(k)).ToList()) Lib.SharedWith.Remove(k);
            Lib.Files = list;
            return new[] { added, removed };
        }

        static bool IsOffice(string ext) { return ext == ".docx" || ext == ".xlsx" || ext == ".txt" || ext == ".csv" || ext == ".md"; }

        // ---------------- 文字提取 ----------------
        public const int MaxText = 60000;

        public static string ExtractText(string path, string ext)
        {
            try
            {
                if (new FileInfo(path).Length > 80L * 1024 * 1024) return null;
                if (ext == ".txt" || ext == ".csv" || ext == ".md")
                {
                    var bytes = File.ReadAllBytes(path);
                    string s = DecodeText(bytes);
                    return Trim(s);
                }
                if (ext == ".docx") return Trim(Docx(path));
                if (ext == ".xlsx") return Trim(Xlsx(path));
            }
            catch { }
            return null;
        }

        static string DecodeText(byte[] b)
        {
            try { return new UTF8Encoding(false, true).GetString(b); }
            catch { return Encoding.GetEncoding("GB18030").GetString(b); }
        }

        static string Trim(string s)
        {
            if (s == null) return null;
            s = s.Replace("\r\n", "\n").Replace('\r', '\n');
            if (s.Trim().Length == 0) return null;
            return s.Length > MaxText ? s.Substring(0, MaxText) : s;
        }

        static string Docx(string path)
        {
            var sb = new StringBuilder();
            using (var z = ZipFile.OpenRead(path))
            {
                var e = z.GetEntry("word/document.xml");
                if (e == null) return null;
                using (var r = XmlReader.Create(e.Open()))
                {
                    while (r.Read())
                    {
                        if (r.NodeType == XmlNodeType.Element && r.LocalName == "t") sb.Append(r.ReadElementContentAsString());
                        else if (r.NodeType == XmlNodeType.Element && r.LocalName == "tab") sb.Append('\t');
                        else if (r.NodeType == XmlNodeType.EndElement && (r.LocalName == "p")) sb.Append('\n');
                        if (sb.Length > MaxText) break;
                    }
                }
            }
            return sb.ToString();
        }

        static string Xlsx(string path)
        {
            var sb = new StringBuilder();
            using (var z = ZipFile.OpenRead(path))
            {
                var shared = new List<string>();
                var se = z.GetEntry("xl/sharedStrings.xml");
                if (se != null)
                    using (var r = XmlReader.Create(se.Open()))
                    {
                        StringBuilder cur = null;
                        while (r.Read())
                        {
                            if (r.NodeType == XmlNodeType.Element && r.LocalName == "si") cur = new StringBuilder();
                            else if (r.NodeType == XmlNodeType.Element && r.LocalName == "t" && cur != null) cur.Append(r.ReadElementContentAsString());
                            if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "si" && cur != null) { shared.Add(cur.ToString()); cur = null; }
                        }
                    }
                foreach (var e in z.Entries.Where(x => x.FullName.StartsWith("xl/worksheets/sheet") && x.FullName.EndsWith(".xml")).OrderBy(x => x.FullName))
                {
                    using (var r = XmlReader.Create(e.Open()))
                    {
                        var row = new List<string>();
                        string t = null;
                        while (r.Read())
                        {
                            if (r.NodeType == XmlNodeType.Element && r.LocalName == "c") t = r.GetAttribute("t");
                            else if (r.NodeType == XmlNodeType.Element && (r.LocalName == "v" || (r.LocalName == "t" && t == "inlineStr")))
                            {
                                string v = r.ReadElementContentAsString();
                                int idx;
                                if (t == "s" && int.TryParse(v, out idx) && idx < shared.Count) v = shared[idx];
                                if (v.Trim().Length > 0) row.Add(v);
                            }
                            if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "row")
                            {
                                if (row.Count > 0) sb.Append(string.Join(" | ", row)).Append('\n');
                                row.Clear();
                            }
                            if (sb.Length > MaxText) return sb.ToString();
                        }
                    }
                }
            }
            return sb.ToString();
        }

        // ---------------- 统计 ----------------
        public ProjectInfo Info(Project p)
        {
            var info = new ProjectInfo();
            foreach (var f in Lib.Files)
            {
                if (!InProject(f, p.Id)) continue;
                info.FileCount++;
                int c; info.CatCount.TryGetValue(f.Cat, out c); info.CatCount[f.Cat] = c + 1;
            }
            foreach (var r in p.Required) if (!info.CatCount.ContainsKey(r)) info.Missing.Add(r);
            for (int i = Cats.StageCats.Length - 1; i >= 0; i--)
                if (info.CatCount.ContainsKey(Cats.StageCats[i])) { info.StageIndex = i; break; }
            if (!string.IsNullOrEmpty(p.Stage)) info.Stage = p.Stage;
            else if (info.StageIndex < 0) info.Stage = "未开始归档";
            else if (info.StageIndex == Cats.StageNames.Length - 1)
            {
                bool approved = Lib.Files.Any(f => InProject(f, p.Id) && f.Cat == Cats.StageCats[6] && f.Name.Contains("批复"));
                info.Stage = approved ? "决算已批复" : "结算决算中";
            }
            else info.Stage = "已到：" + Cats.StageNames[info.StageIndex];
            if (p.IsRoot) info.Stage = "—";
            foreach (var c in p.Contracts) { info.ContractTotal += c.Amount; info.PaidTotal += c.Paid; info.UnpaidTotal += c.Unpaid; }
            return info;
        }

        // ---------------- 概览用的摘要 ----------------
        static readonly Dictionary<string, string> KeyWords = new Dictionary<string, string> {
            { "01 立项及计划", "批复|下达|计划|立项" }, { "02 设计及概预算", "批复|概算|施工图|设计" },
            { "03 招标采购", "中标通知|成交通知|中标|成交|公示" }, { "04 合同协议", "施工合同|合同|协议" },
            { "05 会议纪要及决策", "纪要|会议" }, { "06 施工及监理", "开工|监理|签证|收方" },
            { "07 检测验收", "验收证书|交工|竣工|验收|检测" }, { "08 结算决算审计", "决算批复|财务决算|决算|结算审核|审核报告|结算" },
            { "09 资金支付", "支出明细|支付|凭证" }
        };

        /// <summary>某类资料里最有代表性的一份（批复、中标通知书、施工合同、验收证书、决算批复…）。</summary>
        public FileRec KeyFile(Project p, string cat)
        {
            return SortForDisplay(Lib.Files.Where(f => InProject(f, p.Id) && f.Cat == cat)).FirstOrDefault();
        }

        /// <summary>按“重要程度”排序：批复/中标通知/施工合同/验收证书/决算批复在前，其次 PDF、Word，副本(1)靠后。</summary>
        public static IEnumerable<FileRec> SortForDisplay(IEnumerable<FileRec> files)
        {
            return files.OrderBy(f =>
            {
                string kw;
                if (!KeyWords.TryGetValue(f.Cat, out kw)) return 99;
                int k = Array.FindIndex(kw.Split('|'), x => f.Name.Contains(x));
                return k < 0 ? 99 : k;
            }).ThenBy(f => f.Ext == ".pdf" || f.Ext == ".docx" || f.Ext == ".doc" || f.Ext == ".ofd" ? 0 : f.Ext == ".xlsx" || f.Ext == ".xls" ? 1 : 2)
              .ThenBy(f => f.Name.Contains("(1)") || f.Name.Contains("(2)") ? 1 : 0).ThenBy(f => f.Name.Length).ThenBy(f => f.Name);
        }

        public static double FirstMoney(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var m = System.Text.RegularExpressions.Regex.Match(s.Replace(",", "").Replace("，", ""), @"([0-9]+(?:\.[0-9]+)?)\s*(万元|元)");
            if (!m.Success) return 0;
            double v = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            return m.Groups[2].Value == "万元" ? v * 10000 : v;
        }

        public static string FirstDate(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var m = System.Text.RegularExpressions.Regex.Match(s, @"(20\d\d)[-年.](\d{1,2})(?:[-月.](\d{1,2}))?");
            if (!m.Success) return "";
            return m.Groups[1].Value + "-" + m.Groups[2].Value.PadLeft(2, '0') + (m.Groups[3].Success ? "-" + m.Groups[3].Value.PadLeft(2, '0') : "");
        }

        public string ContractorOf(Project p)
        {
            var c = p.Contracts.FirstOrDefault(x => x.Kind.Contains("施工") && x.Party != "");
            if (c != null) return c.Party;
            return p.Get("中标／施工单位");
        }

        /// <summary>项目金额：优先实际投资/审定，其次合同合计，再次施工合同字段。返回(金额, 口径)。</summary>
        public KeyValuePair<double, string> Amount(Project p)
        {
            double v = FirstMoney(p.Get("实际投资额（元）"));
            if (v > 0) return new KeyValuePair<double, string>(v, "实际投资");
            v = p.Contracts.Sum(c => c.Amount);
            if (v > 0) return new KeyValuePair<double, string>(v, "合同合计");
            v = FirstMoney(p.Get("施工合同／成交金额"));
            if (v > 0) return new KeyValuePair<double, string>(v, "施工合同");
            v = FirstMoney(p.Get("重大事项：金额（元）"));
            if (v == 0) { double d; if (double.TryParse(p.Get("重大事项：金额（元）"), out d)) v = d; }
            if (v > 0) return new KeyValuePair<double, string>(v, "登记金额");
            return new KeyValuePair<double, string>(0, "");
        }

        public string Period(Project p)
        {
            string a = FirstDate(p.Get("实际开工时间")), b = FirstDate(p.Get("实际竣工／完工时间"));
            if (a == "" && b == "")
            {
                var per = p.Get("重大事项：起讫／执行节点");
                var ms = System.Text.RegularExpressions.Regex.Matches(per, @"20\d\d-\d\d-\d\d");
                if (ms.Count >= 2) { a = ms[0].Value; b = ms[1].Value; }
            }
            if (a == "" && b == "") return "";
            return (a == "" ? "?" : a) + " 至 " + (b == "" ? "?" : b);
        }

        // ---------------- 搜索 ----------------
        public List<Hit> Search(string query, string projectId, string cat, bool nameOnly, int max)
        {
            lock (Sync) return SearchCore(query, projectId, cat, nameOnly, max);
        }

        List<Hit> SearchCore(string query, string projectId, string cat, bool nameOnly, int max)
        {
            var tokens = query.Split(new[] { ' ', '　', ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();
            var hits = new List<Hit>();
            if (tokens.Length == 0) return hits;
            foreach (var f in Lib.Files)
            {
                if (projectId != null && !InProject(f, projectId)) continue;
                if (cat != null && f.Cat != cat) continue;
                string pname = "";
                var p = Proj(f.Project); if (p != null) pname = p.Name;
                string text = null;
                if (!nameOnly) Texts.TryGetValue(f.Rel, out text);
                int score = 0; bool all = true; int firstPos = -1; string firstTok = null;
                foreach (var t in tokens)
                {
                    bool inName = f.Name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool inPath = !inName && (f.Rel.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 || pname.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
                    int pos = text == null ? -1 : text.IndexOf(t, StringComparison.OrdinalIgnoreCase);
                    if (!inName && !inPath && pos < 0) { all = false; break; }
                    if (inName) score += 30; else if (inPath) score += 8;
                    if (pos >= 0)
                    {
                        score += 5 + Math.Min(15, Count(text, t));
                        if (firstPos < 0) { firstPos = pos; firstTok = t; }
                    }
                }
                if (!all) continue;
                var h = new Hit { File = f, Score = score };
                if (firstPos >= 0)
                {
                    int s = Math.Max(0, firstPos - 30);
                    int e = Math.Min(text.Length, firstPos + firstTok.Length + 60);
                    h.Snippet = (s > 0 ? "…" : "") + text.Substring(s, e - s).Replace('\n', ' ').Replace('\t', ' ') + (e < text.Length ? "…" : "");
                }
                else h.Snippet = "（文件名命中）" + f.Dir;
                hits.Add(h);
            }
            return hits.OrderByDescending(h => h.Score).ThenBy(h => h.File.Name).Take(max).ToList();
        }

        static int Count(string s, string t)
        {
            int n = 0, i = 0;
            while (n < 50 && (i = s.IndexOf(t, i, StringComparison.OrdinalIgnoreCase)) >= 0) { n++; i += t.Length; }
            return n;
        }

        // ---------------- 导入资料 ----------------
        public static string Sha1(string path)
        {
            using (var s = File.OpenRead(path))
            using (var h = SHA1.Create()) return BitConverter.ToString(h.ComputeHash(s));
        }

        /// <summary>之前从项目里移除的文件中，是否有内容完全相同的。</summary>
        public string FindHidden(string path)
        {
            long len = new FileInfo(path).Length; string hash = null;
            foreach (var rel in Lib.Hidden.ToList())
            {
                string full = Path.Combine(Lib.Root, rel);
                try
                {
                    var fi = new FileInfo(full);
                    if (!fi.Exists || fi.Length != len) continue;
                    if (string.Equals(full, path, StringComparison.OrdinalIgnoreCase)) return rel;
                    if (hash == null) hash = Sha1(path);
                    if (Sha1(full) == hash) return rel;
                }
                catch { }
            }
            return null;
        }

        /// <summary>库里是否已有内容完全相同的文件。</summary>
        public FileRec FindDuplicate(string path)
        {
            long len = new FileInfo(path).Length;
            string hash = null;
            foreach (var f in Lib.Files)
            {
                if (f.Size != len) continue;
                string full = FullPath(f);
                if (string.Equals(full, path, StringComparison.OrdinalIgnoreCase)) return f;
                try
                {
                    if (hash == null) hash = Sha1(path);
                    if (Sha1(full) == hash) return f;
                }
                catch { }
            }
            return null;
        }

        static readonly string[] ArchiveExts = { ".zip", ".7z", ".rar" };
        public static bool IsArchive(string path) { return ArchiveExts.Contains(Path.GetExtension(path).ToLowerInvariant()); }

        public static void Extract(string archive, string dest)
        {
            Directory.CreateDirectory(dest);
            if (Path.GetExtension(archive).ToLowerInvariant() == ".zip")
            {
                try { ZipFile.ExtractToDirectory(archive, dest, Encoding.GetEncoding("GB18030")); return; }
                catch (Exception) { try { Directory.Delete(dest, true); } catch { } Directory.CreateDirectory(dest); }
            }
            // 7z / rar 及解压失败的 zip 交给 Windows 自带的 tar
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "tar.exe"), "-xf \"" + archive + "\" -C \"" + dest + "\"");
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardError = true;
            using (var pr = Process.Start(psi))
            {
                string err = pr.StandardError.ReadToEnd();
                pr.WaitForExit();
                if (pr.ExitCode != 0) throw new Exception("无法解压 " + Path.GetFileName(archive) + "：" + err);
            }
        }

        string importForceCat;   // 指定分类导入时用（如在办理流程“施工”上右键添加）
        /// <summary>最近一次导入后，在本项目里可见的文件（新复制的、恢复的、共用过来的、本来就有的）。</summary>
        public List<string> LastImported = new List<string>();
        string importProjectId;

        /// <summary>导入到指定分类：文件放进项目文件夹下的该分类子文件夹，并固定为该分类。</summary>
        public List<string> ImportToCategory(IEnumerable<string> paths, Project p, string cat, Action<string> progress)
        {
            importForceCat = cat;
            try { return Import(paths, p, true, progress); }
            finally { importForceCat = null; }
        }

        /// <summary>把文件/文件夹/压缩包复制进项目文件夹。返回日志。</summary>
        public List<string> Import(IEnumerable<string> paths, Project p, bool byCategory, Action<string> progress)
        {
            var log = new List<string>();
            string projDir = ProjectFolder(p);
            importProjectId = p.Id;
            LastImported = new List<string>();
            Directory.CreateDirectory(projDir);
            string tmpRoot = Path.Combine(Path.GetTempPath(), "项目档案室导入_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                foreach (var src in paths)
                {
                    if (Directory.Exists(src))
                    {
                        string baseDir = Path.GetDirectoryName(src.TrimEnd('\\'));
                        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                            ImportOne(f, f.Substring(baseDir.Length + 1), projDir, byCategory, log, tmpRoot, progress);
                    }
                    else if (File.Exists(src))
                        ImportOne(src, Path.GetFileName(src), projDir, byCategory, log, tmpRoot, progress);
                }
            }
            finally { try { if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, true); } catch { } }
            return log;
        }

        void ImportOne(string src, string relName, string projDir, bool byCategory, List<string> log, string tmpRoot, Action<string> progress)
        {
            if (progress != null) progress("正在导入：" + Path.GetFileName(src));
            if (IsArchive(src))
            {
                string dest = Path.Combine(tmpRoot, Guid.NewGuid().ToString("N").Substring(0, 6));
                try { Extract(src, dest); }
                catch (Exception ex) { log.Add("× " + ex.Message); return; }
                string folder = Path.GetFileNameWithoutExtension(src);
                string relDir = Path.GetDirectoryName(relName);
                foreach (var f in Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories))
                {
                    string inner = f.Substring(dest.Length + 1);
                    ImportOne(f, Path.Combine(string.IsNullOrEmpty(relDir) ? folder : Path.Combine(relDir, folder), inner), projDir, byCategory, log, tmpRoot, progress);
                }
                log.Add("✓ 已解压 " + Path.GetFileName(src));
                return;
            }
            // 之前从项目里移除（文件还在）的：直接恢复，不再当作重复
            string hid = Lib.Hidden.Count > 0 ? FindHidden(src) : null;
            if (hid != null)
            {
                if (importForceCat != null) Lib.FileCat[hid] = importForceCat;
                Unhide(hid, importProjectId == null ? null : Proj(importProjectId));
                LastImported.Add(hid);
                log.Add("✓ 已恢复之前移除的文件：" + hid); return;
            }
            var dup = FindDuplicate(src);
            if (dup != null && importForceCat != null && dup.Project == importProjectId)
            {
                Lib.FileCat[dup.Rel] = importForceCat; dup.Cat = importForceCat; LastImported.Add(dup.Rel);
                log.Add("· 已有这份文件，已把它归到“" + Cats.Title(importForceCat) + "”：" + dup.Rel); return;
            }
            if (dup != null && importProjectId != null && !InProject(dup, importProjectId) && Proj(importProjectId) != null && !Proj(importProjectId).IsRoot)
            {
                // 别的项目里已有同一份文件：共用过来，不再复制一份
                List<string> l;
                if (!Lib.SharedWith.TryGetValue(dup.Rel, out l)) Lib.SharedWith[dup.Rel] = l = new List<string>();
                if (!l.Contains(importProjectId)) l.Add(importProjectId);
                LastImported.Add(dup.Rel);
                var owner = Proj(dup.Project);
                log.Add("✓ 库里已有（在“" + (owner == null ? "其他项目" : owner.Name) + "”），已共用到本项目：" + dup.Name); return;
            }
            if (dup != null) { LastImported.Add(dup.Rel); log.Add("· 本项目里已有这份文件：" + dup.Rel); return; }
            string target;
            if (importForceCat != null) target = Path.Combine(projDir, importForceCat, Path.GetFileName(relName));
            else if (byCategory) target = Path.Combine(projDir, Cats.Classify("x\\" + relName), Path.GetFileName(relName));
            else target = Path.Combine(projDir, relName);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (File.Exists(target))
            {
                string b = Path.Combine(Path.GetDirectoryName(target), Path.GetFileNameWithoutExtension(target));
                string ext = Path.GetExtension(target); int i = 2;
                while (File.Exists(b + "(" + i + ")" + ext)) i++;
                target = b + "(" + i + ")" + ext;
            }
            File.Copy(src, target);
            // 让本次导入立刻能参与查重
            var fi = new FileInfo(target);
            string rel = target.Substring(Lib.Root.TrimEnd('\\').Length + 1);
            if (importForceCat != null) Lib.FileCat[rel] = importForceCat;
            Lib.Files.Add(new FileRec { Rel = rel, Size = fi.Length, Time = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"), Project = Assign(rel), Cat = CatOf(rel) });
            LastImported.Add(rel);
            log.Add("✓ " + rel);
        }

        // ---------------- 导出 Excel ----------------
        public static void WriteXlsx(string path, string sheetName, IList<string> headers, IList<IList<object>> rows)
        {
            if (File.Exists(path)) File.Delete(path);
            using (var z = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Put(z, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
                Put(z, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                Put(z, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"" + X(sheetName) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Put(z, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
                Put(z, "xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"#,##0.00\"/></numFmts><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"宋体\"/></font><font><b/><sz val=\"11\"/><name val=\"宋体\"/></font></fonts><fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFDDEBF7\"/></patternFill></fill></fills><borders count=\"2\"><border/><border><left style=\"thin\"/><right style=\"thin\"/><top style=\"thin\"/><bottom style=\"thin\"/></border></borders><cellStyleXfs count=\"1\"><xf/></cellStyleXfs><cellXfs count=\"4\"><xf/><xf fontId=\"1\" fillId=\"2\" borderId=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf><xf borderId=\"1\" applyBorder=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf><xf numFmtId=\"164\" borderId=\"1\" applyBorder=\"1\" applyNumberFormat=\"1\"><alignment vertical=\"center\"/></xf></cellXfs></styleSheet>");
                var sb = new StringBuilder();
                sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><cols>");
                for (int c = 0; c < headers.Count; c++)
                {
                    int w = Width(headers[c]);
                    foreach (var r in rows) if (c < r.Count && r[c] != null) w = Math.Max(w, Width(Convert.ToString(r[c])));
                    sb.Append("<col min=\"" + (c + 1) + "\" max=\"" + (c + 1) + "\" width=\"" + Math.Min(60, Math.Max(8, w + 2)) + "\" customWidth=\"1\"/>");
                }
                sb.Append("</cols><sheetData>");
                sb.Append("<row r=\"1\">");
                for (int c = 0; c < headers.Count; c++) sb.Append(Cell(c, 1, headers[c], 1));
                sb.Append("</row>");
                for (int i = 0; i < rows.Count; i++)
                {
                    sb.Append("<row r=\"" + (i + 2) + "\">");
                    for (int c = 0; c < rows[i].Count; c++) sb.Append(Cell(c, i + 2, rows[i][c], 2));
                    sb.Append("</row>");
                }
                sb.Append("</sheetData></worksheet>");
                Put(z, "xl/worksheets/sheet1.xml", sb.ToString());
            }
        }

        static int Width(string s)
        {
            if (s == null) return 0;
            int w = 0;
            foreach (var line in s.Split('\n')) { int lw = 0; foreach (char ch in line) lw += ch > 255 ? 2 : 1; w = Math.Max(w, lw); }
            return w;
        }

        static string Col(int c) { string s = ""; c++; while (c > 0) { int m = (c - 1) % 26; s = (char)('A' + m) + s; c = (c - 1) / 26; } return s; }

        static string Cell(int c, int r, object v, int style)
        {
            string refc = Col(c) + r;
            if (v == null) return "<c r=\"" + refc + "\" s=\"" + style + "\"/>";
            if (v is double || v is int || v is long || v is decimal)
                return "<c r=\"" + refc + "\" s=\"3\"><v>" + Convert.ToDouble(v).ToString(System.Globalization.CultureInfo.InvariantCulture) + "</v></c>";
            return "<c r=\"" + refc + "\" s=\"" + style + "\" t=\"inlineStr\"><is><t xml:space=\"preserve\">" + X(Convert.ToString(v)) + "</t></is></c>";
        }

        static string X(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s ?? "")
            {
                if (ch == '<') sb.Append("&lt;"); else if (ch == '>') sb.Append("&gt;"); else if (ch == '&') sb.Append("&amp;");
                else if (ch == '"') sb.Append("&quot;");
                else if (ch < 0x20 && ch != '\n' && ch != '\t') continue;
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        static void Put(ZipArchive z, string name, string content)
        {
            var e = z.CreateEntry(name, CompressionLevel.Optimal);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(content);
        }
    }
}
