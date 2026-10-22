using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("项目档案室 安装程序")]
[assembly: AssemblyProduct("项目档案室")]
[assembly: AssemblyCompany("misslsf")]
[assembly: AssemblyCopyright("© 2026 misslsf")]
[assembly: AssemblyDescription("项目档案室 1.1 安装程序 · 开发者 misslsf")]
[assembly: AssemblyInformationalVersion("1.1")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace DangAnSetup
{
    static class SetupProgram
    {
        const string AppName = "项目档案室";
        const string ExeName = "项目档案室.exe";
        public const string Version = "1.1";
        public const string Developer = "misslsf";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\XiangMuDangAnShi";

        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            if (args.Length > 0 && args[0] == "/uninstall") { Uninstall(args.Length > 1 && args[1] == "/quiet"); return; }
            if (args.Length > 0 && args[0] == "/silent")
            {
                // 静默安装：/silent [安装目录] ，档案库位置自动查找
                var old = Existing();
                Install(args.Length > 1 ? args[1] : (old != null ? old : DefaultDir), true, SetupForm.FindDb());
                return;
            }
            Application.Run(new SetupForm());
        }

        public static string DefaultDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName); } }

        /// <summary>已安装的位置（注册表里登记过、且程序文件还在）；没装过返回 null。</summary>
        public static string Existing()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                {
                    var dir = k == null ? null : k.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, ExeName))) return dir;
                }
            }
            catch { }
            return File.Exists(Path.Combine(DefaultDir, ExeName)) ? DefaultDir : null;
        }

        public static string ExistingVersion()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey)) return k == null ? "" : (k.GetValue("DisplayVersion") as string ?? ""); }
            catch { return ""; }
        }

        public static void Install(string dir, bool desktop, string dbDir)
        {
            Directory.CreateDirectory(dir);
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
                try { p.CloseMainWindow(); p.WaitForExit(5000); if (!p.HasExited) p.Kill(); } catch { }
            string exe = Path.Combine(dir, ExeName);
            // 覆盖升级：旧程序刚关闭时文件可能还被占用，稍等重试
            for (int i = 0; ; i++)
            {
                try { Extract("app.exe", exe); break; }
                catch (IOException) { if (i >= 20) throw; System.Threading.Thread.Sleep(500); }
                catch (UnauthorizedAccessException) { if (i >= 20) throw; System.Threading.Thread.Sleep(500); }
            }
            Extract("app.ico", Path.Combine(dir, "app.ico"));
            // 安装程序自身复制一份，用作卸载程序
            File.Copy(Application.ExecutablePath, Path.Combine(dir, "卸载项目档案室.exe"), true);
            if (!string.IsNullOrEmpty(dbDir)) File.WriteAllText(Path.Combine(dir, "默认档案库.txt"), dbDir, Encoding.UTF8);

            string programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
            Directory.CreateDirectory(programs);
            Shortcut(Path.Combine(programs, AppName + ".lnk"), exe, dir);
            Shortcut(Path.Combine(programs, "卸载" + AppName + ".lnk"), Path.Combine(dir, "卸载项目档案室.exe"), dir, "/uninstall");
            if (desktop) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"), exe, dir);

            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", Developer);
                k.SetValue("Comments", "项目资料管理、全文查找与填报 · 开发者 " + Developer);
                k.SetValue("DisplayIcon", exe);
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", "\"" + Path.Combine(dir, "卸载项目档案室.exe") + "\" /uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
            }
        }

        static void Extract(string res, string target)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
            using (var f = File.Create(target)) s.CopyTo(f);
        }

        static void Shortcut(string lnk, string target, string workDir, string args = "")
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
            var st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
            st.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc, new object[] { args });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { Path.Combine(workDir, "app.ico") });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "项目资料管理、全文查找与填报 · 版本 " + Version + " · 开发者 " + Developer });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            Marshal.FinalReleaseComObject(sc); Marshal.FinalReleaseComObject(sh);
        }

        static void Uninstall(bool quiet)
        {
            if (!quiet && MessageBox.Show("确定卸载项目档案室吗？\n\n只删除软件本身；你的项目资料和“项目档案库”（档案库.json）都会保留。", AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            string dir = null;
            using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey)) if (k != null) dir = k.GetValue("InstallLocation") as string;
            if (string.IsNullOrEmpty(dir)) dir = Path.GetDirectoryName(Application.ExecutablePath);
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
                try { p.CloseMainWindow(); p.WaitForExit(5000); if (!p.HasExited) p.Kill(); } catch { }
            try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName), true); } catch { }
            try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk")); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            // 卸载程序本身在安装目录中，交给 cmd 在退出后删除
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & rd /s /q \"" + dir + "\"") { CreateNoWindow = true, UseShellExecute = false });
            if (!quiet) MessageBox.Show("已卸载。你的资料和档案库没有被删除。", AppName);
        }
    }

    class SetupForm : Form
    {
        TextBox dirBox, dbBox; CheckBox desk, launch; Button go;
        readonly bool upgrade;

        static int S(int v) { using (var g = Graphics.FromHwnd(IntPtr.Zero)) return (int)(v * g.DpiX / 96f); }

        public SetupForm()
        {
            string old = SetupProgram.Existing();
            upgrade = old != null;
            Text = (upgrade ? "升级 " : "安装 ") + "项目档案室 " + SetupProgram.Version; Font = new Font("Microsoft YaHei UI", 9.5f); BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(600), S(450));
            try { Icon = new Icon(Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico")); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = S(104) };
            banner.Paint += (s, e) =>
            {
                using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(banner.ClientRectangle, DangAn.AppIcon.C1, DangAn.AppIcon.C2, 0f)) e.Graphics.FillRectangle(b, banner.ClientRectangle);
                using (var w = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) e.Graphics.FillEllipse(w, banner.Width - S(160), -S(60), S(220), S(220));
                DangAn.AppIcon.Draw(e.Graphics, new RectangleF(S(24), S(20), S(64), S(64)));
            };
            var t1 = new Label { Text = "项目档案室", Font = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold), ForeColor = Color.White, AutoSize = true, Location = new Point(S(102), S(18)), BackColor = Color.Transparent };
            var t2 = new Label { Text = "项目资料自动归档 · 全文查资料 · 合同台账 · 一键填表", ForeColor = Color.FromArgb(225, 255, 250), AutoSize = true, Location = new Point(S(105), S(62)), BackColor = Color.Transparent };
            var t3 = new Label { Text = "版本 " + SetupProgram.Version + " · 开发者 " + SetupProgram.Developer, ForeColor = Color.FromArgb(225, 255, 250), AutoSize = true, BackColor = Color.Transparent, Font = new Font("Microsoft YaHei UI", 8.5f) };
            t3.Location = new Point(S(600) - TextRenderer.MeasureText(t3.Text, t3.Font).Width - S(20), S(24));
            banner.Controls.AddRange(new Control[] { t1, t2, t3 });

            var l1 = new Label { Text = upgrade ? "检测到已安装" + (SetupProgram.ExistingVersion() != "" ? "的 " + SetupProgram.ExistingVersion() + " 版" : "的版本") + "，将直接覆盖升级（档案库和设置都保留）：" : "安装位置：", AutoSize = true, Location = new Point(S(24), S(118)), ForeColor = upgrade ? Color.FromArgb(14, 120, 100) : Color.Black };
            dirBox = new TextBox { Text = upgrade ? old : SetupProgram.DefaultDir, Location = new Point(S(24), S(142)), Width = S(460) };
            var b1 = new Button { Text = "浏览…", Location = new Point(S(494), S(140)), Width = S(80), Height = S(28) };
            b1.Click += (s, e) => { using (var d = new FolderBrowserDialog()) if (d.ShowDialog(this) == DialogResult.OK) dirBox.Text = Path.Combine(d.SelectedPath, "项目档案室"); };

            var l2 = new Label { Text = "档案库位置（已整理好的“项目档案库”文件夹；首次打开会自动连接）：", AutoSize = true, Location = new Point(S(24), S(186)) };
            string prevDb = upgrade && File.Exists(Path.Combine(old, "默认档案库.txt")) ? File.ReadAllText(Path.Combine(old, "默认档案库.txt"), Encoding.UTF8).Trim() : "";
            dbBox = new TextBox { Text = prevDb != "" && File.Exists(Path.Combine(prevDb, "档案库.json")) ? prevDb : FindDb(), Location = new Point(S(24), S(210)), Width = S(460) };
            var b2 = new Button { Text = "浏览…", Location = new Point(S(494), S(208)), Width = S(80), Height = S(28) };
            b2.Click += (s, e) => { using (var d = new FolderBrowserDialog { Description = "选择“项目档案库”文件夹（里面有 档案库.json）" }) if (d.ShowDialog(this) == DialogResult.OK) dbBox.Text = d.SelectedPath; };
            var tip = new Label { Text = "留空也可以：首次打开软件时再选择项目资料文件夹，软件会自动建立档案库。", ForeColor = Color.Gray, AutoSize = true, Location = new Point(S(24), S(240)) };

            bool hadDesk = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "项目档案室.lnk"));
            desk = new CheckBox { Text = "创建桌面快捷方式", Checked = !upgrade || hadDesk, AutoSize = true, Location = new Point(S(24), S(280)) };
            launch = new CheckBox { Text = "安装完成后打开", Checked = true, AutoSize = true, Location = new Point(S(24), S(308)) };
            var note = new Label { Text = "只安装到当前用户，不需要管理员权限；需要 Windows 10/11。", ForeColor = Color.Gray, AutoSize = true, Location = new Point(S(24), S(346)) };

            go = new Button { Text = upgrade ? "覆盖升级" : "安装", Location = new Point(S(380), S(386)), Width = S(100), Height = S(36), BackColor = Color.FromArgb(22, 112, 238), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold) };
            go.FlatAppearance.BorderSize = 0;
            var cancel = new Button { Text = "取消", Location = new Point(S(490), S(386)), Width = S(84), Height = S(36) };
            cancel.Click += (s, e) => Close();
            go.Click += (s, e) => DoInstall();
            Controls.AddRange(new Control[] { banner, l1, dirBox, b1, l2, dbBox, b2, tip, desk, launch, note, go, cancel });
            AcceptButton = go;
        }

        public static string FindDb()
        {
            // 安装包旁边有“项目档案库”，或默认资料位置
            string here = Path.GetDirectoryName(Application.ExecutablePath);
            foreach (var c in new[] { Path.Combine(here, "项目资料", "项目档案库"), Path.Combine(here, "项目档案库"), Path.Combine(Path.GetDirectoryName(here) ?? here, "项目档案库"), Path.Combine(Directory.GetParent(here) != null && Directory.GetParent(here).Parent != null ? Directory.GetParent(here).Parent.FullName : here, "项目档案库"), @"E:\下载\项目文件\项目档案库" })
                if (File.Exists(Path.Combine(c, "档案库.json"))) return c;
            return "";
        }

        void DoInstall()
        {
            string db = dbBox.Text.Trim();
            if (db != "" && !File.Exists(Path.Combine(db, "档案库.json")))
            {
                MessageBox.Show("这个文件夹里没有 档案库.json：\n" + db + "\n\n请重新选择，或留空。", Text); return;
            }
            go.Enabled = false; Cursor = Cursors.WaitCursor;
            try
            {
                SetupProgram.Install(dirBox.Text.Trim(), desk.Checked, db);
                Cursor = Cursors.Default;
                MessageBox.Show((upgrade ? "升级完成！已更新为 " : "安装完成！版本 ") + SetupProgram.Version + "。\n\n"
                    + (upgrade ? "原来的档案库、项目资料和设置都保留不变。\n" : "")
                    + "开始菜单和桌面上都有“项目档案室”。\n卸载：Windows 设置 → 应用 → 项目档案室。\n\n开发者：" + SetupProgram.Developer,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (launch.Checked) Process.Start(Path.Combine(dirBox.Text.Trim(), "项目档案室.exe"));
                Close();
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default; go.Enabled = true;
                MessageBox.Show("安装失败：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
