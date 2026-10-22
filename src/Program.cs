using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("项目档案室")]
[assembly: System.Reflection.AssemblyProduct("项目档案室")]
[assembly: System.Reflection.AssemblyCompany("misslsf")]
[assembly: System.Reflection.AssemblyCopyright("© 2026 misslsf")]
[assembly: System.Reflection.AssemblyDescription("项目档案室 1.1 · 开发者 misslsf")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.1")]
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.1.0.0")]

namespace DangAn
{
    public static class AppInfo
    {
        public const string Version = "1.1";
        public const string Developer = "misslsf";
    }

    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        static string SettingsDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "项目档案室"); } }
        static string SettingsFile { get { return Path.Combine(SettingsDir, "settings.txt"); } }

        public static string SavedDb()
        {
            try { if (File.Exists(SettingsFile)) return File.ReadAllText(SettingsFile, Encoding.UTF8).Trim(); } catch { }
            return null;
        }

        public static void RememberDb(string dir)
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsFile, dir, Encoding.UTF8);
        }

        [STAThread]
        static int Main(string[] args)
        {
            if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Ui.Scale = g.DpiX / 96f;

            bool created;
            using (var mutex = new Mutex(true, "Local\\XiangMuDangAnShi", out created))
            {
                if (!created && Array.IndexOf(args, "/restart") >= 0)
                {
                    try { created = mutex.WaitOne(15000); } catch (AbandonedMutexException) { created = true; }
                }
                if (!created) { Ui.Info("项目档案室已经打开了。"); return 0; }
                Application.ThreadException += (s, e) => MessageBox.Show("出错了：" + e.Exception.Message + "\n\n" + e.Exception.StackTrace, "项目档案室", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Store st = OpenStore(args.Length > 0 && Directory.Exists(args[0]) ? args[0] : null);
                if (st == null) return 0;
                Application.AddMessageFilter(new WheelRouter());
                Application.Run(new MainForm(st));
            }
            return 0;
        }

        /// <summary>按优先级找档案库：命令行 → 上次使用 → 安装时附带的默认位置 → 让用户选择。</summary>
        static Store OpenStore(string arg)
        {
            string hint = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "默认档案库.txt");
            var candidates = new[] {
                arg,
                SavedDb(),
                File.Exists(hint) ? File.ReadAllText(hint, Encoding.UTF8).Trim() : null,
                @"E:\下载\项目文件\项目档案库"
            };
            foreach (var c in candidates)
            {
                if (string.IsNullOrEmpty(c)) continue;
                string dir = File.Exists(c) ? Path.GetDirectoryName(c) : c;
                if (File.Exists(Path.Combine(dir, Store.LibFile)))
                {
                    try { var st = Store.Load(dir); RememberDb(dir); return st; }
                    catch (Exception ex) { Ui.Info("档案库打不开：" + dir + "\n" + ex.Message); }
                }
            }
            return FirstRun();
        }

        static Store FirstRun()
        {
            var r = MessageBox.Show("欢迎使用项目档案室！\n\n选“是”：选择你存放项目资料的总文件夹，软件会自动建立档案库、按项目和类别整理登记（不会移动或修改你的文件）。\n选“否”：连接一个已有的档案库（“项目档案库”文件夹里的 档案库.json）。",
                "项目档案室", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
            if (r == DialogResult.Yes) return CreateNew(null);
            if (r == DialogResult.No) return PickExisting(null);
            return null;
        }

        static Store CreateNew(IWin32Window owner)
        {
            using (var d = new FolderBrowserDialog { Description = "选择存放项目资料的总文件夹（里面每个子文件夹一般是一个项目）" })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                var st = Store.Create(d.SelectedPath);
                using (var w = new Form { Text = "项目档案室", Size = new Size(Ui.S(460), Ui.S(140)), StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, ControlBox = false })
                {
                    var l = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = Ui.F(10f), Text = "正在建立档案库，请稍候…" };
                    w.Controls.Add(l); w.Show(); Application.DoEvents();
                    st.Scan(s => { l.Text = s; Application.DoEvents(); });
                    foreach (var p in st.RealProjects) Builder.Complete(st, p);
                    st.Save();
                }
                RememberDb(st.DbDir);
                return st;
            }
        }

        static Store PickExisting(IWin32Window owner)
        {
            using (var d = new OpenFileDialog { Filter = "档案库|档案库.json", Title = "选择 档案库.json" })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                var st = Store.Load(Path.GetDirectoryName(d.FileName));
                RememberDb(st.DbDir);
                return st;
            }
        }

        public static void SwitchLibrary(MainForm m)
        {
            var st = PickExisting(m);
            if (st == null) return;
            Restart(m);
        }

        public static void NewLibrary(MainForm m)
        {
            var st = CreateNew(m);
            if (st == null) return;
            Restart(m);
        }

        static void Restart(MainForm m)
        {
            m.StopOcr();
            try { m.St.Save(); } catch { }
            Ui.Info("已切换档案库，软件将重新打开。");
            System.Diagnostics.Process.Start(Application.ExecutablePath, "/restart");
            Environment.Exit(0);
        }
    }
}
