using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class AppTheme
{
    public static readonly Color Bg = Color.FromArgb(8, 14, 16);
    public static readonly Color HeroA = Color.FromArgb(16, 28, 32);
    public static readonly Color HeroB = Color.FromArgb(6, 10, 12);
    public static readonly Color Glass = Color.FromArgb(18, 28, 32);
    public static readonly Color GlassHi = Color.FromArgb(32, 46, 50);
    public static readonly Color Card = Color.FromArgb(20, 30, 34);
    public static readonly Color CardHi = Color.FromArgb(30, 42, 46);
    public static readonly Color Line = Color.FromArgb(70, 255, 255, 255);
    public static readonly Color Btn = Color.FromArgb(36, 44, 48);
    public static readonly Color BtnHover = Color.FromArgb(52, 62, 66);
    public static readonly Color BtnBorder = Color.FromArgb(68, 80, 84);
    public static readonly Color AccentGreen = Color.FromArgb(198, 216, 188);
    public static readonly Color AccentRed = Color.FromArgb(240, 88, 96);
    public static readonly Color Dim = Color.FromArgb(168, 178, 180);
    public static readonly Color Muted = Color.FromArgb(110, 122, 124);
    public const int Radius = 12;
    public const int BtnRadius = 10;
    public const int Caption = 36;
}

internal static class PaintUtil
{
    public static void FillOpaque(Graphics g, Rectangle r, Color color)
    {
        var prev = g.CompositingMode;
        g.CompositingMode = CompositingMode.SourceCopy;
        using (var b = new SolidBrush(Color.FromArgb(255, color)))
            g.FillRectangle(b, r);
        g.CompositingMode = prev;
    }

    public static Color ParentSurface(System.Windows.Forms.Control c)
    {
        if (c == null || c.Parent == null) return AppTheme.Bg;
        return c.Parent.BackColor;
    }
}

internal static class GlassPaint
{
    public static Bitmap MakeFrost(int w, int h, Image logo)
    {
        if (w < 8 || h < 8) return null;
        var haze = BuildHaze(w, h);
        var frost = Blur(haze, w, h, 12);
        haze.Dispose();
        StampLogo(frost, logo);
        return frost;
    }

