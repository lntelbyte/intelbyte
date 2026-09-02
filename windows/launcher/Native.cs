using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class NativeMethods
{
    public const int HWND_BROADCAST = 0xffff;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

    public const int WindowRadius = 16;

    public static void ClipRoundWindow(Form form)
    {
        if (form == null || form.IsDisposed || !form.IsHandleCreated) return;
        var w = form.Width;
        var h = form.Height;
        if (w < 8 || h < 8) return;
        var d = WindowRadius * 2;
        var rgn = CreateRoundRectRgn(0, 0, w + 1, h + 1, d, d);
        if (rgn == IntPtr.Zero) return;
        SetWindowRgn(form.Handle, rgn, true);
    }

    private const int WmNclButtonDown = 0xA1;
    private const int HtCaption = 2;

    public static void ForceHandle(Form form)
    {
        if (form == null) return;
        try { if (form.Handle == IntPtr.Zero) { } } catch {}
    }

    public static void DragMove(Form form)
    {
        if (form == null || form.IsDisposed || !form.IsHandleCreated) return;
        try
        {
            ReleaseCapture();
            SendMessage(form.Handle, WmNclButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
        }
        catch {}
    }
}

internal static class DwmChrome
{
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const int DwmwcpDoNotRound = 1;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int preferredAppMode);

    public static void ForceAppDark()
    {
        try { SetPreferredAppMode(2); } catch {}
    }

    public static void Apply(Form form)
    {
        if (form == null) return;
        ApplyHandle(form);
        form.HandleCreated += delegate { ApplyHandle(form); };
        form.Shown += delegate { ApplyHandle(form); };
    }

    private static void ApplyHandle(Form form)
    {
        if (form == null || form.IsDisposed || !form.IsHandleCreated) return;
        try
        {
            var hwnd = form.Handle;
            var dark = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, 4);
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref dark, 4);
            var round = DwmwcpDoNotRound;
            var caption = ColorTranslator.ToWin32(AppTheme.Bg);
            var border = ColorTranslator.ToWin32(AppTheme.Bg);
            var text = ColorTranslator.ToWin32(Color.White);
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, 4);
            DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, 4);
            DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref border, 4);
            DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref text, 4);
        }
        catch {}
    }
}

internal class IbForm : Form
{
    public IbForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        BackColor = AppTheme.Bg;
        ForeColor = Color.White;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        DwmChrome.Apply(this);
        HandleCreated += delegate { ApplyGlassAndCorners(); };
        SizeChanged += delegate { ClipCorners(); };
        Shown += delegate
        {
            ApplyGlassAndCorners();
            ReclipLater(80);
            ReclipLater(250);
        };
        Opacity = 0.94;
        FormClosing += OnIbClosing;
    }

    private readonly List<Timer> _reclip = new List<Timer>();

    private void OnIbClosing(object sender, FormClosingEventArgs e)
    {
        foreach (var t in _reclip.ToArray())
        {
            try { t.Stop(); t.Dispose(); } catch {}
        }
        _reclip.Clear();
    }

    private void ReclipLater(int ms)
    {
        var later = new Timer { Interval = ms };
        _reclip.Add(later);
        later.Tick += delegate
        {
            later.Stop();
            later.Dispose();
            _reclip.Remove(later);
            ApplyGlassAndCorners();
        };
        later.Start();
    }

    private bool _applying;
    private void ApplyGlassAndCorners()
    {
        if (_applying || IsDisposed || Disposing || !IsHandleCreated || !Visible) return;
        _applying = true;
        try
        {
            DwmGlass.Apply(this);
            NativeMethods.ClipRoundWindow(this);
        }
        catch {}
        finally { _applying = false; }
    }

    private void ClipCorners()
    {
        if (_applying || IsDisposed || Disposing || !IsHandleCreated || !Visible) return;
        _applying = true;
        try { NativeMethods.ClipRoundWindow(this); }
        catch {}
        finally { _applying = false; }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }
}

internal static class DwmGlass
{
    private const int WcaAccentPolicy = 19;
    private const int AccentBlurBehind = 3;
    private const int AccentAcrylic = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int State;
        public int Flags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinCompAttrData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WinCompAttrData data);

    public static void Apply(Form form)
    {
        if (form == null || form.IsDisposed || !form.IsHandleCreated) return;
        var color = unchecked((int)0xB8141418);
        if (!SetAccent(form.Handle, AccentAcrylic, color))
            SetAccent(form.Handle, AccentBlurBehind, color);
    }

    private static bool SetAccent(IntPtr hwnd, int state, int color)
    {
        var accent = new AccentPolicy { State = state, Flags = 2, GradientColor = color };
        var size = Marshal.SizeOf(accent);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WinCompAttrData
            {
                Attribute = WcaAccentPolicy,
                Data = ptr,
                SizeOfData = size,
            };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}

internal static class StreamCapture
{
    private const uint WdaNone = 0x00000000;
    private const uint WdaExcludeFromCapture = 0x00000011;

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    private static readonly List<Form> _forms = new List<Form>();
    public static bool Hidden;

    public static void Attach(Form form)
    {
        if (form == null) return;
        if (!_forms.Contains(form)) _forms.Add(form);
        form.HandleCreated += StreamCaptureOnHandle;
        form.Shown += StreamCaptureOnHandle;
        form.FormClosed += delegate
        {
            _forms.Remove(form);
        };
        Apply(form);
    }

    public static void SetHidden(bool hidden)
    {
        Hidden = hidden;
        foreach (var f in _forms.ToArray()) Apply(f);
    }

    private static void StreamCaptureOnHandle(object sender, EventArgs e)
    {
        Apply(sender as Form);
    }

    private static void Apply(Form form)
    {
        if (form == null || form.IsDisposed || !form.IsHandleCreated) return;
        try { SetWindowDisplayAffinity(form.Handle, Hidden ? WdaExcludeFromCapture : WdaNone); }
        catch {}
    }
}
