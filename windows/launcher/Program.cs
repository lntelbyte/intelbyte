using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [STAThread]
    private static int Main(string[] args)
    {
        var root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var node = Path.Combine(root, "node", "node.exe");
        var app = Path.Combine(root, "app");
        var bin = Path.Combine(app, "bin", "intelbyte.js");

        if (!File.Exists(node) || !File.Exists(bin))
        {
            MessageBox.Show(
                "IntelByte install is broken.\n\nRe-run IntelByte-Setup.exe.",
                "IntelByte", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        var minimized = false;
        var passthrough = new List<string>();
        foreach (var a in args)
        {
            if (a == "--minimized" || a == "/min" || a == "-m") minimized = true;
            else passthrough.Add(a);
        }
        if (passthrough.Count > 0) return RunCliConsole(node, bin, app, passthrough.ToArray());

        bool createdNew;
        var mutex = new Mutex(true, "IntelByte_SingleInstance_v1", out createdNew);
        if (!createdNew)
        {
            try
            {
                NativeMethods.PostMessage(new IntPtr(NativeMethods.HWND_BROADCAST),
                    AppWindow.WmShowIntelByte, IntPtr.Zero, IntPtr.Zero);
            }
            catch {}
            return 0;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        DwmChrome.ForceAppDark();
        try
        {
            var win = new AppWindow(node, bin, app, minimized);
            NativeMethods.ForceHandle(win);
            Application.Run(win);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "IntelByte could not start:\n\n" + ex.Message,
                "IntelByte",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
        GC.KeepAlive(mutex);
        return 0;
    }

    internal static string RunCli(string node, string bin, string app, string[] args)
    {
        var full = new string[args.Length + 1];
        full[0] = bin;
        Array.Copy(args, 0, full, 1, args.Length);

        var psi = new ProcessStartInfo
        {
            FileName = node,
            Arguments = QuoteArgs(full),
            WorkingDirectory = app,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.EnvironmentVariables["INTELBYTE_APP_ROOT"] = app;
        psi.EnvironmentVariables["INTELBYTE_NODE"] = node;
        psi.EnvironmentVariables["NO_COLOR"] = "1";

        try
        {
            using (var proc = Process.Start(psi))
            {
                if (proc == null) return "";
                var outText = proc.StandardOutput.ReadToEnd();
                var errText = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                return outText + errText;
            }
        }
        catch (Exception ex)
        {
            return "ERROR: " + ex.Message;
        }
    }

    private static int RunCliConsole(string node, string bin, string app, string[] args)
    {
        if (!AttachConsole(AttachParentProcess)) AllocConsole();

        var full = new string[args.Length + 1];
        full[0] = bin;
        Array.Copy(args, 0, full, 1, args.Length);

        var psi = new ProcessStartInfo
        {
            FileName = node,
            Arguments = QuoteArgs(full),
            WorkingDirectory = app,
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        psi.EnvironmentVariables["INTELBYTE_APP_ROOT"] = app;
        psi.EnvironmentVariables["INTELBYTE_NODE"] = node;

        using (var proc = Process.Start(psi))
        {
            if (proc == null) return 1;
            proc.WaitForExit();
            return proc.ExitCode;
        }
    }

    private static string QuoteArgs(string[] parts)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(Quote(parts[i]));
        }
        return sb.ToString();
    }

    internal static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (value.IndexOf(' ') < 0 && value.IndexOf('"') < 0) return value;
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    internal static Icon LoadIcon(int size)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("intelbyte.ico"))
            {
                if (s == null) return null;
                return new Icon(s, new Size(size, size));
            }
        }
        catch { return null; }
    }

    internal static Icon LoadIconFull()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("intelbyte.ico"))
            {
                if (s == null) return null;
                return new Icon(s);
            }
        }
        catch { return null; }
    }

    internal static Image LoadLogo()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("intelbyte-logo.png"))
            {
                if (s == null) return null;
                using (var tmp = Image.FromStream(s))
                {
                    return new Bitmap(tmp);
                }
            }
        }
        catch { return null; }
    }

    internal static Font LoadDetectiveFont(float size, FontStyle style = FontStyle.Regular)
    {
        try
        {
            EnsureDetective();
            if (_detective == null || _detective.Families.Length == 0)
                return new Font("Courier New", size, style);
            return new Font(_detective.Families[0], size, style, GraphicsUnit.Point);
        }
        catch
        {
            return new Font("Courier New", size, style);
        }
    }

    private static PrivateFontCollection _detective;
    private static GCHandle _detectivePin;

    private static void EnsureDetective()
    {
        if (_detective != null) return;
        var asm = Assembly.GetExecutingAssembly();
        using (var s = asm.GetManifestResourceStream("SpecialElite-Regular.ttf"))
        {
            if (s == null) return;
            var buf = new byte[s.Length];
            s.Read(buf, 0, buf.Length);
            _detectivePin = GCHandle.Alloc(buf, GCHandleType.Pinned);
            _detective = new PrivateFontCollection();
            _detective.AddMemoryFont(_detectivePin.AddrOfPinnedObject(), buf.Length);
        }
    }
}
