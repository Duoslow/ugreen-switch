// Tray app for the NodeMCU-controlled UGREEN DP switch.
// Build: build.bat (uses the .NET Framework csc that ships with Windows)
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    [STAThread]
    static void Main()
    {
        bool created;
        using (var mutex = new Mutex(true, "UgreenSwitchTray", out created))
        {
            if (!created) return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApp());
        }
    }
}

class TrayApp : ApplicationContext
{
    const string RegKey = @"Software\UgreenSwitch";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "UgreenSwitch";
    const string DefaultHost = "192.168.4.107";

    readonly HttpClient http = new HttpClient();
    readonly NotifyIcon tray = new NotifyIcon();
    readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
    readonly Icon iconOnline, iconOffline, iconBusy;
    readonly PanelForm panel;
    readonly HotkeyWindow hotkey;

    ToolStripMenuItem miStatus, miIn1, miIn2, miStartup;
    string host;
    bool online, busy, polling;
    int active; // 0 = unknown (no sense wire), 1 or 2

    public TrayApp()
    {
        http.Timeout = TimeSpan.FromSeconds(3);
        host = LoadHost();

        iconOnline = MakeIcon(Color.FromArgb(46, 160, 67));
        iconOffline = MakeIcon(Color.FromArgb(120, 120, 120));
        iconBusy = MakeIcon(Color.FromArgb(214, 140, 20));

        panel = new PanelForm(this);
        BuildMenu();

        tray.Icon = iconOffline;
        tray.Text = "UGREEN switch";
        tray.Visible = true;
        tray.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left) panel.Toggle();
            else if (e.Button == MouseButtons.Middle) Send("press");
        };

        hotkey = new HotkeyWindow(() => Send("press"));

        poll.Interval = 5000;
        poll.Tick += (s, e) => Refresh();
        poll.Start();
        Refresh();
    }

    public string Host { get { return host; } }
    public bool Online { get { return online; } }
    public bool Busy { get { return busy; } }
    public int Active { get { return active; } }
    public bool HotkeyOk { get { return hotkey.Registered; } }

    void BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var miSwitch = new ToolStripMenuItem("Switch input", null, (s, e) => Send("press"));
        miSwitch.Font = new Font(miSwitch.Font, FontStyle.Bold);
        miSwitch.ShortcutKeyDisplayString = "Ctrl+Alt+S";
        miIn1 = new ToolStripMenuItem("Input 1", null, (s, e) => Send("1"));
        miIn2 = new ToolStripMenuItem("Input 2", null, (s, e) => Send("2"));
        miStatus = new ToolStripMenuItem("Offline") { Enabled = false };
        miStartup = new ToolStripMenuItem("Start with Windows", null, (s, e) => SetStartup(!GetStartup()));

        menu.Items.Add(miSwitch);
        menu.Items.Add(miIn1);
        menu.Items.Add(miIn2);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(miStatus);
        menu.Items.Add("Open panel", null, (s, e) => panel.ShowNearTray());
        menu.Items.Add(miStartup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => Quit());
        menu.Opening += (s, e) => { miStartup.Checked = GetStartup(); UpdateUi(); };
        tray.ContextMenuStrip = menu;
    }

    public async void Send(string cmd)
    {
        if (busy) return;
        busy = true;
        UpdateUi();
        try
        {
            Parse(await http.GetStringAsync("http://" + host + "/" + cmd));
        }
        catch
        {
            online = false;
            tray.ShowBalloonTip(3000, "UGREEN switch", "Not reachable at " + host, ToolTipIcon.Warning);
        }
        finally
        {
            busy = false;
            UpdateUi();
        }
    }

    public async void Refresh()
    {
        if (busy || polling) return;
        polling = true;
        try { Parse(await http.GetStringAsync("http://" + host + "/state")); }
        catch { online = false; }
        finally { polling = false; UpdateUi(); }
    }

    void Parse(string body)
    {
        online = true;
        var m = Regex.Match(body ?? "", @"active=(\d)");
        active = m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    public string StatusText()
    {
        if (busy) return "Switching...";
        if (!online) return "Offline (" + host + ")";
        return active > 0 ? "Connected - Input " + active : "Connected (" + host + ")";
    }

    void UpdateUi()
    {
        tray.Icon = busy ? iconBusy : online ? iconOnline : iconOffline;
        string tip = "UGREEN switch - " + StatusText();
        tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        miStatus.Text = StatusText();
        miIn1.Visible = miIn2.Visible = active > 0;
        miIn1.Checked = active == 1;
        miIn2.Checked = active == 2;
        panel.UpdateUi();
    }

    public void SetHost(string newHost)
    {
        newHost = newHost.Trim();
        if (newHost.StartsWith("http://")) newHost = newHost.Substring(7);
        newHost = newHost.TrimEnd('/');
        if (newHost.Length == 0) return;
        host = newHost;
        using (var k = Registry.CurrentUser.CreateSubKey(RegKey)) k.SetValue("Host", host);
        online = false;
        UpdateUi();
        Refresh();
    }

    static string LoadHost()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RegKey))
        {
            var v = k == null ? null : k.GetValue("Host") as string;
            return string.IsNullOrEmpty(v) ? DefaultHost : v;
        }
    }

    public bool GetStartup()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
            return k != null && k.GetValue(AppName) != null;
    }

    public void SetStartup(bool on)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (on) k.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
            else k.DeleteValue(AppName, false);
        }
        panel.UpdateUi();
    }

    public void Quit()
    {
        poll.Stop();
        hotkey.Dispose();
        tray.Visible = false;
        tray.Dispose();
        ExitThread();
    }

    static Icon MakeIcon(Color bg)
    {
        using (var bmp = new Bitmap(32, 32))
        {
            using (var g = Graphics.FromImage(bmp))
            using (var pen = new Pen(Color.White, 2.5f))
            using (var brush = new SolidBrush(bg))
            using (var path = RoundRect(new Rectangle(0, 0, 31, 31), 7))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(brush, path);
                g.DrawRectangle(pen, 6, 7, 20, 13);          // screen
                g.DrawLine(pen, 16, 21, 16, 25);             // neck
                g.DrawLine(pen, 10, 25.5f, 22, 25.5f);       // base
            }
            IntPtr h = bmp.GetHicon();
            var icon = (Icon)Icon.FromHandle(h).Clone();
            DestroyIcon(h);
            return icon;
        }
    }

    public static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
}