    public static void FillCard(Graphics g, Rectangle bounds, Bitmap frost)
    {
        if (bounds.Width < 8 || bounds.Height < 8) return;
        PaintUtil.FillOpaque(g, bounds, Color.FromArgb(16, 16, 20));
        var card = new Rectangle(bounds.X, bounds.Y, Math.Max(0, bounds.Width - 1), Math.Max(0, bounds.Height - 1));
        using (var path = UiShapes.RoundedRect(card, NativeMethods.WindowRadius))
        {
            g.SetClip(path);
            using (var wash = new SolidBrush(Color.FromArgb(70, 12, 12, 16)))
                g.FillRectangle(wash, card);
            if (frost != null)
            {
                var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.55f };
                var ia = new System.Drawing.Imaging.ImageAttributes();
                ia.SetColorMatrix(cm);
                g.DrawImage(frost, card, 0, 0, frost.Width, frost.Height, GraphicsUnit.Pixel, ia);
                ia.Dispose();
            }
            using (var tint = new SolidBrush(Color.FromArgb(55, 18, 18, 22)))
                g.FillRectangle(tint, card);
            var sheenR = new Rectangle(card.X - 1, card.Y - 1, card.Width + 2, 92);
            using (var sheen = new LinearGradientBrush(
                sheenR,
                Color.FromArgb(50, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255),
                90f))
            {
                sheen.WrapMode = WrapMode.TileFlipXY;
                g.FillRectangle(sheen, card.X, card.Y, card.Width, 90);
            }
            g.ResetClip();
            using (var pen = new Pen(Color.FromArgb(64, 255, 255, 255), 1f))
                g.DrawPath(pen, path);
        }
    }

    public static void DrawMark(Graphics g, Rectangle box, Image src)
    {
        if (box.Width < 4 || box.Height < 4) return;
        using (var circle = new GraphicsPath())
        {
            circle.AddEllipse(box);
            g.SetClip(circle);
            using (var matte = new SolidBrush(Color.FromArgb(255, 8, 8, 10)))
                g.FillEllipse(matte, box);
            if (src != null)
                g.DrawImage(src, box);
            g.ResetClip();
            using (var pen = new Pen(Color.FromArgb(50, 255, 255, 255), 1f))
                g.DrawEllipse(pen, box);
        }
    }

    public static void DrawDot(Graphics g, Rectangle r, Color c, bool hot)
    {
        if (hot) r.Inflate(1, 1);
        using (var b = new SolidBrush(c))
            g.FillEllipse(b, r);
    }

    public static readonly Color WellFill = Color.FromArgb(255, 40, 40, 46);

    public static void DrawWell(Graphics g, Rectangle r, bool focus)
    {
        using (var path = UiShapes.RoundedRect(r, 12))
        using (var fill = new SolidBrush(WellFill))
            g.FillPath(fill, path);
        using (var path = UiShapes.RoundedRect(r, 12))
        using (var pen = new Pen(Color.FromArgb(focus ? 120 : 36, 255, 255, 255)))
            g.DrawPath(pen, path);
    }

    private static Bitmap BuildHaze(int w, int h)
    {
        var raw = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(raw))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(14, 14, 18));
            using (var haze = new GraphicsPath())
            {
                haze.AddEllipse(w / 4, -h / 4, w, h);
                using (var gb = new PathGradientBrush(haze))
                {
                    gb.CenterColor = Color.FromArgb(90, 200, 205, 210);
                    gb.SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) };
                    g.FillPath(gb, haze);
                }
            }
        }
        return raw;
    }

    private static void StampLogo(Bitmap dest, Image logo)
    {
        if (dest == null || logo == null) return;
        var side = Math.Min(dest.Width, dest.Height) * 6 / 10;
        var box = new Rectangle(dest.Width - side + 16, (dest.Height - side) / 2, side, side);
        using (var mark = SoftLogo(logo, side, 0.28f))
        using (var g = Graphics.FromImage(dest))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImageUnscaled(mark, box.X, box.Y);
        }
    }

    private static Bitmap SoftLogo(Image src, int size, float alpha)
    {
        var hi = Math.Max(64, size * 2);
        var big = new Bitmap(hi, hi, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(big))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(src, new Rectangle(0, 0, hi, hi), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
        }
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha };
            var ia = new System.Drawing.Imaging.ImageAttributes();
            ia.SetColorMatrix(cm);
            g.DrawImage(big, new Rectangle(0, 0, size, size), 0, 0, hi, hi, GraphicsUnit.Pixel, ia);
            ia.Dispose();
        }
        big.Dispose();
        return bmp;
    }

    private static Bitmap Blur(Bitmap src, int w, int h, int fold)
    {
        var smallW = Math.Max(8, w / Math.Max(2, fold));
        var smallH = Math.Max(8, h / Math.Max(2, fold));
        var small = new Bitmap(smallW, smallH);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, smallW, smallH);
        }
        var frost = new Bitmap(w, h);
        using (var g = Graphics.FromImage(frost))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(small, 0, 0, w, h);
        }
        small.Dispose();
        return frost;
    }
}

internal static class UiShapes
{
    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(4, radius * 2);
        if (r.Width < d || r.Height < d) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void ApplyRoundedRegion(Control c, int radius)
    {
        if (c.Width < 8 || c.Height < 8) return;
        using (var path = RoundedRect(new Rectangle(0, 0, c.Width, c.Height), radius))
            c.Region = new Region(path);
    }
}

internal class BareField : TextBox
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string app, string id);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref RECT lParam);

    private const int EmSetRect = 0xB3;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    public BareField()
    {
        BorderStyle = BorderStyle.None;
        BackColor = GlassPaint.WellFill;
        ForeColor = Color.FromArgb(244, 244, 246);
        Multiline = true;
        AcceptsReturn = false;
        WordWrap = false;
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (e.KeyChar == '\r' || e.KeyChar == '\n')
        {
            e.Handled = true;
            return;
        }
        base.OnKeyPress(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { SetWindowTheme(Handle, "", ""); } catch {}
        PadText();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PadText();
    }

    public void PadText()
    {
        if (!IsHandleCreated || Width < 8 || Height < 8) return;
        var line = Math.Max(12, Font.Height);
        var top = Math.Max(0, (Height - line) / 2);
        var rc = new RECT { Left = 14, Top = top, Right = Math.Max(15, Width - 8), Bottom = Height - 2 };
        try { SendMessage(Handle, EmSetRect, IntPtr.Zero, ref rc); } catch {}
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle &= ~0x00000200;
            cp.Style &= ~0x00800000;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0085)
        {
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }
}

internal sealed class GlassField : Panel
{
    public readonly BareField Box;
    public string Ghost;
    public string Kicker;
    public string Optional;
    public bool ShowPlus;
    public bool Busy;
    public event EventHandler PlusClick;

    private bool _plusHot;
    private readonly Font _kicker = new Font("Segoe UI Semibold", 8f);
    private readonly Font _ghost;

