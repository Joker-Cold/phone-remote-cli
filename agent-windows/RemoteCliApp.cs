using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RemoteCli {
/// The program on the computer: a small window and a tray icon around the relay, which listens on this computer's
/// Tailscale address only, and the terminal agent. Everything it starts ends when it exits.
public sealed partial class App : Form {
    const string Version = "1.0.6";
    readonly string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
    readonly string dataDir = TerminalAgent.DefaultData;
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    Dictionary<string, object> config;
    string password = "", address = "";
    Process relay;
    string bound;                   // the tailnet address the last connecting was for; null before the first
    bool connecting;
    TerminalAgent agent;
    int generation;

    readonly StatusLine status = new StatusLine(), sideStatus = new StatusLine();
    readonly Label modeAbout = new Label(), qrHint = new Label(), toast = new Label(), activityNote = new Label();
    readonly Field addressField = new Field(), passwordField = new Field();
    TextBox addressBox { get { return addressField.Box; } }
    TextBox passwordBox { get { return passwordField.Box; } }
    readonly PictureBox qr = new PictureBox();
    readonly RowList folders = new RowList(), activity = new RowList();
    readonly Switch enabledSwitch = new Switch(), autostartSwitch = new Switch();
    readonly Segmented rights = new Segmented { Items = new[] { "默认", "自动改文件", "只读" } };
    // Tailscale may connect, drop or get another address while the program runs: it is looked at every few seconds.
    readonly System.Windows.Forms.Timer tailnetTimer = new System.Windows.Forms.Timer { Interval = 5000 };
    readonly List<Panel> pages = new List<Panel>();
    readonly List<NavItem> nav = new List<NavItem>();
    readonly System.Windows.Forms.Timer toastTimer = new System.Windows.Forms.Timer(), activityTimer = new System.Windows.Forms.Timer();
    RoundButton changePassword;
    readonly Picker skinPick = new Picker();
    string said = "";       // the line of status shown last
    Dictionary<Control, int[]> parts;       // which colour of the skin each control has
    // A window that a test presses stays off the screen and takes no keys: someone at the computer must not mistake it for the program.
    static bool offstage;
    protected override bool ShowWithoutActivation { get { return offstage; } }
    string activityMark = "";
    bool trouble, preparing = true;
    static string ComputerName = Environment.MachineName;
    readonly NotifyIcon tray = new NotifyIcon();
    bool quitting, hinted;

    string ConfigFile { get { return Path.Combine(dataDir, "config.json"); } }
    string PasswordFile { get { return Path.Combine(dataDir, "password.dpapi"); } }
    string ChildrenFile { get { return Path.Combine(dataDir, "children.json"); } }
    int Port { get { return Convert.ToInt32(config["Port"]); } }

    // ---------- settings
    void LoadSettings() {
        Directory.CreateDirectory(dataDir);
        try { config = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigFile, Encoding.UTF8)); } catch { config = null; }
        if (config == null) config = new Dictionary<string, object>();
        foreach (var item in new Dictionary<string, object> { { "Port", 8722 }, { "Server", "" }, { "RemoteEnabled", true },
                 { "RemoteMaxMode", "full" }, { "RemoteDirs", new object[0] }, { "Skin", Theme.Default } })
            if (!config.ContainsKey(item.Key) || config[item.Key] == null) config[item.Key] = item.Value;
        try { password = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(PasswordFile), null, DataProtectionScope.CurrentUser)); } catch { password = ""; }
        if (password.Length == 0) SetPassword(NewPassword());
    }
    // Settings are changed from the window, from the phone and from the thread that connects: a change and the
    // writing of the file are one step, one at a time. Every setting is there from LoadSettings on, so a change
    // never adds one while another thread reads.
    readonly object saving = new object();
    void Save() {
        lock (saving) {
            string temporary = ConfigFile + ".tmp";
            File.WriteAllText(temporary, json.Serialize(config), new UTF8Encoding(false));
            if (File.Exists(ConfigFile)) File.Replace(temporary, ConfigFile, null); else File.Move(temporary, ConfigFile);
        }
    }
    void Keep(string key, object value) { lock (saving) { config[key] = value; Save(); } }
    static string NewPassword() {
        byte[] bytes = new byte[10];
        using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
        string hex = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        return String.Join("-", Enumerable.Range(0, 4).Select(i => hex.Substring(i * 5, 5)));
    }
    void SetPassword(string value) {
        password = value;
        File.WriteAllBytes(PasswordFile, ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
        // Phones that signed in with the old password must sign in again.
        try { File.Delete(Path.Combine(dataDir, "relay", "sessions.json")); } catch { }
    }
    List<string> Dirs() { return ((System.Collections.IEnumerable)config["RemoteDirs"]).Cast<object>().Select(Convert.ToString).ToList(); }

    // ---------- the relay this program starts
    void Remember() {
        int id = 0;
        try { if (relay != null && !relay.HasExited) id = relay.Id; } catch { }
        try { File.WriteAllText(ChildrenFile, json.Serialize(id != 0 ? new[] { id } : new int[0])); } catch { }
    }
    static void Kill(Process process) { try { if (process != null && !process.HasExited) { process.Kill(); process.WaitForExit(3000); } } catch { } }
    // A copy that crashed may have left its relay running; it holds the port.
    void KillLeftovers() {
        try {
            foreach (object id in json.Deserialize<object[]>(File.ReadAllText(ChildrenFile)))
                try { var p = Process.GetProcessById(Convert.ToInt32(id)); if (p.ProcessName == "python") Kill(p); } catch { }
        } catch { }
    }
    void StopChildren() { Kill(relay); relay = null; Remember(); }
    static bool Listening(string host, int port) {
        try { using (var client = new TcpClient()) { var attempt = client.BeginConnect(host, port, null, null); bool ok = attempt.AsyncWaitHandle.WaitOne(400) && client.Connected; return ok; } } catch { return false; }
    }

    void Say(string text, bool problem = false) {
        if (InvokeRequired) { BeginInvoke(new Action(() => Say(text, problem))); return; }
        said = text;
        status.Set(text, problem ? Theme.Bad : text.StartsWith("正在") ? Theme.Busy : Theme.Good);
        trouble = problem; preparing = !problem && text.StartsWith("正在");
        if (nav.Count > 0) RefreshActivity();
        tray.Text = ("Phone Remote CLI：" + text).Length > 60 ? ("Phone Remote CLI：" + text).Substring(0, 60) : "Phone Remote CLI：" + text;
    }
    void ShowAddress(string where) {
        if (InvokeRequired) { BeginInvoke(new Action(() => ShowAddress(where))); return; }
        address = where;
        Image old = qr.Image;
        string link = "remotecli://connect?u=" + Uri.EscapeDataString(where) + "&p=" + Uri.EscapeDataString(password);
        // The computer's name lets a phone that knows several computers tell them apart; it is left out when the code would not fit.
        qr.Image = where.Length > 0 ? (Picture(link + "&n=" + Uri.EscapeDataString(ComputerName), 6) ?? Picture(link, 6)) : null;
        qr.Visible = qr.Image != null; qrHint.Visible = qr.Image == null;
        if (old != null) old.Dispose();
        addressBox.Text = where; passwordBox.Text = password;
    }
    public static Bitmap Picture(string text, int scale) {
        bool[,] dark = QrCode.Encode(text);
        if (dark == null) return null;
        int size = dark.GetLength(0), quiet = 4;
        var picture = new Bitmap((size + quiet * 2) * scale, (size + quiet * 2) * scale);
        using (var g = Graphics.FromImage(picture)) {
            g.Clear(Color.White);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) if (dark[y, x]) g.FillRectangle(Brushes.Black, (x + quiet) * scale, (y + quiet) * scale, scale, scale);
        }
        return picture;
    }

    string Python { get { return Path.Combine(appDir, "python", "python.exe"); } }
    // The program's own relay, listening on the tailnet address alone.
    Process StartRelay(string host, int port, string secret) {
        var start = new ProcessStartInfo(Python, "\"" + Path.Combine(appDir, "relay", "server.py") + "\" --host " + host + " --port " + port
            + " --data \"" + Path.Combine(dataDir, "relay") + "\" --web \"" + Path.Combine(appDir, "web") + "\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = appDir };
        start.EnvironmentVariables["RCLI_PASSWORD"] = secret;
        start.EnvironmentVariables["PYTHONUTF8"] = "1";
        return Process.Start(start);
    }
    async Task<bool> Up(Process process, string host, int port) {
        for (int n = 0; n < 40 && !Listening(host, port); n++) { await Task.Delay(250); if (process.HasExited) break; }
        return Listening(host, port);
    }

    // Starts the relay on this computer's tailnet address. Runs off the window's thread.
    async Task Connect() {
        int mine = ++generation;
        ShowAddress("");
        StopChildren();
        try {
            string ip = bound = Tailnet.Address();
            if (ip.Length == 0) { Say("未检测到 Tailscale。请在这台电脑上打开 Tailscale 并登录，连上后自动就绪", true); return; }
            if (!File.Exists(Python)) { Say("安装不完整：缺少 " + Python, true); return; }
            int port = Port;
            if (Listening(ip, port)) { Say("端口 " + port + " 已被别的程序占用，请到“设置”里换一个端口", true); return; }
            relay = StartRelay(ip, port, password);
            Remember();
            bool up = await Up(relay, ip, port);
            if (mine != generation) return;
            if (!up) { StopChildren(); Say("本机服务没有启动成功", true); return; }
            // The agent reports to the relay at the same address: a connection to this computer's own address stays on it.
            Keep("Server", "http://" + ip + ":" + port);
            if (agent != null) agent.Reset();
            ShowAddress("http://" + ip + ":" + port);
            Say("已就绪。手机打开 Tailscale 后，用 App 扫码连接");
        } catch (Exception error) { Say("启动失败：" + error.Message, true); }
        finally { if (mine == generation) connecting = false; }
    }
    void Reconnect() { connecting = true; Task.Run(() => Connect()); }
    // Tailscale connected, dropped or was given another address since the last look, or the relay ended by itself:
    // the relay is started again for the address there is now.
    void LookAtTailnet() {
        if (connecting || quitting || bound == null) return;
        bool ended = false;
        try { ended = relay != null && relay.HasExited; } catch (InvalidOperationException) { }
        if (ended || Tailnet.Address() != bound) Reconnect();
    }

    // ---------- window
    const int SideWidth = 212, PageWidth = 708, WindowHeight = 600;
    T Place<T>(Control parent, T control, int x, int y, int w, int h) where T : Control {
        control.SetBounds(x, y, w, h);
        parent.Controls.Add(control);
        return control;
    }
    Label Note(Control parent, string text, int x, int y, int w, int h, Color color, float size = 0, bool bold = false) {
        return Place(parent, Theme.Label(text, color, parent.BackColor, size, bold), x, y, w, h);
    }
    RoundButton Action(Control parent, string text, string glyph, ButtonKind kind, int x, int y, int w, int h, EventHandler click) {
        var button = Place(parent, new RoundButton { Text = text, Glyph = glyph, Kind = kind }, x, y, w, h);
        if (click != null) button.Click += click;
        return button;
    }
    Panel Page(string title, string about) {
        var page = Place(this, new Panel { BackColor = Theme.Bg, Visible = false }, SideWidth, 0, PageWidth, WindowHeight);
        Note(page, title, 28, 24, 420, 30, Theme.Ink, 15f, true);
        Note(page, about, 28, 56, 652, 20, Theme.Muted);
        pages.Add(page);
        return page;
    }
    void Go(int index) {
        for (int i = 0; i < pages.Count; i++) { pages[i].Visible = i == index; nav[i].Chosen = i == index; }
        if (index == 2) RefreshActivity();
    }
    // A short confirmation that fades by itself, e.g. after copying.
    void Toast(string text) {
        toast.Text = text;
        using (var g = CreateGraphics()) toast.Width = (int)g.MeasureString(text, toast.Font).Width + (int)(36 * g.DpiX / 96f);
        toast.Left = pages[0].Left + (pages[0].Width - toast.Width) / 2;
        toast.Visible = true; toast.BringToFront();
        toastTimer.Stop(); toastTimer.Start();
    }
    void Copy(string text, string what) {
        if (String.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); Toast("已复制" + what); } catch (ExternalException) { Toast("复制失败，请再试一次"); }
    }
    // Another skin for the window that is open: every control is given the same part of the new palette.
    void Wear(string name) {
        Theme.Use(name);
        if (parts != null) Theme.Paint(parts);
        if (IsHandleCreated) Theme.Title(Handle);
        skinPick.Set(Theme.Titles[Array.IndexOf(Theme.Names, Theme.Skin)]);
        ShowProjects(); activityMark = "\n"; RefreshActivity();
        Say(said.Length > 0 ? said : "正在启动…", trouble);
        Invalidate(true);
    }
    void ShowProjects() {
        folders.Show(Dirs().Select(line => {
            int at = line.IndexOf('=');
            string name = at > 0 ? line.Substring(0, at) : line, path = at > 0 ? line.Substring(at + 1) : "";
            bool there = false; try { there = Directory.Exists(path); } catch { }
            return new Row { Glyph = Theme.IconFolder, Title = name, About = path, Tag = there ? "" : "文件夹不存在", TagColour = Theme.Bad, Value = path };
        }));
        nav[1].SetBadge(folders.Rows.Count > 0 ? folders.Rows.Count.ToString() : "");
    }
    void AddProject(string path) {
        var list = Dirs();
        string full; try { full = Path.GetFullPath(path).TrimEnd('\\'); } catch { return; }
        if (!Directory.Exists(full)) return;
        if (list.Any(line => String.Equals(line.Substring(line.IndexOf('=') + 1).TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase))) { Toast("这个文件夹已经在列表里"); return; }
        string name = new DirectoryInfo(full).Name.Replace("=", " ");
        if (name.Length == 0 || name.EndsWith(":") || name.EndsWith("\\")) name = "磁盘 " + full.Substring(0, 1);
        string wanted = name;
        for (int n = 2; list.Any(line => line.StartsWith(name + "=")); n++) name = wanted + " " + n;
        list.Add(name + "=" + full);
        Keep("RemoteDirs", list.ToArray()); ShowProjects();
        folders.SelectedIndex = folders.Items.Count - 1;
        Toast("已添加 " + name);
    }
    static string Since(DateTime started) {
        TimeSpan gone = DateTime.UtcNow - started;
        return gone.TotalMinutes < 1 ? "刚刚开始" : gone.TotalHours < 1 ? (int)gone.TotalMinutes + " 分钟前开始" : gone.TotalDays < 1 ? (int)gone.TotalHours + " 小时前开始" : (int)gone.TotalDays + " 天前开始";
    }
    void RefreshActivity() {
        List<string[]> running = agent == null ? new List<string[]>() : agent.Running();
        if (running == null) return;
        string mark = String.Join("|", running.Select(r => String.Join(",", r)));
        int phones = running.Count(r => r[6] == "phone"), cli = running.Count(r => r[6] == "cli");
        nav[2].SetBadge(running.Count > 0 ? running.Count.ToString() : "");
        bool allowed = Convert.ToString(config["RemoteEnabled"]) == "True";
        sideStatus.Set(trouble ? "还不能连接" : preparing ? "正在准备…" : !allowed ? "已暂停手机访问" : phones == 0 ? "手机可以连接" : phones + " 个共用终端运行中",
            trouble ? Theme.Bad : preparing || !allowed ? Theme.Busy : Theme.Good);
        if (mark == activityMark && running.Count > 0 && !pages[2].Visible) return;
        activityMark = mark;
        activity.Show(running.Select(r => {
            DateTime started; DateTime.TryParse(r[3], null, System.Globalization.DateTimeStyles.RoundtripKind, out started);
            string tool = r[0] == "claude" ? "Claude Code" : r[0] == "codex" ? "Codex" : "PowerShell";
            string tag = r[6] == "shared" ? "被应用占用" : r[6] == "remote" ? "其它远程终端" : r[6] == "unknown" ? "被程序占用" : r[6] == "cli" ? "电脑自己的窗口" : r[2] == "busy" ? "正在执行" : "手机和电脑共用";
            return new Row { Glyph = Theme.IconTerminal, Title = (r[4].Length > 0 ? r[4] : tool) + " · " + r[1], About = (r[5].Length > 0 ? r[5] + " · " : "") + tool + (r[6] == "phone" ? " · 双击在电脑上打开 · " + Since(started.ToUniversalTime()) : ""), Value = r,
                             Tag = tag, TagColour = r[6] == "phone" || r[6] == "cli" ? Theme.Good : Theme.Busy };
        }));
        activityNote.Text = running.Count == 0 ? "" : phones + " 个共用终端 · " + cli + " 个电脑独立窗口。共用终端在手机和电脑上显示同一画面，两端均可输入。";
    }
    // A window on this computer for the very pages the phone uses. The terminals in it are the same ones the phone
    // shows, so a terminal can be looked at and typed into from both at once, and nothing has to be handed over.
    // The window signs in with a ticket the relay gives out for one use: the password is never part of an address.
    void OpenWindow(string terminal) {
        string origin = Convert.ToString(config["Server"]).TrimEnd('/');
        if (origin.Length == 0 || password.Length == 0) { Toast("电脑后台还没有准备好，请稍后再试"); return; }
        Task.Run(() => {
            try {
                string token = Convert.ToString(Ask(origin + "/api/login", new Dictionary<string, object> { { "password", password } }, "")["token"]);
                string ticket = Convert.ToString(Ask(origin + "/api/ticket", new Dictionary<string, object>(), token)["ticket"]);
                string url = origin + "/" + (terminal != null ? "?open=" + terminal : "") + "#t=" + Uri.EscapeDataString(ticket);
                string browser = AppBrowser();
                // A browser's "app" window has no address bar and keeps its sign-in in a folder of its own.
                if (browser != null) Process.Start(new ProcessStartInfo(browser, "--app=\"" + url + "\" --user-data-dir=\"" + Path.Combine(dataDir, "window") + "\" --window-size=480,880 --no-first-run --no-default-browser-check") { UseShellExecute = false });
                else Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            } catch (Exception error) {
                BeginInvoke(new Action(() => Toast("窗口没有打开：" + error.Message)));
            }
        });
    }
    Dictionary<string, object> Ask(string url, Dictionary<string, object> payload, string token) {
        var request = (HttpWebRequest)WebRequest.Create(url); request.Method = "POST"; request.ContentType = "application/json"; request.Timeout = 8000;
        if (token.Length > 0) request.Headers["Authorization"] = "Bearer " + token;
        byte[] body = Encoding.UTF8.GetBytes(json.Serialize(payload));
        using (var stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
        using (var reply = request.GetResponse()) using (var reader = new StreamReader(reply.GetResponseStream(), Encoding.UTF8))
            return json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
    }
    static string AppBrowser() {
        foreach (string root in new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
            foreach (string inner in new[] { @"Microsoft\Edge\Application\msedge.exe", @"Google\Chrome\Application\chrome.exe" }) {
                if (String.IsNullOrEmpty(root)) continue;
                string file = Path.Combine(root, inner);
                if (File.Exists(file)) return file;
            }
        return null;
    }
    void OpenActivity() {
        var row = activity.Selected; var raw = row == null ? null : row.Value as string[];
        if (raw == null || raw.Length < 8) { Toast("请先在列表中选择一个终端"); return; }
        if (raw[6] != "phone") { Toast("这段对话在电脑的独立窗口中运行，请在该窗口中使用"); return; }
        OpenWindow(raw[7]);
    }
    void TakeoverActivity() {
        var row = activity.Selected; var raw = row == null ? null : row.Value as string[];
        if (raw == null || raw.Length < 8 || raw[6] != "phone") { Toast("请先在列表中选择一个手机和电脑共用的终端"); return; }
        if (agent == null) { Toast("电脑后台还没有准备好，请稍后再试"); return; }
        string tool = raw[0] == "claude" ? "Claude Code" : raw[0] == "codex" ? "Codex" : "PowerShell";
        if (raw[0] == "shell") { Toast("PowerShell 没有可恢复的会话，不能转为独立窗口；可通过“在电脑上打开”直接使用"); return; }
        if (!Confirm("转成电脑上的独立窗口？", "这个终端会先结束，然后在电脑的命令行窗口里接着同一段对话，正在执行的任务会中断，手机上不再能看到它。\n仅需在电脑上使用时，选“在电脑上打开”即可，无需转换。", "结束它并转成独立窗口", true)) return;
        activity.Enabled = false;
        Task.Run(() => {
            try {
                var takeover = agent.TakeoverForComputer(raw[7]);
                BeginInvoke(new Action(() => {
                    activity.Enabled = true;
                    try { Process.Start(TerminalAgent.ComputerProcess(takeover, Convert.ToString(config["RemoteMaxMode"]))); Toast("已转成电脑上的独立窗口：" + tool); RefreshActivity(); }
                    catch (Exception error) { Toast("电脑终端没有打开：" + error.Message); }
                }));
            } catch (Exception error) {
                BeginInvoke(new Action(() => { activity.Enabled = true; Toast(error.Message); RefreshActivity(); }));
            }
        });
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.Title(Handle); }
    public App() {
        LoadSettings();
        KillLeftovers();
        Text = "Phone Remote CLI"; Font = Theme.Text();
        AutoScaleMode = AutoScaleMode.None;      // positions below are for 100%; the whole window is scaled once at the end
        ClientSize = new Size(SideWidth + PageWidth, WindowHeight); FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg; ForeColor = Theme.Ink;
        Icon = Theme.MarkIcon(32);

        // ---- side bar
        var side = Place(this, new Panel { BackColor = Theme.Side }, 0, 0, SideWidth, WindowHeight);
        Place(side, new PictureBox { Image = Theme.Mark(88), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Side }, 20, 24, 40, 40);
        Note(side, "Phone Remote CLI", 70, 24, 138, 22, Theme.Ink, 10.5f, true);
        Note(side, "v" + Version, 71, 46, 130, 18, Theme.Faint, 8.25f);
        string[] names = { "连接", "项目", "活动", "设置" }, glyphs = { Theme.IconPhone, Theme.IconFolder, Theme.IconPulse, Theme.IconGear };
        for (int i = 0; i < names.Length; i++) {
            int index = i;
            var item = Place(side, new NavItem { Title = names[i], Glyph = glyphs[i] }, 12, 92 + i * 46, SideWidth - 24, 40);
            item.Click += (s, e) => Go(index);
            nav.Add(item);
        }
        Place(side, sideStatus, 18, WindowHeight - 84, SideWidth - 30, 22); sideStatus.BackColor = Theme.Side; sideStatus.Set("正在启动…", Theme.Busy);
        Note(side, "关闭窗口后仍在托盘运行", 20, WindowHeight - 58, SideWidth - 30, 18, Theme.Faint, 8.25f);
        Action(side, "退出", Theme.IconPower, ButtonKind.Ghost, 12, WindowHeight - 38, 76, 28, (s, e) => Quit()).BackColor = Theme.Side;

        // ---- page: connect
        var connect = Page("连接手机", "在手机 App 里点“扫码连接”，扫下面的二维码。");
        Place(connect, status, 28, 82, 652, 26); status.Font = Theme.Text(9.5f, true); status.Set("正在启动…", Theme.Busy);
        var code = Place(connect, new Card(), 28, 122, 252, 318);
        var tile = Place(code, new Card { Fill = Color.White }, 22, 22, 208, 208);
        Place(tile, qr, 10, 10, 188, 188); qr.SizeMode = PictureBoxSizeMode.Zoom; qr.BackColor = Color.White;
        Place(tile, qrHint, 10, 84, 188, 40); qrHint.BackColor = Color.White; qrHint.ForeColor = Color.FromArgb(120, 124, 140); qrHint.TextAlign = ContentAlignment.MiddleCenter; qrHint.Text = "正在准备…";
        var caption = Note(code, "用手机 App 扫码", 12, 242, 228, 22, Theme.Ink, 10f, true); caption.TextAlign = ContentAlignment.MiddleCenter;
        var codeAbout = Note(code, "或在 App 里输入右边的地址和密码", 12, 266, 228, 18, Theme.Muted, 8.5f); codeAbout.TextAlign = ContentAlignment.MiddleCenter;
        var keys = Note(code, Theme.IconLock + "  请勿向他人提供地址和密码", 12, 288, 228, 18, Theme.Faint, 8.25f); keys.TextAlign = ContentAlignment.MiddleCenter; keys.Font = new Font(Theme.Icons, 8.25f);
        keys.Paint += (s, e) => { };

        var way = Place(connect, new Card(), 296, 122, 384, 318);
        Note(way, "连接方式 · Tailscale", 20, 16, 300, 18, Theme.Muted, 8.5f, true);
        Place(way, modeAbout, 20, 42, 344, 120); modeAbout.BackColor = Theme.Panel; modeAbout.ForeColor = Theme.Muted; modeAbout.Font = Theme.Text(8.5f);
        modeAbout.Text = "只在本机的 Tailscale 地址上提供服务，局域网和公网都访问不到。流量经 Tailscale 端到端加密。" + Environment.NewLine + Environment.NewLine
            + "手机和电脑需登录同一个 Tailscale 账号，手机上保持 Tailscale 连接。建议在 Tailscale 的访问控制里只允许自己的手机访问本机端口。";
        Note(way, "地址", 20, 172, 200, 18, Theme.Muted, 8.5f, true);
        Place(way, addressField, 20, 194, 256, 34); addressBox.ReadOnly = true;
        Action(way, "复制", Theme.IconCopy, ButtonKind.Normal, 284, 194, 80, 34, (s, e) => Copy(addressBox.Text, "地址"));
        Note(way, "密码", 20, 238, 200, 18, Theme.Muted, 8.5f, true);
        Place(way, passwordField, 20, 260, 256, 34); passwordBox.ReadOnly = true; passwordBox.Font = new Font("Consolas", 10f);
        Action(way, "复制", Theme.IconCopy, ButtonKind.Normal, 284, 260, 80, 34, (s, e) => Copy(passwordBox.Text, "密码"));
        Action(connect, "重新连接", Theme.IconRefresh, ButtonKind.Normal, 28, 456, 116, 34, (s, e) => Reconnect());
        changePassword = Action(connect, "换一个密码", Theme.IconLock, ButtonKind.Ghost, 152, 456, 124, 34, (s, e) => {
            if (!Confirm("换一个密码？", "换密码后，已连接的手机需要重新扫码。", "换密码", false)) return;
            SetPassword(NewPassword()); Reconnect(); Toast("密码已更新");
        });
        Note(connect, "手机只能在“项目”中添加的文件夹内启动终端；终端启动后拥有与本机操作相同的权限，可以访问整台电脑。", 28, 506, 652, 36, Theme.Faint, 8.5f);

        // ---- page: projects
        var projects = Page("项目文件夹", "手机只能在这些文件夹里新建终端和继续对话。可以把文件夹直接拖进来。");
        var listCard = Place(projects, new Card(), 28, 92, 652, 410);
        Place(listCard, folders, 8, 8, 636, 394);
        folders.EmptyTitle = "还没有项目文件夹"; folders.EmptyAbout = "把文件夹拖到这里，或点下面的“添加文件夹”。\n添加后在手机上就能看到这个项目。";
        Action(projects, "添加文件夹", Theme.IconAdd, ButtonKind.Primary, 28, 518, 128, 36, (s, e) => {
            using (var dialog = new FolderBrowserDialog { Description = "选择一个项目文件夹" }) if (dialog.ShowDialog(this) == DialogResult.OK) AddProject(dialog.SelectedPath);
        });
        var rename = Action(projects, "重命名", Theme.IconEdit, ButtonKind.Normal, 164, 518, 100, 36, (s, e) => {
            int at = folders.SelectedIndex; if (at < 0) return;
            var list = Dirs(); string path = list[at].Substring(list[at].IndexOf('=') + 1);
            string name = Ask("项目名称", "手机上显示的名称，不改动文件夹本身。", folders.Rows[at].Title).Trim().Replace("=", " ");
            if (name.Length == 0 || name.Length > 40) return;
            if (list.Where((line, i) => i != at).Any(line => line.StartsWith(name + "="))) { Toast("已有同名项目"); return; }
            list[at] = name + "=" + path; Keep("RemoteDirs", list.ToArray()); ShowProjects();
        });
        var open = Action(projects, "打开文件夹", Theme.IconOpen, ButtonKind.Normal, 272, 518, 124, 36, (s, e) => {
            var row = folders.Selected; if (row == null) return;
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + row.Value + "\"")); } catch (Exception) { Toast("打不开这个文件夹"); }
        });
        var remove = Action(projects, "移除", Theme.IconDelete, ButtonKind.Danger, 588, 518, 92, 36, (s, e) => {
            int at = folders.SelectedIndex; if (at < 0) return;
            if (!Confirm("移除“" + folders.Rows[at].Title + "”？", "仅取消手机对该文件夹的访问，文件夹及其中的对话不会被删除。", "移除", true)) return;
            var list = Dirs(); list.RemoveAt(at); Keep("RemoteDirs", list.ToArray()); ShowProjects();
        });
        EventHandler chosen = (s, e) => { rename.Enabled = open.Enabled = remove.Enabled = folders.SelectedIndex >= 0; };
        folders.SelectedIndexChanged += chosen; chosen(null, EventArgs.Empty);
        folders.DoubleClick += (s, e) => open.PerformClick();
        foreach (Control target in new Control[] { projects, listCard, folders }) {
            target.AllowDrop = true;
            target.DragEnter += (s, e) => { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; };
            target.DragDrop += (s, e) => { var dropped = e.Data.GetData(DataFormats.FileDrop) as string[]; if (dropped != null) foreach (string path in dropped) AddProject(path); };
        }
        ShowProjects();

        // ---- page: activity
        var activityPage = Page("活动", "手机和电脑使用同一批终端。双击终端可在电脑上打开，手机端仍可查看和输入，无需交接。");
        var liveCard = Place(activityPage, new Card(), 28, 92, 652, 362);
        Place(liveCard, activity, 8, 8, 636, 346);
        activity.EmptyTitle = "没有正在运行的终端"; activity.EmptyAbout = "点下面的“新建或查看全部”，或在手机上选一个项目新建终端，\n它就会出现在这里，两边都能用。";
        activity.DoubleClick += (s, e) => OpenActivity();
        activity.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; OpenActivity(); } };
        Action(activityPage, "在电脑上打开", Theme.IconTerminal, ButtonKind.Primary, 28, 468, 150, 36, (s, e) => OpenActivity());
        Action(activityPage, "新建或查看全部", Theme.IconOpen, ButtonKind.Normal, 188, 468, 164, 36, (s, e) => OpenWindow(null));
        Action(activityPage, "转成独立窗口", "", ButtonKind.Ghost, 362, 468, 130, 36, (s, e) => TakeoverActivity());
        Place(activityPage, activityNote, 28, 518, 652, 20); activityNote.BackColor = Theme.Bg; activityNote.ForeColor = Theme.Muted;

        // ---- page: settings
        var settings = Page("设置", "这些设置只影响这台电脑。");
        var rows = Place(settings, new Card(), 28, 92, 652, 5 * 53 + 12);
        enabledSwitch.On = Convert.ToString(config["RemoteEnabled"]) == "True";
        enabledSwitch.Changed += (s, e) => { Keep("RemoteEnabled", enabledSwitch.On); RefreshActivity(); Toast(enabledSwitch.On ? "已允许手机访问" : "已暂停手机访问，立即生效"); };
        autostartSwitch.Changed += (s, e) => Set("autostart", autostartSwitch.On);
        rights.Changed += (s, e) => { Set("rights", Rights[rights.Chosen]); Toast("对之后新开的终端生效"); };
        ShowSettings();
        var portBox = new Field(); portBox.Box.Text = Port.ToString(); portBox.Box.MaxLength = 5;
        EventHandler port = (s, e) => {
            int wanted;
            if (!Int32.TryParse(portBox.Box.Text.Trim(), out wanted) || wanted < 1024 || wanted > 65535) { portBox.Box.Text = Port.ToString(); Toast("端口要在 1024 到 65535 之间"); return; }
            if (wanted == Port) return;
            Keep("Port", wanted); Toast("端口已改为 " + wanted); Reconnect();
        };
        portBox.Box.Leave += port; portBox.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; port(s, e); } };
        skinPick.Set(Theme.Titles[Array.IndexOf(Theme.Names, Theme.Default)]);
        skinPick.Opening += (s, e) => {
            var menu = Theme.Menu();
            for (int i = 0; i < Theme.Names.Length; i++) {
                string name = Theme.Names[i];
                var item = new ToolStripMenuItem(Theme.Titles[i]) { Checked = name == Theme.Skin };
                item.Click += (c, a) => { Keep("Skin", name); Wear(name); };
                menu.Items.Add(item);
            }
            menu.MinimumSize = new Size(skinPick.Width, 0);
            menu.Closed += (c, a) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(skinPick, new Point(0, skinPick.Height + 2));
        };
        var all = new[] {
            new SettingRow("允许手机访问", "关闭后手机立即不能操作，正在运行的终端不受影响", enabledSwitch, 46, 26),
            new SettingRow("登录 Windows 后自动启动", "开机后在托盘里运行，不弹出窗口", autostartSwitch, 46, 26),
            new SettingRow("新终端的权限", "Claude Code 和 Codex 从手机启动时用什么权限模式", rights, 250, 34),
            new SettingRow("本机端口", "Tailscale 地址上使用的端口，被占用时换一个", portBox, 96, 32),
            new SettingRow("外观", "和手机 App 相同的几种配色，默认与 App 一致", skinPick, 170, 34) };
        for (int i = 0; i < all.Length; i++) Place(rows, all[i], 2, 6 + i * 53, 648, 53);
        Action(settings, "打开数据文件夹", Theme.IconOpen, ButtonKind.Normal, 28, 544, 150, 36, (s, e) => { try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dataDir + "\"")); } catch (Exception) { } });
        Action(settings, "退出 Phone Remote CLI", Theme.IconPower, ButtonKind.Danger, 530, 544, 150, 36, (s, e) => Quit());

        // ---- toast, tray, timers
        Place(this, toast, 0, WindowHeight - 58, 200, 34);
        toast.Visible = false; toast.BackColor = Theme.Ink; toast.ForeColor = Theme.OnAccent; toast.TextAlign = ContentAlignment.MiddleCenter; toast.Font = Theme.Text(9f, true);
        toastTimer.Interval = 1600; toastTimer.Tick += (s, e) => { toastTimer.Stop(); toast.Visible = false; };
        activityTimer.Interval = 2000; activityTimer.Tick += (s, e) => RefreshActivity();
        tray.Icon = Theme.MarkIcon(32); tray.Visible = true; tray.Text = "Phone Remote CLI";
        tray.DoubleClick += (s, e) => Reveal();
        tray.ContextMenuStrip = new ContextMenuStrip();
        tray.ContextMenuStrip.Items.Add("显示窗口", null, (s, e) => Reveal());
        tray.ContextMenuStrip.Items.Add("复制地址", null, (s, e) => { if (address.Length > 0) try { Clipboard.SetText(address); } catch (ExternalException) { } });
        tray.ContextMenuStrip.Items.Add("重新连接", null, (s, e) => Reconnect());
        tray.ContextMenuStrip.Items.Add("-");
        tray.ContextMenuStrip.Items.Add("退出", null, (s, e) => Quit());
        FormClosing += (s, e) => {
            if (quitting || e.CloseReason != CloseReason.UserClosing) { Shutdown(); return; }
            e.Cancel = true; Hide();
            if (!hinted) { hinted = true; tray.ShowBalloonTip(4000, "Phone Remote CLI 仍在运行", "手机仍可连接。如需停止，请在托盘图标上点右键选“退出”。", ToolTipIcon.Info); }
        };
        Go(Dirs().Count == 0 ? 1 : 0);        // a first start begins where something has to be done
        // The window was made in the default skin, whose colours all differ: which of them each control has is noted
        // now, and the skin chosen in the settings is put on.
        parts = Theme.Parts(this);
        if (Convert.ToString(config["Skin"]) != Theme.Default) Wear(Convert.ToString(config["Skin"]));
        using (var g = CreateGraphics()) {
            float factor = g.DpiX / 96f;
            if (factor > 1.01f) { Scale(new SizeF(factor, factor)); folders.ItemHeight = (int)(54 * factor); activity.ItemHeight = (int)(54 * factor); }
        }
        Shown += (s, e) => {
            agent = new TerminalAgent(dataDir) { Version = Version };
            agent.Computer = Itself;
            tailnetTimer.Tick += (t, a) => LookAtTailnet();
            connecting = true;
            Task.Run(async () => { await Connect(); await agent.Run(); });
            tailnetTimer.Start();
            activityTimer.Start();
        };
    }
    void Reveal() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    void Shutdown() { generation++; tray.Visible = false; StopChildren(); }
    void Quit() {
        if (!Confirm("退出 Phone Remote CLI？", "退出后手机不能再连接，正在运行的终端会结束。", "退出", true)) return;
        quitting = true; Shutdown();
        Environment.Exit(0);
    }

    // A small window in the program's own look: a question with two answers, optionally with lines to fill in, each
    // under its label when it has one. Returns what was filled in, or null when the question was not agreed to.
    string[] Dialog(string title, string text, string yes, bool danger, string[] labels, string[] values) {
        using (var dialog = new Form { Text = "Phone Remote CLI", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                                       MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, Font = Theme.Text(), BackColor = Theme.Bg, ForeColor = Theme.Ink, AutoScaleMode = AutoScaleMode.None, Icon = Icon }) {
            dialog.HandleCreated += (s, e) => Theme.Title(dialog.Handle);
            Place(dialog, Theme.Label(title, Theme.Ink, Theme.Bg, 11.5f, true), 24, 22, 372, 26);
            Place(dialog, Theme.Label(text, Theme.Muted, Theme.Bg), 24, 52, 372, 40);
            var boxes = new List<Field>();
            int y = 96;
            for (int i = 0; labels != null && i < labels.Length; i++) {
                if (labels[i].Length > 0) { Place(dialog, Theme.Label(labels[i], Theme.Muted, Theme.Bg, 8.5f), 24, y, 372, 18); y += 22; }
                var box = new Field(); box.BackColor = Theme.Bg; box.Box.Text = values != null && i < values.Length ? values[i] ?? "" : "";
                Place(dialog, box, 24, y, 372, 34); boxes.Add(box);
                y += 44;
            }
            y = boxes.Count == 0 ? 108 : y + 6;
            dialog.ClientSize = new Size(420, y + 50);
            var ok = new RoundButton { Text = yes, Kind = danger ? ButtonKind.Danger : ButtonKind.Primary, DialogResult = DialogResult.OK };
            var cancel = new RoundButton { Text = "取消", DialogResult = DialogResult.Cancel };
            int wide = yes.Length > 4 ? 128 : 96;
            Place(dialog, cancel, 396 - wide - 8 - 96, y, 96, 34); Place(dialog, ok, 396 - wide, y, wide, 34);
            dialog.AcceptButton = ok; dialog.CancelButton = cancel;
            using (var g = dialog.CreateGraphics()) { float factor = g.DpiX / 96f; if (factor > 1.01f) dialog.Scale(new SizeF(factor, factor)); }
            if (boxes.Count > 0) dialog.Shown += (s, e) => { boxes[0].Box.Focus(); boxes[0].Box.SelectAll(); };
            return dialog.ShowDialog(Visible ? this : null) == DialogResult.OK ? boxes.Select(b => b.Box.Text).ToArray() : null;
        }
    }
    bool Confirm(string title, string text, string yes, bool danger) { return Dialog(title, text, yes, danger, null, null) != null; }
    string Ask(string title, string text, string value) { var filled = Dialog(title, text, "确定", false, new[] { "" }, new[] { value }); return filled == null ? "" : filled[0]; }

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [STAThread] public static int Main(string[] args) {
        // --qr <text> <file.png>: writes the picture the window would show; used by the tests.
        if (args.Length == 3 && args[0] == "--qr") { using (var picture = Picture(args[1], 8)) { if (picture == null) return 2; picture.Save(args[2], ImageFormat.Png); } return 0; }
        bool created;
        // One copy per data folder: a copy pointed at another folder with REMOTECLI_DATA (a test, a second setup) may run beside it.
        string elsewhere = Environment.GetEnvironmentVariable("REMOTECLI_DATA");
        using (var single = new Mutex(true, "Local\\RemoteCliApp" + (String.IsNullOrWhiteSpace(elsewhere) ? "" : "-" + Math.Abs(elsewhere.ToLowerInvariant().GetHashCode())), out created)) {
            if (!created) { MessageBox.Show("Phone Remote CLI 已经在运行，请看屏幕右下角的托盘图标。", "Phone Remote CLI"); return 0; }
            ServicePointManager.DefaultConnectionLimit = 8;      // the held request and the reports run side by side
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length >= 2 && args[0] == "--screenshot") ComputerName = "my-computer";       // a published picture must not carry this computer's name
            var window = new App();
            // --screenshot <file.png>: draws the window into a picture after a moment and exits; used for the documentation.
            if (args.Length >= 2 && args[0] == "--screenshot") {      // an optional third argument chooses the page (0 to 3)
                // Such a window is never one to use: it stays off the screen, also when only a picture is asked for.
                offstage = true; window.StartPosition = FormStartPosition.Manual; window.Location = new Point(-30000, -30000); window.ShowInTaskbar = false;
                window.Show();
                if (args.Length >= 3) { int page; if (Int32.TryParse(args[2], out page)) window.Go(page); }
                Action<double> wait = seconds => { var end = DateTime.UtcNow.AddSeconds(seconds); while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(30); } };
                var until = DateTime.UtcNow.AddSeconds(4);
                while (DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(30); }
                using (var picture = new Bitmap(window.ClientSize.Width, window.ClientSize.Height)) {
                    using (var g = Graphics.FromImage(picture)) g.Clear(Theme.Bg);
                    Rectangle client = window.RectangleToScreen(window.ClientRectangle);
                    using (var whole = new Bitmap(window.Width, window.Height)) {
                        window.DrawToBitmap(whole, new Rectangle(0, 0, window.Width, window.Height));
                        using (var g = Graphics.FromImage(picture)) g.DrawImage(whole, -(client.X - window.Left), -(client.Y - window.Top));
                    }
                    picture.Save(args[1], ImageFormat.Png);
                }
                window.quitting = true; window.Shutdown();
                Environment.Exit(0);
            }
            // --offstage: the program runs as usual where nobody sees it or can press it, for a test that plays the phone.
            if (args.Contains("--offstage")) { offstage = true; window.StartPosition = FormStartPosition.Manual; window.Location = new Point(-30000, -30000); window.ShowInTaskbar = false; window.tray.Visible = false; }
            if (args.Contains("--hidden")) { window.WindowState = FormWindowState.Minimized; window.ShowInTaskbar = false; window.Shown += (s, e) => { window.Hide(); window.ShowInTaskbar = true; }; }
            if (String.IsNullOrWhiteSpace(elsewhere)) window.AutostartFromHere();      // a test's copy leaves the real setting alone
            Application.Run(window);
        }
        return 0;
    }
}
}
