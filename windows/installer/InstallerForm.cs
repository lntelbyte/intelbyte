using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace IntelByteSetup
{
    internal sealed class InstallerForm : Form
    {
        private readonly TextBox _pathBox;
        private readonly CheckBox _desktopShortcut;
        private readonly CheckBox _launchAfter;
        private readonly ProgressBar _progress;
        private readonly Label _status;
        private readonly Button _installBtn;
        private readonly Button _cancelBtn;
        private readonly bool _updateMode;

        public InstallerForm() : this(new string[0]) {}

        public InstallerForm(string[] args)
        {
            _updateMode = HasUpdateFlag(args);
            Text = "IntelByte Setup";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(520, 300);
            BackColor = Color.FromArgb(18, 18, 18);
            ForeColor = Color.White;

            var title = new Label
            {
                Text = "IntelByte",
                Font = new Font("Segoe UI", 22f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 20),
                ForeColor = Color.White,
            };
            Controls.Add(title);

            var subtitle = new Label
            {
                Text = "Hide your email and phone on screen while you stream.",
                Font = new Font("Segoe UI", 10f),
                AutoSize = true,
                Location = new Point(26, 58),
                ForeColor = Color.FromArgb(180, 180, 180),
            };
            Controls.Add(subtitle);

            var pathLabel = new Label
            {
                Text = "Install location:",
                AutoSize = true,
                Location = new Point(24, 98),
                ForeColor = Color.FromArgb(200, 200, 200),
            };
            Controls.Add(pathLabel);

            _pathBox = new TextBox
            {
                Location = new Point(24, 120),
                Width = 390,
                Text = DefaultInstallDir(),
                BackColor = Color.FromArgb(32, 32, 32),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
            };
            Controls.Add(_pathBox);

            var browse = new Button
            {
                Text = "Browse…",
                Location = new Point(420, 118),
                Width = 76,
                Height = 24,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
            };
            browse.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            browse.Click += delegate { BrowseForFolder(); };
            Controls.Add(browse);

            _desktopShortcut = new CheckBox
            {
                Text = "Create desktop shortcut",
                Location = new Point(24, 156),
                AutoSize = true,
                Checked = true,
                ForeColor = Color.FromArgb(210, 210, 210),
            };
            Controls.Add(_desktopShortcut);

            _launchAfter = new CheckBox
            {
                Text = "Launch IntelByte after install",
                Location = new Point(24, 180),
                AutoSize = true,
                Checked = true,
                ForeColor = Color.FromArgb(210, 210, 210),
            };
            Controls.Add(_launchAfter);

            _progress = new ProgressBar
            {
                Location = new Point(24, 214),
                Width = 472,
                Style = ProgressBarStyle.Continuous,
            };
            Controls.Add(_progress);

            _status = new Label
            {
                Text = "Ready to install.",
                Location = new Point(24, 242),
                AutoSize = true,
                ForeColor = Color.FromArgb(160, 160, 160),
            };
            Controls.Add(_status);

            _cancelBtn = new Button
            {
                Text = "Cancel",
                Location = new Point(336, 258),
                Width = 76,
                Height = 28,
                DialogResult = DialogResult.Cancel,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
            };
            _cancelBtn.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            Controls.Add(_cancelBtn);
            CancelButton = _cancelBtn;

            _installBtn = new Button
            {
                Text = "Install",
                Location = new Point(420, 258),
                Width = 76,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.Black,
            };
            _installBtn.FlatAppearance.BorderSize = 0;
            _installBtn.Click += delegate { BeginInstall(); };
            Controls.Add(_installBtn);
            AcceptButton = _installBtn;

            _pathBox.TextChanged += delegate { UpdateInstallLabel(); };
            UpdateInstallLabel();

            if (_updateMode)
            {
                Text = "IntelByte Update";
                _pathBox.Text = UpdateTarget(args) ?? DefaultInstallDir();
                _pathBox.Enabled = false;
                browse.Visible = false;
                _desktopShortcut.Checked = true;
                _launchAfter.Checked = true;
                _desktopShortcut.Visible = false;
                _launchAfter.Visible = false;
                _installBtn.Visible = false;
                _cancelBtn.Visible = false;
                _status.Text = "Updating IntelByte…";
                Shown += delegate { BeginInstall(); };
            }
        }

        private static bool HasUpdateFlag(string[] args)
        {
            if (args == null) return false;
            foreach (var arg in args)
                if (string.Equals(arg, "/update", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arg, "--update", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string UpdateTarget(string[] args)
        {
            if (args == null) return null;
            for (var i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], "/update", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(args[i], "--update", StringComparison.OrdinalIgnoreCase))
                {
                    var value = (args[i + 1] ?? "").Trim();
                    if (value.Length > 0 && !value.StartsWith("/", StringComparison.Ordinal)) return value;
                }
            }
            return null;
        }

        private void UpdateInstallLabel()
        {
            var exists = false;
            try { exists = Directory.Exists(_pathBox.Text.Trim()); }
            catch { exists = false; }
            _installBtn.Text = exists ? "Reinstall" : "Install";
        }

        private static string DefaultInstallDir()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "Programs", "IntelByte");
        }

        private void BrowseForFolder()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Choose install folder";
                dlg.SelectedPath = _pathBox.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _pathBox.Text = dlg.SelectedPath;
            }
        }

        private void BeginInstall()
        {
            var target = _pathBox.Text.Trim();
            if (string.IsNullOrEmpty(target))
            {
                MessageBox.Show(this, "Choose an install folder.", "IntelByte Setup",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _installBtn.Enabled = false;
            _cancelBtn.Enabled = false;
            _pathBox.Enabled = false;
            _desktopShortcut.Enabled = false;
            _launchAfter.Enabled = false;

            var desktop = _desktopShortcut.Checked;
            var launch = _launchAfter.Checked;

            var worker = new Thread(() =>
            {
                try
                {
                    RunInstall(target, desktop, launch);
                    BeginInvoke(new Action(InstallSucceeded));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(() => InstallFailed(ex.Message)));
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void SetStatus(string text, int percent)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetStatus(text, percent)));
                return;
            }
            _status.Text = text;
            _progress.Value = Math.Max(0, Math.Min(100, percent));
        }

        private void RunInstall(string targetDir, bool desktopShortcut, bool launchAfter)
        {

            SetStatus("Closing any running IntelByte…", 6);
            StopRunningInstall(targetDir);

            SetStatus("Preparing…", 12);
            if (Directory.Exists(targetDir))
            {
                Exception last = null;
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    try { Directory.Delete(targetDir, true); last = null; break; }
                    catch (Exception ex)
                    {
                        last = ex;
                        StopRunningInstall(targetDir);
                        Thread.Sleep(600);
                    }
                }
                if (last != null && Directory.Exists(targetDir))
                    throw new InvalidOperationException(
                        "Could not replace the existing install folder.\n\n" +
                        "Right-click the IntelByte icon near the clock → Quit, then run setup again.");
            }
            Directory.CreateDirectory(targetDir);

            SetStatus("Extracting files…", 20);
            var payloadZip = Path.Combine(Path.GetTempPath(), "intelbyte-payload-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                File.WriteAllBytes(payloadZip, LoadPayloadBytes());
                ExtractZip(payloadZip, targetDir);
            }
            finally
            {
                try { if (File.Exists(payloadZip)) File.Delete(payloadZip); }
                catch {}
            }

            var appExe = Path.Combine(targetDir, "IntelByte.exe");
            var nodeExe = Path.Combine(targetDir, "node", "node.exe");
            var binJs = Path.Combine(targetDir, "app", "bin", "intelbyte.js");
            if (!File.Exists(appExe) || !File.Exists(nodeExe) || !File.Exists(binJs))
                throw new InvalidOperationException("Install package is incomplete. Re-download IntelByte-Setup.exe.");

            SetStatus("Creating shortcuts…", 88);
            if (desktopShortcut)
                CreateShortcut(
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                        "IntelByte.lnk"),
                    appExe,
                    targetDir,
                    "IntelByte — screen privacy shield");

            CreateShortcut(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    "IntelByte.lnk"),
                appExe,
                targetDir,
                "IntelByte — screen privacy shield");

            SetStatus("Finishing…", 96);
            if (launchAfter)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = appExe,
                    WorkingDirectory = targetDir,
                    UseShellExecute = true,
                });
            }
            SetStatus("Install complete.", 100);
        }

        private static void StopRunningInstall(string targetDir)
        {
            string norm;
            try { norm = Path.GetFullPath(targetDir).TrimEnd('\\') + "\\"; }
            catch { return; }

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    string path = null;
                    try { if (p.MainModule != null) path = p.MainModule.FileName; }
                    catch { path = null; }
                    if (path != null && path.StartsWith(norm, StringComparison.OrdinalIgnoreCase))
                    {
                        try { p.Kill(); p.WaitForExit(3000); }
                        catch {}
                    }
                }
                catch {}
                finally { try { p.Dispose(); } catch {} }
            }
        }

        private static byte[] LoadPayloadBytes()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("IntelByteSetup.Payload.zip"))
            {
                if (stream == null)
                    throw new InvalidOperationException("Embedded install package not found.");
                var bytes = new byte[stream.Length];
                var read = 0;
                while (read < bytes.Length)
                    read += stream.Read(bytes, read, bytes.Length - read);
                return bytes;
            }
        }

        private static void ExtractZip(string zipPath, string destDir)
        {
            var root = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    var rel = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                    if (rel.Length == 0) continue;
                    while (rel.StartsWith("." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        rel = rel.Substring(2);
                    if (rel.IndexOf("..", StringComparison.Ordinal) >= 0) continue;

                    var dest = Path.GetFullPath(Path.Combine(destDir, rel));
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;

                    if (string.IsNullOrEmpty(entry.Name)
                        || rel.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(dest);
                        continue;
                    }

                    var parent = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    entry.ExtractToFile(dest, true);
                }
            }
        }

        private static void CreateShortcut(string shortcutPath, string targetExe, string workingDir, string description)
        {
            var dir = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(shortcutPath)) File.Delete(shortcutPath);

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new InvalidOperationException("Could not create shortcuts.");
            var shell = Activator.CreateInstance(shellType);
            var shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { shortcutPath });
            shortcut.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetExe });
            shortcut.GetType().InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });
            shortcut.GetType().InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
            shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }

        private void InstallSucceeded()
        {
            if (_updateMode)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            MessageBox.Show(this,
                "IntelByte is installed.\n\nThe IntelByte window will open — protection turns on automatically. Use the desktop shortcut to open it again anytime.",
                "IntelByte Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void InstallFailed(string message)
        {
            _installBtn.Enabled = true;
            _cancelBtn.Enabled = true;
            _pathBox.Enabled = true;
            _desktopShortcut.Enabled = true;
            _launchAfter.Enabled = true;
            _progress.Value = 0;
            _status.Text = "Install failed.";
            MessageBox.Show(this, message, "IntelByte Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
