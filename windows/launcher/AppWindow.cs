using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal sealed class AppWindow : IbForm
{
    private readonly string _node;
    private readonly string _bin;
    private readonly string _app;
    private readonly bool _startMinimized;

    private readonly GlassView _ui;

    private NotifyIcon _tray;
    private System.Windows.Forms.Timer _refreshTimer;
    private bool _running;
    private bool _busy;
    private bool _reallyQuit;
    private bool _allowShow;
    private bool _bootDone;
    private bool _balloonShown;
    private volatile bool _refreshing;

    public static readonly int WmShowIntelByte =
        NativeMethods.RegisterWindowMessage("IntelByte_Show_Message_v1");

    public AppWindow(string node, string bin, string app, bool startMinimized)
    {
        _node = node;
        _bin = bin;
        _app = app;
        _startMinimized = startMinimized;
        _allowShow = !startMinimized;

        Text = "IntelByte";
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = true;
        ClientSize = new Size(880, 540);
        Font = new Font("Segoe UI", 9.5f);
        var full = Program.LoadIconFull();
        if (full != null) Icon = full;

        _ui = new GlassView { Dock = DockStyle.Fill };
        _ui.CloseClick += delegate { Close(); };
        _ui.MinimizeClick += delegate { WindowState = FormWindowState.Minimized; };
        _ui.ToggleClick += delegate { ToggleShield(); };
        _ui.AddClick += delegate { QuickAdd(); };
        _ui.KindClick += delegate(string kind) { AddItem(kind); };
        _ui.RemoveClick += delegate { RemoveSelected(); };
        _ui.StartupClick += delegate
        {
            _ui.Startup = !_ui.Startup;
            OnStartupToggled();
            _ui.Invalidate();
        };
        _ui.Field.KeyDown += AddFieldKeyDown;
        Controls.Add(_ui);

        SetupTray();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _refreshTimer.Tick += delegate { if (!_busy) RefreshAsync(false); };
        if (!startMinimized) _refreshTimer.Start();

        if (Environment.GetEnvironmentVariable("INTELBYTE_GUI_PREVIEW") != "1")
            StreamCapture.HideFromCapture(this);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_bootDone) return;
        _bootDone = true;
        _ui.Startup = StartupEnabled();
        AutoStart();
        if (_startMinimized) HideToTray();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!_allowShow && _startMinimized) { base.SetVisibleCore(false); return; }
        base.SetVisibleCore(value);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmShowIntelByte)
        {
            try { ShowFromTray(); } catch {}
            return;
        }
        base.WndProc(ref m);
    }

    private void SetupTray()
    {
        _tray = new NotifyIcon();
        var ico = Program.LoadIcon(16);
        _tray.Icon = ico != null ? ico : (Icon ?? SystemIcons.Application);
        _tray.Text = "IntelByte — screen privacy";
        _tray.Visible = true;
        _tray.DoubleClick += delegate { ShowFromTray(); };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open IntelByte", null, delegate { ShowFromTray(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Start / Stop protection", null, delegate { ToggleShield(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, delegate { _reallyQuit = true; Close(); });
        _tray.ContextMenuStrip = menu;
    }

    private void HideToTray()
    {
        _allowShow = false;
        if (_refreshTimer != null) _refreshTimer.Stop();
        Hide();
        if (!_balloonShown)
        {
            _balloonShown = true;
            try { _tray.ShowBalloonTip(2500, "IntelByte", "Still protecting in the background. Right-click the tray icon to quit.", ToolTipIcon.None); }
            catch {}
        }
    }

    private void ShowFromTray()
    {
        _allowShow = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
        if (_refreshTimer != null && !_refreshTimer.Enabled) _refreshTimer.Start();
        RefreshAsync(true);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyQuit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        if (_tray != null) _tray.Visible = false;
        base.OnFormClosing(e);
    }

    private void AutoStart()
    {
        if (Environment.GetEnvironmentVariable("INTELBYTE_GUI_PREVIEW") == "1")
        {
            RefreshAsync(true);
            _ui.SetLoading(false, null);
            SetBusy(false, "");
            return;
        }
        SetBusy(true, "");
        BootOn();
    }

    private void BootOn()
    {
        _ui.SetLoading(true, "Turning on");
        ThreadPool.QueueUserWorkItem(delegate
        {
            try { Program.RunCli(_node, _bin, _app, new[] { "start" }); }
            catch {}
            string status = null, list = null;
            try
            {
                status = Program.RunCli(_node, _bin, _app, new[] { "status" });
                list = Program.RunCli(_node, _bin, _app, new[] { "list", "--reveal" });
            }
            catch {}
            try
            {
                BeginInvoke((Action)delegate
                {
                    ApplyState(status, list);
                    _ui.SetLoading(false, null);
                    SetBusy(false, "");
                });
            }
            catch {}
        });
    }

    private void ToggleShield()
    {
        if (_busy || _ui.Loading) return;
        if (!_running)
        {
            SetBusy(true, "");
            BootOn();
            return;
        }
        SetBusy(true, "Stopping…");
        RunBg(new string[][] { new[] { "stop" } }, delegate
        {
            RefreshAsync(true);
            SetBusy(false, "");
        });
    }

    private void AddFieldKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.Handled = true;
        e.SuppressKeyPress = true;
        QuickAdd();
    }

    private void QuickAdd()
    {
        var text = (_ui.FieldText ?? "").Trim();
        if (text.Length == 0)
        {
            AddItem("email");
            return;
        }
        if (_busy) return;
        var kind = GuessKind(text);
        var cmd = kind == "email" ? "protect-mail" : kind == "phone" ? "protect-phone" : "protect-custom";
        SetBusy(true, "Adding…");
        RunBg(new string[][] { new[] { cmd, text } }, delegate
        {
            _ui.FieldText = "";
            RefreshAsync(true);
            SetBusy(false, "");
        });
    }

    private static string GuessKind(string v)
    {
        if (v.IndexOf('@') >= 0) return "email";
        var digits = 0;
        for (var i = 0; i < v.Length; i++)
            if (char.IsDigit(v[i])) digits++;
        if (digits >= 7) return "phone";
        return "custom";
    }

    private void AddItem(string kind)
    {
        if (_busy) return;
        string cmd;
        if (kind == "email") cmd = "protect-mail";
        else if (kind == "phone") cmd = "protect-phone";
        else cmd = "protect-custom";

        using (var dlg = new InputPrompt(kind))
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var real = (dlg.Value ?? "").Trim();
            if (real.Length == 0) return;
            var fake = (dlg.Fake ?? "").Trim();

            string[] command;
            if (fake.Length > 0)
            {
                if (kind == "custom") command = new[] { "protect-custom-custom", real, fake };
                else command = new[] { cmd, "custom", real, fake };
            }
            else
            {
                command = new[] { cmd, real };
            }

            SetBusy(true, "Adding…");
            RunBg(new string[][] { command }, delegate
            {
                RefreshAsync(true);
                SetBusy(false, "");
            });
        }
    }

    private void RemoveSelected()
    {
        if (_busy) return;
        var row = _ui.SelectedRow;
        if (row == null) return;
        string cmd = row.Type == "Email" ? "unprotect-mail" : row.Type == "Phone" ? "unprotect-phone" : "unprotect-custom";
        _ui.Forget(row.Real);
        SetBusy(true, "Removing…");
        RunBg(new string[][] { new[] { cmd, row.Real } }, delegate
        {
            RefreshAsync(true);
            SetBusy(false, "");
        });
    }

    private static string StartupLnkPath()
    {
        var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        return Path.Combine(startup, "IntelByte.lnk");
    }

    private static bool StartupEnabled()
    {
        return File.Exists(StartupLnkPath());
    }

    private void OnStartupToggled()
    {
        var want = _ui.Startup;
        if (want == StartupEnabled()) return;
        try
        {
            if (want)
            {
                var exe = Assembly.GetExecutingAssembly().Location;
                CreateShortcut(StartupLnkPath(), exe, "--minimized",
                    Path.GetDirectoryName(exe), "IntelByte — screen privacy shield");
                _ui.Foot = "Opens to tray when Windows starts.";
            }
            else
            {
                var lnk = StartupLnkPath();
                if (File.Exists(lnk)) File.Delete(lnk);
                _ui.Foot = "Won’t start with Windows.";
            }
            _ui.Invalidate();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not update auto-start:\n" + ex.Message, "IntelByte",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _ui.Startup = StartupEnabled();
            _ui.Invalidate();
        }
    }

    private static void CreateShortcut(string path, string target, string args, string workdir, string desc)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(path)) File.Delete(path);

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) throw new InvalidOperationException("Windows Script Host unavailable.");
        var shell = Activator.CreateInstance(shellType);
        var sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
        var t = sc.GetType();
        t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
        t.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc, new object[] { args });
        t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workdir });
        t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
        t.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { desc });
        t.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
    }

    private void RunBg(string[][] commands, Action done)
    {
        ThreadPool.QueueUserWorkItem(delegate
        {
            foreach (var c in commands)
            {
                try { Program.RunCli(_node, _bin, _app, c); }
                catch {}
            }
            try { BeginInvoke(done); } catch {}
        });
    }

    private void RefreshAsync(bool full)
    {
        if (_refreshing) return;
        _refreshing = true;
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                var status = Program.RunCli(_node, _bin, _app, new[] { "status" });
                var list = Program.RunCli(_node, _bin, _app, new[] { "list", "--reveal" });
                try { BeginInvoke((Action)delegate { ApplyState(status, list); }); }
                catch {}
            }
            finally { _refreshing = false; }
        });
    }

    private void ApplyState(string status, string list)
    {
        var running = status != null && status.IndexOf("running", StringComparison.OrdinalIgnoreCase) >= 0
                      && status.IndexOf("Not running", StringComparison.OrdinalIgnoreCase) < 0;
        _running = running;
        _ui.Big = running ? "ON" : "OFF";
        _ui.Sub = running ? "Masking email & phone on stream." : "Click ON to start protecting.";
        var rows = ParseList(list);
        _ui.SetRows(rows);
        _ui.Status = running ? "On" : "Off";
        _ui.Count = rows.Count.ToString();
        if (!_busy) _ui.Foot = "";
        _ui.Invalidate();
    }

    private static List<RowData> ParseList(string list)
    {
        var rows = new List<RowData>();
        if (string.IsNullOrEmpty(list)) return rows;
        var type = "";
        var lines = list.Replace("\r", "").Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var t = line.Trim();
            if (t.StartsWith("Protected email")) { type = "Email"; continue; }
            if (t.StartsWith("Protected phone")) { type = "Phone"; continue; }
            if (t.StartsWith("Protected custom")) { type = "Custom"; continue; }
            var arrow = line.IndexOf("→");
            if (arrow < 0) arrow = line.IndexOf("->");
            if (arrow >= 0 && type.Length > 0)
            {
                var left = line.Substring(0, arrow).Trim();
                var right = line.Substring(arrow + 1).Trim();
                if (right.StartsWith(">")) right = right.Substring(1).Trim();
                if (left.Length == 0) continue;
                rows.Add(new RowData { Type = type, Real = left, Fake = right });
            }
        }
        return rows;
    }

    private void SetBusy(bool busy, string msg)
    {
        _busy = busy;
        _ui.Busy = busy;
        if (msg.Length > 0)
        {
            _ui.Foot = msg;
            _ui.Sub = msg;
        }
        else
        {
            _ui.Foot = "";
            _ui.Sub = _running ? "Masking email & phone on stream." : "Click ON to start protecting.";
        }
        _ui.Invalidate();
    }
}
