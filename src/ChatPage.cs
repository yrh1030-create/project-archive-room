using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace DangAn
{
    /// <summary>AI 助手：像聊天一样让 AI 查档案、处理 Word 和 Excel。用的是“设置与备份”里填的 AI 接口。</summary>
    public class ChatPage : Panel, IPage
    {
        readonly MainForm m; Store St { get { return m.St; } }
        AiChat chat;
        RichTextBox view; TextBox input; Button send; Label status; ComboBox modelBox; bool modelLoading;
        FlowLayoutPanel attachRow, outRow, tips;
        Panel pendPanel; CheckedListBox pendList; Label pendTitle; string noteForAi = "";
        readonly List<string> attachments = new List<string>();
        bool busy;
        /// <summary>对话里的文件链接：文字位置 → 文件。</summary>
        class Link { public int Start, Len; public string Path; }
        readonly List<Link> links = new List<Link>();
        public string Crumb { get { return "项目档案室  ›  AI 助手"; } }

        static readonly string[] Examples =
        {
            "列出所有项目的施工合同金额和施工单位，生成一张 Excel",
            "2025年农村公路安全生产工程的预付款是多少？依据是哪份文件？",
            "根据档案写一份2025年农村公路安全生产基础设施建设工程的情况说明（Word）",
            "帮我用档案把附件里的表填好",
            "生成2025年农村公路安全生产基础设施建设工程的七公开公告栏",
        };

        public ChatPage(MainForm m)
        {
            this.m = m; BackColor = Ui.Bg;
            var head = new Panel { Height = Ui.S(84) };
            var t1 = Ui.Lbl("AI 助手", 19f, true, Ui.Text); t1.Location = new Point(0, 0);
            var t2 = Ui.Lbl("问档案里的事（回答里的文件点一下就打开原件），或把文件、压缩包拖进来让 AI 处理。生成的都是新文件，原文件不动。", 9.5f, false, Ui.Sub); t2.Location = new Point(Ui.S(2), Ui.S(54));
            var hr = Ui.Row(); hr.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var bNew = Ui.Btn("新对话", false, "plus"); bNew.Click += (s, e) => NewChat();
            var bDir = Ui.Btn("打开输出文件夹", false, "open"); bDir.Click += (s, e) => Ui.Open(AiChat.OutDir);
            var bSet = Ui.Btn("AI 接口设置", false, "gear"); bSet.Click += (s, e) => { AiSettingsDialog.Show(this); UpdateStatus(); };
            hr.Controls.AddRange(new Control[] { bNew, bDir, bSet });
            head.Controls.AddRange(new Control[] { t1, t2, hr });
            head.Resize += (s, e) => hr.Location = new Point(head.Width - hr.PreferredSize.Width, 0);

            view = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = Ui.F(10.5f), DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Vertical };
            var viewCard = new Card { Padding = new Padding(Ui.S(18), Ui.S(14), Ui.S(10), Ui.S(10)) }; viewCard.Controls.Add(view);
            // 文件链接：单击打开原件，右键可打开所在文件夹
            view.MouseMove += (s, e) => view.Cursor = LinkAt(e.Location) != null ? Cursors.Hand : Cursors.IBeam;
            view.MouseUp += (s, e) =>
            {
                var lk = LinkAt(e.Location); if (lk == null) return;
                if (e.Button == MouseButtons.Right)
                {
                    var cm = new ContextMenuStrip { Font = Ui.F(9.5f) };
                    cm.Items.Add("打开", null, (a, b) => OpenLink(lk.Path)); cm.Items.Add("打开所在文件夹", null, (a, b) => Ui.Reveal(lk.Path));
                    cm.Show(view, e.Location);
                }
                else if (view.SelectionLength == 0) OpenLink(lk.Path);
            };
            // 文件、压缩包直接拖进对话区或输入框
            foreach (Control dz in new Control[] { view, this })
            {
                dz.AllowDrop = true;
                dz.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
                dz.DragDrop += (s, e) => Attach((string[])e.Data.GetData(DataFormats.FileDrop));
            }

            tips = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.White, Padding = new Padding(0, 0, 0, Ui.S(6)) };
            foreach (var ex in Examples)
            {
                var b = (RoundButton)Ui.Btn(ex, false); b.Style = RoundButton.Kind.Soft; b.Font = Ui.F(9f); b.Margin = new Padding(0, 0, Ui.S(6), Ui.S(6));
                string q = ex; b.Click += (s, e) => { input.Text = q; input.Focus(); input.SelectionStart = input.TextLength; };
                tips.Controls.Add(b);
            }
            viewCard.Controls.Add(tips);

            // AI 提交的档案修改：确认后才写入
            pendPanel = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(200), Visible = false, BackColor = Color.FromArgb(255, 250, 235), Padding = new Padding(Ui.S(10), Ui.S(6), Ui.S(10), Ui.S(6)) };
            pendTitle = Ui.Lbl("", 10f, true, Ui.Orange); pendTitle.Dock = DockStyle.Top; pendTitle.AutoSize = false; pendTitle.Height = Ui.S(28);
            pendList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(255, 250, 235), Font = Ui.F(9.5f), HorizontalScrollbar = true };
            var pr = Ui.Row(); pr.Dock = DockStyle.Bottom; pr.Height = Ui.S(44);
            var bOk = Ui.Btn("确认写入所选", true, "check"); bOk.Click += (s, e) => ApplyPending();
            var bNo = Ui.Btn("全部放弃", false); bNo.Click += (s, e) => DropPending();
            pr.Controls.AddRange(new Control[] { bOk, bNo, Ui.Lbl("写入后在项目页的关键信息、合同台账、财务里都能看到，也可以再改。", 8.5f, false, Ui.Sub) });
            pendPanel.Controls.Add(pendList); pendPanel.Controls.Add(pr); pendPanel.Controls.Add(pendTitle);
            viewCard.Controls.Add(pendPanel);

            outRow = new FlowLayoutPanel { Height = Ui.S(34), Dock = DockStyle.Bottom, Visible = false, BackColor = Ui.Bg, WrapContents = false, AutoScroll = true, Padding = new Padding(0, Ui.S(6), 0, 0) };
            attachRow = new FlowLayoutPanel { Height = Ui.S(34), Dock = DockStyle.Top, Visible = false, BackColor = Color.White, WrapContents = false, AutoScroll = true };

            var box = new Card { Height = Ui.S(118), Padding = new Padding(Ui.S(14), Ui.S(10), Ui.S(14), Ui.S(10)) };
            input = new TextBox { Multiline = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = Ui.F(10.5f), ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, AllowDrop = true };
            input.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            input.DragDrop += (s, e) => Attach((string[])e.Data.GetData(DataFormats.FileDrop));
            input.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; Send(); }
            };
            var ops = Ui.Row(); ops.Dock = DockStyle.Bottom; ops.Height = Ui.S(44);
            var bAtt = Ui.Btn("添加附件（文件 / 压缩包）", false, "upload");
            bAtt.Click += (s, e) =>
            {
                using (var d = new OpenFileDialog { Multiselect = true, Title = "选择要交给 AI 处理的文件", Filter = "Word / Excel / PDF / 图片|*.doc;*.docx;*.wps;*.xls;*.xlsx;*.xlsm;*.et;*.pdf;*.ofd;*.jpg;*.jpeg;*.png;*.txt;*.csv;*.zip;*.rar;*.7z|所有文件|*.*" })
                    if (d.ShowDialog(this) == DialogResult.OK) Attach(d.FileNames);
            };
            send = Ui.Btn("发送", true, "chat"); send.Click += (s, e) => Send();
            status = Ui.Lbl("", 8.5f, false, Ui.Sub); status.Margin = new Padding(Ui.S(10), Ui.S(12), 0, 0);
            var ml = Ui.Lbl("模型", 9f, false, Ui.Sub); ml.Margin = new Padding(Ui.S(12), Ui.S(11), Ui.S(4), 0);
            modelBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = Ui.S(170), Font = Ui.F(9.5f), Margin = new Padding(0, Ui.S(7), 0, 0) };
            modelBox.SelectedIndexChanged += (s, e) => SaveModel();
            modelBox.Leave += (s, e) => SaveModel();
            modelBox.DropDown += (s, e) => { if (modelBox.Items.Count <= 1) FetchModels(); };
            ops.Controls.AddRange(new Control[] { send, bAtt, ml, modelBox, status });
            box.Controls.Add(input); box.Controls.Add(attachRow); box.Controls.Add(ops);

            AllowDrop = true;
            Ui.Stack(this, viewCard, head);
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(118) + Ui.S(46), BackColor = Ui.Bg };
            box.Dock = DockStyle.Fill; bottom.Controls.Add(box); bottom.Controls.Add(outRow);
            var gap = Ui.Gap(10); gap.Dock = DockStyle.Bottom;
            Controls.Add(gap); Controls.Add(bottom);
            gap.SendToBack(); bottom.SendToBack();
            NewChat();
        }

        public void Reload() { Tag = null; UpdateStatus(); }

        void UpdateStatus()
        {
            var cfg = AiConfig.Load();
            status.Text = cfg.Ready ? cfg.Provider + "　Enter 发送，Shift+Enter 换行" : "还没设置 AI 接口密钥，点右上角“AI 接口设置”";
            status.ForeColor = cfg.Ready ? Ui.Sub : Ui.Orange;
            modelLoading = true;
            modelBox.Items.Clear();
            foreach (var x in cfg.Models) modelBox.Items.Add(x);
            if (cfg.Model != "" && !modelBox.Items.Contains(cfg.Model)) modelBox.Items.Insert(0, cfg.Model);
            modelBox.Text = cfg.Model;
            modelLoading = false;
        }

        /// <summary>切换模型：马上保存，下一条消息起生效。</summary>
        void SaveModel()
        {
            if (modelLoading) return;
            string mname = modelBox.Text.Trim();
            var cfg = AiConfig.Load();
            if (mname == "" || mname == cfg.Model) return;
            cfg.Model = mname; cfg.Save();
            Line("　· 已切换模型：" + mname + "（从下一条消息起使用）", Ui.Sub, 9f, false);
        }

        void FetchModels()
        {
            var cfg = AiConfig.Load();
            if (!cfg.Ready) return;
            Cursor = Cursors.WaitCursor;
            List<string> list = null;
            var th = new Thread(() => { try { list = AiClient.ListModels(cfg); } catch { } });
            th.IsBackground = true; th.Start();
            while (th.IsAlive) { Application.DoEvents(); Thread.Sleep(30); }
            Cursor = Cursors.Default;
            if (list == null || list.Count == 0) return;
            cfg.Models = list; cfg.Save();
            modelLoading = true;
            string cur = modelBox.Text;
            modelBox.Items.Clear(); foreach (var x in list) modelBox.Items.Add(x);
            if (cur != "" && !list.Contains(cur)) modelBox.Items.Insert(0, cur);
            modelBox.Text = cur;
            modelLoading = false;
        }

        void NewChat()
        {
            if (busy) return;
            chat = new AiChat(St);
            if (pendPanel != null) { pendPanel.Visible = false; pendList.Items.Clear(); }
            chat.OnTool = s => OnUi(() => Line("　· " + s, Ui.Sub, 9f, false));
            chat.OnOutput = p => OnUi(() => AddOutput(p));
            chat.PickFolder = name => (string)Invoke(new Func<string>(() => SevenDisclosure.PickDir(this, name)));   // AI 在后台线程，弹窗回到界面线程
            view.Clear(); links.Clear(); attachments.Clear(); RefreshAttach();
            foreach (Control c in outRow.Controls.Cast<Control>().ToList()) { outRow.Controls.Remove(c); c.Dispose(); }
            outRow.Visible = false; tips.Visible = true;
            Line("你好！我可以：", Ui.Text, 10.5f, true);
            Line("· 查档案：项目的合同金额、开工时间、文号、预付款……都会去原文里找依据；", Ui.Text, 10f, false);
            Line("· 处理附件：把上级发来的 Excel 用档案填好，按你的要求改 Word / Excel，总结文件内容；", Ui.Text, 10f, false);
            Line("· 写材料、做汇总：生成情况说明、报告（Word）或清单、台账（Excel）。", Ui.Text, 10f, false);
            Line("把文件拖到这个页面，就会作为附件交给 AI。", Ui.Sub, 9f, false);
            Line("", Ui.Text, 6f, false);
            view.SelectionStart = 0; view.ScrollToCaret();
            UpdateStatus();
        }

        void OnUi(Action a) { if (IsDisposed) return; if (InvokeRequired) BeginInvoke(a); else a(); }

        void Line(string text, Color c, float size, bool bold)
        {
            view.SelectionStart = view.TextLength; view.SelectionLength = 0;
            view.SelectionFont = Ui.F(size, bold ? FontStyle.Bold : FontStyle.Regular);
            view.SelectionColor = c;
            view.AppendText(text + "\n");
            view.SelectionStart = view.TextLength; view.ScrollToCaret();
        }

        /// <summary>AI 的回答：去掉 Markdown 记号，标题加粗；[[路径]] 和对话里出现过的文件名做成可点的链接。</summary>
        void Answer(string text)
        {
            var names = chat.Known.Keys.Where(k => k.IndexOf('\\') < 0 && k.Length >= 5 && k.Contains(".")).OrderByDescending(k => k.Length).ToList();
            foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string l = raw.TrimEnd();
                if (Regex.IsMatch(l, @"^\s*\|?\s*:?-{3,}")) continue;
                bool head = Regex.IsMatch(l, @"^#{1,4}\s");
                l = Regex.Replace(l, @"^#{1,4}\s*", "");
                bool bold = head || Regex.IsMatch(l, @"^\*\*[^*]+\*\*[:：]?$");
                l = l.Replace("**", "").Replace("`", "");
                l = Regex.Replace(l, @"^\s*[-*]\s+", "· ");
                // 切成“普通文字 / 文件链接”几段
                var segs = new List<KeyValuePair<string, string>>();
                int pos = 0;
                foreach (Match mt in Regex.Matches(l, @"\[\[([^\]]+)\]\]"))
                {
                    if (mt.Index > pos) Plain(segs, l.Substring(pos, mt.Index - pos), names);
                    string path = chat.ResolveLink(mt.Groups[1].Value);
                    string shown = Path.GetFileName(mt.Groups[1].Value.Trim());
                    segs.Add(new KeyValuePair<string, string>(path != null ? shown : mt.Groups[1].Value, path));
                    pos = mt.Index + mt.Length;
                }
                if (pos < l.Length) Plain(segs, l.Substring(pos), names);
                Segs(segs, bold);
            }
        }

        /// <summary>普通文字里出现对话中查到过的文件名，也做成链接。</summary>
        void Plain(List<KeyValuePair<string, string>> segs, string s, List<string> names)
        {
            while (s.Length > 0)
            {
                int best = -1; string hit = null;
                foreach (var n in names) { int i = s.IndexOf(n, StringComparison.OrdinalIgnoreCase); if (i >= 0 && (best < 0 || i < best)) { best = i; hit = n; } }
                if (hit == null) { segs.Add(new KeyValuePair<string, string>(s, null)); return; }
                if (best > 0) segs.Add(new KeyValuePair<string, string>(s.Substring(0, best), null));
                segs.Add(new KeyValuePair<string, string>(s.Substring(best, hit.Length), chat.ResolveLink(hit)));
                s = s.Substring(best + hit.Length);
            }
        }

        void Segs(List<KeyValuePair<string, string>> segs, bool bold)
        {
            foreach (var sg in segs)
            {
                view.SelectionStart = view.TextLength; view.SelectionLength = 0;
                bool isLink = sg.Value != null;
                view.SelectionFont = Ui.F(10.5f, (bold ? FontStyle.Bold : FontStyle.Regular) | (isLink ? FontStyle.Underline : FontStyle.Regular));
                view.SelectionColor = isLink ? Ui.Blue : Ui.Text;
                int start = view.TextLength;
                view.AppendText(isLink ? "📄" + sg.Key : sg.Key);
                if (isLink) links.Add(new Link { Start = start, Len = view.TextLength - start, Path = sg.Value });
            }
            view.SelectionStart = view.TextLength; view.SelectionFont = Ui.F(10.5f); view.AppendText("\n");
            view.SelectionStart = view.TextLength; view.ScrollToCaret();
        }

        Link LinkAt(Point pt)
        {
            if (links.Count == 0) return null;
            int i = view.GetCharIndexFromPosition(pt);
            var p0 = view.GetPositionFromCharIndex(i);
            if (pt.Y < p0.Y - 2 || pt.Y > p0.Y + view.Font.Height * 2 || Math.Abs(pt.X - p0.X) > view.Font.Height * 2) return null;
            return links.FirstOrDefault(l => i >= l.Start && i < l.Start + l.Len);
        }

        void OpenLink(string path)
        {
            if (File.Exists(path) || Directory.Exists(path)) Ui.Open(path);
            else Ui.Info("找不到这个文件（可能被移动或删除了）：\n" + path);
        }

        /// <summary>添加附件：先复制一份到“AI 输出\附件”，微信等清理临时文件后也不影响。</summary>
        public void Attach(IEnumerable<string> files)
        {
            string dir = Path.Combine(AiChat.OutDir, "附件");
            foreach (var f in files)
            {
                if (Directory.Exists(f)) { Attach(Directory.GetFiles(f, "*", SearchOption.AllDirectories).Take(60)); continue; }   // 拖进来的是文件夹
                if (!File.Exists(f)) continue;
                if (Store.IsArchive(f))
                {
                    // 压缩包：解压到“AI 输出\附件\压缩包名”，里面的文件都作为附件
                    string to = Path.Combine(dir, Path.GetFileNameWithoutExtension(f));
                    for (int i = 2; Directory.Exists(to); i++) to = Path.Combine(dir, Path.GetFileNameWithoutExtension(f) + "(" + i + ")");
                    try
                    {
                        Store.Extract(f, to);
                        var inner = Directory.GetFiles(to, "*", SearchOption.AllDirectories).Where(x => !Store.IsArchive(x)).ToList();
                        Line("　📦 已解压 " + Path.GetFileName(f) + "：" + inner.Count + " 个文件" + (inner.Count > 40 ? "（前 40 个作为附件）" : ""), Ui.Sub, 9f, false);
                        foreach (var x in inner.Take(40)) if (!attachments.Contains(x)) attachments.Add(x);
                    }
                    catch (Exception ex) { Line("　× 解压失败：" + Path.GetFileName(f) + "（" + ex.Message + "）", Ui.Red, 9f, false); }
                    continue;
                }
                string keep = f;
                try
                {
                    Directory.CreateDirectory(dir);
                    keep = Path.Combine(dir, Path.GetFileName(f));
                    if (!string.Equals(keep, f, StringComparison.OrdinalIgnoreCase))
                    {
                        for (int i = 2; File.Exists(keep) && new FileInfo(keep).Length != new FileInfo(f).Length; i++)
                            keep = Path.Combine(dir, Path.GetFileNameWithoutExtension(f) + "(" + i + ")" + Path.GetExtension(f));
                        if (!File.Exists(keep)) File.Copy(f, keep);
                    }
                }
                catch { keep = f; }
                if (!attachments.Contains(keep)) attachments.Add(keep);
            }
            RefreshAttach();
        }

        void RefreshAttach()
        {
            foreach (Control c in attachRow.Controls.Cast<Control>().ToList()) { attachRow.Controls.Remove(c); c.Dispose(); }
            foreach (var f in attachments)
            {
                var b = (RoundButton)Ui.Btn("📎 " + Path.GetFileName(f) + "  ×", false); b.Style = RoundButton.Kind.Soft; b.Font = Ui.F(8.5f); b.Margin = new Padding(0, 0, Ui.S(6), 0);
                string ff = f; b.Click += (s, e) => { attachments.Remove(ff); RefreshAttach(); };
                new ToolTip().SetToolTip(b, f + "\n点一下移除");
                attachRow.Controls.Add(b);
            }
            attachRow.Visible = attachments.Count > 0;
        }

        /// <summary>其他页面让 AI 助手做事（如项目页“AI 核对补全”）。</summary>
        public void Ask(string text)
        {
            if (busy) { Ui.Info("AI 助手还在处理上一件事，稍等再试。"); return; }
            input.Text = text; Send();
        }

        void ShowPending()
        {
            pendList.Items.Clear();
            foreach (var c in chat.Pending) pendList.Items.Add(c, c.Use);
            pendPanel.Visible = chat.Pending.Count > 0;
            pendPanel.Height = Ui.S(Math.Min(320, 90 + chat.Pending.Count * 24));
            pendTitle.Text = "AI 提交了 " + chat.Pending.Count + " 条档案修改，请核对：勾选的点“确认写入”才会生效";
        }

        void ApplyPending()
        {
            int ok = 0, bad = 0, skip = 0; var done = new List<string>();
            for (int i = 0; i < pendList.Items.Count; i++)
            {
                var c = (PendingChange)pendList.Items[i];
                if (!pendList.GetItemChecked(i)) { skip++; continue; }
                try { c.Apply(); ok++; done.Add(c.Area + "｜" + c.Desc); }
                catch (Exception ex) { bad++; Line("　× 没写成：" + c.Desc + "（" + ex.Message + "）", Ui.Red, 9f, false); }
            }
            St.Save();
            Line("　✓ 已写入档案库 " + ok + " 条" + (skip > 0 ? "，放弃 " + skip + " 条" : "") + (bad > 0 ? "，失败 " + bad + " 条" : ""), Ui.Green, 9.5f, true);
            foreach (var d in done) Line("　　" + AiClient.Cut(d, 120), Ui.Green, 9f, false);
            noteForAi = "（系统提示：你上次提交的档案修改，用户已确认写入 " + ok + " 条" + (skip > 0 ? "、放弃 " + skip + " 条" : "") + "。）\n";
            chat.Pending.Clear(); pendPanel.Visible = false;
            m.ReloadAll();
        }

        void DropPending()
        {
            Line("　已放弃 AI 提交的 " + chat.Pending.Count + " 条修改，档案库没有改动。", Ui.Sub, 9f, false);
            noteForAi = "（系统提示：你上次提交的 " + chat.Pending.Count + " 条档案修改，用户全部放弃了。）\n";
            chat.Pending.Clear(); pendPanel.Visible = false;
        }

        void AddOutput(string path)
        {
            if (!outRow.Visible) { outRow.Controls.Add(Ui.Lbl("生成的文件：", 9f, true, Ui.Text)); outRow.Visible = true; }
            var l = new LinkLabel { Text = Path.GetFileName(path), AutoSize = true, Font = Ui.F(9f), LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(Ui.S(4), Ui.S(2), Ui.S(10), 0) };
            l.LinkClicked += (s, e) => { if (File.Exists(path)) Ui.Open(path); };
            var ctx = new ContextMenuStrip(); ctx.Items.Add("打开", null, (s, e) => Ui.Open(path)); ctx.Items.Add("打开所在文件夹", null, (s, e) => Ui.Reveal(path)); l.ContextMenuStrip = ctx;
            outRow.Controls.Add(l);
            var segs = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("　生成了：", null), new KeyValuePair<string, string>(Path.GetFileName(path), path) };
            Segs(segs, false);
        }

        void Send()
        {
            if (busy) return;
            string text = input.Text.Trim();
            if (text == "" && attachments.Count == 0) return;
            var cfg = AiConfig.Load();
            if (!cfg.Ready)
            {
                if (Ui.Ask("还没有设置 AI 接口（DeepSeek 等）的密钥。现在去设置吗？")) AiSettingsDialog.Show(this);
                cfg = AiConfig.Load(); UpdateStatus();
                if (!cfg.Ready) return;
            }
            if (text == "") text = "请看看附件，告诉我里面是什么，需要怎么处理。";
            string sendText = noteForAi + text; noteForAi = "";
            var atts = attachments.ToList();
            tips.Visible = false;
            Line("你", Ui.Blue, 9.5f, true);
            Line(text, Ui.Text, 10.5f, false);
            foreach (var a in atts) Line("　📎 " + Path.GetFileName(a), Ui.Sub, 9f, false);
            Line("", Ui.Text, 4f, false);
            Line("AI 助手", Ui.Teal, 9.5f, true);
            input.Clear(); attachments.Clear(); RefreshAttach();
            busy = true; send.Enabled = false; status.Text = "AI 正在处理……（查档案、读文件可能要几十秒）"; status.ForeColor = Ui.Blue;
            var t0 = DateTime.Now;
            AiClient.TokensIn = AiClient.TokensOut = 0;
            var th = new Thread(() =>
            {
                string reply = null, err = null;
                try { reply = chat.Send(cfg, sendText, atts); }
                catch (Exception ex) { err = ex is AiException ? ex.Message : ex.GetType().Name + "：" + ex.Message; }
                OnUi(() =>
                {
                    busy = false; send.Enabled = true;
                    if (err != null) Line("出错了：" + err, Ui.Red, 10f, false);
                    else Answer(reply);
                    ShowPending();
                    Line("", Ui.Text, 6f, false);
                    UpdateStatus();
                    status.Text += "　（上一条用时 " + (int)(DateTime.Now - t0).TotalSeconds + " 秒）";
                    input.Focus();
                });
            });
            th.IsBackground = true; th.SetApartmentState(ApartmentState.STA); th.Start();
        }
    }
}