    public GlassField(string ghost)
    {
        Ghost = ghost;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = GlassPaint.WellFill;
        TabStop = false;
        _ghost = new Font("Segoe UI", 11.5f);
        Box = new BareField
        {
            BackColor = GlassPaint.WellFill,
            ForeColor = Color.FromArgb(244, 244, 246),
            Font = _ghost,
            Visible = false,
        };
        Controls.Add(Box);
        Box.GotFocus += delegate { ShowBox(); Invalidate(); };
        Box.LostFocus += delegate { HideBoxIfEmpty(); Invalidate(); };
        Box.TextChanged += delegate { HideBoxIfEmpty(); Invalidate(); };
        SizeChanged += delegate { LayoutInner(); };
    }

    private Rectangle PlusRect
    {
        get { return new Rectangle(Math.Max(0, Width - Height), 0, Height, Height); }
    }

    private bool HasText
    {
        get { return !string.IsNullOrEmpty(Box.Text); }
    }

    private void ShowBox()
    {
        LayoutInner();
        if (!Box.Visible) Box.Visible = true;
        Box.PadText();
    }

    private void HideBoxIfEmpty()
    {
        if (Box.Focused || HasText) return;
        Box.Visible = false;
    }

    private void LayoutInner()
    {
        UiShapes.ApplyRoundedRegion(this, Math.Min(12, Math.Max(4, Height / 2)));
        if (ShowPlus)
            Box.Bounds = new Rectangle(0, 0, Math.Max(8, Width - Height), Height);
        else if (!string.IsNullOrEmpty(Kicker))
            Box.Bounds = new Rectangle(0, 26, Width, Math.Max(8, Height - 26));
        else
            Box.Bounds = new Rectangle(0, 0, Width, Height);
        Box.PadText();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = UiShapes.RoundedRect(new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1)), Math.Min(12, Math.Max(4, Height / 2))))
        using (var b = new SolidBrush(GlassPaint.WellFill))
            g.FillPath(b, path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        LayoutInner();
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var r = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        var rad = Math.Min(12, Math.Max(4, Height / 2));
        if (Box.Focused)
        {
            using (var path = UiShapes.RoundedRect(r, rad))
            using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255)))
                g.DrawPath(pen, path);
        }
        if (!string.IsNullOrEmpty(Kicker))
        {
            TextRenderer.DrawText(g, Kicker, _kicker,
                new Rectangle(16, 8, 140, 16), Color.FromArgb(140, 140, 144),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            if (!string.IsNullOrEmpty(Optional))
                TextRenderer.DrawText(g, Optional, _kicker,
                    new Rectangle(Width - 16 - 80, 8, 80, 16), Color.FromArgb(118, 118, 122),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
        if (!HasText && !Box.Focused && !string.IsNullOrEmpty(Ghost))
        {
            var ghostR = !string.IsNullOrEmpty(Kicker)
                ? new Rectangle(16, 30, Math.Max(8, Width - 32), Math.Max(8, Height - 38))
                : new Rectangle(14, 0, Math.Max(8, Width - (ShowPlus ? Height : 0) - 16), Height);
            TextRenderer.DrawText(g, Ghost, _ghost, ghostR, Color.FromArgb(140, 168, 168, 172),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
        if (ShowPlus) DrawPlus(g, PlusRect, _plusHot, Busy);
    }

    private static void DrawPlus(Graphics g, Rectangle r, bool hot, bool busy)
    {
        if (r.Width < 8) return;
        var ink = busy
            ? Color.FromArgb(110, 110, 114)
            : hot ? Color.White : Color.FromArgb(210, 210, 214);
        if (hot && !busy)
        {
            var disc = r;
            disc.Inflate(-8, -8);
            using (var b = new SolidBrush(Color.FromArgb(36, 255, 255, 255)))
                g.FillEllipse(b, disc);
        }
        var cx = r.X + r.Width / 2f;
        var cy = r.Y + r.Height / 2f + 0.5f;
        var s = 6f;
        using (var p = new Pen(ink, 1.7f))
        {
            p.StartCap = LineCap.Round;
            p.EndCap = LineCap.Round;
            g.DrawLine(p, cx - s, cy, cx + s, cy);
            g.DrawLine(p, cx, cy - s, cx, cy + s);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hot = ShowPlus && PlusRect.Contains(e.Location);
        if (hot != _plusHot)
        {
            _plusHot = hot;
            Cursor = hot ? Cursors.Hand : Cursors.IBeam;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_plusHot)
        {
            _plusHot = false;
            Cursor = Cursors.Default;
            Invalidate();
        }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && ShowPlus && !Busy && PlusRect.Contains(e.Location))
        {
            if (PlusClick != null) PlusClick(this, EventArgs.Empty);
            return;
        }
        ShowBox();
        Box.Focus();
        base.OnMouseDown(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _kicker.Dispose();
            _ghost.Dispose();
        }
        base.Dispose(disposing);
    }
}