class PanelForm : Form
{
    readonly TrayApp app;
    readonly Label lblStatus, lblHotkey;
    readonly Panel dot;
    readonly Button btnSwitch, btn1, btn2, btnSave;
    readonly TextBox txtHost;
    readonly CheckBox chkStartup;
    readonly Font boldFont = new Font("Segoe UI", 9f, FontStyle.Bold);
    DateTime hiddenAt = DateTime.MinValue;
    bool updating;

    public PanelForm(TrayApp app)
    {
        this.app = app;
        Text = "UGREEN switch";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(300, 260);

        var title = new Label { Text = "UGREEN DP Switch", Font = new Font("Segoe UI", 11f, FontStyle.Bold), AutoSize = true, Location = new Point(16, 14) };
        var close = new Label { Text = "\u2715", AutoSize = true, ForeColor = Color.Gray, Cursor = Cursors.Hand, Location = new Point(276, 14) };
        close.Click += (s, e) => HidePanel();

        dot = new Panel { Size = new Size(10, 10), Location = new Point(18, 47) };
        dot.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(app.Busy ? Color.FromArgb(214, 140, 20) : app.Online ? Color.FromArgb(46, 160, 67) : Color.Gray))
                e.Graphics.FillEllipse(b, 0, 0, 9, 9);
        };
        lblStatus = new Label { AutoSize = true, ForeColor = Color.DimGray, Location = new Point(32, 43) };

        btnSwitch = new Button { Text = "Switch input", Font = new Font("Segoe UI", 11f, FontStyle.Bold), Size = new Size(268, 46), Location = new Point(16, 72), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 103, 192), ForeColor = Color.White, Cursor = Cursors.Hand };
        btnSwitch.FlatAppearance.BorderSize = 0;
        btnSwitch.Click += (s, e) => app.Send("press");

        btn1 = new Button { Text = "Input 1", Size = new Size(130, 32), FlatStyle = FlatStyle.System };
        btn2 = new Button { Text = "Input 2", Size = new Size(130, 32), FlatStyle = FlatStyle.System };
        btn1.Click += (s, e) => app.Send("1");
        btn2.Click += (s, e) => app.Send("2");

        var lblHost = new Label { Text = "NodeMCU address", AutoSize = true, ForeColor = Color.DimGray, Name = "lblHost" };
        txtHost = new TextBox { Width = 190 };
        btnSave = new Button { Text = "Save", Size = new Size(70, 25), FlatStyle = FlatStyle.System };
        btnSave.Click += (s, e) => app.SetHost(txtHost.Text);
        txtHost.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { app.SetHost(txtHost.Text); e.SuppressKeyPress = true; } };

        chkStartup = new CheckBox { Text = "Start with Windows", AutoSize = true };
        chkStartup.CheckedChanged += (s, e) => { if (!updating) app.SetStartup(chkStartup.Checked); };
        lblHotkey = new Label { AutoSize = true, ForeColor = Color.Gray };

        Controls.AddRange(new Control[] { title, close, dot, lblStatus, btnSwitch, btn1, btn2, lblHost, txtHost, btnSave, chkStartup, lblHotkey });

        Deactivate += (s, e) => HidePanel();
        FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HidePanel(); } };
        Paint += (s, e) => { using (var p = new Pen(Color.FromArgb(200, 200, 200))) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); };
    }

    void LayoutControls()
    {
        int y = 128;
        bool sense = app.Active > 0;
        btn1.Visible = btn2.Visible = sense;
        if (sense)
        {
            btn1.Location = new Point(16, y);
            btn2.Location = new Point(154, y);
            y += 44;
        }
        Controls["lblHost"].Location = new Point(14, y); y += 20;
        txtHost.Location = new Point(16, y);
        btnSave.Location = new Point(214, y - 1); y += 34;
        chkStartup.Location = new Point(16, y); y += 26;
        lblHotkey.Location = new Point(14, y); y += 30;
        ClientSize = new Size(300, y);
    }

    public void UpdateUi()
    {
        updating = true;
        lblStatus.Text = app.StatusText();
        dot.Invalidate();
        btnSwitch.Enabled = !app.Busy;
        btn1.Enabled = btn2.Enabled = !app.Busy;
        btn1.Font = app.Active == 1 ? boldFont : Font;
        btn2.Font = app.Active == 2 ? boldFont : Font;
        if (!txtHost.Focused) txtHost.Text = app.Host;
        chkStartup.Checked = app.GetStartup();
        lblHotkey.Text = app.HotkeyOk ? "Hotkey: Ctrl+Alt+S  \u2022  Middle-click tray icon to switch" : "Hotkey Ctrl+Alt+S is used by another app";
        updating = false;
        LayoutControls();
    }

    public void Toggle()
    {
        if (Visible) HidePanel();
        else if ((DateTime.Now - hiddenAt).TotalMilliseconds > 300) ShowNearTray(); // click that just closed it via Deactivate
    }

    public void ShowNearTray()
    {
        UpdateUi();
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(wa.Right - Width - 12, wa.Bottom - Height - 12);
        Show();
        Activate();
        app.Refresh();
    }

    void HidePanel()
    {
        if (!Visible) return;
        hiddenAt = DateTime.Now;
        Hide();
    }
}

class HotkeyWindow : NativeWindow, IDisposable
{
    const int WM_HOTKEY = 0x0312, MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000, ID = 1;
    readonly Action onHotkey;
    public bool Registered { get; private set; }

    public HotkeyWindow(Action onHotkey)
    {
        this.onHotkey = onHotkey;
        CreateHandle(new CreateParams());
        Registered = RegisterHotKey(Handle, ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (int)Keys.S);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == ID) onHotkey();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Registered) UnregisterHotKey(Handle, ID);
        DestroyHandle();
    }

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, int mods, int vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
