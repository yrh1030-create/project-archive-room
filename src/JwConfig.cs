using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace DangAn
{
    /// <summary>交委账号：用户名、显示名、加密的密码、对应的项目。密码只用当前 Windows 用户能解开的方式保存在本机。</summary>
    public class JwAccount
    {
        public string User { get; set; }
        public string Display { get; set; }
        public string PwData { get; set; }
        public bool Disabled { get; set; }
        public List<string> Projects { get; set; }
        public JwAccount() { User = ""; Display = ""; PwData = ""; Projects = new List<string>(); }

        [ScriptIgnore]
        public string Password
        {
            get
            {
                if (string.IsNullOrEmpty(PwData)) return "";
                try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(PwData), null, DataProtectionScope.CurrentUser)); }
                catch { return ""; }
            }
            set { PwData = string.IsNullOrEmpty(value) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser)); }
        }

        public override string ToString() { return (Display != "" ? Display + "（" + User + "）" : User) + (Disabled ? "［停用］" : ""); }
    }

    /// <summary>交委“以奖代补”系统设置：网址、账号可由使用人维护，改了不用重装。存 %APPDATA%\项目档案室\jw-portal.json，与字段映射、填报记录分开。</summary>
    public class JwPortal
    {
        /// <summary>首次使用时的默认登录地址（来自交委通知），使用人可在设置里改。</summary>
        public const string NoticeUrl = "https://58.144.223.143:9900/jwportal/shncgl/login.jsp";

        public string Name { get; set; }
        public string Url { get; set; }
        public List<JwAccount> Accounts { get; set; }
        public string DefaultUser { get; set; }
        /// <summary>使用人确认过可以发送账号的主机（主机:端口）。网址换了主机要重新确认。</summary>
        public List<string> ConfirmedHosts { get; set; }
        /// <summary>改网址或账号后递增，浏览器据此作废旧的登录会话。</summary>
        public int SessionStamp { get; set; }

        public JwPortal() { Name = "以奖代补系统"; Url = NoticeUrl; Accounts = new List<JwAccount>(); DefaultUser = ""; ConfirmedHosts = new List<string>(); }

        public static string Dir { get { var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "项目档案室"); Directory.CreateDirectory(d); return d; } }
        static string FilePath { get { return Path.Combine(Dir, "jw-portal.json"); } }

        public static JwPortal Load()
        {
            JwPortal p = null;
            try { if (File.Exists(FilePath)) p = new JavaScriptSerializer().Deserialize<JwPortal>(File.ReadAllText(FilePath, Encoding.UTF8)); } catch { }
            if (p == null) p = new JwPortal();
            if (p.Accounts == null) p.Accounts = new List<JwAccount>();
            if (p.ConfirmedHosts == null) p.ConfirmedHosts = new List<string>();
            foreach (var a in p.Accounts) if (a.Projects == null) a.Projects = new List<string>();
            return p;
        }

        public void Save() { File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false)); }

        public static string HostOf(string url)
        {
            try { var u = new Uri(url); return u.Host + ":" + u.Port; } catch { return ""; }
        }

        /// <summary>这个项目用哪个账号：对应了项目的优先，其次默认账号。</summary>
        public JwAccount AccountFor(string projectId)
        {
            var live = Accounts.Where(a => !a.Disabled).ToList();
            return live.FirstOrDefault(a => a.Projects.Contains(projectId)) ?? live.FirstOrDefault(a => a.User == DefaultUser) ?? live.FirstOrDefault();
        }
    }

    // ================== 每个项目的交委填报数据（抽取结果、附件、人工改过的值）
    public class JwField
    {
        public string Module { get; set; }     // 招投标信息 / 质量及验收
        public string Group { get; set; }      // 施工招标、监理招标、交工验收信息……
        public string Label { get; set; }      // 页面上的字段名
        public string Unit { get; set; }
        public string Value { get; set; }      // 准备填入的值（人可改）
        public string Raw { get; set; }        // 档案原值（如“1691000元”）
        public string Source { get; set; }     // 依据
        public string Confidence { get; set; } // 高 / 中 / 低
        public bool Edited { get; set; }
        public JwField() { Module = Group = Label = Unit = Value = Raw = Source = Confidence = ""; }
    }

    public class JwAttach
    {
        public string Module { get; set; }     // 招投标信息 / 质量及验收
        public string Kind { get; set; }       // 中标通知书、施工合同……（业务类别，不是系统下拉选项）
        public string Rel { get; set; }        // 档案原件（相对路径）
        public string Pdf { get; set; }        // 转换 / 压缩后的上传副本（jw-cache 里）
        public long OrigSize { get; set; }
        public long PdfSize { get; set; }
        public string Level { get; set; }      // 未压缩 / 150dpi / 120dpi / 100dpi / 拆分第 n 份
        public string Why { get; set; }        // 依据：为什么认为是这一类
        public string Confidence { get; set; }
        public bool Use { get; set; }
        public string Uploaded { get; set; }   // 已选入页面 / 服务器已接收 的时间
        public string Hash { get; set; }
        public JwAttach() { Module = Kind = Rel = Pdf = Level = Why = Confidence = Uploaded = Hash = ""; Use = true; }
    }

    public class JwProjectData
    {
        public string ProjectId { get; set; }
        public List<JwField> Fields { get; set; }
        public List<JwAttach> Attachments { get; set; }
        public List<string> Notes { get; set; }
        public string Extracted { get; set; }
        public JwProjectData() { ProjectId = ""; Fields = new List<JwField>(); Attachments = new List<JwAttach>(); Notes = new List<string>(); Extracted = ""; }

        static string DirFor(Store st)
        {
            // 按档案库位置分开存，换库不串
            string key = BitConverter.ToString(SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(st.DbDir.ToLowerInvariant()))).Replace("-", "").Substring(0, 12);
            string d = Path.Combine(JwPortal.Dir, "jw-data", key); Directory.CreateDirectory(d); return d;
        }

        public static JwProjectData Load(Store st, string projectId)
        {
            string f = Path.Combine(DirFor(st), projectId + ".json");
            JwProjectData d = null;
            try { if (File.Exists(f)) d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<JwProjectData>(File.ReadAllText(f, Encoding.UTF8)); } catch { }
            if (d == null) d = new JwProjectData { ProjectId = projectId };
            if (d.Fields == null) d.Fields = new List<JwField>();
            if (d.Attachments == null) d.Attachments = new List<JwAttach>();
            if (d.Notes == null) d.Notes = new List<string>();
            return d;
        }

        public void Save(Store st) { File.WriteAllText(Path.Combine(DirFor(st), ProjectId + ".json"), new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(this), new UTF8Encoding(false)); }
    }

    /// <summary>填报记录：只存本机 %APPDATA%\项目档案室\jw-logs\，不写密码、不写验证码。</summary>
    public static class JwLog
    {
        public static string Dir { get { var d = Path.Combine(JwPortal.Dir, "jw-logs"); Directory.CreateDirectory(d); return d; } }

        public static void Write(string host, string project, string user, string module, string what)
        {
            try
            {
                File.AppendAllText(Path.Combine(Dir, DateTime.Now.ToString("yyyy-MM") + ".txt"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + host + "\t" + project + "\t" + user + "\t" + module + "\t" + what.Replace("\r", " ").Replace("\n", " ") + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
